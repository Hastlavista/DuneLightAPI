# ADR-0019: Uklanjanje legacy `UserRole` (`users.role`)

- **Status:** Prihvaćeno, implementirano 2026-10-06
- **Datum:** 2026-10-06
- **Zapis segmenta:** [Foundation cleanup](../foundation-cleanup/FOUNDATION_CLEANUP_DECISION_RECORD.md)

## Kontekst
Nakon prelaska na autorizaciju isključivo grantovima (ADR-0004) `users.role` (`UserRole` Admin/Member/Reception) više nije
bio sigurnosni mehanizam, ali je i dalje postojao: pisao se pri registraciji i kreiranju korisnika, išao u JWT/ApiKey
`role` claim (`JwtService`, `ApiKeyAuthenticationHandler`, `UserRoleClaims`) i bio izložen u API-ju
(`PATCH /api/employees/{id}/role`, `role` u `GET /api/employees/me`, `EmployeeDto` i `AuthResponse`, filter `role` na
listi zaposlenika). Dvostruki pojam "uloge" zbunjuje i otvara rizik da netko ponovno počne autorizirati po njoj.
Target Architecture v1 ovo je ostavio otvorenim (#8).

## Razmotrene opcije
1. **Zadržati kao ne-sigurnosnu oznaku** - nema posla, ali ostaje dvosmislen pojam pored grant grupa i workforce `Role`.
2. **Ukloniti u potpunosti** - grant grupe su jedini izvor ovlasti, workforce `Role` jedina poslovna oznaka.

## Odluka
`UserRole` se uklanja u potpunosti, bez kompatibilnosti: enum, stupac `users.role`, `role` claim u JWT-u i ApiKey
identitetu, `UserRoleClaims` i svi API ugovori koji ga nose. Invarijanta: **nijedna autorizacijska odluka ne smije ovisiti
o `UserRole` ni o JWT role claimu.** Ponašanje se ne prenosi na grantove (nema zamjenskog filtera ni endpointa).
Workforce `Role` (poslovna oznaka, npr. "Trener", `UserRoleAssignment`) i grant grupe ostaju.

## Posljedice
- Audit prije brisanja (2026-10-06): nijedna putanja nije koristila `UserRole` za poslovnu semantiku. Upotrebe su bile
  samo: role claim (JWT, ApiKey), kozmetička vrijednost pri Register/CreateWithLogin, `PATCH .../role` s
  `EmployeeAuditLog` zapisom, filter `role` na `GET /api/employees` i `role` polja u DTO-ovima. Autorizacijska default
  policy traži samo autentikaciju; nigdje nema `[Authorize(Roles=...)]`. `with-login` već dodjeljuje ovlasti preko
  `GrantGroupIds`.
- Breaking change za frontend: nestaju `role` u `AuthResponse`, `EmployeeDto`, `EmployeeMeDto`, query parametar `role` na
  `GET /api/employees`, endpoint `PATCH /api/employees/{id}/role` i grant `employees.role.manage` (i u `grants` listi
  i u capability/predložak API-jima). Frontend koristi `grants` iz `GET /api/employees/me`.
- Migracija `Migration_2026_10_23_DropUserRole` ispušta `users.role` (bez backfilla i bez Down migracije, ADR-0003).
  Povijesni `EmployeeAuditLog` zapisi s `ChangeType = "Role"` ostaju kao povijest.
- Uklonjeni testovi koji su postojali samo zbog `UserRole`: `Legacy_UserRole_Admin_does_not_bypass_inactive_status`,
  `LegacyUserRoleAdmin_HasNoEffectOnInvariant`.
- Kod `LAST_ACTIVE_ADMIN` već ne postoji u `ErrorCodes`.
- Grant `employees.role.manage` je štitio samo uklonjeni `PATCH .../role`, pa je povučen istim postojećim mehanizmom
  verzioniranja (migracije `20261023000001`/`20261023000002`): uklonjen iz `Grants.cs`; objavljen admin predložak v6
  (= v5 bez njega); capability verzija `employees.role.manage` v1 je povučena (`is_active = false`, `deprecated_at`),
  ne obrisana — referencirane verzije se nikad ne brišu fizički (FK `Restrict`); admin v1–v5 su deaktivirani jer ga
  biraju; iz tenant grant grupa uklonjeni su sirovi grant, capability snapshot i template provenance. Nije zamijenjen
  drugim grantom. `GrantDiagnosticsService` više ne prijavljuje deprecated capability verzije kao
  `CapabilityReferencesUnknownGrant`. Upgrade v5 → v6 za tako očišćenu grupu nema konflikta.
- Ne miješati s `UserRoleAssignment` / `Role` (workforce oznake) ni s `CapabilityDefinitionGrant.Role` (uloga granta
  unutar capabilityja) — oni ostaju.
