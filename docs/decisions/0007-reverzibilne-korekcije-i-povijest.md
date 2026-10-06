# ADR-0007: Reverzibilne korekcije i čuvanje povijesti (untouched delete pravilo)

- **Status:** Prihvaćeno
- **Datum:** 2026-10-05 (Decision Log v1 #17–#18)

## Kontekst
Statusi moraju biti ispravljivi (i nakon Closed), a financijski i operativni efekti (provizije, paketi, plaćanja) moraju
ostati revizijski traživi. Stari kod je mogao brisati ili prepisivati povijest.

## Razmotrene opcije
1. **Update/delete na licu mjesta** - jednostavno, ali gubi trag i otvara dvostruke obračune.
2. **Korekcija kao domenska operacija s kompenzacijskim zapisima** - stari efekti se reverziraju novim zapisima, s
   `SourceVersion` identitetom.

## Odluka
- Lifecycle korekcije su reverzibilne. Povijesni efekti se reverziraju kompenzacijskim zapisima, nikad brisanjem ili
  prepisivanjem povijesti.
- Participation je "untouched" samo ako je: Confirmed, `StatusVersion == 0`, nema `ArrivedAt/By`, nema
  `CancellationReason`, nema late oznake, nema povijesti, settlementa, checkouta/plaćanja ni potrošnje paketa. Samo
  untouched se smije fizički obrisati; inače `REFERENCED_CANNOT_DELETE`. Izračun cijene sam po sebi nije povijest.

## Posljedice
- Pravilo je u `Infrastructure/Utils/ParticipationHistory.cs`.
- Ledgeri (`PackageConsumption`, `CommissionEntry`, plaćanja) nose status/reverzal metapodatke i unique po `SourceVersion`.
- Korekcija natrag na Confirmed automatski vraća Appointment iz Closed u Scheduled.
- P1 uvodi jedinstvenu korekcijsku matricu za Individual i Group (ADR-0018).
