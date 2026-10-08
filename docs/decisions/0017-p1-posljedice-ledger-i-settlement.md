# ADR-0017: P1 — Ledger posljedica, Due, paketna kazna, surplus i provizija

- **Status:** Prihvaćeno, implementirano 2026-10-06 (migracije `20261026000000` – `20261026000002`)
- **Datum:** 2026-10-05 (P1 Decision Record, odluke D4, D5, D6, D7, D8)
- **Izvor:** [P1 Decision Record](../p1/P1_DECISION_RECORD.md)

## Kontekst
Danas `ParticipationSettlement` ignorira status (Cancelled/NoShow zadržavaju punu cijenu kao dug), dashboard isključuje samo
Cancelled, checkout odbija Cancelled a prihvaća NoShow — tri različita implicitna pravila. Nijedna putanja ne troši paket
na Cancelled/NoShow.

## Razmotrene opcije
1. **Zamijeniti `Participation.Amount` naknadom** - gubi se cijena usluge i povijest.
2. **Nepromjenjiv ledger posljedica + jedna status-aware derivacija Due.**

## Odluka
- **D4** Verzija politike konfigurira dva događaja: LateCancellation (samo kasni Client cancel) i NoShow (svaki valjani).
  FeeType `None | Fixed (≥0) | Percentage (0–100)`; osnovica `Participation.Amount` (FinalPrice) u trenutku događaja;
  `finalFee = Round(min(rawFee, base), 2, AwayFromZero)`, `WasFeeCapped = rawFee > base`. Samo decimal aritmetika.
- **D5** Svaki primijenjeni događaj (i FeeType None) stvara nepromjenjiv `ParticipationPolicyConsequence`
  (unique ParticipationId + SourceVersion; status `Active | Waived | Reversed`), nikad brisan ni prepisan. MonetaryDue:
  Confirmed/Completed → postojeći service Due; Cancelled/NoShow s Active posljedicom → `CalculatedFeeAmount` (ili 0 ako je
  potrošen paket); inače 0. `Outstanding = MonetaryDue − Settled` (ne klampa se). Svi potrošači (dashboard, summary,
  read modeli) koriste istu derivaciju. Checkout prihvatljivost = pozitivan Outstanding, neovisno o statusu.
- **D6** `PackageAction: None | ConsumeUnit` po događaju — jedinica i naknada su ALTERNATIVE, nikad zbroj. Samo brojeni
  paketi; neograničeni nikad nisu izvor kazne. Odabir: postoji aktivno novčano podmirenje → naknada; eksplicitni
  `ClientPackageId` → mora biti prihvatljiv (`PACKAGE_NOT_ELIGIBLE`); jedan prihvatljiv → automatski; više →
  `PACKAGE_SELECTION_REQUIRED`; nijedan → naknada. `PackageConsumption.Trigger: ServiceCompletion | PolicyConsequence`.
- **D7** P1 nikad ne pomiče novac (nema voida, povrata, kredita, premještanja alokacija). Izveden informativni
  `SurplusAmount = max(Settled − MonetaryDue, 0)` — nije Client Credit.
- **D8** Događaji politike nikad ne generiraju proviziju; grupna session provizija ostaje nepromijenjena.

## Posljedice
- Nova tablica `participation_policy_consequences`; `package_consumptions` dobiva `Trigger` +
  `ParticipationPolicyConsequenceId?` (CHECK + parcijalni unique).
- Uklanja se lokalni `Status != Cancelled` filter u `OperationalDashboardService` i `CHECKOUT_ITEM_NOT_ELIGIBLE` za Cancelled.
- Uklanjaju se mrtvi `ReturnPackageEntry` / `ReturnEntryForClientIds`.
- Settlement DTO-ovi dobivaju `SurplusAmount`.
- Dug (P3): povrati, Client Credit, preraspodjela alokacija; jedinica + naknada zajedno; provizija na naknade.
