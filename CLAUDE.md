# CLAUDE.md

Ovu datoteku Claude Code automatski čita na početku svake sesije.
Drži je kratkom i ažurnom. Detalji idu u `docs/`, ovdje samo ono što treba znati UVIJEK.

## O projektu
BlueDragon.DuneLight je multi-tenant backend (Web API) za vođenje fitness/wellness studija s više poslovnica:
raspored (termini, grupe, liste čekanja), klijenti, katalog i cjenik, paketi, naplata, provizije, roster zaposlenika.
Postojeća aplikacija se **inkrementalno modernizira** (nema prepisivanja od nule, vidi ADR-0002).
Frontend je zasebna Angular aplikacija u drugom repozitoriju; ovdje se radi samo backend.

## Stack
- Jezik / framework: C# / .NET 10 (`net10.0`, SDK u `global.json`), ASP.NET Core Web API
- Baza: PostgreSQL (shema `dunelight`) + EF Core 10 (Npgsql); shema se gradi **FluentMigrator** migracijama
- Ostalo: JWT + `X-Api-Key` autentikacija, Serilog (+ Seq), Swagger, transakcijski outbox (hosted service),
  Nager.Date API za praznike, xUnit integracijski testovi nad stvarnim Postgresom

## Naredbe
```bash
dotnet build BlueDragon.DuneLight.sln                                    # build
dotnet run --project BlueDragon.DuneLight.API                            # pokretanje lokalno (Swagger na /swagger u Developmentu)
dotnet run --project BlueDragon.DuneLight.DatabaseMigration -- --c=Local # migracije (Local | Development | Production)
dotnet test BlueDragon.DuneLight.sln                                     # testovi (trebaju lokalni Postgres s migriranom shemom)
dotnet test BlueDragon.DuneLight.UnitTests --filter "FullyQualifiedName~Scheduling"  # samo scheduling suite
```
Lint: nema konfiguriranog lintera/analizatora (nema `.editorconfig`); build upozorenja su jedini signal.

## Struktura
```
BlueDragon.DuneLight.API/                # kontroleri (tanki), auth handleri, RequireGrant atributi, ExceptionHandlingMiddleware, Startup (DI)
BlueDragon.DuneLight.Core/               # DTO-ovi, enumi, servisni interfejsi, Grants, ErrorCodes/WarningCodes, iznimke
BlueDragon.DuneLight.Infrastructure/     # entiteti + DatabaseContext, Handlers (pristup bazi), Services (poslovna logika),
                                         # Utils (čista domenska pravila), UnitOfWork, Outbox, Integrations
BlueDragon.DuneLight.DatabaseMigration/  # FluentMigrator konzolni runner, Migrations/2026/*
BlueDragon.DuneLight.UnitTests/          # integracijski testovi; Scheduling/ = karakterizacijska suite
docs/
  ARCHITECTURE.md   # cjelokupna arhitektura, pročitaj prije većih promjena
  decisions/        # zapisnik arhitektonskih odluka (ADR)
  p1/, foundation-cleanup/  # zapisi faza (decision record + dnevnik odluka); svaka nova faza dobiva svoju mapu
  WORKFLOW.md       # kako radimo
  appointment-booking-characterization.md  # karakterizacijska test suite i nalazi
```

## Konvencije
- Tok poziva: `Controller → Service → Handler → DatabaseContext`. Poslovna logika u servisima i `Utils`, ne u kontrolerima.
- Višetablične operacije idu kroz jedan `IUnitOfWork` (jedan context, jedna transakcija, jedan commit).
- Autorizacija isključivo grantovima: `[RequireGrant(Grants.X)]` + own/all provjera u servisu. Nikad po ulozi ili imenu grupe.
- Own-scope uvijek `prijavljeni User → Employee`; nikad ne vjeruj `EmployeeId` iz zahtjeva.
- Greške: bacaj `ValidationAppException` / `BusinessRuleException` / ... s kodom iz `Core/Shared/ErrorCodes.cs`
  (jedini izvor istine za kodove). Upozorenja preko `WarningCodes`.
- Uske poslovne naredbe (PATCH/POST po namjeni), nikad generički `PUT` cijelog agregata (ADR-0014).
- Komentari i poruke grešaka uglavnom na hrvatskom (prati jezik okolnog koda); nazivi domenskih pojmova na engleskom (Appointment, Segment, Participation...).
- Instanti u UTC (`DateTimeOffset`), poslovni datumi `DateOnly`; nikad `DateTime.Now` / `TimeZoneInfo.Local`.
- "Sada" samo kroz injektirani `TimeProvider` (poslovni sat, T1); nikad `DateTime(Offset).Now/UtcNow`. `Utils` primaju `now`
  parametrom. `TimeProvider.System` samo za sistemsko (JWT istek, outbox obrada). Testovi: `TestClock`, ne `DateTimeOffset.UtcNow`.
