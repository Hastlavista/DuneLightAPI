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

## Uključiv kraj (T1-10, 2026-10-09)
Odluka (T1 dnevnik "Tri odluke T1 (1)"): **svaki "vrijedi do / završava" dan (`DateOnly`) uključuje taj dan** — kao cjenik i
izvještaj provizija. Datumi "od" (`EffectiveFrom`, `DeactivatedFrom`, `PendingEffectiveOn`, početak perioda, `DueOn`) nisu "do" i
ostaju kakvi jesu.

- **Promijenjeno (CHANGED in T1):** fond godišnjeg — `LeaveFundYearCalculator.IsExpired` = danas > `ExpiresAt`;
  `LeaveFundHandler.GetEligible` = `ExpiresAt >= asOf` (prije: na dan `ExpiresAt` fond je već istekao).
- **Već uključivo, bez promjene:** kraj članstva `EndsOn`, kraj perioda članstva, `ValidUntilDate` paketa, kraj pauze (klijentove i
  sustavne `CompanyClosure`), zadnji dan grace perioda (`DueOn + GraceDays`), datum otkaza članstva (zapisuje se kao `EndsOn` = zadnji
  dan), `ValidTo` cjenika, `DateTo` roster zapisa, kraj ponavljajućeg niza, `ToDate` generiranja grupe.
- Puna tablica (datoteka:redak, usporedba, razlog) i otvorena pitanja: [T1 record, T1-10](../t1/T1_DECISION_RECORD.md).

## Dopuna T1-11 (2026-10-09): trajanje "N dana"
Odluka (T1 dnevnik "T1-11 (1)"): **"N dana" znači točno N kalendarskih dana uključujući prvi dan.** Paket kupljen 1.10. s valjanošću
"30 dana" vrijedi do 30.10. uključivo; "3 mjeseca" od 5.10. → do 4.1. (zadnji dan = početak + trajanje − 1 dan).

- **Promijenjeno (CHANGED in T1):** `PackageExpiryCalculator` za `DayCount` = dan kupnje + N − 1 (prije + N, tj. N + 1 dan uz uključiv
  kraj). `EndOfMonth` i `FixedDate` bez promjene.
- **Već u skladu, bez promjene:** periodi članstva (mjesečno/godišnje, kraj = sljedeći početak − 1), pauza po danima i `MaxPauseDays`
  (`EndsOn − StartsOn + 1`), pomak perioda zbog pauze, 12-mjesečni prozor limita pauza, otkazni rok (`zahtjev + N − 1` = najraniji zadnji
  dan), rok najave izmjene plana (dan objave je 1. dan, novi uvjeti najranije od dana N + 1).
- **Grace:** zadnji grace dan = `DueOn + GraceDays` (grace su dani nakon dana dospijeća); nije mijenjano — ako grace treba uključivati
  dan dospijeća, to je zasebna odluka.
- Tablica: [T1 record, T1-11](../t1/T1_DECISION_RECORD.md).
