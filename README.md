# BlueDragon.DuneLight

Web API za vođenje manjeg fitness/wellness obrta koji posluje na više lokacija — zamjena za Excel tablice (raspored, evidencija usluga, blagajna, troškovnik, zaposlenici).

## Stack

- .NET 8 Web API, sloj: `Domain` / `Infrastructure` / `Api` (bez CQRS/MediatR/repository patterna nad EF-om)
- EF Core + PostgreSQL (mapiranje postojeće sheme; shema se gradi FluentMigrator migracijama, ne `dotnet ef migrations`)
- Swagger/OpenAPI
- Valuta: EUR, `decimal(10,2)`
- Multi-tenant: sve poslovne tablice imaju `organization_id`

## Struktura rješenja

| Projekt | Sadržaj |
|---|---|
| `BlueDragon.DuneLight.Core` | Enumi, DTO-ovi, servisni interfejsi, zajednički tipovi (`PagedResult`, `ErrorResponse`, custom exceptioni) |
| `BlueDragon.DuneLight.Infrastructure` | Entiteti (`Domain/Models`), `DatabaseContext`, Handler sloj (pristup bazi), Service sloj (poslovna logika) |
| `BlueDragon.DuneLight.API` | Kontroleri, autentikacija, middleware, `Startup.cs` (DI) |
| `BlueDragon.DuneLight.DatabaseMigration` | Samostalni FluentMigrator runner (konzolna aplikacija) |

Uzorak po entitetu: `IXxxHandler`/`XxxHandler` (čisti pristup bazi, nova instanca `DatabaseContext` po pozivu) → `IXxxService`/`XxxService` (poslovna logika, validacije, DTO mapiranje) → `XxxController` (tanki, `[Authorize(Roles=...)]`).

## Pokretanje

```bash
dotnet build BlueDragon.DuneLight.sln
dotnet run --project BlueDragon.DuneLight.API
```

Swagger UI dostupan je u razvoju na `/swagger` kad je `ASPNETCORE_ENVIRONMENT=Development`.

## Migracije

```bash
dotnet run --project BlueDragon.DuneLight.DatabaseMigration -- --c=Local
```

Dostupne nazvane konfiguracije: `Local`, `Development`, `Production` (`DatabaseMigration/Configuration/DatabaseConfiguration.cs`). Alternativno, proslijedi vlastiti connection string: `-- --d=PostgreSQL --s="Host=...;Database=...;Username=...;Password=..."`.

Migracije se pokreću redom po `[DeveloperMigration(godina, mjesec, dan, autor, redni_broj)]` atributu (`DatabaseMigration/Extensions/DeveloperMigrationAttribute.cs`) — nema `dotnet ef migrations add/update`.

## Auth