- 403 samo kroz tvorničke metode `ForbiddenAppException` (`MissingGrant(s)`, `MissingAnyGrant`, `OutOfScope`) sa svim grantovima
  koji nedostaju (T1-5).

## Pravila rada
- Prije veće promjene pročitaj `docs/ARCHITECTURE.md` i relevantne ADR-ove.
- Ako promjena odstupa od arhitekture ili zahtijeva novu odluku, STANI i predloži je
  prije implementacije. Ne odstupaj tiho.
- Ako implementacija traži poslovno pravilo koje nije odlučeno (OPEN u ADR-ovima / sekciji 7 ARCHITECTURE.md):
  STANI i pitaj (AskUserQuestion). Ne izmišljaj pravila.
- Nakon implementacije koja mijenja arhitekturu, ažuriraj `docs/ARCHITECTURE.md`
  i po potrebi dodaj novi ADR u `docs/decisions/`.
- Nakon implementacije pokreni build i testove i navedi rezultate u završnom izvještaju.
- Svako pitanje postavljeno tijekom implementacije i korisnikov odgovor odmah upiši u dnevnik odluka zapisa faze
  (`docs/<faza>/<FAZA>_DECISION_RECORD.md`, vidi `docs/WORKFLOW.md`).
- **Na kraju svake faze** (backend i frontend) ažuriraj `KONTEKST_ZA_CHAT.md` u korijenu repozitorija (status faza, sljedeći
  koraci, otvorene teme). Obavezno.
- Commitove, push i PR-ove radi korisnik (Rider); Claude ne radi git operacije, nego implementira, zapisuje i dokumentira.
  PR-ovi idu na granu `development-claude`. Opseg ovog repozitorija je samo backend.
- Svaka promjena DTO-a (oblik odgovora/zahtjeva) uključuje ponovno generiranje frontend tipova iz Swaggera u istoj promjeni
  (frontend FE-ADR-0004); Swagger mora ostati točan (nullable, enumi kao stringovi, formati datuma, oblik grešaka).
- Frontend je zaseban repozitorij `C:\Users\Silvio\WebstormProjects\BlueDragon.DuneLight` s vlastitim `CLAUDE.md`,
  `ARCHITECTURE.md`, `docs/adr/` (FE-ADR) i `docs/f<N>/` (ARCHITECTURE.md §9). Poslovna pravila su samo ovdje; frontend ih referencira.
- Karakterizacijski test koji počne padati = promjena ponašanja. Mijenja se samo ako odluka (ADR) to namjerno mijenja.

## Na što paziti
- Nema produkcijskih podataka: čista ciljna shema, bez compatibility stupaca, dual-writea i backfilla (ADR-0003).
- Migracije: `[DeveloperMigration(god, mj, dan, Developer.SilvioHabazin, redni_broj)]`. Nikad ne mijenjaj ni ne
  renumeriraj migraciju koja je već primijenjena lokalno — dodaj novu.
- `Participation` (= `BookingSegmentParticipation`) je izvor istine za status, cijenu, paket i settlement. Booking i
  Appointment nemaju vlastiti status ni cijenu; Appointment status je izveden (ADR-0005, ADR-0006).
- Nikad automatski birati jednog od više Employeeja (pricing), ni implicitno izvoditi Group Segment (ADR-0009, ADR-0011).
- `PricingEmployeeId` ≠ korisnik provizije ≠ "primarni" Employee (nema primarnog Employeeja).
- Korekcije ne brišu povijest: kompenzacijski zapisi uz `SourceVersion`/`StatusVersion` (ADR-0007).
- Migracije ne seedaju ništa (ADR-0022). Katalog grantova i capabilityja je u kodu (`Grants.cs`, `CapabilityCatalog.cs`);
  novi grant = nova migracija koja ga dodaje grupama s `grant_groups.system_key = 'admin'` (ADR-0023).
- P1 (Cancellation policy engine) je implementiran (ADR-0015 – ADR-0018, `docs/p1/`): politika se razrješava samo kroz
  `ICancellationPolicyResolver`, posljedice piše samo `IParticipationPolicyService`, prijelaz sudjelovanja je jedna matrica
  u `BookingService` (Individual = Group). Otkazivanje uvijek traži initiator; metapodaci uvijek odgovaraju statusu.
