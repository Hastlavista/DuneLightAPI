# ADR-0012: Settlement po Participationu; paket ≠ plaćanje; isključivost

- **Status:** Prihvaćeno
- **Datum:** 2026-10-05 (Decision Log v1 #29–#34; implementirano u D3B3A, D3B3A.1/.2, D3B3B)

## Kontekst
Stari sustav je paket tretirao kao način plaćanja, a naplatu vezao uz Booking. Izvršenje i financijsko podmirenje su
nezavisni procesi (Completed ≠ plaćeno, prepayment je legitiman).

## Razmotrene opcije
1. **Paket kao PaymentMethod, settlement na Bookingu** - jednostavno, ali miješa pravo na uslugu s novčanim tokom.
2. **Settlement po Participationu, paket kao zasebna domena potrošnje.**

## Odluka
- Granica settlementa je Participation; checkout stavka je participation-native (CheckoutItem → PaymentAllocation → Payment).
- Izvedeno: Final, Due, Settled, Outstanding. Prepayment je dopušten. Prihod = stvarna plaćanja.
- Paket je zaseban od Membershipa. `PackageConsumption` je vezan uz Participation (ledger s reverzalom). Valjanost paketa se
  provjerava na DATUM IZVRŠENJA usluge (`DateOnly`). Potrošnja paketa nije prihod. Pokriće paketom ne mijenja FinalPrice.
- Paket i novac su međusobno isključivi po Participationu (`SettlementExclusivityPolicy`).
- Organizacijska postavka `PackageConsumptionTiming` postoji; jedina vrijednost je `OnCompletion`.

## Posljedice
- Derivacija je u `Utils/ParticipationSettlement.cs`; danas Due ignorira status (Cancelled/NoShow zadržavaju punu cijenu) —
  P1 to mijenja u jedinstvenu status-aware derivaciju (ADR-0017).
- Djelomična vrijednost paketa i miješano paket + novac su otvoreni (Decision Log #32, dug O).
- Client Credit, povrati i preraspodjela alokacija su faza P3.
