# ADR-0025: P2 — Članarine kao zasebna domena; katalog planova s nepromjenjivim verzijama uvjeta

- **Status:** Prihvaćeno; katalog planova implementiran 2026-10-07 (faza 2A, migracije `20261027000000` – `20261027000001`)
- **Datum:** 2026-10-07 (P2 Decision Record, dizajn zatvoren 2026-10-07)

> Izvor: [P2 Decision Record](../p2/P2_DECISION_RECORD.md) i [P2 plan](../p2/P2_PLAN.md). Za P2 detalje record ima
> prednost pred ovim sažetkom. Ovaj ADR pokriva domenu i katalog planova; pokriće, naplata i provizije dobivaju vlastite
> ADR-ove u svojim fazama (2B–2F).

## Kontekst
Target Architecture v1 (§15) traži članarinu kao zasebnu domenu s ponavljajućim lifecycleom i naplatom; stari sustav je
nema (sve se modeliralo paketom ili uslugom). Članarina daje pravo na usluge (pokriće s limitima) i kasnije cjenovnu
pogodnost, obnavlja se automatski po periodima, a uvjeti plana se kroz vrijeme mijenjaju dok postojeća članstva moraju
zadržati uvjete pod kojima su kupljena.

## Razmotrene opcije
1. **Članarina kao podtip paketa** - koristi postojeći ledger i prodaju kroz checkout, ali paket nema periode, obnovu ni
   naplatu po periodu; Target Arch to izričito odbija.
2. **Plan kao jedan promjenjivi redak** - jednostavno, ali izmjena uvjeta retroaktivno mijenja postojeća članstva i nema
   povijesti.
3. **Plan (profil) + nepromjenjive verzije uvjeta** - isti obrazac kao P1 politike (ADR-0015); članstvo snapshotira verziju.

## Odluka
- Članarina je **zasebna domena** (`MembershipPlan`, kasnije `ClientMembership`, `MembershipPeriod`, `MembershipCharge`,
  ledger pokrića), nije podtip paketa.
- **Opcija 3:** `MembershipPlan` (naziv, opis, aktivnost, `MaxActiveMemberships`) + nepromjenjiva `MembershipPlanVersion`
  sa svim uvjetima koje klijent osjeti: cijena, početna naknada, interval (`Monthly | Yearly`), način obnove
  (`PurchaseDate | CalendarMonth`, kalendarski samo uz mjesečni interval), eksplicitni opseg poslovnica
  (`AllCompanies | SelectedCompanies`, prazna lista nikad ne znači "sve"), pokrivene usluge (barem jedna u 2A), limiti
  korištenja (plan ili usluga × `Period | Day | Week | Month | Quarter`, AND), minimalna obveza, otkazni rok, pravila pauze.
  Objava nove verzije = izmjena uvjeta (Version + 1 pod lockom plana); verzije se nikad ne mijenjaju ni brišu.
- Plan se ne briše, samo deaktivira (nema nove prodaje; postojeća članstva traju do kraja tekućeg perioda i ne obnavljaju se).
- Validacija prozora prema duljini perioda (Q49): prozor iste duljine kao period uz kredite nije dopušten; kraći mora biti
  manji, dulji veći od kredita istog opsega; limiti bez učinka su upozorenja.
- Nova referenca (usluga, poslovnica) mora biti aktivna; ona iz najnovije verzije smije ostati neaktivna (grandfathering).

## Posljedice
- Tablice: `membership_plans` (aktivni naziv unique, trim + case-insensitive), `membership_plan_versions` (unique plan +
  version, CHECK-ovi za interval/način obnove/pauzu), `membership_plan_version_services`, `membership_plan_version_companies`,
  `membership_plan_usage_limits` (unique verzija + opseg + prozor). Sve nose `organization_id`, djeca složeni FK na verziju.
- API `/api/membership-plans` (uske naredbe: details, capacity, versions, activate, deactivate); grantovi
  `catalog.memberships.view/.manage` (migracijom samo Admin grupama, ADR-0023).
- Verzije planova blokiraju trajno brisanje usluga i poslovnica (`IsReferenced`).
- U 2A nova verzija vrijedi samo za nove prodaje (Q14 default); prijenos na postojeća članstva s rokom najave i automatska
  klasifikacija povoljnih izmjena (Q48) dolaze s članstvima (2B).
- Dug: godišnja članarina po kalendarskoj godini (proporcionalni prvi period, prodaja više od mjesec dana unaprijed);
  plan samo s cjenovnom pogodnošću (2E mijenja pravilo "barem jedna usluga").
