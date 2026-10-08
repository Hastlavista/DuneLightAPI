# ADR-0018: P1 — Korekcijska matrica, waiver i Group pravila

- **Status:** Prihvaćeno, implementirano 2026-10-06 (migracije `20261026000000` – `20261026000002`)
- **Datum:** 2026-10-05 (P1 Decision Record, odluke D9, D10, D12)
- **Izvor:** [P1 Decision Record](../p1/P1_DECISION_RECORD.md)

## Kontekst
Danas su korekcije asimetrične: Individual odbija Cancelled → Confirmed (400), Group dopušta bilo koji → bilo koji bez
guardova; ponovljeni cancel/no-show vraća `ALREADY_COMPLETED`; ručna plaćanja blokiraju korekciju
(`BOOKING_HAS_NON_REVERSIBLE_PAYMENT`); Group un-check-in nulira cijenu; stari razlozi i late oznake preživljavaju korekcije.

## Razmotrene opcije
1. **Zadržati odvojena pravila za Individual i Group** - već su dokazano nekonzistentna.
2. **Jedna tranzicijska matrica, eksplicitni waiver i grantovi za iznimke.**

## Odluka
- **D12** Jedna matrica za Individual i Group: svaka promjena u drugi status je dopuštena uz guardove ciljnog događaja
  (Client cancel prije starta, NoShow nakon starta, NoShow → Cancelled samo Business). Isti status je pravi no-op (bez
  StatusVersion, audita, outboxa, efekata). Terminal → terminal je jedna atomarna korekcija: reverziraj sve Active efekte,
  očisti stare metapodatke, StatusVersion +1 jednom, primijeni novi događaj, novi efekti s novim SourceVersion. Ručna
  plaćanja više ne blokiraju korekcije (ostaju kao settlement). Reaktivacija u zauzimajući status ponovno provjerava
  preklapanja, Room/Resource i Group soft kapacitet. Reverzija Active posljedice sa stvarnim efektom traži
  `appointments.policy.override` + razlog. Group Completed → ne-Completed više ne nulira cijenu. Kaskade (Booking-wide,
  Appointment-wide) mijenjaju samo Confirmed; bez ičega za promijeniti → `NO_ACTIVE_PARTICIPATIONS`.
- **D10** Waiver uklanja CIJELU posljedicu (status Waived, obavezni razlog, nepovratno u P1); klasifikacija se ne mijenja.
  U trenutku događaja (`WaivePolicyConsequence` + `WaiverReason`) ili naknadno
  `POST /api/participations/{id}/policy-consequence/waive`. Grant `appointments.policy.override` (samo Admin template),
  nikad ne širi own scope. Business initiator na Participation/Booking razini traži `appointments.write.all` + razlog.
  Audit `PolicyConsequenceWaived`.
- **D9** Group: close-out bez automatskog NoShowa (samo upozorenje `GROUP_APPOINTMENT_UNRESOLVED_BOOKINGS`); nema
  waivera kad se mjesto popuni s liste čekanja; auto-promovirani klijenti bez iznimke. Uklanjanje člana / odznačavanje
  templatea = System; otkazivanje cijelog occurrencea = Business. Attendance `Attended = false` → NoShow politika.
  Otkazivanje oslobađa mjesto i može promovirati FIFO; NoShow ne promovira.

## Posljedice
- Karakterizacijski testovi koji pinaju staro ponašanje se ažuriraju (17 namjernih promjena popisanih u P1 recordu), ne
  tretiraju se kao regresije.
- Novi grant `appointments.policy.override`; novi kod `PACKAGE_SELECTION_REQUIRED`, `CANCELLATION_POLICY_IN_USE`; reuse
  `ATTENDANCE_BEFORE_START`, `NO_ACTIVE_PARTICIPATIONS`.
- Ostaje dug: ponovljeno eksplicitno otkazivanje Appointmenta ponovno upisuje `CancelledAt/By` i audit; Appointment-wide
  NoShow istječe Group waitlist s razlogom AppointmentCancelled; automatski waiver kod popunjenog mjesta; djelomični waiver;
  backdated korekcija.
