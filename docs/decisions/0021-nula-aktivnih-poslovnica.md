# ADR-0021: Organizacija smije imati nula aktivnih poslovnica

- **Status:** Prihvaćeno, nije implementirano
- **Datum:** 2026-10-06

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
