# ADR-0027: P2 — Periodi, zaduženja i obnova članarine

- **Status:** Prihvaćeno, implementirano 2026-10-07 (faza 2C, migracije `20261027000006` – `20261027000008`)
- **Datum:** 2026-10-07 (P2 Decision Record: Q15, Q16, Q19, Q20/Q24, Q45, Q51 i odluke 2C)

> Izvor: [P2 Decision Record](../p2/P2_DECISION_RECORD.md). Za P2 detalje record ima prednost.

## Kontekst
Obnova i naplata su odvojene: obnova otvara novi period s novim pravom (domena članarine), a za period nastaje zaduženje koje
se naplaćuje kroz postojeći checkout (bez payment providera; kasnije Stripe naplaćuje isto zaduženje). Status članstva se
izvodi iz duga, ne sprema se kao "plaćeno". Kasnije se na naplatu veže račun/fiskalni zapis.

## Razmotrene opcije
1. **Spremljeni status plaćenosti na zaduženju** - jednostavni upiti, ali dva izvora istine (status i alokacije plaćanja).
2. **Plaćenost izvedena iz alokacija + projekcija samo za upite** - jedan izvor istine (isti obrazac kao settlement
   sudjelovanja), projekcija jedan pisac u istoj transakciji.

## Odluka
- **Opcija 2.** `membership_charges` sprema samo lifecycle `Open | WrittenOff | Voided` i nepromjenjiv iznos;
  `settled_amount`/`settlement_status` su projekcija koju piše samo `IClientMembershipHandler.RefreshChargeSettlement` nakon
  svake promjene alokacija (RecordPayment, VoidPayment), u istoj transakciji. Odluke (dug, poništavanje, otpis) se donose iz
  alokacija kroz `Utils/MembershipChargeSettlement`. Konačno = Paid ili WrittenOff; PartiallyPaid je za dug neplaćeno.
- **Periodi:** `membership_periods` materijalizira otvorene periode (granice iz `MembershipPeriodCalendar`, uvjeti i cijena
  snapshot); prvi period i zaduženje (te početna naknada) nastaju pri prodaji, dospijeće = datum početka.
- **Obnova:** `IMembershipRenewalService.CatchUp` je jedina putanja koja otvara periode i zaduženja; idempotentna (unique
  članstvo + početak), sustiže propušteno. Na granici obnove redom: automatski završetak nakon N neplaćenih perioda
  (postavka, default isključeno; razlog `NonPayment`, dug ostaje), primjena zakazanih uvjeta (prelazak na plan deaktiviran
  prije stupanja na snagu se ne primjenjuje, uz trajnu oznaku), završetak ako plan nije aktivan (`PlanDeactivated`). Pokreće je
  `MembershipRenewalBackgroundService` za sve organizacije ("danas" u zoni organizacije) i naredbe koje mijenjaju periode.
- **Sidro uvjeta:** `client_memberships.terms_anchor_on` — granice trenutnih uvjeta računaju se od datuma od kad vrijede
  (promjena uvjeta s drugačijim načinom obnove ili intervalom počinje novi niz); rolling 12 mjeseci pauza i dalje od početka.
- **Naplata:** stavka checkouta `MembershipCharge` (FK + lock zaduženja u otvorenom checkoutu, isti mehanizam kao sudjelovanje);
  iznos stavke = dio koji se plaća (djelomično dopušteno); checkout se zatvara samo potpuno plaćen, kao i dosad.
- **Dug (Q15):** stanje (`Current | InGrace | Delinquent`) iz najstarijeg nekonačnog dospjelog zaduženja i grace perioda;
  pauza nije dopuštena uz `Delinquent`. Pravila duga su postavka organizacije (`PUT /api/organization/settings/membership-debt`).
- **Otpis:** `POST /api/membership-charges/{id}/write-off` (zaseban grant), samo otvoreno nekonačno zaduženje izvan otvorenog
  checkouta; otpisuje se preostali dug. Poništavanje prodaje prebacuje sva zaduženja (i otpisana) u Voided, a zapis otpisa ostaje;
  izvještaj otpisa broji samo lifecycle WrittenOff (pregled 2C).
- **Minimalna obveza nakon promjene uvjeta (pregled 2C):** kraj obveze je kasniji od dosadašnjeg kraja (`commitment_floor_on`)
  i kraja obveze novih uvjeta brojeno od datuma promjene (`commitment_from_on`); pauza produljuje periode, pa i obvezu.

## Posljedice
- Outbox događaji članarina se ne pišu do P4 (događaj bez handlera se nikad ne označi obrađenim); P4 handleri rade iz stanja u bazi.
- Storno uplate postoji samo u otvorenom checkoutu (`VoidPayment`); zaduženje plaćeno kroz zatvoren checkout ostaje plaćeno.
- 2D (pokriće) koristi isto stanje duga (`MembershipChargeSettlement.Standing`) i iste periode.
