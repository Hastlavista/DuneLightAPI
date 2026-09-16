# Pregled poslovne logike — BlueDragon.DuneLight backend

*Multi-tenant sustav za upravljanje fitness studijem (.NET 8 / EF Core / Postgres). Svaki entitet visi o `OrganizationId` (root tenant). Analiza temeljena na čitanju cijelog `API`, `Infrastructure` i `Core` projekta — modeli, DbContext, migracije, kontroleri, autorizacija, middleware, servisi i handleri.*

## 1. Mapa modula

| # | Modul | Svrha |
|---|---|---|
| 1 | **Users / Auth / Organizacije** | Registracija organizacije, login (lozinka i PIN), promjena lozinke/PIN-a, JWT/API-key autentikacija |
| 2 | **Permisije** (GrantGroups / Roles) | Fino-zrnati sustav ovlasti (grant ključevi), kozmetičke organizacijske oznake |
| 3 | **Katalog** | Poslovnice (Company), usluge (Service), paketi (Package), cjenik (PriceListItem), sobe (Room) |
| 4 | **Zaposlenici** | Zaposlenici, njihova povezanost s poslovnicama i uslugama, tip angažmana |
| 5 | **Klijenti** | Klijenti, kupljeni paketi (ClientPackage), oznake (tagovi), GDPR |
| 6 | **Termini / Rezervacije** | Pojedinačni termini, plaćanje, otkazivanje, ponavljajući termini, dostupni slotovi |
| 7 | **Grupe** | Grupni satovi (npr. joga), članstvo, generiranje ponavljajućih grupnih termina, evidencija prisutnosti |
| 8 | **Roster** (raspored zaposlenika) | Radno vrijeme (predlošci), odsutnosti (godišnji/bolovanje), fond godišnjeg odmora, praznici poslovnice |

Dodatno: **Organization Branding** (logo/boje/favicon per-tenant) i **Onboarding** (read-only checklist za postavljanje) — manji potporni moduli.

---

## 2. Entiteti i relacije (po modulu)

### 2.1 Users / Auth / Organizacije
- **`Organization`** — `Id, Name, Slug` (unique), branding polja (`Logo, Favicon, PrimaryColor, SecondaryColor, SurfaceColor`). Root tenant.
- **`User`** — `Id, OrganizationId, Email` (unique per org), `PasswordHash, ApiKey, Role` (legacy enum), `IsActive`, **`IsOwner`** (točno jedan po organizaciji, zaobilazi sve permisije), `MustChangeCredentialsOnFirstLogin, PinHash`.
- **`OrganizationBrandingAuditLog`** — log promjena brandinga (`ChangeType, OldValue, NewValue, ChangedBy`).

### 2.2 Permisije
- **`GrantGroup`** (`OrganizationId, Name` unique) → **`GrantGroupGrant`** (`GrantKey` string, cascade) → **`UserGrantGroup`** (M:N User↔GrantGroup).
- **`Role`** + **`UserRoleAssignment`** — kozmetička oznaka, M:N s User, **ne utječe na autorizaciju**.
- Katalog svih grant ključeva (~50, kod, ne DB) u `Core/Shared/Grants.cs`, grupirano po modulu, npr. `employees.*`, `appointments.write.own/all`, `roster.leave-fund.manage`.

### 2.3 Katalog
- **`Company`** — `Name, Address, Phone, ColorHex, Country` (ISO alpha-2, pokreće generiranje praznika), `IsActive`.
- **`Service`** — `Name` (unique dok aktivan), `ExecutionMode` (`Individual`/`Group`), `DefaultDurationMinutes, DefaultPrice`.
- **`Package`** — `EntryMode` (`SharedPool`/`PerService`), `TotalEntryCount, ValidityType` (`DayCount/EndOfMonth/FixedDate`), → **`PackageServiceItem`** (M:N s Service, `EntryCount`).
- **`PriceListItem`** — `ServiceId` XOR `PackageId` (**CHECK constraint**), `CompanyId` (nullable = sve poslovnice), `ValidFrom/ValidTo` → **`PriceListItemHistory`** (audit).
- **`Room`** — `CompanyId` (obavezan), `AllowConcurrentBookings` (default false = tvrdo blokira preklapanja u istoj sobi).

