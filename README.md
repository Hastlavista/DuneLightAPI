# BlueDragon.DuneLight

Web API za vođenje manjeg fitness/wellness obrta s više poslovnica — zamjena za Excel tablice (raspored, grupe, klijenti,
katalog i cjenik, paketi, naplata, provizije, zaposlenici i roster). Ovaj repozitorij je samo backend; frontend je zasebna
Angular aplikacija.

**Dokumentacija:**
- [`CLAUDE.md`](CLAUDE.md) — kratka pravila rada i konvencije
- [`docs/ARCHITECTURE.md`](docs/ARCHITECTURE.md) — arhitektura, model podataka, otvorena pitanja
- [`docs/decisions/`](docs/decisions/) — arhitektonske odluke (ADR)
- [`docs/p1/P1_DECISION_RECORD.md`](docs/p1/P1_DECISION_RECORD.md) — zapis faze P1 (Cancellation policy engine)
- [`docs/WORKFLOW.md`](docs/WORKFLOW.md) — kako radimo
- [`docs/appointment-booking-characterization.md`](docs/appointment-booking-characterization.md) — karakterizacijska test suite i nalazi
- Swagger (`/swagger` u Developmentu) — točan, uvijek ažuran API ugovor

## Stack

- .NET 10 (`net10.0`), ASP.NET Core Web API; bez CQRS/MediatR/repository patterna nad EF-om
- EF Core 10 + PostgreSQL (shema `dunelight`); shema se gradi **FluentMigrator** migracijama, ne `dotnet ef migrations`
- Autentikacija: JWT ili `X-Api-Key`; zasebna `PlatformBearer` shema za platformski Management API
- Autorizacija: isključivo grantovi (`[RequireGrant(...)]`), vidi [ADR-0004](docs/decisions/0004-autorizacija-grantovima.md)
- Serilog (+ Seq), Swagger/OpenAPI, transakcijski outbox (hosted service)
- Valuta EUR, `numeric(10,2)`; multi-tenant — sve poslovne tablice imaju `organization_id`

## Struktura rješenja

| Projekt | Sadržaj |
|---|---|
| `BlueDragon.DuneLight.Core` | DTO-ovi, enumi, servisni interfejsi, `Grants`, `ErrorCodes`/`WarningCodes`, iznimke, `PagedRequest`/`PagedResult` |
| `BlueDragon.DuneLight.Infrastructure` | Entiteti (`Domain/Models`), `DatabaseContext`, Handler sloj (pristup bazi), Service sloj (poslovna logika), `Utils` (čista domenska pravila), `UnitOfWork`, `Outbox`, `Integrations` |
| `BlueDragon.DuneLight.API` | Kontroleri, autentikacija, grant autorizacija, `ExceptionHandlingMiddleware`, `Startup.cs` (DI) |
| `BlueDragon.DuneLight.DatabaseMigration` | Samostalni FluentMigrator runner (konzolna aplikacija) |
| `BlueDragon.DuneLight.UnitTests` | xUnit integracijski testovi nad stvarnim PostgreSQL-om |

Tok poziva: `XxxController` (tanak, `[RequireGrant]`) → `IXxxService` (poslovna logika, validacija, own/all provjera,
DTO mapiranje) → `IXxxHandler` (pristup bazi). Operacije koje pišu u više tablica rade u jednom `IUnitOfWork`
(jedan `DatabaseContext`, jedna transakcija).

## Pokretanje

```bash
dotnet build BlueDragon.DuneLight.sln
dotnet run --project BlueDragon.DuneLight.API
```

Swagger UI je dostupan na `/swagger` kad je `ASPNETCORE_ENVIRONMENT=Development`. Lokalna baza i ostale postavke su u
`BlueDragon.DuneLight.API/appsettings.json`.

## Migracije

```bash
dotnet run --project BlueDragon.DuneLight.DatabaseMigration -- --c=Local
```

Nazvane konfiguracije: `Local`, `Development`, `Production` (`DatabaseMigration/Configuration/DatabaseConfiguration.cs`).
Alternativno vlastiti connection string: `-- --d=PostgreSQL --s="Host=...;Database=...;Username=...;Password=..."`.

