# T1 — Sat sustava i testni alati (preduvjet za F1) — plan

> Status: **implementiran 2026-10-09.** Zaključane odluke i stvarna implementacija: [T1_DECISION_RECORD.md](T1_DECISION_RECORD.md)
> (ima prednost pred planom; odstupanja od plana su tamo). Mala backend faza prije frontend faze F1 (frontend `docs/f1/`).

## 0. Opseg

| # | Stavka | Trajno / privremeno |
|---|---|---|
| T1-1 | Jedan poslovni sat (`TimeProvider`) za cijeli sustav, umjesto 252 izravna čitanja sata | trajno |
| T1-2 | Simulirani pomak sata po organizaciji (samo naprijed) + izvršavanje propuštenih poslova nakon skoka | **privremeno** (testni alat) |
| T1-3 | Endpoint "trenutno vrijeme organizacije" | trajno (bez pomaka = stvarno vrijeme) |
| T1-4 | Seed iz Management portala (nova demo organizacija / dopuna postojeće) | **privremeno** (testni alat) |
| T1-5 | Oblik 403: `details.requiredGrants` + `details.reason` | trajno |
| T1-6 | Točnost Swaggera za generiranje frontend tipova (nullable, enumi, datumi, oblik grešaka) | trajno |

T1-5 i T1-6 nisu dio TimeProvidera, ali su backend preduvjeti F1 (odluke F1-Q5, F1-Q8); predlažem ih u istoj fazi da F1 ne
čeka dvije backend faze. Ako ih želiš odvojeno, T1 = T1-1 … T1-4.

## 1. T1-1 Sat sustava

### 1.1 Model
- **Poslovni sat**: `TimeProvider` kroz DI. ~~Scoped po organizaciji~~ → **singleton `BusinessTimeProvider` + organizacija
  toka izvršavanja (`OrganizationClockContext`, AsyncLocal)**, jer su handleri singletoni i ne mogu primiti scoped ovisnost
  (izmjena u implementaciji, T1 record). `GetUtcNow()` = stvarni UTC + pomak organizacije (0 kad testni alati nisu uključeni
  ili organizacija nema pomak). U HTTP zahtjevu organizaciju postavlja middleware iz tokena; pozadinski servisi po organizaciji.
- **Sistemski sat** (`TimeProvider.System`, nikad pomaknut): JWT istek (tenant i platforma), outbox lease/backoff/`processed_at`,
  bootstrap platformskog računa, `Task.Delay` pozadinskih servisa. Razlog: token izdan "u budućnosti" bi odmah istekao ili
  vrijedio predugo; lease je tehnički mehanizam, ne poslovni događaj.
- Čista pravila u `Utils` ne čitaju sat: dobivaju `now`/`today` parametrom (danas npr. `AppointmentLifecycle` ima `at ?? UtcNow`).
- "Danas" se i dalje računa samo kroz `OrganizationCalendar` (ADR-0013), sada iz poslovnog sata.

### 1.2 Popis mjesta (stanje 2026-10-09)
252 čitanja (`DateTime.Now/UtcNow`, `DateTimeOffset.Now/UtcNow`) u 61 datoteci produkcijskog koda + 128 u testovima. Nema
`now()`/`CURRENT_DATE` u SQL-u ni defaulta vremena u migracijama (provjereno grepom; ponovno se provjerava na kraju).

