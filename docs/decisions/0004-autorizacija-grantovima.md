# ADR-0004: Autorizacija isključivo preko grantova

- **Status:** Prihvaćeno
- **Datum:** 2026-10-05 (implementirano u "Grant-only Tenant Authorization Refactor"; ovdje formalizirano)

## Kontekst
Stari kod je koristio `[Authorize(Roles=...)]` nad `UserRole` (s Owner/Admin bypassom), imena grupa i workforce uloge kao
sigurnost. To nije podržavalo prilagodljive grupe ovlasti ni own/all razlikovanje po Segmentu.

## Razmotrene opcije
1. **Role-based (UserRole + bypass)** - jednostavno, ali kruto i otvara eskalaciju preko uloga/imena.
2. **Grant-only** - runtime ovlasti isključivo iz raw grantova kroz prilagodljive grant grupe.

## Odluka
Lanac: `User → UserGrantGroup → GrantGroup → GrantGroupGrant → Grant`. Nema Owner bypassa, Admin runtime bypassa,
autorizacije po imenu GrantGroupe, workforce `Role` ni legacy `UserRole` kao sigurnosti.
- Invarijanta: barem jedan aktivni User mora zadržati efektivni `permissions.manage`.
- User i Employee su odvojeni pojmovi. Own-scope uvijek ide `prijavljeni User → Employee`; `EmployeeId` iz zahtjeva se
  nikad ne koristi za vlasništvo.
- Own Segment = Employee prijavljenog korisnika je u `Segment.Employees`. Operacije nad cijelim Appointmentom traže
  `appointments.write.all`; Booking-wide own operacija traži vlasništvo nad SVIM pogođenim Segmentima.

## Posljedice
- Kod: `[RequireGrant(Grants.X, ...)]` (OR među navedenima), `ControllerExtensions.HasGrant` za `.all` unutar akcije,
  `GrantResolver` čita grantove iz baze po zahtjevu (cache ~30 s, bez invalidacije — svjesni trade-off). Grantovi nisu u JWT-u.
- Invarijanta `permissions.manage` se provjerava u `PermissionAdministrationSafetyService` prije svake promjene koja je
  može narušiti (grupe, dodjele, template upgrade, deaktivacija korisnika).
- Popis grantova: `Core/Shared/Grants.cs`. Capability definicije i default role templatei su samo autorski metapodaci.
- Deaktivirani User se odbija centralno (`ActiveUserGuard` u JWT `OnTokenValidated` i u ApiKey handleru).
- `users.role` / `UserRole` (Admin/Member/Reception) i dalje postoji i ide u `role` claim, ali nije sigurnosni mehanizam;
  njegova sudbina je otvoreno pitanje (ARCHITECTURE.md §7.2).
- Platformski operateri (`PlatformAccount`) imaju zasebnu `PlatformBearer` shemu bez veze s tenant Userima.