Migracije se izvršavaju redom po `[DeveloperMigration(godina, mjesec, dan, autor, redni_broj)]` atributu
(`DatabaseMigration/Extensions/DeveloperMigrationAttribute.cs`). Već primijenjena migracija se ne mijenja ni ne
renumerira — dodaje se nova. Razvojna baza nema produkcijskih podataka i smije se ponovno izgraditi
([ADR-0003](docs/decisions/0003-politika-razvojne-baze.md)).

## Testovi

Testovi su integracijski, nad stvarnim PostgreSQL-om (shema mora biti migrirana, connection string je u
`BlueDragon.DuneLight.UnitTests/TestSupport.cs`):

```bash
dotnet test BlueDragon.DuneLight.sln
dotnet test BlueDragon.DuneLight.UnitTests --filter "FullyQualifiedName~Scheduling"
```

Rezultati ne smiju ovisiti o vremenskoj zoni hosta.

## Auth

Prijava: `POST /api/public/Auth/Register` (kreira Organizaciju + prvog korisnika s administratorskom grant grupom,
`409 AUTH_ORGANIZATION_SLUG_TAKEN` ako naziv organizacije već postoji), `POST /api/public/Auth/Login`
(`401 AUTH_INVALID_CREDENTIALS` ako podaci ne odgovaraju aktivnom korisniku). JWT ili `X-Api-Key` header nose
`organizationId` (bez `role` claima — legacy `UserRole` je uklonjen, [ADR-0019](docs/decisions/0019-uklanjanje-userrole.md)); grantovi se NE nalaze u tokenu nego se čitaju iz baze po zahtjevu (kratki cache ~30 s), pa promjena
dozvola vrijedi gotovo odmah. Frontend dobiva efektivne grantove iz `GET /api/employees/me` (`grants`).

### Deaktivacija korisničkog računa

`User.IsActive` (default `true`) provjerava se pri prijavi i centralno na svakom zahtjevu (`ActiveUserGuard` u JWT
validaciji i u `ApiKeyAuthenticationHandler`) — deaktiviran korisnik ne može koristiti ni već izdani JWT ni API ključ.

### Promjena lozinke

`POST /api/public/Auth/ChangePassword` (`[Authorize]`) — prijavljeni korisnik mijenja vlastitu lozinku; `userId` se uzima
iz tokena, ne iz tijela zahtjeva. Vraća `401 AUTH_CURRENT_PASSWORD_INVALID` ako trenutna lozinka ne odgovara (isti kôd i za
"korisnik ne postoji/nije aktivan" — namjerno, da se ne otkriva status naloga).

```json
// Request
{ "currentPassword": "staraLozinka1", "newPassword": "novaLozinka1" }

// 200 OK (bez tijela)

// 401 Unauthorized
{ "error": { "code": "AUTH_CURRENT_PASSWORD_INVALID", "message": "Trenutna lozinka nije ispravna." } }
```

### PIN prijava (brzo prebacivanje na dijeljenom uređaju)

Scenarij: dijeljeni uređaj (npr. tablet na recepciji) na kojem se kroz dan izmjenjuju zaposlenici; kolege "preuzimaju"
sesiju kratkim PIN-om umjesto pune lozinke.

`POST /api/public/Auth/PinLogin` — javni endpoint, isti oblik odgovora (`AuthResponse`) kao `Login`; provjerava `PinHash`
i izdaje nov JWT. Vraća `401 AUTH_INVALID_PIN` ako organizacija/email/PIN ne odgovaraju aktivnom korisniku s postavljenim
PIN-om (isti kôd i kad PIN nije postavljen). **Svjesna odluka:** bez ograničenja broja pokušaja — PIN je pogodnost za
fizički kontroliran uređaj; mjesto za buduće ograničenje je komentirano u `AuthService.PinLogin`.

`POST /api/public/Auth/ChangePin` (`[Authorize]`) — postavlja/mijenja vlastiti PIN, uvijek potvrđeno **lozinkom**. Isti
obrazac grešaka kao `ChangePassword`. `EmployeeMeDto.hasPinSet` govori treba li prikazati "Postavi PIN" ili "Promijeni PIN".