- P2 (Memberships) je ZAKLJUČEN 2026-10-08 (`docs/p2/`, završni pregled `P2_ZAVRSNI_PREGLED.md`; nova faza samo na nalog): faze 2A (katalog planova, ADR-0025), 2B (članstva, ADR-0026)
  2C (periodi, zaduženja, obnova, ADR-0027), 2D (pokriće, ADR-0028), 2E (cjenovna pogodnost, ADR-0029) i 2F (provizije, Vagaro
  model, ADR-0030) implementirane. Odluke su u `P2_DECISION_RECORD.md` (ima prednost pred planom); članarina je zasebna domena, nije paket, a
  paketi se u P2 ne mijenjaju. Granice perioda računa samo `Utils/MembershipPeriodCalendar`. Pokriće sudjelovanja ide samo kroz
  `IMembershipCoverageService` (svaka nova ulazna točka sudjelovanja mora pozvati `SyncParticipation`; bez članarine no-op).
  Automatsku cijenu sudjelovanja s obzirom na članarinu (pokriće, pogodnost) postavlja samo taj servis.
- Provizije rade kao Vagaro (ADR-0010 + ADR-0030): nema pravila = nema provizije; pravilo za uslugu ima prednost pred općim
  pravilom zaposlenika, "Bez provizije" je izričit izbor (nula nikad ne znači "vrati se na drugo pravilo"); deaktivacija nikad ne
  vraća stariju verziju; uz svaku proviziju se sprema objašnjenje izbora pravila. Payroll (razdoblja, tiered, klase...) je
  zasebna faza nakon P2 (`docs/payroll/`). Pravilo se bira po datumu važenja; zarađena provizija je
  nepromjenjiv snapshot, storno samo kroz `CommissionService.Reverse` (Reversed + razlog), izvještaj po događajima. Korisnik
  provizije na prvu prodaju članarine je samo na članstvu; svaka promjena plaćenosti P1 naknade mora pozvati
  `SyncPolicyFeeCommission`.
- K1 (dorade iz povratnih informacija klijenta) je ZAKLJUČEN 2026-10-08 (K2 samo na nalog) (`docs/k1/`, ADR-0031; povratne informacije i
  plan faza K2/K3 u `docs/klijent/POVRATNE_INFORMACIJE_v1.md`). Članstvo "stoji" dok su sve poslovnice opsega plana
  neaktivne: sustavna pauza (`source = CompanyClosure`) koju otvara/zatvara samo obnova; otvorena ne ulazi u matematiku perioda.
  Razlog otkaza studija = tekst ILI šifra razloga. `Resources = null` na zakazivanju = zadani resursi usluge.
- K2 (ovlasti) implementiran 2026-10-09 (`docs/k2/`, ADR-0032). Zatvorenost termina određuje samo `Utils/AppointmentClosure`
  (ručno ili kraj poslovnog dana, izvedeno, isto za individualni i grupni). Prije zatvaranja promjena statusa je označavanje bez
  granta; nakon zatvaranja korekcija iz terminalnog statusa traži `appointments.corrections.<izvorni status>` i razlog.
  Korekcija ≠ otpis: korekcija posljedicu poništava (Reversed), otpis (Waived) traži `policy.fee.waive` ili `policy.unit.waive` po
  učinku (samo `Utils/PolicyOverride`). Override radne snage samo kroz `Utils/AvailabilityOverride` (grant, neovisan o opsegu,
  bez granta 403, audit). Nema uloga ni raspodjele grantova po ulogama, ni u dokumentaciji.
  Redoslijed nakon K2: K3 → P3 → P4 → P5 → P6 → Paketi v2 / P1+ → Payroll.
- T1 (sat sustava i testni alati) implementiran 2026-10-09 (`docs/t1/`, ADR-0033 – ADR-0035; tipovi: trenutak `DateTimeOffset`, dan
  `DateOnly`, vrijeme dana `TimeOnly`, trajanje `TimeSpan`; cijena zamrznuta na sudjelovanju, cjenik po danu poslovnice; paket
  pokriva od dana kupnje, kupnja unatrag traži `clients.packages.write.past`): jedan poslovni sat (`BusinessTimeProvider`,
  pomak po organizaciji samo kroz testne alate u Managementu), `GET api/organization/clock`, seed iz Managementa, 403 s
  `details.reason` + `requiredGrants` (own opseg na tuđem = 403 `OutOfScope`, kod `NOT_OWNER`), Core nullable za Swagger (validacija
  nepromijenjena). Testni alati (`[TestToolsOnly]`, `TestTools:Enabled`) su PRIVREMENI i uklanjaju se prije go-livea.
- Grantovi su granularni po poslovnoj radnji (ne po polju); novi grant ide migracijom samo Admin grupama (ADR-0023).
- Nema `UserRole` ni `role` claima (uklonjeno, ADR-0019). Nijedna autorizacijska odluka ne smije ovisiti o ulozi; workforce `Role` je samo poslovna oznaka.
