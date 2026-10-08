# ADR-0026: P2 — Članstvo: uvjeti kao verzija plana, izvedeno stanje i deterministička matematika perioda

- **Status:** Prihvaćeno, implementirano 2026-10-07 (faza 2B, migracije `20261027000002` – `20261027000005`)
- **Datum:** 2026-10-07 (P2 Decision Record: Q3, Q5/Q12, Q10, Q14, Q19, Q22, Q45 – Q48 i odluke 2B)

> Izvor: [P2 Decision Record](../p2/P2_DECISION_RECORD.md), [P2 plan](../p2/P2_PLAN.md) §20. Za P2 detalje record ima prednost.

## Kontekst
Članstvo traje kroz periode koji se obnavljaju, mijenjaju pauzama (produljenje ili preskok), otkazom (otkazni rok, minimalna
obveza), ranijim izlaskom i promjenom uvjeta od obnove. Retci perioda i zaduženja dolaze tek s naplatom (2C), a lifecycle
naredbe (otkaz, pauza, promjena plana) trebaju granice perioda već sada. Izmjena plana nikad ne smije retroaktivno promijeniti
uvjete postojećeg članstva.

## Razmotrene opcije
1. **Snapshot uvjeta kopiranjem na članstvo + spremljeni status i retci perioda unaprijed** - dupli podaci, status se mora
   održavati pri svakoj promjeni datuma, pauza bi morala prepravljati retke perioda.
2. **Uvjeti = referenca na nepromjenjivu verziju plana; stanje izvedeno; granice perioda izračunate čistom funkcijom** -
   jedan izvor istine, pauze i promjene uvjeta su ulazi u izračun, bez prepravljanja zapisa.

## Odluka
- **Opcija 2.** `client_memberships.plan_version_id` pokazuje na nepromjenjivu verziju plana (ADR-0025) — to je snapshot.
  Zakazana promjena uvjeta (`pending_*`, izvor `ClientPlanChange | PlanUpdate`) primjenjuje se na obnovi; klijentova promjena
  ima prednost, a izmjenu plana koju istisne pamti s izvornim datumom (`displaced_*`), pa povlačenje promjene vraća stanje kao
  da je nije bilo (pregled 2B). Izmjena plana koja bi stvorila preklapanje se ne primjenjuje i ostavlja trajnu oznaku na
  članstvu (`plan_update_skipped_*`) dok je kasnija uspješna izmjena ne riješi.
- Stanje (`Scheduled | Active | Paused | Ended | Voided`) se **ne sprema**; izvodi ga `Utils/MembershipLifecycleRules.State`
  iz datuma (zona organizacije, Q22), pauza, `ends_on` i `voided_at`.
- Granice perioda računa `Utils/MembershipPeriodCalendar` (čisto): od izvornog sidra s kraćenjem kraja mjeseca (Q3),
  kalendarski mjeseci s kratkim prvim periodom uz punu cijenu, Days pauza pomiče sve kasnije granice, SkipPeriods preskače
  cijele periode, raniji povratak otvara period od dana povratka (Q47). Zakazani datum promjene uvjeta je **donja granica**:
  promjena stupa na snagu na prvoj obnovi na ili nakon njega, pa pauze ne ostavljaju zastarjele datume.
- Datum od kad otkaz djeluje = max(kraj tekućeg perioda, otkazni rok, minimalna obveza u nepauziranim periodima); prije
  početka = kraj prvog perioda. Pauze koje još nisu počele se ne računaju, jer ih zahtjev za otkaz poništava.
- Pauze: limiti su **zbroj** dana/perioda i broj pauza u rolling 12 mjeseci od početka članstva; pauza nije dopuštena uz
  zakazan završetak.
- Preklapanje članarina (Q10/Q46) je čista provjera po vremenu + uslugama + poslovnicama, uključujući zakazane promjene.
- Svaka naredba je jedna transakcija pod zaključanim retkom članstva; lock redoslijed klijent → članstvo → plan (prodaja:
  klijent → plan; objava plana: plan → članstva). Svaka radnja ima zaseban grant.

## Posljedice
- Tablice `client_memberships`, `membership_pauses`, `client_membership_audit_log`; `organization_settings.membership_change_notice_days`.
- 2C pretvara izračunate periode u retke perioda i zaduženja (obnova primjenjuje zakazane uvjete); 2D veže claimove pokrića
  na iste granice. Matematika perioda je jedino mjesto granica — 2C/2D je ne smiju duplicirati.
- `MembershipPlanChangeClassifier` mora uspoređivati ili izričito izuzeti svako svojstvo verzije plana (test refleksijom).