```json
// POST PinLogin — Request
{ "organizationSlug": "moj-studio", "email": "ana@dunelight.local", "pin": "1234" }

// POST ChangePin — Request
{ "currentPassword": "lozinka123", "newPin": "1234" }
```

Popis poznatih korisnika na uređaju NIJE backend koncept — to frontend sam pamti (`localStorage`).

### Platformski Management API

Platformski operateri (`PlatformAccount`) su strukturno odvojeni od tenant korisnika: vlastita prijava
(`PlatformAuthController`), vlastiti JWT (`PlatformJwtSettings`) i `PlatformBearer` shema. Prvi račun se stvara pri
startu iz `PlatformSettings` (`PlatformAccountBootstrapper`).

## Jedinstveni format greške

Sve greške (validacijske, poslovne, auth, framework-level 401/403) vraćaju se u istom obliku (`ExceptionHandlingMiddleware`
+ `ConfigureApiBehavior` za automatsku model-validaciju):

```json
{ "error": { "code": "PRICE_OVERLAP", "message": "Čitljiva poruka (fallback za prikaz, frontend i18n se veže na code).", "details": { "polje": ["detalji po polju, samo kod VALIDATION_ERROR"] } } }
```

`code` je stabilan ugovor — nove greške dobivaju nov, specifičan kôd. **Jedini izvor istine za popis kodova:**
`BlueDragon.DuneLight.Core/Shared/ErrorCodes.cs` (upozorenja: `WarningCodes.cs`).

| Iznimka | HTTP |
|---|---|
| `ValidationAppException` (default `VALIDATION_ERROR`) | 400 |
| `UnauthorizedAppException`, framework 401 (`UNAUTHORIZED`) | 401 |
| `ForbiddenAppException`, framework 403 (`FORBIDDEN` — nedostaje potreban grant) | 403 |
| `NotFoundAppException` (default `NOT_FOUND`) | 404 |
| `BusinessRuleException` (poslovno pravilo, npr. `REFERENCED_CANNOT_DELETE`, preklapanja, kapaciteti) | 409 |
| neuhvaćena iznimka (`INTERNAL_ERROR`) | 500 |

## Moduli

Poslovna pravila su u ADR-ovima i zapisima faza;
točni endpointi i DTO-ovi u Swaggeru.

| Modul | Kontroleri (`BlueDragon.DuneLight.API/Controllers/`) | Bazna ruta |
|---|---|---|
| Auth | `AuthController` | `/api/public/Auth` |
| Termini (Appointment / Segment / Participation) | `Appointments/*` | `/api/appointments`, `/api/segments`, `/api/participations` |
| Grupe | `Groups/*` | `/api/groups` |
| Pauze | `ScheduleBreaks/*` | `/api/schedule-breaks` |
| Katalog (poslovnice, usluge, cjenik, paketi, sobe, resursi) | `Catalog/*` | `/api/catalog/...` |
| Klijenti (klijenti, oznake, paketi klijenta, grupe klijenta) | `Clients/*` | `/api/clients` |
| Naplata | `Checkouts/*` | `/api/checkouts` |
| Provizije | `Commissions/*` | `/api/commissions` |
| Zaposlenici | `Employees/*` | `/api/employees` |
| Roster (radno vrijeme, odsutnosti, praznici, fond godišnjeg) | `Roster/*` | `/api/roster/...`, `/api/companies/{id}/holidays`, `/api/employees/{id}/leave-*` |
| Proizvodi i skladište | `Products/*` | `/api/products`, `/api/stock` |
| Dozvole (grantovi, grant grupe, capabilities, workforce uloge) | `Permissions/*` | `/api/grants`, `/api/permissions/...` |
| Organizacija (postavke, branding) | `Organization/*` | `/api/organization/...` |
| Onboarding, Dashboard, Notifikacije, Dijagnostika | `Onboarding/*`, `Dashboard/*`, `Notifications/*`, `Diagnostics/*` | `/api/onboarding-status`, `/api/dashboard`, `/api/notifications`, `/api/_internal/diagnostics/grants` |
| Platformski Management | `Management/*` | `/api/management/...` |
