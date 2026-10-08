# ADR-0016: P1 — Initiator otkazivanja, lateness i vremenski guardovi

- **Status:** Prihvaćeno, implementirano 2026-10-06 (migracije `20261026000000` – `20261026000002`)
- **Datum:** 2026-10-05 (P1 Decision Record, odluke D2, D3)
- **Izvor:** [P1 Decision Record](../p1/P1_DECISION_RECORD.md)

## Kontekst
Danas su sva otkazivanja ista: Group povlačenja člana i otkazivanja Appointmenta dobivaju `IsLateCancellation` i izgledaju
kao klijentska otkazivanja; otkazivanje nakon starta je dopušteno i uvijek "kasno"; NoShow prije starta je moguć;
NoShow koristi `CancellationReason`; Participation nema `CancelledAt/By`.

## Razmotrene opcije
1. **Zadržati jedan tip otkazivanja** - politika bi kažnjavala i otkazivanja koja je inicirao studio ili sustav.
2. **Eksplicitni initiator + strukturirani metapodaci + vremenski guardovi.**

## Odluka
- **D2** `CancellationInitiator`: `Client | Business | System`. Politika se evaluira samo za Client. Initiator je odvojen od
  korisnika koji radi radnju (Client + CancelledBy = recepcionar je valjano). Nijedna naredba nema default initiator.
  Participation i Booking-wide cancel primaju Client | Business (obavezno); Appointment-wide cancel je samo Business.
  System postavlja samo interni kod (uklanjanje člana grupe, odznačavanje templatea). NoShow nema initiator.
  `booking.cancelled.v1` nosi `CancellationInitiator`.
- **D3** Client otkazivanje je kasno kad `Segment.PlannedStart − CancelledAt < CancellationWindowMinutes` (po Segmentu
  Participationa, jedan serverski timestamp, granica = na vrijeme, binarno, apsolutno trajanje između UTC instanata).
  Prozor 0 je valjan. Client cancel zahtijeva `CancelledAt < PlannedStart`, inače `CANCELLATION_AFTER_START`; Business i
  System smiju nakon starta. NoShow zahtijeva `now >= PlannedStart`, inače `ATTENDANCE_BEFORE_START` na svim putanjama;
  Appointment-wide no-show je atomaran.
- Metapodaci na Participationu: `CancellationInitiator`, `CancelledAt/By`, `CancellationReason` (opcionalno za Client,
  obavezno za Business, kod za System), `IsLateCancellation` (samo Client), `CancellationPolicyId/Version`,
  `AppliedCancellationWindowMinutes` (samo Client), `NoShowAt/By/Reason` (samo NoShow).

## Posljedice
- Nova polja na `booking_segment_participations`; novi kod greške `CANCELLATION_AFTER_START`.
- Business na Participation/Booking razini traži `appointments.write.all` + razlog (ADR-0018).
- Dug: Booking-wide Client cancel zadnjeg klijenta ostavlja Appointment Scheduled i slot zauzet (atomarni "oslobodi slot"
  je budući rad); automatska reaktivacija provenance (dug H) ostaje otvorena.
- Ne-ciljevi: tierovi, backdating ili timestamp od klijenta, prozori u lokalnom vremenu.