### 2.4 Zaposlenici
- **`Employee`** — `FirstName/LastName, Oib, EngagementTypeId, UserId` (1:1 s User, unique) → **`EmployeeCompany`** (M:N, `IsPrimary` — točno jedan primarni per employee, partial unique index), **`EmployeeServiceAssignment`** (M:N; prazno = ne smije nijednu uslugu — eksplicitna capability, isti obrazac kao ServiceCompany).
- **`EngagementType`** — codebook tipova angažmana.
- **`EmployeeAuditLog`** — log promjena statusa/uloge (free-text `ChangeType`).

### 2.5 Klijenti
- **`Client`** — `MemberNumber` (unique per org), `HealthNote`, `GdprConsentGiven/Date`, `HomeCompanyId/HomeTrainerId` (informativno), **`IsAnonymized`** (nepovratno).
- **`ClientPackage`** — kupljena instanca paketa, snapshot cijene/pravila u trenutku kupnje, `RemainingSharedEntries`, **`Status`** (`Active/Expired/Depleted/Cancelled`), `Version` (xmin optimistic concurrency) → **`ClientPackageServiceEntry`** (per-service brojači, isti concurrency pattern).
- **`ClientTag`** + **`ClientTagAssignment`** (M:N).

### 2.6 Termini
- **`Appointment`** — centralni entitet, samo okvir/resurs (bez naplate). `Form` (`Individual/Group`), `StartsAt, DurationMinutes` (snapshot), `ServiceId, EmployeeId` (nullable za grupne), `CompanyId, RoomId`, **`Status`** (`Scheduled/Completed/Cancelled`), `CancellationReason`, `RecurrenceGroupId`.
- **`Booking`** — Klijent↔Appointment (zamjenjuje stare `AppointmentClient`/`AppointmentAttendance`), po klijentu: `Amount, SuggestedAmount, IsAmountManuallyOverridden` (komercijalna obveza), `ClientPackageId` s praćenjem povrata unosa, `CoverageType` (`MonthlyPackage/SessionPackage/SinglePaid`, prvenstveno za Group). `PaidAmount/OutstandingAmount/IsPaid` su izvedeni (ne persistirani) iz Payment ledgera — vidi `Payment` ispod.
- **`Payment`** — monetarni ledger nad Bookingom (od 2026-09-16, zamjenjuje stari `Booking.PaymentMethod/IsPaid`): `Amount, Method` (`Cash/Card/BankTransfer/Other`), `Status` (`Completed/Voided`), `VoidedAt/VoidedBy/VoidReason`. Više Paymenta po Bookingu (partial/split), zbroj aktivnih ≤ `Booking.Amount`. Paket-pokriće (`ClientPackageId`) NIKAD ne stvara Payment — entitlement nije novac.
- **`AppointmentAuditLog`**, **`ScheduleBreak`** (pauza trenera, bez naplate).

### 2.7 Grupe
- **`Group`** (recurring class definicija, bez datuma isteka) → **`GroupSlot`** (tjedna ponavljanja: `DayOfWeek, StartTime`), **`GroupMember`** ("tko obično dolazi", partial unique index dok aktivan).
- **`GroupAuditLog`**.