Prijava: `POST /api/public/Auth/Register` (kreira Organizaciju + prvog Admin korisnika, `409 AUTH_ORGANIZATION_SLUG_TAKEN` ako naziv organizacije već postoji), `POST /api/public/Auth/Login` (`401 AUTH_INVALID_CREDENTIALS` ako podaci ne odgovaraju aktivnom korisniku). JWT ili `X-Api-Key` header nose `organizationId` i `role` claim. Auth rute vraćaju greške u istom obliku kao svi ostali moduli — vidi [Jedinstveni format greške](#jedinstveni-format-greške).

### Uloge

`UserRole` enum (`Admin`/`Member`/`Reception`) — nazivi članova su namjerno identični claim-stringovima (`UserRoleClaims`), nema prijevoda (stariji `Owner`/`Receptionist`/"Trener" nazivi su ranije napušteni). `[Authorize(Roles = "Admin")]` itd. gleda tu istu claim vrijednost, ne enum izravno.

### Deaktivacija korisničkog računa

`User.IsActive` (default `true`) provjerava se u `AuthService.Login` i `ApiKeyAuthenticationHandler` — deaktiviran korisnik se ne može prijaviti niti koristiti API ključ. Već izdani JWT tokeni ostaju važeći do isteka (nema trenutne revokacije sesije).

### Promjena lozinke

`POST /api/public/Auth/ChangePassword` (`[Authorize]`, bilo koja rola) — prijavljeni korisnik mijenja vlastitu lozinku; `userId` se uzima iz tokena, ne iz tijela zahtjeva. Vraća `401 AUTH_CURRENT_PASSWORD_INVALID` ako trenutna lozinka ne odgovara (isti kôd i za "korisnik ne postoji/nije aktivan" — namjerno, da se ne otkriva status naloga). Nema „prisili promjenu pri prvoj prijavi" ni „admin resetira tuđu lozinku" — izvan opsega.

```json
// Request
{ "currentPassword": "staraLozinka1", "newPassword": "novaLozinka1" }

// 200 OK (bez tijela)

// 401 Unauthorized
{ "error": { "code": "AUTH_CURRENT_PASSWORD_INVALID", "message": "Trenutna lozinka nije ispravna." } }
```

## Jedinstveni format greške

Sve greške (validacijske, poslovne, auth, framework-level 401/403) vraćaju se u istom obliku (`ExceptionHandlingMiddleware` za sve module + `Auth` rute; `ConfigureApiBehavior` za automatsku `[ApiController]` model-validaciju):

```json
{ "error": { "code": "PRICE_OVERLAP", "message": "Čitljiva poruka (fallback za prikaz, frontend i18n se veže na code).", "details": { "polje": ["detalji po polju, samo kod VALIDATION_ERROR"] } } }
```

`code` je stabilan ugovor — vrijednosti se ne mijenjaju, nove greške dobivaju nov, specifičan kôd (ne generički bucket). Jedini izvor istine: `BlueDragon.DuneLight.Core.Shared.ErrorCodes`.

| Kôd | HTTP | Značenje |
|---|---|---|
| `VALIDATION_ERROR` | 400 | Neispravan zahtjev (model-validacija ili poslovna validacija). `details` nosi poruke po polju. |
| `NOT_FOUND` | 404 | Traženi zapis ne postoji (bilo koji entitet — entitet/id su u `message`, ne u `code`). |
| `UNAUTHORIZED` | 401 | Framework-level: nedostaje ili je istekao/nevažeći token (nema ga u iznimkama ispod). |
| `FORBIDDEN` | 403 | Framework-level: prijavljeni korisnik nema potrebnu ulogu za radnju (`[Authorize(Roles=...)]`). |
| `INTERNAL_ERROR` | 500 | Neuhvaćena/neočekivana iznimka. |
| `AUTH_INVALID_CREDENTIALS` | 401 | Login — organizacija/e-mail/lozinka ne odgovaraju aktivnom korisniku. |
| `AUTH_ORGANIZATION_SLUG_TAKEN` | 409 | Register — organizacija s izvedenim nazivom (slug) već postoji. |
| `AUTH_CURRENT_PASSWORD_INVALID` | 401 | ChangePassword — trenutna lozinka nije ispravna (ili korisnik ne postoji/nije aktivan). |
| `REFERENCED_CANNOT_DELETE` | 409 | Trajno brisanje odbijeno jer je zapis referenciran drugdje — deaktivirati umjesto brisati. Koristi se kroz sve module (Katalog, Zaposlenici, Klijenti, Roster...). |
| `DUPLICATE_NAME` | 409 | Aktivan zapis s istim nazivom već postoji (šifrarnici: vrste angažmana, oznake klijenta, kategorije/usluge, vrste rostera). |
| `DUPLICATE_MEMBER_NUMBER` | 409 | Broj člana klijenta je već zauzet. |
| `EMAIL_ALREADY_IN_USE` | 409 | E-mail već postoji kao korisnički račun u organizaciji (`POST /api/employees/with-login`). |
| `USER_ALREADY_LINKED` | 409 | Odabrani korisnički račun je već povezan s drugim zaposlenikom. |
| `LAST_ACTIVE_ADMIN` | 409 | Pokušaj deaktivacije ili promjene uloge zadnjeg aktivnog Admina. |
| `LAST_ACTIVE_LOCATION` | 409 | Pokušaj deaktivacije zadnje aktivne lokacije. |
| `LAST_ACTIVE_SLOT` | 409 | Pokušaj deaktivacije zadnjeg aktivnog slota grupe. |
| `ALREADY_MEMBER` | 409 | Klijent je već aktivan član grupe. |
| `ALREADY_COMPLETED` | 409 | Termin je već označen kao odrađen. |
| `SAME_DAY_ONLY` | 409 | Trajno brisanje termina dopušteno samo isti dan unosa. |
| `NOT_OWNER` | 409 | Trener pokušava upravljati terminom/grupnim terminom/roster-zapisom koji nije njegov. |
| `PACKAGE_NOT_ELIGIBLE` | 409 | Odabrani paket nije valjan za klijenta ili ne pokriva uslugu (termini, grupna prisutnost). |
| `PACKAGE_SERVICE_NOT_COVERED` | 409 | Odabrani paket ne pokriva traženu uslugu (trošenje ulaska). |
| `CATEGORY_IN_USE` | 409 | Kategorija usluge se koristi na aktivnim uslugama — ne može se deaktivirati. |
| `INACTIVE_EMPLOYEE` | 409 | Roster zapis referencira neaktivnog zaposlenika. |
| `INACTIVE_TYPE` | 409 | Roster zapis referencira neaktivnu vrstu rostera. |
| `PRICE_OVERLAP` | 409 | Preklapanje razdoblja aktivnih stavki cjenika za istu kombinaciju usluge/paketa i lokacije. |
| `CLIENT_ANONYMIZED` | 409 | Klijent je anonimiziran (GDPR) i više se ne može uređivati. |
| `APPOINTMENT_OVERLAP` | 409 | Trener ili klijent već ima drugi termin koji se preklapa s novim/izmijenjenim terminom (`schedule`, `complete`, `{id}/complete`, `PUT {id}`, `{id}/move`). |

## Modul Katalog

Lokacije, kategorije usluga, usluge, cjenik, paketi. Sve pisanje `Admin`, čitanje `Admin,Member`.

| Endpoint | Opis |
|---|---|
| `GET/POST/PUT/DELETE /api/catalog/locations`, `PATCH .../{id}/activate`\|`deactivate` | Lokacije. Zadnja aktivna lokacija se ne može deaktivirati. |
| `GET/POST/PUT/DELETE /api/catalog/service-categories`, `PATCH .../{id}/activate`\|`deactivate` | Konfigurabilne kategorije usluga (naziv, način izvođenja Individual/Group, boja...). |
| `GET/POST/PUT/DELETE /api/catalog/services`, `PATCH .../{id}/activate`\|`deactivate` | Usluge (naziv, kategorija, trajanje, zadana cijena). |
| `GET/POST/PUT/DELETE /api/catalog/price-list`, `PATCH .../{id}/activate`\|`deactivate` | Stavke cjenika (usluga ili paket × lokacija-ili-sve × razdoblje). Zabranjeno preklapanje razdoblja za istu kombinaciju. |
| `GET /api/catalog/price-list/effective?locationId=&date=` | Trenutno važeći cjenik za lokaciju (pregledni prikaz). |
| `GET /api/catalog/price-list/resolve?subjectType=&subjectId=&locationId=&date=` | Razriješena cijena — algoritam: lokacija → sve lokacije → zadana cijena. |
| `GET/POST/PUT/DELETE /api/catalog/packages`, `PATCH .../{id}/activate`\|`deactivate` | Paketi (SharedPool/PerService ulasci, DayCount/EndOfMonth/FixedDate valjanost). |

## Modul Zaposlenici

Tko su zaposlenici, gdje rade, koje usluge izvode, kakav im je korisnički račun. **Bez obračuna naknada** — samo informativna bilješka; stvarne isplate ide se ručno u budući financijski modul.

Sve pisanje `Admin`, čitanje `Admin` — jedina iznimka je pogled za kolege.

| Endpoint | Opis |
|---|---|
| `GET/POST/PUT/DELETE /api/employees/engagement-types`, `PATCH .../{id}/activate`\|`deactivate` | Šifrarnik vrsta angažmana (puno radno vrijeme, pola radnog vremena, honorarno, vanjski suradnik). |
| `GET/POST/PUT/DELETE /api/employees`, `PATCH .../{id}/activate`\|`deactivate` | Zaposlenici — puni admin prikaz. |
| `PATCH /api/employees/{id}/role` | Promjena uloge povezanog korisničkog računa (`{ "role": "Admin" \| "Member" \| "Reception" }`). |
| `GET /api/employees/directory` | **Ograničeni pogled za kolege** (`Admin,Member,Reception`) — samo ime, prezime, boja, lokacije, aktivnost. Zaseban DTO (`EmployeeDirectoryDto`), ne filtriranje punog zapisa. |
| `POST /api/employees/with-login` | **Kreira korisnički račun i zaposlenika zajedno**, u jednoj transakciji (`Admin`) — za slučaj kad zaposlenik još nema login (obični `POST /api/employees` i dalje zahtijeva postojeći `UserId`). Vidi primjer ispod. |
| `GET /api/employees/me` | **"Tko sam ja"** (bilo koja rola) — zaposlenik povezan s prijavljenim korisnikom; `404` ako prijavljeni korisnik nema povezanog zaposlenika (npr. čisti Admin bez Employee zapisa). |

#### `POST /api/employees/with-login` — primjer

Spaja polja `EmployeeCreateRequest`-a (bez obaveznog `UserId` — korisnik se stvara ovdje) s login poljima (`password`, `role`). `email` je jedno zajedničko obavezno polje — koristi se i kao kontakt i kao login e-mail, jedinstven unutar organizacije. Ako bilo koji korak (validacija, stvaranje korisnika, stvaranje zaposlenika) padne, ništa se ne sprema.

```json
// Request
{
  "firstName": "Ana", "lastName": "Anić", "phone": "+385911234567",
  "email": "ana@dunelight.local",
  "employmentStartDate": "2026-07-21T00:00:00Z",
  "engagementTypeId": "…",
  "locationIds": ["…"], "primaryLocationId": "…",
  "password": "lozinka123", "role": "Member"
}

// 201 Created
{ "employeeId": "…", "userId": "…", "email": "ana@dunelight.local", "role": "Member" }
```

`role` prima enum vrijednosti `Admin`/`Member`/`Reception` — ista vrijednost se vraća u odgovoru, nema prijevoda (isto kao `PATCH .../role`). `409 EMAIL_ALREADY_IN_USE` ako email već postoji u organizaciji.

#### `GET /api/employees/me` — primjer

```json
// 200 OK
{
  "employeeId": "…", "firstName": "Marko", "lastName": "Trener", "role": "Member", "colorHex": "#3498db",
  "locations": [{ "locationId": "…", "locationName": "Centar", "isPrimary": true }]
}

// 404 Not Found (prijavljeni korisnik nema povezanog zaposlenika)
{ "error": { "code": "NOT_FOUND", "message": "Employee with id '…' was not found." } }
```

### Model

- Zaposlenik radi na više lokacija (`EmployeeLocation`, više-na-više), točno jedna je matična (`IsPrimary`) — DB-razina jamči i preko partial unique indeksa.
- Popis usluga koje smije izvoditi je opcionalan (`EmployeeService`) — prazan popis = smije sve.
- Svaki zaposlenik ima obavezan, jedinstven `UserId` — uloga živi na `User.Role`, ne duplicira se na zaposleniku.
- Deaktivacija zaposlenika automatski postavlja `User.IsActive = false` (onemogućuje login). Mora postojati barem jedan aktivan Admin — deaktivacija/promjena uloge zadnjeg admina je blokirana.
- `EmployeeAuditLog` bilježi tko/kada/prethodnu vrijednost za promjene uloge i statusa (aktivan/neaktivan); postojanje zapisa blokira tvrdo brisanje zaposlenika (dopuštena je samo deaktivacija).
- `IFutureAppointmentsProvider` (`FutureAppointmentsProvider`) provjerava ima li zaposlenik zakazane buduće termine (`Status=Scheduled`) — koristi se za upozorenje (ne zabranu) pri deaktivaciji.

### Minimalni seed podaci

Migracija `SeedEmployeesMinimal` kreira demo organizaciju, 2 lokacije, 4 vrste angažmana i 3 zaposlenika za ručno testiranje kroz Swagger (lozinka za sve: `password123`):

| Uloga | E-mail | Napomena |
|---|---|---|
| Admin | `admin@dunelight.local` | Vlasnica, lokacija Centar |
| Member | `marko@dunelight.local` | Lokacija Centar |
| Member | `iva@dunelight.local` | Vanjski suradnik, lokacija Riverside |

## Modul Klijenti

Evidencija klijenata (zamjena Excel baze korisnika).

Pristup se namjerno razlikuje od ostalih modula: **svi treneri (i recepcija) vide sve klijente**, uključivo zdravstvenu napomenu i kontakt — matični trener je čisto informativan (redoslijed prikaza), ne pristupno ograničenje, jer kolega mora moći preuzeti klijenta kad je matični trener odsutan.

| Endpoint | Opis |
|---|---|
| `GET/POST/PUT /api/clients/tags`, `PATCH .../{id}/activate`\|`deactivate`, `DELETE` | Šifrarnik oznaka klijenta (naziv, boja). Čitanje `Admin,Member,Reception`, pisanje `Admin`. |
| `GET/POST/PUT /api/clients`, `PATCH .../{id}/activate`\|`deactivate`, `DELETE` | Klijenti. Čitanje i kreiranje/uređivanje `Admin,Member,Reception`; (de)aktivacija i brisanje `Admin`. |
| `GET /api/clients?mineFirst=true` | Popis klijenata — kad je `mineFirst=true`, klijenti prijavljenog trenera (matični) idu prvi (redoslijed, ne filter). Filtri: `tagId`, `homeTrainerId`, `homeLocationId`; `search` pretražuje ime/prezime/telefon/e-mail. |
| `GET /api/clients/birthdays?from=&to=` | Klijenti kojima je rođendan u zadanom razdoblju, sortirano po datumu — izvedeno iz `DateOfBirth`, bez obzira na godinu (uklj. prijelaz preko Nove godine). |
| `GET /api/clients/next-member-number` | Prijedlog sljedećeg slobodnog broja člana (broj člana se inače ručno unosi, radi uvoza postojećih Excel brojeva). |
| `POST /api/clients/{id}/anonymize` | **GDPR pravo na zaborav** (`Admin`) — nepovratno uklanja osobne/zdravstvene podatke i oznake, čuva `Id`/broj člana radi referencijalnog integriteta. Idempotentno. |

### Model

- `MemberNumber` (broj člana) je jedinstven po organizaciji i unosi se ručno — nema auto-generiranja, radi uvoza postojećih Excel brojeva.
- `HomeLocationId`/`HomeTrainerId` su opcionalni i čisto informativni — ne ograničavaju tko vidi klijenta.
- `ClientTag` veza je više-na-više (`ClientTagAssignment`), isti obrazac kao kategorije usluga u Katalogu.
- Anonimizirani klijent (`IsAnonymized = true`) se više ne može uređivati niti reaktivirati (`CLIENT_ANONYMIZED`).
- `IClientFutureActivityProvider` (`ClientFutureActivityProvider`) blokira tvrdo brisanje klijenta ako je ikad referenciran — bilo koji termin (`AppointmentClient`) ili prodani paket (`ClientPackage`).

### Minimalni seed podaci

Migracija `SeedClientsMinimal` dodaje 3 oznake (VIP, Radionice, Foto/video pristanak) i 4 klijenta pod istom demo organizacijom: Ivana Kovač (zdravstvena napomena, matična lokacija/trener), Petar Perić (oznaka VIP, matična lokacija/trener), Ana Anić (bez matičnog trenera/lokacije), Marko Novak (oznaka Radionice, bez matičnog trenera).

## Modul Prodani paketi klijenta

Instanca paketa kupljena od strane klijenta (`ClientPackage`) — snapshotira strukturu kataloškog `Package`-a u trenutku kupnje (način trošenja, broj ulazaka, valjanost) tako da kasnija promjena definicije paketa u Katalogu ne utječe na već prodane instance. Nužan preduvjet za plaćanje termina iz paketa (vidi modul Termini niže).

| Endpoint | Opis |
|---|---|
| `GET /api/clients/{clientId}/packages` | Svi prodani paketi klijenta, najnoviji prvi. |
| `GET /api/clients/{clientId}/packages/{id}` | Detalj jednog prodanog paketa (uklj. stanje ulazaka po usluzi). |
| `GET /api/clients/{clientId}/packages/eligible?serviceId=&date=` | Aktivni paketi klijenta koji pokrivaju uslugu i imaju preostalih ulazaka (ili su neograničeni) na dani datum — za odabir kod plaćanja termina. |
| `POST /api/clients/{clientId}/packages` | Kupnja paketa. Cijena se predlaže preko `IPricingService.ResolvePrice` ako nije ručno zadana. |

Čitanje/kreiranje `Admin,Member,Reception`. Nema Update/Delete — paket se nakon kupnje mijenja isključivo kroz trošenje (skidanje/vraćanje ulaska), ne ručnim uređivanjem.

### Model

- Snapshot polja: `EntryMode`, `TotalEntryCount`, `ValidityType`, `ExpiryDate` (izračunat jednom preko `PackageExpiryCalculator`) — kopirana iz `Package`-a u trenutku kupnje, zatim neovisna o njemu.
- `ClientPackageServiceEntry` — jedan red po usluzi iz izvornog paketa, i za SharedPool i za PerService način: kod SharedPool-a red služi samo kao marker "usluga je pokrivena" (brojanje ide preko `ClientPackage.RemainingSharedEntries`), kod PerService-a nosi vlastiti `RemainingEntries` brojač.
- `Status`: `Active`/`Cancelled` su jedina eksplicitno postavljana stanja; `Depleted` se postavlja event-driven kod skidanja zadnjeg ulaska; `Expired` se ne perzistira (provjerava se dinamički preko `ExpiryDate`, nema background joba).

## Modul Termini i raspored

Središnji modul — iz termina se izvode raspored, evidencija dolazaka i (kasnije) financije. Zamjenjuje Excel "Tjedni raspored". Individualni termini se kreiraju/uređuju izravno kroz ovaj modul. **Grupni termini (`Form=Group`) se ne kreiraju ovdje** — generiraju se iz rasporeda grupe preko modula Grupe (`POST /api/groups/generate-appointments`); ovaj modul i dalje nosi zajedničke podatke (`Appointment`, `AppointmentAttendance`) i zajedničke akcije nad pojedinim terminom (otkazivanje, no-show, trajno brisanje istog dana) bez obzira na oblik.

Svi (Admin i Member) vide sve termine. Member smije kreirati/mijenjati/otkazivati samo termine na kojima je on sam trener; Admin nema ograničenja.

| Endpoint | Opis |
|---|---|
| `GET /api/appointments/schedule?from=&to=&locationId=&employeeId=&serviceId=&serviceCategoryId=&status=` | Raspored za razdoblje. `locationId` zadano = sve lokacije. Uključuje otkazane/no-show termine (za precrtani prikaz). Ćelija nosi `form`/`groupId`/`groupName`/`attendanceCount`/`expectedCount` za grupne termine (vidi Model ispod) — `clientNames` je prazan za grupne, frontend prikazuje `groupName` umjesto imena klijenata. |
| `GET /api/appointments/{id}` | Puni detalj termina (za klik na ćeliju rasporeda). |
| `GET /api/appointments/by-client/{clientId}` | Povijest termina klijenta, paginirano, najnoviji prvi. |
| `POST /api/appointments/schedule` | **"Zakaži"** — status `Scheduled`, bez naplate. Cijena/trajanje se predlažu iz kataloga i snapshotiraju na termin. `409 APPOINTMENT_OVERLAP` za preklapanje. |
| `POST /api/appointments/complete` | **"Upiši odrađeno"** — novi termin odmah u statusu `Completed`, naplata odmah (`PaymentMethod` obavezan). `409 APPOINTMENT_OVERLAP` za preklapanje. |
| `PATCH /api/appointments/{id}/complete` | Prijelaz postojećeg (obično `Scheduled`) termina u `Completed` — trenutak naplate za termine zakazane unaprijed. `409 APPOINTMENT_OVERLAP` za preklapanje. |
| `PUT /api/appointments/{id}` | Izmjena vremena/usluge/trenera/lokacije/klijenata/napomene/iznosa. Ne dira plaćanje/paket. `409 APPOINTMENT_OVERLAP` za preklapanje. |
| `PATCH /api/appointments/{id}/move` | **Brzo pomicanje** (drag-and-drop na rasporedu) — mijenja samo `startsAt` i po potrebi `employeeId`/`locationId`; sve ostalo (usluga, klijenti, iznos, napomena, plaćanje, paket) ostaje netaknuto. `409 APPOINTMENT_NOT_MOVABLE` za `Cancelled`/`NoShow` termine, `409 APPOINTMENT_OVERLAP` za preklapanje. Vidi primjer ispod. |
| `POST /api/appointments/{id}/cancel` | Otkazivanje (`Status=Cancelled`, termin ostaje vidljiv precrtan). `ReturnEntryForClientIds` — eksplicitni popis klijenata kojima se vraća skinuti ulazak (ništa se ne vraća automatski). |
| `POST /api/appointments/{id}/no-show` | Isto kao cancel, `Status=NoShow` — zaseban status, isti prompt/mehanizam za vraćanje ulaska. |
| `DELETE /api/appointments/{id}` | Trajno brisanje — samo `Admin`, samo ako je termin unesen isti dan (pogrešan unos); inače koristiti otkazivanje. |
| `POST /api/appointments/recurring` | Generira niz individualnih termina (isti klijent(i)/usluga/trener/lokacija/vrijeme) do `EndDate` — `recurrenceType`: `Weekly` (+7 dana) ili `Daily` (svaki kalendarski dan, uključivo vikend). Izmjena/otkaz pojedinačnog termina iz niza ne dira ostale. **Tvrda unaprijedna provjera** (samo ovaj endpoint, vidi napomenu u Model ispod): ako se ijedan datum iz niza sudara s postojećim terminom trenera ili roster odsutnošću, cijeli zahtjev pada s `409 RECURRING_CONFLICT`, ništa se ne sprema. |

#### `PATCH /api/appointments/{id}/move` — primjer

`employeeId`/`locationId` su opcionalni — šalju se samo ako se stvarno mijenjaju (npr. drag-and-drop na drugi red/lokaciju u rasporedu); izostavljeni = nepromijenjeni. `startsAt` je uvijek obavezan. Trajanje termina se **ne** mijenja (ostaje snapshot iz trenutka kreiranja) jer usluga nije dio zahtjeva.

```json
// Request
{
  "startsAt": "2026-07-23T10:00:00+02:00",
  "employeeId": "…",
  "locationId": "…"
}

// 200 OK — puni AppointmentDto
{
  "id": "…", "startsAt": "2026-07-23T10:00:00+02:00", "durationMinutes": 60,
  "serviceId": "…", "serviceName": "Individualni trening",
  "employeeId": "…", "employeeName": "Marko Trener",
  "locationId": "…", "locationName": "Centar",
  "amount": 20.00, "suggestedAmount": 20.00, "isAmountManuallyOverridden": false,
  "paymentMethod": null, "isPaid": false, "status": "Scheduled", "note": null,
  "clients": [{ "clientId": "…", "clientName": "Ivana Kovač", "clientPackageId": null, "packageEntryDeducted": false, "packageEntryReturned": false }],
  "warnings": [],
  "createdAt": "…", "updatedAt": "…"
}

// 409 Conflict — termin je otkazan/no-show
{ "error": { "code": "APPOINTMENT_NOT_MOVABLE", "message": "Otkazan ili izostao termin se ne može pomicati." } }

// 409 Conflict — preklapanje (trener ili klijent već zauzet u tom vremenu)
{ "error": { "code": "APPOINTMENT_OVERLAP", "message": "Trener već ima termin u ovom vremenskom razdoblju.", "details": null } }
// ili, ako je problem klijent:
{ "error": { "code": "APPOINTMENT_OVERLAP", "message": "Klijent Ana Anić je već zakazan u ovom vremenskom razdoblju.", "details": null } }
```

Vlasništvo (ne-admin smije pomicati samo svoje termine → `409 NOT_OWNER`) i postojanje `employeeId`/`locationId` (`404 NOT_FOUND`) provjeravaju se isto kao kod `PUT /{id}`. Preklapanje (trener/klijenti) se provjerava **prije spremanja** i blokira kao `409 APPOINTMENT_OVERLAP` — vidi napomenu o preklapanju u sekciji Model ispod. Trener se provjerava prvi; ako trener nema preklapanje, provjeravaju se klijenti redom i vraća se prva pronađena poruka (ne oboje odjednom).

#### `POST /api/appointments/recurring` — primjer

```json
// Request — Daily, uključivo vikend
{
  "recurrenceType": "Daily",
  "serviceId": "…",
  "employeeId": "…",
  "locationId": "…",
  "clientIds": ["…"],
  "firstOccurrenceStartsAt": "2026-08-03T17:00:00+02:00",
  "endDate": "2026-08-09T17:00:00+02:00",
  "note": null
}

// 200 OK — niz od 7 AppointmentDto (3.8. - 9.8., svaki dan uklj. subotu/nedjelju), isti recurrenceGroupId

// 409 Conflict — barem jedan datum u nizu se sudara; NIŠTA nije spremljeno
{
  "error": {
    "code": "RECURRING_CONFLICT",
    "message": "Neki termini u nizu se sudaraju s postojećim obavezama.",
    "details": {
      "conflicts": [
        { "date": "2026-08-04T17:00:00+02:00", "reason": "EXISTING_APPOINTMENT" },
        { "date": "2026-08-07T17:00:00+02:00", "reason": "ROSTER_ABSENCE" }
      ]
    }
  }
}
```

`details.conflicts` nabraja **svaki** sudarajući datum iz generiranog niza (ne samo prvi) — `reason` je `EXISTING_APPOINTMENT` (trener već ima termin, jednokratni ili ponavljajući) ili `ROSTER_ABSENCE` (godišnji/bolovanje i sl. na taj datum). Ako datum ima oba problema, prijavljuje se `EXISTING_APPOINTMENT`. Otkazani/no-show termini se ne računaju kao sudar.

### Model

- Zajednička polja: `StartsAt` (UTC, proizvoljno vrijeme), `DurationMinutes` (snapshot iz `Service.DefaultDurationMinutes`), `Amount`/`SuggestedAmount`/`IsAmountManuallyOverridden` (predložena cijena iz `IPriceResolutionService`, ručna izmjena se bilježi), `PaymentMethod`, `IsPaid`, `Status` (`Scheduled`/`Completed`/`Cancelled`/`NoShow`), `Note`.
- Individualni termin dopušta jednog ili više klijenata (`AppointmentClient`, npr. par/duo trening) — naplata (`Amount`/`PaymentMethod`) je i dalje jedna po terminu. Duo/par varijante se rješavaju odabirom druge usluge iz kataloga (npr. "Individualni trening u paru"), ne posebnom logikom.
- **Plaćanje iz paketa je po klijentu, ne po terminu**: kod duo termina svaki klijent skida ulazak iz svog vlastitog `ClientPackage`-a, neovisno o ostalima na istom terminu (`AppointmentClient.ClientPackageId`/`PackageEntryDeducted`). Kad je `PaymentMethod=Package`, zahtjev mora sadržavati odabir paketa za svakog klijenta na terminu (`PackageSelections`), svaki provjeren protiv `GET .../packages/eligible`.
- Preklapanja (trener već zauzet / klijent već zakazan u to vrijeme) su **tvrda greška** — provjeravaju se prije spremanja na svih pet mutirajućih endpointa (`schedule`, `complete`, `{id}/complete`, `PUT {id}`, `{id}/move`) i blokiraju spremanje s `409 APPOINTMENT_OVERLAP` ako se dogode; termin se u tom slučaju uopće ne sprema. Kod izmjene/pomicanja postojećeg termina, taj isti termin se isključuje iz vlastite provjere preklapanja. Otkazani i no-show termini se ne računaju kao preklapanje — na istom slotu smiju postojati i otkazani i novi termin. `AppointmentDto.Warnings` polje ostaje u DTO-u (za eventualnu buduću upotrebu), ali se više ne puni preklapanjem — uvijek prazno.
- **`POST /recurring` ima zaseban, stroži mehanizam** koji NE dira gornju provjeru na pojedinačnim endpointima: prije bilo kakvog spremanja generira se cijeli niz datuma (`Daily`/`Weekly`) i za SVAKI datum se provjerava (a) preklapanje s postojećim terminom trenera (jednokratnim ili ponavljajućim, isto pravilo isključivanja otkazanih/no-show) i (b) preklapanje s roster odsutnošću trenera (`RosterType.IsAbsence`) na taj datum. Ako ijedan datum ima sudar, baca se `409 RECURRING_CONFLICT` s popisom SVIH sudarajućih datuma (`details.conflicts`, vidi primjer iznad) i **ništa se ne sprema** — ni termini bez sudara. Tek ako nijedan datum nema sudar, cijeli niz se generira odjednom, svi u `Scheduled`.
- Nema vremenskih ograničenja unosa — termini se kreiraju/mijenjaju slobodno u prošlost i budućnost.
- Bez pravog brisanja osim istog dana unosa — inače samo promjena statusa; termin nikad ne nestaje iz rasporeda.
- `AppointmentAuditLog` bilježi tko/kada za ručnu izmjenu iznosa i vraćanje ulaska iz paketa (isti obrazac kao `EmployeeAuditLog`) — kod grupnih termina isti zapis bilježi i automatsko vraćanje ulaska pri poništavanju prisutnosti (vidi modul Grupe).
- `EmployeeId` je nullable — grupni termin generiran iz grupe bez zadanog trenera nema dodijeljenog trenera dok se ručno ne postavi po terminu. Individualni termin uvijek ima trenera (poslovno pravilo u `AppointmentService`, ne ograničenje baze).
- Grupni termin (`Form=Group`) nema vlastiti iznos (`Amount`/`SuggestedAmount`=0, `IsPaid`=false) — naplata ide kroz pakete članova na razini `AppointmentAttendance` (vidi modul Grupe).
- `AppointmentScheduleCellDto` nosi `form`, i za grupne termine (`form="Group"`): `groupId`, `groupName` (za prikaz umjesto imena klijenta — `clientNames` je za njih uvijek prazan, grupni termini nemaju `AppointmentClient` retke), `attendanceCount` (broj `AppointmentAttendance.Attended=true` redaka za taj termin) i `expectedCount` (broj trenutno aktivnih članova grupe — `GroupMember.IsActive`, nije snapshot na dan termina). Za individualne termine su `groupId`/`groupName`/`attendanceCount`/`expectedCount` svi `null`.

### Minimalni seed podaci

Migracija `SeedCatalogForAppointments` dodaje minimalni katalog (kategorija "Individualni treninzi", usluge "Individualni trening"/"Individualni trening u paru", paket "Paket 10 individualnih" — SharedPool, 10 ulazaka, 90 dana), jer Katalog dotad nije imao seed podataka. `SeedAppointmentsMinimal` zatim dodaje:

- Jedan prodani paket (Ivana Kovač, "Paket 10 individualnih", 9/10 preostalih ulazaka).
- 6 individualnih termina kroz tekući tjedan, na obje lokacije, kod oba trenera: 3× `Scheduled`, 2× `Completed` (jedan plaćen gotovinom, jedan iz paketa), 1× `Cancelled`.
- Jedan termin s dva klijenta (Ivana + Petar, usluga "Individualni trening u paru").

## Modul Grupe

Aktivira grupni oblik termina: definicija grupa, generiranje grupnih termina iz tjednog rasporeda grupe, evidencija prisutnosti s pokrićem iz paketa. Zamjenjuje Excel "Evidencija po grupama". Izvan opsega: roster i financije/obračun — naplata gosta bez paketa ide kroz postojeći mehanizam (usluga/paket "grupni trening x1"), ne gradi se posebna logika.

Svi (Admin i Member) vide sve grupe/termine/prisutnost. Uređivanje definicije grupe, slotova i članova je isključivo `Admin`. Čekiranje prisutnosti je `Admin,Member` — Member smije samo na terminima gdje je on dodijeljeni trener (vlasništvo se provjerava po terminu, ne po grupi, jer se trener po terminu može mijenjati).

| Endpoint | Opis |
|---|---|
| `GET /api/groups?isActive=` | Popis grupa. |
| `GET /api/groups/{id}` | Detalj — slotovi, aktivni članovi, nadolazeći i protekli termini. |
| `POST/PUT /api/groups`, `PATCH .../{id}/activate`\|`deactivate` | Definicija grupe (naziv, usluga, lokacija, kapacitet, zadani trener, napomena). Kreiranje zahtijeva barem jedan slot. Deaktivacija ne dira već generirane termine, zaustavlja samo buduće generiranje. |
| `POST/PUT/DELETE /api/groups/{id}/slots[/{slotId}]` | Tjedni slotovi (dan + vrijeme). `DELETE` je zapravo deaktivacija (nema tvrdog brisanja) — blokirana ako je posljednji aktivan slot grupe. Izmjena dana/vremena ne dira već generirane termine. |
| `POST/DELETE /api/groups/{id}/members[/{memberId}]` | Članovi grupe. Kapacitet je upozorenje, ne zabrana — dodavanje preko kapaciteta vraća `Warnings`, ali se dopušta. `DELETE` deaktivira članstvo (čuva `JoinedAt` povijest). |
| `POST /api/groups/generate-appointments` | **Idempotentno** generiranje grupnih termina za zadani raspon — `GroupId=null` generira za sve aktivne grupe. Ponovno pokretanje za isti raspon ne stvara duplikate. |
| `GET/POST /api/groups/appointments/{appointmentId}/attendance` | Evidencija prisutnosti na jednom grupnom terminu. `GET` vraća `Expected` (aktivni članovi bez zabilježenog retka) i `Recorded`. `POST` čekira/poništava jednog klijenta (član ili gost izvan popisa). |
| `GET /api/clients/{clientId}/groups` | Grupe čiji je klijent član, aktivno i povijesno — dopuna Klijent detalja. |

### Model

- `Group` traje neograničeno (nema datuma kraja). Trener **nije** dio identiteta grupe — `DefaultTrainerId` je samo prijedlog koji se snapshotira na generirani termin (`Appointment.EmployeeId`) i mijenja se po terminu (zamjene) bez diranja grupe; smije biti prazan, tada se termin generira bez trenera i dodjeljuje se ručno.
- `GroupSlot` (dan u tjednu + vrijeme) — soft-delete (`IsActive`), nikad tvrdo brisanje, jer generirani `Appointment.GroupSlotId` na njega referencira. Izmjena/deaktivacija slota ne dira već generirane buduće termine (isto načelo kao promjena trenera na terminu) — utječe samo na sljedeća pokretanja generiranja. Grupa mora imati barem jedan aktivan slot.
- `GroupMember` — "tko normalno dolazi", odvojeno od stvarnog dolaska (`AppointmentAttendance`). Soft-delete kod napuštanja grupe (čuva `JoinedAt`); ponovni upis nakon napuštanja dopušten (partial unique index na `(GroupId, ClientId) WHERE IsActive`).
- **Idempotentno generiranje**: `Appointment.GroupSlotId` (FK na `GroupSlot`) je eksplicitna poveznica "koji je slot generirao ovaj termin" — provjera duplikata je `(GroupSlotId, StartsAt)`, dodatno osigurano unique indeksom na razini baze. Datum/vrijeme termina računa se iz `FromDate` requesta (nosi offset) + dan u tjednu i vrijeme slota; usluga/trajanje/lokacija/zadani trener se snapshotiraju iz grupe u trenutku generiranja.
- Grupni termin nema vlastiti iznos (`Amount`/`SuggestedAmount`=0, `IsPaid`=false, `Status` ostaje `Scheduled` — nema "odrađeno" prijelaza kao kod individualnog) — naplata ide kroz pakete članova na razini `AppointmentAttendance`. Pauze/praznici/otkazivanje pojedinog termina idu kroz postojeći mehanizam otkazivanja (`POST /api/appointments/{id}/cancel`), isto kao ponavljajući individualni.
- **Evidencija prisutnosti** (`AppointmentAttendance`, po terminu × klijentu): `Attended` (bool?, null = još nezabilježeno), `CoverageType` (`MonthlyPackage`/`SessionPackage`/`SinglePaid`) određuje kako je dolazak pokriven:
  - `MonthlyPackage` — klijent ima aktivan paket kod kojeg je relevantni brojač ulazaka `null` (neograničeno) → samo se bilježi prisutnost, ništa se ne skida.
  - `SessionPackage` — klijent ima paket s konačnim brojem ulazaka → skida se jedan ulazak (`ClientPackageService.DeductEntry`, poštuje SharedPool/PerService iz Kataloga).
  - `SinglePaid` — gost/probni bez odgovarajućeg paketa → bilježi se prisutnost, naplata ide zasebno kroz postojeći unos (usluga/paket "grupni trening x1").
  - Pokriće se predlaže preko `IClientPackageService.GetEligibleForService` — kad postoji točno jedan prihvatljiv paket koristi se automatski, kad ih ima više potrebno je ručno odabrati (`SetGroupAttendanceRequest.ClientPackageId`).
  - **Poništavanje prisutnosti kod SessionPackage automatski vraća skinuti ulazak** — za razliku od individualnih termina, gdje je vraćanje uvijek eksplicitna admin/trener akcija (`ReturnEntryForClientIds`). Ponovno čekiranje nakon vraćanja ponovno skida ulazak (svježe razrješenje pokrića).
  - Osoba izvan popisa članova grupe (zamjena, gost) može se dodati u prisutnost bez da postane član grupe.
- `GroupAuditLog` bilježi promjene kapaciteta, zadanog trenera, aktivnosti grupe i članstva (isti obrazac kao `EmployeeAuditLog`); vraćanje ulaska kod poništavanja prisutnosti bilježi se u postojeći `AppointmentAuditLog` (`ChangeType="PackageEntryReturn"`), jer je prisutnost svejedno vezana na `Appointment`.

### Minimalni seed podaci

Migracija `SeedGroupsMinimal` dodaje katalog za grupne treninge (kategorija "Grupni treninzi", usluga "Grupni trening", paketi "Paket 10 grupnih" i "Grupno neograničeno mjesečno"), zatim:

- 2 grupe: "Jutarnja joga" (Centar, Marko, dva slota — PON i SRI 07:00) i "Večernji funkcionalni" (Riverside, Iva, jedan slot — UTO 19:00).
- 4 članstva: Ivana Kovač (Jutarnja joga, "Paket 10 grupnih" — SessionPackage) i Ana Anić (Jutarnja joga, bez paketa) — Petar Perić (Večernji funkcionalni, "Grupno neograničeno mjesečno" — MonthlyPackage) i Marko Novak (Večernji funkcionalni, bez paketa).
- Generirani termini za tekući tjedan (kao da je pokrenuto `POST /api/groups/generate-appointments`).
- Evidentirana prisutnost: Ivana (`SessionPackage`, ulazak skinut, 9/10 preostalo) i Petar (`MonthlyPackage`, ništa skinuto) — Ana i Marko Novak ostaju kao `Expected` kandidati za ručno testiranje čekiranja/prijedloga pokrića.

## Modul Roster

Ručna evidencija odrađenog radnog vremena i odsutnosti — zamjenjuje Excel "R S" (radni sati). **Nema nikakve veze s terminima**: ne planira se unaprijed, ne otkazuje i ne miče termine automatski — bolovanje/godišnji se samo bilježi, raspored termina se po potrebi mijenja ručno, zasebno. Izvan opsega: planiranje rasporeda rada unaprijed i obračun plaća (financije, ručno).

Svi (Admin i Member) vide sve zapise (roster je timski transparentan). Zaposlenik upisuje/mijenja/briše samo svoje zapise; Admin sve, bez ograničenja. Bez odobravanja (upisano odmah vrijedi) i bez vremenskog ograničenja unosa (unatrag/unaprijed slobodno, za oba). Šifrarnik vrsta je zajednički za firmu i svima dostupan za čitanje; uređuje ga samo Admin.

| Endpoint | Opis |
|---|---|
| `GET/POST/PUT /api/roster/types`, `PATCH .../{id}/activate`\|`deactivate`, `DELETE .../{id}` | Šifrarnik vrsta (Smjena, Bowen, Godišnji...). Čitanje `Admin,Member`; pisanje samo `Admin`. Brisanje blokirano ako vrstu koristi barem jedan zapis (deaktivirati umjesto toga). |
| `GET /api/roster/entries?employeeId=&rosterTypeId=&from=&to=` | Popis zapisa, paginirano. |
| `GET /api/roster/entries/{id}` | Detalj jednog zapisa. |
| `POST/PUT /api/roster/entries`, `DELETE .../{id}` | Upis/izmjena/brisanje. Isti zahtjev za oba oblika (rad/odsutnost) — koja su polja obavezna/zabranjena određuje `RosterType.IsAbsence`, ne posebno polje. Member smije samo na `EmployeeId` koji je njegov vlastiti (i ne smije ga izmjenom prebaciti na drugog zaposlenika). |
| `GET /api/roster/team-monthly?year=&month=&locationId=` | Timski mjesečni pregled — matrica dani × zaposlenici (kao Excel "R S") sa zbrojevima po vrsti na dnu. `locationId` filtrira zaposlenike po dodijeljenoj lokaciji (`employee_locations`), roster zapis sam po sebi lokaciju ne nosi. |
| `GET /api/roster/personal?employeeId=&from=&to=` | Osobni pregled jednog zaposlenika za proizvoljno razdoblje, sa zbrojevima. Member smije dohvatiti samo svoj `employeeId`. |

### Model

- Jedna evidencija (`RosterEntry`) za oba oblika — oblik određuje isključivo `RosterType.IsAbsence`, nema posebnog polja za to:
  - **Rad** (Smjena, Bowen, Rec/dvok...): `DateFrom` = jedini dan, `DateTo` uvijek `null`, `StartTime`/`EndTime` obavezni, `DurationHours` izračunat i pohranjen. Dvokratni rad (npr. 07:00–12:00 i 17:00–19:00 isti dan) su **dva zasebna zapisa**, ne jedan s dva vremena.
  - **Odsutnost** (Godišnji, Bolovanje, Praznik...): `DateFrom`/`DateTo` = raspon bez vremena; `DateTo` smije biti `null` (otvorena odsutnost, npr. bolovanje dok se ne zna dokad traje) — kasnije se "zatvara" naknadnim `PUT`-om koji upiše `DateTo`. Jedan zapis pokriva cijeli raspon (godišnji 1.–15.7. je jedan zapis, ne 15).
  - Šifrarnik (`RosterType`) nosi `CountsAsWork` (ulazi li u zbroj radnih sati) i `IsAbsence` odvojeno od `RequiresTime` — potonje je čisto informativno svojstvo, stvarni oblik zapisa uvijek određuje `IsAbsence`.
- **Preklapanje je upozorenje, ne zabrana** (`RosterEntryDto.Warnings`, isti obrazac kao `AppointmentDto.Warnings`) — provjerava se preko svih zapisa istog zaposlenika bez obzira na kombinaciju vrsta (rad/rad, rad/odsutnost, odsutnost/odsutnost). Odsutnost pokriva cijeli dan; rad pokriva samo svoj vremenski prozor — zato dva rad-zapisa istog dana na različitim satima (dvokratni) ispravno ne prijavljuju preklapanje, dok rad upisan unutar raspona odsutnosti ispravno prijavljuje.
- **Razlaganje raspona odsutnosti u pojedine dane** (za matricu i zbroj "broj dana po vrsti") broji **kalendarske dane**, uključujući vikende — najjednostavnija varijanta, bez ovisnosti o danu u tjednu ili drugim zapisima. Otvorena odsutnost (`DateTo=null`) se za potrebe prikaza/zbroja unutar traženog razdoblja klipa na kraj tog razdoblja (mjesec kod timskog pregleda, `to` kod osobnog) — ne broji se unedogled u budućnost.
- `RosterAuditLog` bilježi `ChangeType` `Created`/`Updated`/`Deleted` s cijelim sažetkom retka u `OldValue`/`NewValue` (za razliku od `EmployeeAuditLog`/`GroupAuditLog`/`AppointmentAuditLog`, koji prate samo pojedina polja) — jer se rad/odsutnost mijenja/briše kao cjelina. **Namjerno nema FK na `roster_entries`**: roster dopušta trajno brisanje zapisa (za razliku od termina/klijenata) i audit trag mora preživjeti to brisanje — zapis se piše prije fizičkog brisanja retka.
- Validacije: zaposlenik i vrsta moraju postojati i biti aktivni; rad zahtijeva vrijeme od/do (do nakon od) i jedan datum (bez raspona); odsutnost ne smije imati vrijeme, `DateTo` (ako upisan) ne smije biti prije `DateFrom`.

### Minimalni seed podaci

Migracija `SeedRosterMinimal` dodaje šifrarnik (Smjena, Bowen, Rec/dvok — rad; Godišnji, Bolovanje, Praznik — odsutnost), zatim za tekući mjesec:

- Marko Trener: dvokratni radni dan (Smjena 07:00–12:00 i 17:00–19:00, dva zapisa) i Godišnji kao raspon (5 dana, jedan zapis).
- Iva Vanjska: Bowen (09:00–11:00, jedan zapis) i otvoreno Bolovanje (`DateTo` prazan, još traje).