| Grupa | Datoteke (broj čitanja) | Sat |
|---|---|---|
| Raspored i lifecycle | `BookingService` (21), `GroupService` (15), `AppointmentService` (13) + `.Segments` (5), `WaitlistService` (4), `ScheduleBreakService` (3), `GroupAttendanceService` (1), `AppointmentHandler` (4), `Utils/AppointmentLifecycle` (2), `Utils/BookingParticipations` (2), `Utils/AvailabilityOverride` (1), `Outbox/ParticipationEvents` (4, `OccurredAt`) | poslovni |
| Komercijala | `CommissionService` (16), `CheckoutService` (13), `ClientPackageService` (8), `PricingService` (5), `PaymentService` (4), `StockService` (2), `ProductService` (3), `PackageService` (3), `PriceListController` (1, zadani datum) | poslovni |
| Članarine | `ClientMembershipService` (15), `MembershipPlanService` (7), `MembershipRenewalService` (6), `MembershipCoverageService` (3), `GroupMembershipSkipService` (2), `Utils/MembershipTimelines` (1) | poslovni |
| Workforce / roster | `RosterEntryService` (11), `EmployeeService` (5), `LeaveFundService` (2), `LeaveFundHandler` (3), `EmployeeLeaveSettingsHandler` (2), `WorkingHoursTemplateHandler` (2), `RosterTypeService` (3), `RosterTypeHandler` (1), `EngagementTypeService` (3), `CompanyHolidayService` (2) | poslovni |
| Katalog, klijenti, postavke | `ClientService` (6), `RoomService` (5), `ResourceService` (4), `ServiceCatalogService` (3), `CompanyService` (3), `CompanyHandler` (1), `ClientTagService` (3), `CancellationPolicyService` (5), `CancellationReasonService` (3), `OrganizationSettingsHandler` (1), `OrganizationBrandingService` (1), `GrantGroupService` (2), `GrantGroupHandler` (1), `RoleService` (1), `OperationalDashboardService` (2) | poslovni |
| Notifikacije | `Outbox/Handlers/*NotificationHandler` (3), `OutboxWriter` (1, `CreatedAt` poruke) | poslovni za zapis, sistemski za `available_at` |
| Sistemsko | `JwtService` (1), `Management/PlatformJwtService` (1), `AuthService` (1 od 3: istek tokena), `OutboxProcessorService` (1), `OutboxHandler` (2), `Management/PlatformAccountBootstrapper` (1) | sistemski |
| Registracija | `AuthService` (2: `CreatedAt` organizacije i korisnika) | stvarni (organizacija tada još nema pomak) |

Na kraju faze: grep mora vratiti nula izravnih čitanja izvan jedne klase sistemskog sata; popis u T1 recordu.

### 1.3 Testovi
- Testovi koriste isti mehanizam: fiksni sat (`FakeTimeProvider`, paket `Microsoft.Extensions.TimeProvider.Testing`) u
  `SchedulingTestHost`/`SchedulingWorld` i ostalim hostovima; testovi koji danas računaju "sutra" od stvarnog sata računaju od
  fiksnog. Bez zasebnih mehanizama.
- Karakterizacijski testovi se ne smiju promijeniti po ponašanju; ako koji padne, to je nalaz, ne prilagodba.

## 2. T1-2 Pomak sata (odgovori na 2–4)

### 2.1 Samo naprijed i povratak (točka 2)
- Pomak se sprema u zasebnu tablicu testnih alata (npr. `test_clock_offsets`: organizacija, pomak, tko i kada), ne u postavke
  organizacije, da se prije go-livea ukloni jednom migracijom.
- Naredba prima ciljni trenutak ili broj dana; novi pomak mora biti ≥ trenutnog, inače `TEST_CLOCK_BACKWARDS`.
- **Povratak na stvarno vrijeme = nova demo organizacija.** "Reset" demo organizacije u Managementu stvara novu demo
  organizaciju s pomakom 0 i svježim seedom, a staru deaktivira. Razlog: brisanje svih podataka jedne organizacije (desetci
  tablica s ledgerima) je velik i rizičan posao za privremeni alat; nova organizacija daje isti rezultat u jednom koraku.
- Organizacija koju puniš ručno (dopuna seedom) nema povratak: ako je pomakneš, ostaje pomaknuta. Prijedlog: pomak koristiti
  samo na demo organizacijama.

### 2.2 Poslovi nakon skoka (točka 3)
Većina stanja se već **izvodi** iz sata pri čitanju, pa nakon skoka "samo vrijedi": zatvorenost termina (ADR-0032), stanje
članstva i pauze (ADR-0026), isteka paketa, otkazni prozori. Jedini zapisi koje sat stvara su u **prolazu obnove** (ADR-0027):
periodi, zaduženja, istek grace perioda i automatski završetak zbog duga, kraj stajanja (K1), usklađivanje pokrića i horizonta,
naknadno dodavanje preskočenih članova grupe, `PriceStale`.

**Dodatna provjera (2026-10-09, na zahtjev): drugih zakazanih poslova nema.** Hosted servisi su samo `OutboxProcessorService`
(tehnički, sistemski sat), `MembershipRenewalBackgroundService` i `PlatformAccountBootstrapper` (jednokratno pri startu).
Generiranje grupnih termina je ručna naredba (`POST /api/groups/generate-appointments`, nema pomičnog horizonta); lista čekanja
ističe samo na događaj termina (otkaz / završetak, `WaitlistExpiredReasons`), ne po vremenu; istek paketa se izvodi iz
`ValidUntilDate` i današnjeg datuma pri čitanju; podsjetnika i vanjske dostave obavijesti nema (P4). Dakle dan-po-dan se
pokreće samo prolaz obnove.

