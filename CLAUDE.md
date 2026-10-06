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
- Commitove, push i PR-ove radi korisnik (Rider); Claude ne radi git operacije, nego implementira, zapisuje i dokumentira.
  PR-ovi idu na granu `development-claude`. Opseg ovog repozitorija je samo backend.
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
- P1 (Cancellation policy engine) je dizajniran, NIJE implementiran (ADR-0015 – ADR-0018).
- Nema `UserRole` ni `role` claima (uklonjeno, ADR-0019). Nijedna autorizacijska odluka ne smije ovisiti o ulozi; workforce `Role` je samo poslovna oznaka.
