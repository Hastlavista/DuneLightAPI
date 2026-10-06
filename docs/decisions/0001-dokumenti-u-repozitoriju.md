# ADR-0001: Arhitektonski dokumenti žive u repozitoriju

- **Status:** Prihvaćeno
- **Datum:** 2026-10-05

## Kontekst
Znanje o projektu bilo je raspoređeno po dugim chat razgovorima, koji s vremenom
gube kontekst i ne vide stvarni kod. Planovi i implementacija su se razilazili.

## Razmotrene opcije
1. **Jedan dugi chat kao "memorija"** - jednostavno, ali kontekst degradira i nema verzioniranja.
2. **Dokumenti u repozitoriju uz CLAUDE.md** - jedan izvor istine, verzioniran s kodom,
   čitaju ga i ljudi i Claude.

## Odluka
Arhitektura se vodi u `docs/ARCHITECTURE.md`, odluke u `docs/decisions/`,
a pravila rada u `CLAUDE.md`.

## Posljedice
- Novi chatovi i sesije mogu krenuti "od nule" bez gubitka znanja.
- Ažuriranje dokumenata postaje dio svake veće promjene.
