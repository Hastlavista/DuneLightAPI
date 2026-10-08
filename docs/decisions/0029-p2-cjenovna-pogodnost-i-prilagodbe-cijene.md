# ADR-0029: P2 — Cjenovna pogodnost članarine i opći mehanizam prilagodbi cijene

- **Status:** Prihvaćeno, implementirano 2026-10-08 (faza 2E, migracija `20261027000012`)
- **Datum:** 2026-10-08 (P2 Decision Record: Q1, Q2, Q48 i odluke 2E u dnevniku)

> Izvor: [P2 Decision Record](../p2/P2_DECISION_RECORD.md). Za P2 detalje record ima prednost.

## Kontekst
Članarina osim pokrića daje i cjenovnu pogodnost na sesije koje klijent stvarno plaća (Q2). Sustav pogodnosti (promo kodovi,
pogodnosti po tagu, grupe klijenata) sigurno dolazi kasnije, pa se gradi opći mehanizam prilagodbi u kojem je članarina prvi i
u P2 jedini izvor (Q1: najbolja cijena, jedna prilagodba, bez slaganja; P2_PLAN §10.4).

## Razmotrene opcije
1. **Popust kao zasebno polje dugovanja** (cijena ostaje cjenik, settlement oduzima popust) — krši pravilo da je
   `Participation.Amount` izvor istine za cijenu.
2. **Prilagodba upisana u cijenu sudjelovanja** uz zapis izvora i evaluacije kandidata — cijena ostaje jedini izvor istine,
   a objašnjenje (zašto je pogodnost primijenjena ili nije) putuje sa snapshotom cijene.

## Odluka
- **Opcija 2.** Pravila pogodnosti su dio nepromjenjive verzije plana (`membership_plan_price_benefits`): izričit opseg
  `AllServices | Service` (prazna usluga nikad ne znači "sve"; "sve" uključuje i kasnije usluge), vrsta `PercentOff | AmountOff |
  FixedPrice`; pravilo usluge ima prednost; najviše jedno po usluzi i jedno "sve" po verziji. Plan pokriva barem jednu uslugu ILI
  ima pogodnost. Promjena pogodnosti je promjena uvjeta i ulazi u klasifikaciju Q48 (efektivno pravilo po usluzi).
- **Uvjeti** kao pokriće bez limita: članstvo vrijedi na datum sesije, poslovnica u opsegu plana, nije u pauzi, nije u dugu uz
  "ne pokrivaj"/"blokiraj" (uz "nastavi pokrivati" pogodnost ostaje). Samo nepokrivene sesije (ni članarinom ni paketom, Q2).
- **Izbor** (`Utils/PriceAdjustmentResolver`, čisto): najniža cijena ispod cjenika pobjeđuje; kod iste cijene fiksni redoslijed
  tipova `Membership → ClientTag → ClientGroup → Promo`, pa izvora. Rezultat zaokružen na 0,01, nikad ispod 0. Svaki kandidat dobiva
  ishod (`Applied | LostToBetterPrice | LostOnTie | NoReduction | NotApplicable` + razlog).
- **Zapis na sudjelovanju:** `adjustment_type`, `adjustment_source_id`, `adjustment_rule_snapshot` (jsonb), `adjustment_evaluation`
  (jsonb) uz postojeći `AdjustmentAmount` (predložena − osnovna). Opće re-cijenjenje (cjenik, ručni iznos, check-in) briše prilagodbu;
  ponovno je postavlja samo `IMembershipCoverageService`.
- **Automatska cijena** prati odluku o pokriću na istim događajima kao pokriće (rezervacija, otkaz/oslobođeno mjesto, pauza, otkaz
  i raniji izlazak članstva, promjena plana, obnova i dug, storno uplate, poništavanje prodaje, promjena vremena): pokrivena sesija =
  cjenik (Q9), nepokrivena = najbolja cijena. Zaštićeno: ručni iznos i aktivna novčana alokacija (razlog na projekciji pokrića).
  Svaka automatska promjena iznosa ide u audit (`AmountRepricedByMembership`, stara i nova cijena, događaj) i na projekciju
  pokrića (zadnja promjena). Sudjelovanje koje je u trenutku usklađivanja strane članstva zaključano drugom naredbom se preskače
  (`FOR UPDATE SKIP LOCKED`, bez deadlocka) i dobiva oznaku `PriceStale` do sljedeće obrade. Jamstvo (pregled 2E #4): pozadinski prolaz
  uz obnovu usklađuje sve zastarjele cijene, a checkout (dodavanje, plaćanje) i prijelaz u zauzimajuće stanje ih prvo usklađuju —
  zastarjela cijena se nikad ne naplaćuje. Fiksna cijena za člana koja nije niža od cjenika daje upozorenje pri čitanju plana.

## Posljedice
- Klijent bez članarine: polja prilagodbe ostaju prazna, cijene nepromijenjene (regresijski test).
- Shema tablice sudjelovanja dobiva nove stupce (shema-test ažuriran namjerno).
- Popust za članove na proizvode nije u P2 (zabilježeno za kasnije); osnovica provizije ("oduzmi popuste") dolazi u 2F.
