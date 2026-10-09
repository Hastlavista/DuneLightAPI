# ADR-0033: T1 — Jedan poslovni sat organizacije (TimeProvider) i simulirani pomak za testiranje

- **Status:** Prihvaćeno, implementirano
- **Datum:** 2026-10-09
- **Zapis faze:** [T1 record](../t1/T1_DECISION_RECORD.md), [T1 plan](../t1/T1_PLAN.md)

## Kontekst
Kod je "sada" čitao izravno sa sata (252 mjesta). Vremenski ovisna pravila (obnova članarina, grace i dug, zatvaranje termina,
otkazni prozori, roster u prošlosti, validacija prošlosti) nisu se mogla ručno testirati bez čekanja ili SQL-a, a razvojni prolaz
obnove za zadani datum pomicao je samo obnovu, ne ostala pravila. Frontend (F1) mora prikazivati isti "danas" kao backend.

## Razmotrene opcije
1. **Zaseban razvojni mehanizam po pravilu** (kao dosadašnji prolaz obnove) — nedosljedno, svako pravilo ima svoj "danas".
2. **Jedan `TimeProvider` kroz DI, pomak po organizaciji** — sva pravila gledaju isti sat; pomak samo naprijed i samo u
   testnim alatima.

## Odluka
- Sva poslovna pravila "sada" čitaju kroz injektirani `TimeProvider` (`BusinessTimeProvider`: stvarni sat + pomak organizacije
  iz `OrganizationClockContext`). Izravno čitanje sata nije dopušteno; čista pravila (`Utils`) primaju `now` parametrom.
- Sistemske stvari (istek JWT-a, obrada outboxa, bootstrap) koriste `TimeProvider.System` i nikad se ne pomiču.
- Pomak sata je **privremeni testni alat** (`TestTools:Enabled`, nikad u Production — pokretanje se odbija): samo naprijed,
  najviše 400 dana po skoku, iz Management portala; propušteni prolazi obnove izvršavaju se dan po dan. Povratak na stvarno
  vrijeme = reset demo organizacije (nova organizacija); u ne-demo organizaciji pomak je trajan.
- `GET /api/organization/clock` je trajni izvor "sada"/"danas" za frontend (frontend FE-ADR-0005).
- Testovi koriste isti mehanizam: fiksni testni sat ispod `BusinessTimeProvidera`, drugi dan samo pomakom sata organizacije.

## Posljedice
- Novi kod nikad ne koristi `DateTime(Offset).Now/UtcNow` (pravilo u `CLAUDE.md`).
- Prije go-livea se uklanjaju testni alati (pomak sata, seed) i tablica `test_tool_organizations`; poslovni sat i endpoint ostaju.
- Zatvorena tehnička stavka ARCHITECTURE §7.4 "Nema apstrakcije vremena".
