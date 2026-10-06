# ADR-0021: Organizacija smije imati nula aktivnih poslovnica

- **Status:** Prihvaćeno, implementirano 2026-10-06
- **Datum:** 2026-10-06
- **Zapis segmenta:** [Foundation cleanup](../foundation-cleanup/FOUNDATION_CLEANUP_DECISION_RECORD.md)

## Kontekst
Kod blokira deaktivaciju zadnje aktivne poslovnice (`CompanyService`, `LAST_ACTIVE_COMPANY`). Target Architecture v1 §2 i
Dodatak A kažu da Organization smije imati nula aktivnih Companyja, a povijesne reference na neaktivne Companyje ostaju
čitljive i netaknute.

## Razmotrene opcije
1. **Zadržati blokadu** - organizacija uvijek ima barem jednu aktivnu poslovnicu, ali se ne može privremeno "ugasiti".
2. **Dopustiti nula aktivnih** - deaktivacija zadnje poslovnice je dopuštena; neaktivna Company blokira samo nove odnose.

## Odluka
Deaktivacija zadnje aktivne poslovnice je dopuštena. Organizacija smije imati nula aktivnih poslovnica.

## Posljedice
- Uklanja se provjera i kod greške `LAST_ACTIVE_COMPANY` (ako ga više ništa ne koristi).
- Ostaje pravilo da neaktivna Company ne prima nove termine, stavke cjenika ni dodjele.
- Treba provjeriti mjesta koja pretpostavljaju barem jednu aktivnu Company (onboarding, dashboard, primarna Company
  zaposlenika, odabir poslovnice) — nalazi se bilježe pri implementaciji.
- Što se događa s budućim terminima deaktivirane poslovnice ostaje dio otvorene politike deaktivacije kataloga (dug M, faza P6).

## Implementacija (2026-10-06)
- `CompanyHandler.Deactivate` više ne lockira (`SELECT ... FOR UPDATE`) ni ne broji ostale aktivne poslovnice — lock je
  postojao samo za tu provjeru; deaktivacija dira samo ciljnu poslovnicu (ponovljena deaktivacija je idempotentna).
- Uklonjeni `CompanyDeactivationOutcome.Blocked`, odgovarajuća grana u `CompanyService.Deactivate` i
  `ErrorCodes.LastActiveCompany` (`LAST_ACTIVE_COMPANY`, nije se koristio nigdje drugdje). Frontend: API taj kod više
  ne vraća.
- Testovi: `ZeroActiveCompaniesTests` (2).

### Nalazi audita mjesta koja pretpostavljaju aktivnu poslovnicu
- **Onboarding:** `HasCompany` ostaje "postoji AKTIVNA poslovnica" — ako su sve deaktivirane, wizard ponovno prikazuje
  korak poslovnice. Bez promjene koda.
- **Dashboard:** po poslovnici, već prikazuje i neaktivne poslovnice. Bez promjene.
- **Primarna poslovnica zaposlenika:** već je mogla biti neaktivna. Bez promjene.
- **Kreiranje zaposlenika i termina:** i dalje traži aktivnu poslovnicu (ispravno — neaktivna Company blokira nove odnose).
