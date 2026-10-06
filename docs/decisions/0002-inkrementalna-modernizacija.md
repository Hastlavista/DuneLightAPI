# ADR-0002: Inkrementalna modernizacija umjesto prepisivanja

- **Status:** Prihvaćeno
- **Datum:** 2026-10-05 (odluka donesena ranije u arhitektonskom chatu; ovdje formalizirana)

## Kontekst
DuneLight je postojeća aplikacija s mnogo poslovnih pravila provjerenih s klijentom. Dokument "Target Architecture &
Business Rules v1" opisivao je čist rebuild (stari backend samo kao "donor"), a grana `claude-backend-branch`
(commit `96cac68`) bila je eksperiment takvog rebuilda. Istovremeno je karakterizacijska suite pokazala da postojeći kod
može postupno doći do ciljnog modela (faze S1–S3, Timezone, C, D1–D3, M0–M1H na grani `development-claude`).

## Razmotrene opcije
1. **Clean rebuild prema Target Architecture v1** - čista shema od nule; ali gubi provjereno ponašanje, dugo traje i
   dva sustava bi se morala usklađivati.
2. **Inkrementalna modernizacija postojećeg koda** - male faze s karakterizacijskim testovima kao mrežom; ciljni model se
   dostiže postupno, kroz jasne cutovere.

## Odluka
Postojeća aplikacija se modernizira inkrementalno. Nema prepisivanja od nule ni paralelne arhitekture. Poslovna pravila
iz Target Architecture v1 i Decision Loga v1 vrijede gdje nisu u sukobu s kasnijim odlukama; njihova strategija (rebuild)
je odbačena. `claude-backend-branch` / `96cac68` je samo povijest i nikad se ne uključuje.

## Posljedice
- Svaka netrivijalna promjena je uska faza: utvrdi cilj, pregledaj postojeće ponašanje, koristi postojeće obrasce,
  napiši pretpostavke, definiraj testove prije koda, čuvaj ponašanje osim ako ga odluka namjerno mijenja, odvoji nužne
  popravke od nepovezanog duga, napravi adversarial self-review.
- Karakterizacijski testovi (`UnitTests/Scheduling`) su mreža: test koji padne znači promjenu ponašanja koja mora biti namjerna.
- Završni izvještaj implementacije navodi: staro ponašanje/uzrok, što se promijenilo i zašto, što namjerno NIJE, utjecaj
  na autorizaciju, promjene baze, testove, rezultate build/testa, konkurentnost gdje je relevantna, preostali dug.
- Plan faza: P1 Policy engine → P2 Memberships → P3 Client Credit Ledger → P4 Notifications → P5 Group occurrence
  propagacija → P6 Workforce/catalog integritet.
