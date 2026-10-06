# ADR-0013: Vremenske zone — UTC instanti, DateOnly, efektivna zona Companyja

- **Status:** Prihvaćeno
- **Datum:** 2026-10-05 (implementirano u "Timezone foundation" fazi, popravak F-19)

## Kontekst
Raspored je ovisio o zoni hosta (F-19): `Npgsql.EnableLegacyTimestampBehavior` i konverzije preko lokalnog offseta
pomicale su odsutnosti, pauze i slobodne termine na ne-UTC hostovima (10 testova je padalo na CET hostu).

## Razmotrene opcije
1. **Sve u lokalnoj zoni hosta** - radi samo ako server i studio dijele zonu.
2. **UTC instanti + eksplicitna poslovna zona po Organization/Company** - neovisno o hostu.

## Odluka
- Organization ima IANA zonu (default `Europe/Zagreb`); Company je može nadjačati (`Company.TimeZone`, NULL = nasljeđuje).
  Efektivna zona = `Company.TimeZone ?? Organization.TimeZone` i mjerodavna je za lokalno raspoređivanje.
- Instanti su UTC (`DateTimeOffset`); poslovni datumi su `DateOnly` / PostgreSQL `date`. Nema `TimeZoneInfo.Local` ni
  `DateTime.Now` u jezgri. Legacy Npgsql timestamp ponašanje je uklonjeno.
- Ponavljanje čuva lokalno vrijeme kroz DST (proljetna rupa: pomak naprijed; jesensko preklapanje: prvo pojavljivanje).
- Preklapanja ostaju usporedbe UTC instanata.

## Posljedice
- `OrganizationCalendar` / `IOrganizationCalendarService` su jedino mjesto lokalno ↔ UTC konverzije.
- Radno vrijeme, slobodni termini, pauze, ponavljanje, generiranje grupa, dashboard dan i pravilo brisanja istog dana
  koriste efektivnu zonu Companyja; roster "danas" koristi primarnu Company zaposlenika.
- Testovi moraju prolaziti neovisno o zoni hosta (UTC, Europe/Zagreb, America/New_York, ...).