Prijedlog:
- Naredba pomaka u istom zahtjevu, nakon spremanja pomaka, pokreće prolaz obnove **dan po dan** od starog do novog lokalnog
  datuma organizacije (`RunForOrganization(org, dan)` za svaki dan, redom). Prolaz je idempotentan, a dan-po-dan daje točno ono
  što bi dnevni pozadinski servis napravio (npr. grace i automatski završetak se procjenjuju na pravi dan, ne tek na zadnji).
- Unutar dana redoslijed ostaje postojeći (`CatchUp`: kraj stajanja → obnova perioda redom → dug i grace).
- Odgovor naredbe: novo vrijeme, broj obrađenih dana i članstava.
- Pozadinski servis obnove radi s poslovnim satom svake organizacije (scope po organizaciji), pa nakon skoka više ne "vraća"
  stanje stvarnim datumom (danas poznato ograničenje dev alata).
- Outbox poruke nastale tijekom skoka obrađuju se normalno (sistemski sat).
- Ograničenje: skok od više godina × mnogo članstava traje; za demo podatke prihvatljivo. Gornja granica jednog skoka (npr. 400
  dana) — prijedlog.
- Postojeći `POST /api/dev/time/membership-renewal-run` se **uklanja** (jedan sat za sve).

### 2.3 Nikad u produkciji (točka 4)
- Izričita postavka `TestTools:Enabled` (default `false`), neovisna o imenu okruženja; pokriva pomak sata i seed (jedan
  prekidač).
- Ako je `TestTools:Enabled = true` u okruženju `Production`, aplikacija odbija pokretanje s jasnom porukom (provjera u startupu).
- Kad je isključena: rute testnih alata ne postoje (404, isti obrazac kao `[DevelopmentOnly]`, koji se zamjenjuje atributom
  `[TestToolsOnly]`), poslovni sat = stvarni sat, tablica pomaka se ne čita.

### 2.4 Gdje se pomiče
Iz **Management portala** (platformski API, kao seed): `POST /api/management/organizations/{id}/test-tools/clock/advance`.
Prijedlog, jer pomak i seed rade zajedno i samo ti im pristupaš.

## 3. T1-3 Trenutno vrijeme organizacije

`GET /api/organization/clock` (svaki prijavljeni korisnik, bez granta):
`realUtc`, `effectiveUtc`, `offset` (0 bez pomaka), `isSimulated`, `timeZone` organizacije, `localDate` i `localTime` u zoni
organizacije, te popis poslovnica kojima je zona različita od organizacije (`companyId`, `timeZone`, `localDate`). Trajni
endpoint: frontend iz njega uzima "sada" i "danas" (pravilo F1, frontend FE-ADR-0005).

## 4. T1-4 Seed (Management)

- `POST /api/management/test-tools/demo-organizations` — nova demo organizacija (vlasnik + testni korisnici, podaci).
- `POST /api/management/test-tools/demo-organizations/{id}/reset` — nova demo organizacija s pomakom 0 (§2.1), stara deaktivirana.
- `POST /api/management/organizations/{id}/test-tools/seed` — dopuna postojeće organizacije.
- Seed ide kroz postojeće servise (aplikacijski tokovi, u duhu ADR-0022); **samo dodaje**, ne mijenja ni briše postojeće
  poslovnice, radno vrijeme, usluge, grupe, cjenike, grupe ovlasti ni korisnike.
- Bez zastavice "testna organizacija", bez zaštite od duplikata; odgovor = broj dodanog po vrsti.
- Sadržaj (prijedlog): 2 poslovnice, radno vrijeme, sobe i resursi, ~6 usluga (individualne i grupna), cjenik, 2 paketa,
  2 plana članarine, politika otkazivanja s naknadom, razlozi otkazivanja, 3 zaposlenika s loginom (jedan sa svim grantovima,
  jedan s dijelom, jedan bez K2 grantova), grupe grantova, ~20 klijenata, grupa s članovima, nekoliko termina u prošlosti i budućnosti.
- Lozinke testnih korisnika: vraćaju se jednom u odgovoru Managementu (ne spremaju se nigdje drugdje).
- Isti prekidač `TestTools:Enabled`; na popisu za go-live.

## 5. T1-5 Oblik 403

