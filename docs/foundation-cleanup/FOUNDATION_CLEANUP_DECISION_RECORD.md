# Foundation cleanup — Decision Record

> Segment čišćenja temelja prije P1. Nije nova poslovna faza: uklanja zaostale ugovore i usklađuje kod s već
> zaključanim ciljnim pravilima. Odluke: [ADR-0019](../decisions/0019-uklanjanje-userrole.md),
> [ADR-0020](../decisions/0020-jedinstven-email-klijenta.md), [ADR-0021](../decisions/0021-nula-aktivnih-poslovnica.md).

## Opseg

| # | Stavka | ADR | Status |
|---|---|---|---|
| 1 | Ukloniti legacy `UserRole` / `users.role` / `role` claim | 0019 | Implementirano 2026-10-06 |
| 2 | Email klijenta i korisničkog računa jedinstven unutar organizacije (trim, case-insensitive) | 0020 | Implementirano 2026-10-06 |
| 3 | Organizacija smije imati nula aktivnih poslovnica | 0021 | Implementirano 2026-10-06 |

## Zaključana pravila
- Nijedna autorizacijska odluka ne smije ovisiti o `UserRole` ni o JWT role claimu (stavka 1).
- Bez kompatibilnosti za uklonjena polja i endpointe; razvojna baza se smije ponovno izgraditi (ADR-0003).
- Ako kod koristi uklonjeni pojam za stvarnu poslovnu semantiku: STANI i prijavi, ne reinterpretiraj i ne prenosi na grantove.

## Namjerne promjene ponašanja (stavka 1)

| Područje | Prije | Poslije |
|---|---|---|
| JWT / ApiKey | `ClaimTypes.Role` claim s Admin/Member/Reception | Nema role claima |
| `POST /api/public/Auth/*` | `AuthResponse.role` | Polje uklonjeno |
| `GET /api/employees` | Query parametar `role` | Uklonjen |
| `PATCH /api/employees/{id}/role` | Mijenja `users.role` + audit | Endpoint uklonjen |
| `EmployeeDto`, `EmployeeMeDto` | Polje `role` | Uklonjeno |
| Baza | `users.role` | Stupac ispušten (`20261023000000`) |
| Grant `employees.role.manage` | U katalogu, admin predlošcima v1–v5 i tenant Admin grupama | Uklonjen iz kataloga; admin v6 bez njega; capability deprecated; tenant reference obrisane |

## Implementation decision log

Format: datum — pitanje/kontekst — odgovor — posljedica.

- 2026-10-06 — Što s legacy `users.role`? — Ukloniti u potpunosti; nema sigurnosnu ulogu ni u kojem obliku. — ADR-0019.
- 2026-10-06 — Čime zamijeniti `role` u `with-login` i filter `role` na listi zaposlenika? — Ničim; ne reinterpretirati
  i ne prenositi na grantove. Audit je potvrdio da `with-login` već dodjeljuje ovlasti preko `GrantGroupIds`. — Filter i
  endpoint uklonjeni bez zamjene.
- 2026-10-06 — Duplikat emaila klijenta? — Zabranjeno unutar organizacije (kao za korisnike), usporedba ignorira velika
  i mala slova. — ADR-0020.
- 2026-10-06 — Zadnja aktivna poslovnica? — Treba dopustiti nula aktivnih poslovnica. — ADR-0021.
- 2026-10-06 — Smije li Claude sam pokretati migracije lokalno? — Da. — Migracije, build i testove pokreće Claude.
- 2026-10-06 — `docs/poslovna-logika-pregled.md` je zastario? — Drop; opisivao je staru logiku iz koje su proizašla
  pitanja za klijenta i više nije validan. — Dokument obrisan, reference uklonjene.
- 2026-10-06 — Grant `employees.role.manage` ostaje bez svrhe nakon uklanjanja `PATCH .../role`. — Ukloniti sada,
  bez zamjenskog granta; nove verzije capabilityja/predložaka postojećim mehanizmom + migracija koja čisti postojeće
  reference; to je namjerno čišćenje, ne dug. — Admin v6, capability povučen (deprecated, ne obrisan zbog FK
  `Restrict`), admin v1–v5 deaktivirani, tenant reference obrisane (ADR-0019).
- 2026-10-06 — Lokalna baza je odlutala od koda (migracija `2026-09-26` mijenjana nakon primjene), pa migracije padaju
  na `20261006000001`. Smije li se shema obrisati i izgraditi od nule? — Da. — Shema `dunelight` u bazi `postgres`
  obrisana i ponovno migrirana (ostale sheme u bazi nisu dirane).
- 2026-10-06 — ADR-0020: trimati email? — Da: trim + usporedba neovisna o velikim/malim slovima; sprema se trimana
  vrijednost. — Primijenjeno na klijente i korisničke račune.
- 2026-10-06 — ADR-0020: kod greške za duplikat emaila klijenta? — Novi `CLIENT_EMAIL_ALREADY_IN_USE` (409).
- 2026-10-06 — ADR-0020: uskladiti i email korisničkog računa (danas case-sensitive)? — Da. — Jedinstvenost i prijava
  po emailu za `users` postaju case-insensitive (kod ostaje `EMAIL_ALREADY_IN_USE`).
- 2026-10-06 — ADR-0020: na koju granu? — Stackati na granu foundation cleanupa (ADR-0019). Commitove i PR-ove radi
  korisnik iz Ridera; Claude ne radi git operacije, samo implementira i dokumentira.
- 2026-10-06 — ADR-0021: što onboarding `HasCompany` vraća kad su sve poslovnice deaktivirane? — Ostaje "postoji
  AKTIVNA poslovnica" (wizard ponovno prikazuje korak poslovnice). — Bez promjene onboarding koda.
