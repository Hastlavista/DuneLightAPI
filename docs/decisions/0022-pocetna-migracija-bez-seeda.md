# ADR-0022: Početna (baseline) migracija trenutne sheme, bez seeda

- **Status:** Prihvaćeno, implementirano 2026-10-06
- **Datum:** 2026-10-06
- **Zapis segmenta:** [Baseline reset](../baseline-reset/BASELINE_RESET_DECISION_RECORD.md)

## Kontekst
Shema se gradila kroz 79 migracijskih datoteka (~9.000 redaka): preimenovanja, backfillovi, cutoveri koji grade stare
tablice pa ih prebacuju u nove, povlačenje grantova i predložaka. Nema podataka koje treba sačuvati (ADR-0003);
korisnik ručno briše shemu `dunelight` (lokalno i na produkcijskoj bazi) i pokreće migracije od nule.

## Razmotrene opcije
1. **Zadržati povijest migracija** - svaka promjena ostaje vidljiva, ali svaka nova baza prolazi kroz zastarjele
   prijelaze i seed koji više ne odgovara kodu.
2. **Jedna početna migracija trenutne ciljne sheme** - čisto, brzo, bez arheologije; povijest ostaje u gitu.

## Odluka
Sve dosadašnje migracije se brišu i zamjenjuju početnom migracijom koja gradi **samo trenutnu ciljnu shemu**,
podijeljenu u male korake (po jedna klasa po domeni, isti datum, redni brojevi).
- Migracije **ne seedaju ništa**: prazna baza nema nijedan redak. Podatke stvaraju isključivo aplikacijski tokovi
  (registracija organizacije itd.).
- Ne rekreiraju se prijelazne/povijesne migracije, stari predlošci, povučeni grantovi, compatibility stupci ni backfillovi.
- Početna migracija nema Down (nema stanja u koje bi se vraćala).

## Posljedice
- Pravilo "primijenjena migracija se ne mijenja ni ne renumerira" vrijedi za sve migracije nakon početne.
- Testovi cutover migracija se brišu (prijelazi više ne postoje); schema testovi ostaju i provjeravaju početnu shemu.
- Otvoreno pitanje o Down migracijama (ARCHITECTURE §7.2) zatvoreno za početnu migraciju; nove migracije i dalje bez
  razrađenih Down metoda (ADR-0003).
- Buduće migracije koje dodaju novi grant moraju ga dodati inicijalnim Admin grupama (ADR-0023) — to je promjena
  tenant podataka, ne seed.
- Postoji i produkcijska baza; do go-livea se tretira kao potrošna (korisnik je ručno resetira), vidi ADR-0024.
