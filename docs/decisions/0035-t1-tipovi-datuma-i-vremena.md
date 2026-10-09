# ADR-0035: T1 — Tipovi datuma i vremena u API-ju, entitetima i bazi

- **Status:** Prihvaćeno, implementirano (T1-7)
- **Datum:** 2026-10-09
- **Zapis faze:** [T1 record](../t1/T1_DECISION_RECORD.md) (dnevnik "Tipovi datuma i vremena", "T1-7 …"); proširuje [ADR-0013](0013-vremenske-zone.md)

## Kontekst
Nalaz T1-6: poslovni dani (rođendan, zaposlenje, cjenik, kupnja paketa, fond godišnjeg, kraj niza, generiranje grupa, roster,
praznici, dostupnost, dashboard, izvještaj provizija) putovali su kao `DateTimeOffset`, a vrijeme dana kao `TimeSpan`. Dan kao
instant se pri pretvorbi zone pomakne na prethodni/sljedeći dan; servisi su dan "pogađali" iz offseta ulazne vrijednosti.

## Odluka
- **Trenutak → `DateTimeOffset`; dan u kalendaru → `DateOnly` ("yyyy-MM-dd", PostgreSQL `date`); vrijeme dana bez datuma →
  `TimeOnly` ("HH:mm:ss", PostgreSQL `time`); trajanje → `TimeSpan` (samo tu).** Isto u DTO-ovima, query parametrima,
  entitetima i bazi. Swagger prikazuje `format: date` / `format: time`.
- Dan cjenika termina = lokalni datum POČETKA segmenta u efektivnoj zoni poslovnice termina (termin preko ponoći pripada danu
  početka) — jedino mjesto: `IPricingService.ResolveForServiceStart`. Stavka cjenika vrijedi `ValidFrom <= dan <= ValidTo`
  (oba kraja uključena); preklapanje se provjerava po danima, pa `A.ValidTo == B.ValidFrom` je preklapanje.
- Izvještaj provizija: `From`/`To` su dani u zoni ORGANIZACIJE, oba kraja uključena; servis ih pretvara u instante
  `[početak From, početak To+1)` i zadržava dosadašnje brojanje po događajima (`EarnedAt` / `ReversedAt`).
- `ClientPackage.PurchaseDate` je poslovni dan kupnje u zoni poslovnice prodaje (checkout: današnji dan po poslovnom satu u zoni
  poslovnice checkouta; ručni upis: zadani dan ili današnji dan u zoni poslovnice, bez nje organizacije). Trenutak prodaje
  ostaje `CreatedAt` i checkout. Valjanost (`ValidUntilDate`) se računa iz tog dana.
- Kraj ponavljajućeg niza (termini, pauze) je zadnji lokalni dan niza u zoni poslovnice, uključivo
  (`OrganizationCalendar.RepeatAtLocalTime(first, lastDate, step)`); `EndDate` ne smije biti prije lokalnog dana prve pojave.
- Fond godišnjeg: `OpenedAt`/`ExpiresAt` su dani; fond se smije trošiti zaključno s `ExpiresAt` (danas po kalendaru
  organizacije), `IsExpired` = danas > `ExpiresAt`.
- Istek tokena (`AuthResponse.TokenExpiration`, `PlatformAuthResponse.TokenExpiration`) je instant po SISTEMSKOM satu.
- Unutarnja aritmetika vremena dana (radno vrijeme, slobodni termini) ostaje `TimeSpan` pomak od ponoći
  (`WorkingHoursCalculator.Interval`); pretvorba u `TimeOnly` je samo na rubu (entitet / DTO).

## Posljedice
- Promjena ugovora: frontend šalje i prima "yyyy-MM-dd" / "HH:mm:ss" (ponovno generirati tipove iz Swaggera, FE-ADR-0004).
  Instant poslan u polje dana je 400.
- Migracija `20261030000001` (T1DateTypes) pretvara postojeće `timestamptz` stupce u `date` u mjerodavnoj zoni (vidi migraciju).
- `CalendarDates` (pogađanje dana iz offseta) je uklonjen.
- Karakterizacijski i ugovorni testovi s promijenjenim očekivanjem nose oznaku `CHANGED in T1`.