```json
{ "error": { "code": "FORBIDDEN", "message": "...", "details": {
    "reason": "MissingGrant",
    "requiredGrants": ["appointments.corrections.completed", "appointments.corrections.no-show"],
    "match": "All" } } }
```
- `requiredGrants`: svi grantovi koji nedostaju; `match`: `All` (treba svaki) ili `Any` (dovoljan jedan; `[RequireGrant]` s više
  grantova).
- `reason` (popis svih 403 u kodu, 2026-10-09):

| `reason` | Izvor | Detalji |
|---|---|---|
| `MissingGrant` | `[RequireGrant]` filter (danas `ForbidResult` bez tijela); `AppointmentClosure` (korekcija, ponovno otvaranje — svi nedostajući); `AvailabilityOverride`; `PolicyOverride` (otpis); `GroupCapacityGuard`; `RosterEntryService` (prošlost); `AppointmentService` ("vrati termin"); `BookingService` (Business otkazivanje) | `requiredGrants`, `match` |
| `OutOfScope` | own opseg na tuđem resursu (`AppointmentService` "Nemate pristup ovom terminu", `BookingService` sudjelovanje), `RequireGrantOrAssignedCompany` | `currentScope` (`Own`), `requiredScope` (`All`), `requiredGrants` (npr. `appointments.write.all`) |
| `CompanyNotAssigned` | pristup poslovnici koja korisniku nije dodijeljena (`RequireGrantOrAssignedCompany`, ako odbija na tom temelju) | `companyId` |
| `InactiveUser` | `ActiveUserGuard` | — |
| `PermissionSafety` | `PermissionAdministrationSafetyService` (zadnji `permissions.manage`) — ako vraća 403 a ne 409/422 | — |

Konačni popis (svako mjesto i njegov `reason`) u T1 recordu nakon implementacije. Middleware ostaje jedino mjesto koje piše
odgovor; `ForbiddenAppException` dobiva `Reason` i `Details`.

## 6. T1-6 Točnost Swaggera

Nalazi (2026-10-09):
- **Nullable reference types nisu uključeni** u API/Core/Infrastructure (samo u testovima), pa Swagger ne zna je li `string` u
  DTO-u obavezan ili nullable. Prijedlog: uključiti `<Nullable>enable</Nullable>` samo u **Core** (DTO-ovi) i označiti DTO-ove;
  Swashbuckle `SupportNonNullableReferenceTypes`. Upozorenja u Infrastructureu se ne diraju u ovoj fazi.
- **Enumi:** JSON ih već serijalizira kao stringove (`JsonStringEnumConverter`), ali Swagger shema ih mora opisati kao string
  enum (provjeriti; po potrebi schema filter).
- **Datumi:** `DateOnly` → `format: date`, `DateTimeOffset` → `date-time`, `TimeOnly`/`TimeSpan` — provjeriti i popraviti.
- **Oblik greške:** `ErrorResponse` (+ `details` za 403 i ostale razloge odbijanja) i `warnings` opisani u shemi (`ProducesResponseType`
  za 400/403/404/409 globalno).
- **Postojeći nedosljedni tipovi**, npr. `AuthResponse.TokenExpiration` je `DateTime` (bez zone).
Popis promjena ide u T1 record i FE inventar.

## 7. Redoslijed
T1-1 (sat, testovi zeleni bez promjene ponašanja) → T1-3 → T1-2 → T1-4 → T1-5 → T1-6. Build i testovi nakon svakog koraka.

## 8. Dokumenti nakon faze
- ARCHITECTURE §7.4: ukloniti TimeProvider iz duga; dodati "testni alati (pomak sata, seed) — ukloniti prije go-livea" na popis
  "Prije produkcije"; §2 opis poslovnog sata.
- ADR: "Poslovni sat organizacije" (trajno) i oblik 403.
- Backend i frontend `CLAUDE.md`: pravilo poslovnog sata; `KONTEKST_ZA_CHAT.md`.

## 9. Otvoreno (pitanja uz plan)
- T1-Q1 Povratak na stvarno vrijeme = nova demo organizacija (stara deaktivirana)?
- T1-Q2 Pomak iz Management portala (ne iz tenant aplikacije)?
- T1-Q3 T1-5 i T1-6 u ovoj fazi?
- T1-Q4 `<Nullable>enable</Nullable>` u Core (DTO-ovi) radi točnog Swaggera?
- T1-Q5 Gornja granica jednog skoka (prijedlog 400 dana)?