### 2.8 Roster
- **`RosterType`** — codebook (Rad/Godišnji/Bolovanje su default-seed), `IsAbsence, CountsAsWork, DeductsFromLeaveFund`.
- **`RosterEntry`** — jedan zapis (rad ili odsutnost, oblik ovisi o `RosterType.IsAbsence`), `IsOverride`.
- **`WorkingHoursTemplate`** — singleton po Employee XOR Company (**CHECK constraint**), `CycleType` (`Weekly/Fortnightly/FourWeekly`) → **`WorkingHoursInterval`**.
- **`EmployeeLeaveSettings`** (1:1), **`LeaveFund`** (per employee per fiscal godina, `Version` xmin) → **`LeaveFundUsage`** (cascade od RosterEntry, bez nav-a — poslovna logika mora vratiti dane prije brisanja).
- **`CompanyHoliday`** — po poslovnici (nema "sve poslovnice" koncepta), `IsAutoGenerated`.

**Relacijska pravila (DbContext):** Restrict dominira za referentne podatke (Service/Company/Employee/Package/Client/Room/RosterType) — sprječava osirotjele povijesne zapise. Cascade samo za "podređene" retke (npr. slotovi, junction tablice, audit koji smije nestati s vlasnikom — osim log-ova poput `RosterAuditLog`/`EmployeeAuditLog`/`AppointmentAuditLog`/`GroupAuditLog` koji **namjerno nemaju FK/nav** natrag na subjekt, da povijest preživi brisanje.

---

## 3. Uloge i permisije

Sustav ima **tri odvojena, lako pobrkljiva koncepta**:

1. **`UserRole` enum** (`Admin, Member, Reception`) — legacy, spremljen na `User`, u JWT claimu, koristi se samo kao **filter** u API-ju (npr. `GET /employees?role=`), **više ne odlučuje o autorizaciji**.
2. **`Role`** (DB entitet) — slobodni tekstualni "posao" tag (npr. "Trener"), eksplicitno dokumentiran u kodu kao **bez utjecaja na autorizaciju**.
3. **`GrantGroup`** — stvarni sustav ovlasti. Efektivne ovlasti korisnika = unija `GrantKey`-jeva svih dodijeljenih grupa. `User.IsOwner = true` zaobilazi sve provjere (koristi se samo za korisnika koji je registrirao organizaciju).

**Autorizacijski atributi** (`API/Authorization/`):
- `[RequireGrant(...)]` — OR logika preko više grant ključeva; Owner uvijek prolazi. Koristi se na većini endpointa.
- `[RequireGrantOrAssignedCompany(...)]` — dodatno propušta ako je zaposlenik dodijeljen poslovnici iz rute (samo `CompanyHolidaysController.GetForCompany`).
- `[RequireOwner]` — samo `IsOwner`. Koristi se na kontrolerima koji upravljaju samim sustavom ovlasti: `GrantGroupsController`, `GrantsController`, `RolesController`.

**Own/All pattern** — mnoge ovlasti dolaze u paru `*.write.own` / `*.write.all` (ili `.view.*`). Kontroler u tijelu akcije odlučuje (`HasGrant(...)`) je li pozivatelj ograničen na svoje podatke ili sve, i taj boolean prosljeđuje servisu koji stvarno provjerava vlasništvo (`employee.Id == currentUserId`).

### Permisije po modulu (sažetak, own/all gdje postoji)

| Modul | Ključne ovlasti |
|---|---|
| Zaposlenici | `employees.directory.view` (kolege, ograničena polja), `employees.view` (puni prikaz), `employees.manage`, `employees.role.manage` (posebno, escalation-osjetljivo) |
| Katalog | `catalog.{companies\|services\|packages\|price-list\|rooms}.view/manage` |
| Klijenti | `clients.view/manage/status-manage`, `clients.anonymize` (posebno), `clients.packages.view/manage`, `clients.tags.view/manage` |
| Termini | `appointments.write.own/all`, posebna `appointments.delete` ovlast (same-day-only) |
| Grupe | `groups.view/manage`, `groups.attendance.view`, `groups.attendance.own/all` |
| Roster | `roster.entries.write.own/all`, `roster.templates.view/manage` (**bez own/all split** — jer stvara tvrdu blokadu za sve), `roster.leave-fund.view.own/all`, `roster.leave-fund.manage`, `roster.leave-fund-settings.view/manage`, `roster.reviews.team.view`, `roster.reviews.personal.view.own/all` |
| Schedule Breaks | `schedule-breaks.write.own/all` |
| Organizacija/Branding | `organization.branding.manage` |
| Permisije | sve `[RequireOwner]` — samo vlasnik organizacije |

**Što pojedina uloga smije/ne smije** (efektivno, kroz grant kombinacije koje se realno koriste):
- **Vlasnik (IsOwner)** — sve, bez iznimke; jedini smije upravljati GrantGroups/Roles/Grants katalogom.
- **Admin (tipično sve `.manage`/`.all` ovlasti)** — puno upravljanje katalogom, zaposlenicima, klijentima, terminima svih trenera, roster predlošcima/praznicima, ali NE upravljanje ovlastima/grant grupama (to je Owner-only).
- **Trener/Reception (tipično `.own` ovlasti)** — vidi/uređuje samo svoje termine, svoje roster zapise, svoj fond godišnjeg; ograničeni pregled kolega (`employees.directory.view`, bez osjetljivih polja); ne može mijenjati radno vrijeme (own/all namjerno izostavljen za templates).

---

## 4. Poslovna pravila i validacije po modulu

*Format: uvjet → posljedica → lokacija.*

### 4.1 Auth
- Login/PinLogin: bilo koji razlog neuspjeha (org ne postoji, user ne postoji, neaktivan) → **isti generički** `AUTH_INVALID_CREDENTIALS`/`AUTH_INVALID_PIN` (401), namjerno bez otkrivanja razloga (`AuthService.Login/PinLogin`).
- Registracija: slug organizacije mora biti jedinstven — provjera unaprijed + hvatanje Postgres unique-violation (TOCTOU race) → `AUTH_ORGANIZATION_SLUG_TAKEN` (409) (`AuthService.Register`).
- ChangePassword/ChangePin: mora se ponoviti trenutna lozinka (i za PIN promjenu) → inače `AUTH_CURRENT_PASSWORD_INVALID` (401) (`AuthService.ChangePassword/ChangePin`).
- **Nema rate-limitinga/lockouta na PIN login** — eksplicitna, dokumentirana odluka (dijeljeni fizički uređaj) (`AuthService.cs` komentar).

### 4.2 Zaposlenici
- "Zadnji aktivni admin" — deaktivacija ili promjena uloge s Admin na drugo, ako je to jedini aktivni admin → blokirano, `LAST_ACTIVE_ADMIN` (409) (`EmployeeService.SetActive`, `UpdateRole`).
- Brisanje blokirano ako postoji audit log ili bilo kakva poslovna referenca (termini — bilo koji, ne samo budući; pauze, roster, radno vrijeme, fond godišnjeg, matični trener klijenta/grupe) → `REFERENCED_CANNOT_DELETE`, poruka upućuje na deaktivaciju (`EmployeeService.Delete`, `IEmployeeHandler.HasBusinessReferences`). `EmployeeCompany`/`EmployeeServiceAssignment` su konfiguracijski (Cascade) i sami po sebi ne blokiraju.
- Deaktivacija s budućim terminima **ne blokira**, samo vraća `WARNING: EMPLOYEE_HAS_FUTURE_APPOINTMENTS` (`EmployeeService.SetActive`).
- Neaktivna poslovnica/usluga smije ostati dodijeljena ako je već bila dodijeljena prije izmjene ("grandfathering"), ali se ne smije NOVO dodijeliti neaktivna (`EnsureCompaniesUsable/EnsureServicesUsable`).
- Kreiranje s loginom: email mora biti slobodan u organizaciji → `EMAIL_ALREADY_IN_USE` (`EmployeeService.CreateWithLogin`).

### 4.3 Klijenti
- GDPR anonimizacija je **nepovratna** — blokira daljnje uređivanje i reaktivaciju (`CLIENT_ANONYMIZED`, 409); sama `Anonymize` je idempotentna (`ClientService`, `ClientHandler.Anonymize`).
- Brisanje blokirano ako klijent ima IKAD ijedan termin ili paket → `REFERENCED_CANNOT_DELETE` (**napomena**: naziv provjere "future activity" je zavaravajući, u praksi provjerava cijelu povijest, ne samo buduću — vidi §7).
- `MemberNumber` mora biti jedinstven per org → `DUPLICATE_MEMBER_NUMBER`.
- Eligibilnost paketa za uslugu (`ClientPackageHandler.GetEligibleForService`): status `Active`, nije istekao, pokriva traženu uslugu, ima preostalih unosa (null = neograničeno).
- Mutacija broja unosa paketa (check-in/check-out) štićena **optimističkom konkurentnošću** (xmin, do 3 pokušaja) → `CONCURRENCY_CONFLICT` ako se sudari (npr. dva istovremena check-ina na dijeljenom paketu) (`ClientPackageService.DeductEntry/ReturnEntry`).

### 4.4 Termini
- **Tvrdo blokirano** (`APPOINTMENT_OVERLAP`, 409) — uvijek: trener već ima termin u tom prozoru (isključujući Cancelled/NoShow), soba zauzeta (osim ako `Room.AllowConcurrentBookings`), bilo koji od klijenata već rezerviran drugdje (`AppointmentService.EnsureNoHardOverlapCollectWarnings`).
- Pauza trenera (`ScheduleBreak`) u istom terminu — samo **upozorenje**, ne blokira ("zaposlenik je tehnički dostupan").
- Radno vrijeme — samo **upozorenje** za individualne termine (osim što se potpuno preskače za "Complete" akcije koje bilježe već odrađen rad).
- Plaćanje paketom: svaki klijent na terminu mora imati točno jedan odabran paket (bez duplikata/praznina) → `ValidationAppException`; paket mora biti u eligible listi tog klijenta → `PACKAGE_NOT_ELIGIBLE` (`ValidatePackageSelections`).
- Status-ovisna pravila: `Complete` na već `Completed` terminu → `ALREADY_COMPLETED`; `Move` na `Cancelled`/`NoShow` terminu → `APPOINTMENT_NOT_MOVABLE`; hard-delete dopušten **samo istog dana kad je kreiran** → `SAME_DAY_ONLY` (inače: otkazati).
- Ponavljajući termini: pravi sukobi (trener/soba/traženi klijent) blokiraju **cijeli batch** prije spremanja → `RECURRING_CONFLICT` (409) s popisom sukoba; radno vrijeme/praznik/odsutnost/pauza više NE blokiraju batch (promjena iz ranije faze — sada samo upozorenja per-termin).
- Soba mora pripadati istoj poslovnici kao termin → `ROOM_COMPANY_MISMATCH`.

### 4.5 Grupe
- Usluga mora biti `ExecutionMode = Group` → `SERVICE_NOT_GROUP_MODE`.
- Sukob sobe/slota (druga aktivna grupa, ista soba bez `AllowConcurrentBookings`, preklapajući dan/vrijeme) → tvrdi blok `APPOINTMENT_OVERLAP`.
- "Zadnji aktivni slot" — grupa mora zadržati ≥1 aktivni slot → `LAST_ACTIVE_SLOT`.
- Dodavanje člana koji je već aktivan → `ALREADY_MEMBER`; **kapacitet nije tvrdi limit** — prekoračenje daje samo `WARNING: GROUP_CAPACITY_EXCEEDED`, dodavanje uspijeva.
- Deaktivacija grupe **trajno briše** (ne otkazuje) buduće još-neodržane generirane termine (prazni su, ništa naplaćeno) — prošli/završeni termini ostaju netaknuti.
- `GenerateAppointments`: pravi sukobi trenera/sobe blokiraju cijeli batch (`RECURRING_CONFLICT`), radno vrijeme/odsutnost/pauza su samo upozorenja.
- Prisutnost (`SetAttendance`): ako klijent ima >1 eligible paket, mora se eksplicitno odabrati (bez tihog auto-odabira) → `ValidationAppException`; odjava prisutnosti **automatski** vraća odbijeni unos paketa (za razliku od pojedinačnih termina gdje je povrat opt-in — nekonzistentnost, vidi §7).

### 4.6 Roster
- Vlasništvo: bez "all" ovlasti, korisnik smije upravljati samo svojim zapisima → `NOT_OWNER` (409) (primjenjuje se na RosterEntry, ScheduleBreak, LeaveFund, GroupAttendance — jedinstveni obrazac).
- Tip roster zapisa (`RosterType.IsAbsence`) određuje oblik: odsutnost zabranjuje vrijeme i traži `DateTo >= DateFrom`; rad traži točno jedan dan + vrijeme.
- Zapis koji troši fond godišnjeg (`DeductsFromLeaveFund`) mora imati zatvoren raspon (`DateTo` obavezan) → `LEAVE_FUND_ENTRY_REQUIRES_END_DATE`.
- Alokacija fonda: troši najstariji fond prvi, sve-ili-ništa (bez djelomične mutacije prije provjere) → `LEAVE_FUND_EXCEEDED` ako nema dovoljno dana.
- **Preklapanje roster zapisa je samo upozorenje** (`WARNING: ROSTER_ENTRY_OVERLAP`), ne blokira — suprotno od `ScheduleBreak` preklapanja koje je tvrdi blok (nekonzistentnost, §7).
- `WorkingHoursTemplate`: intervali unutar istog (ciklus-tjedan, dan) se ne smiju preklapati; `EndTime > StartTime`.
- Generiranje praznika: pokušava vanjski API, fallback na `DefaultCompanyHolidays` — trenutno definiran **samo za "HR"**, druge zemlje → `HOLIDAY_CATALOG_NOT_DEFINED_FOR_COUNTRY`.
- Duplikat datuma praznika → `DUPLICATE_HOLIDAY_DATE`.

### 4.7 Katalog / Cjenik
- Cjenik: raspon `ValidTo >= ValidFrom` (oba inkluzivna); preklapanje istog subjekta+poslovnice unutar aktivnih stavki → `PRICE_OVERLAP`; točno jedno od `ServiceId`/`PackageId` (CHECK constraint na DB razini + validacija u servisu).
- `CompanyId == null` = "za sve poslovnice"; preklapanje se provjerava unutar TOČNO iste poslovnice (uklj. null) — org-wide i poslovnica-specifična stavka smiju se preklapati (različiti prioritet u razrješavanju).
- Kreiranje NOVE stavke cjenika zahtijeva aktivan subjekt (`Service.IsActive`/`Package.IsActive` → `INACTIVE_SERVICE`/`INACTIVE_PACKAGE`) i aktivnu poslovnicu ako je `CompanyId` zadan (`INACTIVE_COMPANY`); postojeće stavke ostaju netaknute kad subjekt/poslovnica kasnije postanu neaktivni.
- Identitet stavke (`ServiceId`/`PackageId`/`CompanyId`/`OrganizationId`) je nepromjenjiv nakon kreiranja — update DTO dopušta samo `Price`/`ValidFrom`/`ValidTo`.
- Brisanje cjenovne stavke blokirano ako postoji povijest promjena → `REFERENCED_CANNOT_DELETE`; povijest (`PriceListItemHistory`) bilježi staru/novu vrijednost za `Price`, `ValidFrom` i `ValidTo` zajedno, atomično s updateom (jedan `SaveChanges`).
- Razlučivanje cijene (`PriceResolutionService`, bez pristupa bazi): specifična cijena za poslovnicu > cijena "za sve poslovnice" > default cijena subjekta; preklapanje bi po pravilu trebalo spriječiti dvoznačnost, `OrderByDescending(ValidFrom)` je samo sigurnosna mreža.
- Provjera preklapanja je isključivo na razini aplikacije (isti obrazac kao `APPOINTMENT_OVERLAP`) — nema DB exclusion constrainta, pa postoji teoretski race prozor kod dva istovremena zahtjeva (nekonzistentnost/rizik, §7).
- "Zadnja aktivna poslovnica" — org mora imati ≥1 aktivnu poslovnicu → `LAST_ACTIVE_COMPANY`.
- Uzorak "jedinstveno ime dok aktivno + zaštita od brisanja ako referencirano" ponavlja se identično kroz Service/Package/Room/EngagementType/ClientTag/RosterType.

### 4.8 Permisije
- Grant ključevi u zahtjevu moraju postojati u kod-katalogu `Grants.Catalog` → inače `ValidationAppException` s popisom nepoznatih ključeva.
- `GrantGroup` se ne smije obrisati ako su joj korisnici dodijeljeni → `REFERENCED_CANNOT_DELETE` ("prvo im dodijelite drugu grupu") — **`Role` entitet nema ovu zaštitu** (nekonzistentnost, §7).

---

## 5. Statusi i tokovi (state machine)

**`AppointmentStatus`**: `Scheduled → Completed` | `Scheduled → Cancelled` | `Scheduled → NoShow`. `Completed` je terminalan (ne može natrag). `Cancelled`/`NoShow` blokiraju `Move`; termin se briše samo istog dana kreiranja, inače mora ići kroz `Cancel`. Trigeri: trener/admin (own/all ovlasti); `Cancel`/`NoShow` opcionalno vraćaju odbijene unose paketa po klijentu.

**`ClientPackageStatus`**: `Active ⇄ Depleted` (event-driven, na 0 preostalih unosa; vraća se na `Active` bilo kojim povratom unosa), `Active → Cancelled` (ručna akcija), `Expired` — **nikad perzistira**, računa se dinamički iz `ExpiryDate` u trenutku čitanja.

**`AppointmentAttendance` (grupni termini)**: `Attended: null → true/false`. Prijelaz na `true` troši unos paketa (ako `SessionPackage`); prijelaz natrag na `false` automatski vraća unos. Trigerira trener (own) ili admin (all).

**Roster/LeaveFund**: nema eksplicitnog statusnog enuma, ali implicitan tok: `LeaveFund` se lijeno otvara → `UsedDays` raste alokacijom → briše se/vraća pri update/delete povezanog `RosterEntry`-ja (uvijek potpuni reverse-then-reallocate, bez diffanja).

---

## 6. Error handling

Jedinstveni JSON envelope za cijeli API:
```json
{ "error": { "code": "STRING_CODE", "message": "Poruka na hrvatskom", "details": null | {...} } }
```
- `ValidationAppException` → 400, `NotFoundAppException` → 404, `UnauthorizedAppException` → 401, `BusinessRuleException` → 409, sve ostalo → 500 (generička poruka, bez stack trace-a klijentu).
- Framework 401/403 (iz `[Authorize]`/`RequireGrant` neuspjeha) presretnuti u `ExceptionHandlingMiddleware` i prepisani u isti envelope (`UNAUTHORIZED`/`FORBIDDEN`).
- `ErrorCodes.cs` je jedini izvor istine (dokumentirano kao stabilan ugovor prema frontendu, "ne mijenjati postojeće vrijednosti").
- Paralelan, **ne-blokirajući** `WarningCodes` sustav — upozorenja se vraćaju uz uspješan odgovor (`warnings[]`) za akcije koje uspijevaju unatoč kršenju "mekog" pravila (kapacitet, radno vrijeme, preklapanje odsutnosti/pauze, buduće aktivnosti zaposlenika).
- Nema FluentValidation-a — samo Data Annotations (za osnovnu strukturu DTO-a) + ručna validacija u servisima (za poslovna pravila).

---

## 7. Uočene nekonzistentnosti i nedostaci

1. **`Role` vs `GrantGroup` brisanje** — `GrantGroupService.Delete` ima zaštitu od brisanja ako su korisnici dodijeljeni, `RoleService.Delete` nema nikakvu zaštitu (briše bezuvjetno) iako je model isti.
2. **Zavaravajuć naziv provjere** — `ClientFutureActivityProvider.HasFutureActivity` u stvarnosti provjerava POSTOJI LI IKAD bilo koji termin/paket (ne samo budući) — naziv sugerira drugačije ponašanje nego stvarno postoji.
3. **Nekonzistentan tretman preklapanja** — preklapanje `RosterEntry` je samo upozorenje, dok je preklapanje `ScheduleBreak` tvrdi blok, iako su konceptualno slična (oboje "zaposlenik je zauzet u ovom terminu").
4. **Nekonzistentan povrat unosa paketa** — kod pojedinačnih termina povrat unosa pri otkazivanju je opt-in (`ReturnEntryForClientIds`), kod grupne prisutnosti automatski se događa pri odjavi — različito zadano ponašanje za konceptualno istu radnju u dva modula.
5. **`AuthHandler.UpdateRole`** baca običan `ArgumentException` umjesto `BusinessRuleException`/strukturirane greške — odstupa od konvencije koja se dosljedno koristi svugdje drugdje.
6. **Praznici definirani samo za Hrvatsku** — `DefaultCompanyHolidays` sadrži samo "HR" katalog; svaka druga zemlja tiho vraća prazan popis i traži potpuno ručan unos (pomični blagdani poput Uskrsa isključeni čak i za HR — namjerno, ali vrijedno napomene).
7. **Cache ovlasti bez invalidacije** — `GrantResolver` cache-ira efektivne ovlasti 30s po korisniku bez invalidacije na write; promjena ovlasti korisniku može ostati "aktivna" do 30s nakon oduzimanja.
8. **CORS potpuno otvoren** — `SetIsOriginAllowed(_ => true)` uz `AllowCredentials()` — svaka domena smije slati kredencijalizirane zahtjeve; vrijedno preispitati za produkciju.
9. **Audit log `ChangeType` kao slobodan tekst** — `EmployeeAuditLog`, `AppointmentAuditLog`, `GroupAuditLog`, `RosterAuditLog` sve koriste string umjesto enuma za tip promjene — nema kompajlerske zaštite od tipfelera.
10. **Legacy `UserRole` polje** — i dalje postoji na `User`/`Employee`, koristi se kao filter i u JWT claimu, ali ne utječe na autorizaciju; sami komentari u kodu najavljuju buduće uklanjanje — trenutno stanje je tranzicijsko i može zbuniti nove developere.
11. **`CompanyHoliday` nema "sve poslovnice" koncept** — svaki praznik se mora unijeti po poslovnici pojedinačno, nema mogućnosti definiranja državnog praznika jednom za cijelu organizaciju.
12. **`DefaultRosterTypes` se ne backfilla** — ako se default set (Rad/Godišnji/Bolovanje) promijeni u kodu, postojeće organizacije to ne dobivaju retroaktivno; samo nove registracije.
13. **Nema rate-limitinga na PIN login** — svjesna odluka za dijeljeni uređaj, ali vrijedi eksplicitno dokumentirati kao sigurnosni kompromis, ne previdjeti ga.
14. **`GrantGroupGrant.GrantKey`** nije validiran na razini baze protiv kod-katalog `Grants.Catalog` (samo u servisu pri kreiranju) — ako se katalog promijeni/preimenuje ključ, postojeći DB retci mogu ostati "osiročeni" bez upozorenja.
15. **Preklapanje (`PRICE_OVERLAP`, `APPOINTMENT_OVERLAP`) provjerava se isključivo na razini aplikacije** — provjera "postoji li preklapanje" i `INSERT` nisu u istoj transakciji/DB constraintu (nema exclusion constrainta), pa dva istovremena zahtjeva teoretski mogu oba proći provjeru prije nego ijedan upiše red. Konzistentno kroz cijeli kod, ali nije DB-safe.