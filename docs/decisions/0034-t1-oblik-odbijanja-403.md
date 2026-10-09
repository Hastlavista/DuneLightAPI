# ADR-0034: T1 — Oblik odbijanja 403 (razlog i svi grantovi koji nedostaju)

- **Status:** Prihvaćeno, implementirano
- **Datum:** 2026-10-09
- **Zapis faze:** [T1 record](../t1/T1_DECISION_RECORD.md) (T1-5); frontend FE-ADR-0003

## Kontekst
Nakon K2 (ADR-0032) mnoge radnje traže granularne grantove, a 403 je nosio naziv granta samo u poruci (`[RequireGrant]` čak
prazno tijelo). Frontend poruke ne prikazuje, nego prevodi kodove, pa korisnik nije vidio što mu nedostaje. Own opseg na tuđem
resursu bio je 409 `NOT_OWNER`, nerazlučiv od poslovnih pravila.

## Odluka
- Svaki 403 ima `error.details` (`ForbiddenDetails`): `reason` (`MissingGrant`, `OutOfScope`, `CompanyNotAssigned`),
  `requiredGrants` (SVI grantovi koji nedostaju), `match` (`All`/`Any`), uz `OutOfScope` `currentScope`/`requiredScope`, uz
  `CompanyNotAssigned` `companyId`.
- `ForbiddenAppException` se stvara samo tvorničkim metodama; `[RequireGrant]` i `[RequireGrantOrAssignedCompany]` vraćaju isti
  oblik. Oblik je opisan u Swaggeru (`ForbiddenErrorResponse`).
- Own opseg na tuđem resursu je **403 `OutOfScope`** (kod `NOT_OWNER` ostaje), umjesto dosadašnjeg 409.

## Posljedice
- Frontend prikazuje prevedene nazive svih grantova koji nedostaju ili poruku o opsegu (FE-ADR-0003).
- Namjerna promjena ponašanja: 409 → 403 za `NOT_OWNER` (karakterizacijski testovi označeni `CHANGED in T1`).
- Novo odbijanje u kodu mora koristiti tvorničke metode s grantovima koji nedostaju.
