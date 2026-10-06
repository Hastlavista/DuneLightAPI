# ADR-0003: Politika razvojne baze — čista shema, bez compatibility slojeva

- **Status:** Prihvaćeno
- **Datum:** 2026-10-05 (formalizacija postojećeg pravila)

## Kontekst
Nema produkcijskih podataka. Shema se gradi isključivo FluentMigrator migracijama (`BlueDragon.DuneLight.DatabaseMigration`),
a razvojna baza se smije srušiti i ponovno izgraditi. Raniji cutoveri (D1–D3, M1B, M1H) pokazali su da compatibility
stupci i dual-write ostavljaju dvosmislene izvore istine.

## Razmotrene opcije
1. **Klasične produkcijske migracije** (dual-write, backfill, compatibility stupci, razrađene Down migracije) - sigurno za
   živi sustav, ali skupo i ostavlja zastarjele ugovore.
2. **Čista ciljna shema** - migracija ide ravno u ciljni oblik; zastarjeli stupci i API ugovori se uklanjaju.

## Odluka
Preferira se čista ciljna shema i uklanjanje zastarjelih stupaca i ugovora. Ne uvode se compatibility stupci, dual-write,
backfillovi, kompromisi radi migracije ni razrađene Down migracije. Karakterizacijski testovi i postojeće poslovno
ponašanje i dalje vrijede osim ako se pravilo eksplicitno mijenja.

## Posljedice
- Cutover migracija smije ispustiti stupce i podatke koji više nisu autoritativni.
- Verzija migracije = `[DeveloperMigration(god, mj, dan, Developer, redni_broj)]`; migracija koja je već primijenjena
  lokalno se ne mijenja ni ne renumerira — dodaje se nova.
- Neke postojeće migracije imaju pune Down metode (npr. `Migration_2026_10_22_MultiEmployeeSegments`); vidi otvoreno
  pitanje u ARCHITECTURE.md §7.1.
- Politika vrijedi dok postoji samo razvojna baza; prvi produkcijski deploy zahtijeva novi ADR.
