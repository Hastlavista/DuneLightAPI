# ADR-0024: Produkcijska baza je potrošna do go-livea

- **Status:** Prihvaćeno
- **Datum:** 2026-10-06
- **Zapis segmenta:** [Baseline reset](../baseline-reset/BASELINE_RESET_DECISION_RECORD.md)
- **Dopunjuje:** ADR-0003 ("prvi produkcijski deploy zahtijeva novi ADR")

## Kontekst
ADR-0003 dopušta tretirati bazu kao potrošnu (drop sheme, migracije bez backfilla i kompatibilnih slojeva), ali samo dok
postoji isključivo razvojna baza. Uz lokalnu bazu postoji i produkcijsko okruženje s vlastitom bazom; njen
connection string migracijski runner čita iz varijable okoline (`--c=Production`), a migracije na njoj korisnik
pokreće ručno. Pri uvođenju početne migracije (ADR-0022) ta je baza ručno resetirana. Trebalo je odlučiti vrijedi li
politika ADR-0003 i za nju.

Stanje 2026-10-06: produkcijska baza sadrži samo testne/demo podatke; nitko je stvarno ne koristi.

## Razmotrene opcije
1. **Produkcijska pravila odmah** - migracije čuvaju podatke, bez resetiranja; sigurno, ali skupo dok nema što čuvati.
2. **Potrošna do go-livea** - politika ADR-0003 vrijedi i za produkcijsku bazu dok korisnik ne proglasi go-live.

## Odluka
Opcija 2.
- Dok produkcijska baza sadrži samo testne/demo podatke, za nju vrijedi ADR-0003: smije se resetirati (drop sheme
  `dunelight` + migracije od nule), bez backfilla i kompatibilnih slojeva.
- Nema unaprijed određenog roka. Go-live (prvi stvarni korisnik/podaci) proglašava korisnik; tada se piše novi ADR koji
  zamjenjuje ovaj, i od tog trenutka produkcijska baza se više ne resetira.
- Pravilo za migracije nakon go-livea koje je već odlučeno: **svaka migracija se prije pokretanja na produkciji testira**
  (nad lokalnom bazom ili kopijom produkcijske baze).
- Produkcijske migracije pokreće korisnik ručno; Claude ih ne pokreće.

## Posljedice
- Do go-livea razvoj ostaje jednak razvoju nad lokalnom bazom; reset produkcije je dopušten, ali ga radi korisnik.
- Prije go-livea treba odlučiti (i upisati u novi ADR) ostala produkcijska pravila koja ovdje NISU odlučena: backup
  prije migracije, kako se mijenjaju stupci s podacima (dodaj → popuni → ukloni u kasnijoj migraciji), postupanje kod
  pogrešne migracije (Down ili popravak novom migracijom).
- Produkcija ne smije dobiti stvarne podatke dok taj ADR ne postoji.
