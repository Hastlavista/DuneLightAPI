# ADR-0005: Appointment → Segment → Booking → Participation model

- **Status:** Prihvaćeno
- **Datum:** 2026-10-05 (Decision Log v1 #1–#13; implementirano kroz D1–D3B3 i M0–M1H)

## Kontekst
Stari Appointment je predstavljao jednu uslugu s jednim zaposlenikom, sobom, vremenom i cijenom, a Booking je nosio status
i cijenu. To nije moglo izraziti multi-service termine, više zaposlenika, grupe s djelomičnim sudjelovanjem ni različite
ishode po usluzi za istog klijenta.

## Razmotrene opcije
1. **Appointment = jedna usluga** (+ dodatna polja tipa `Service2Id`, `SecondaryEmployeeId`) - malo promjena, ne skalira.
2. **Appointment kao container sa Segmentima i Participationima** - jedan model za Individual i Group.

## Odluka
- **Appointment**: operativni container u točno jednoj Company. Nema Service, Employee, Room, trajanje ni cijenu. Sadrži
  Note, lifecycle činjenice, `Segments[]`, `Bookings[]`. `PlannedStart = min(Segment.PlannedStart)`,
  `PlannedEnd = max(Segment.PlannedEnd)`; praznine su dopuštene.
- **AppointmentSegment**: Service, `PlannedStart/End`, `ActualStart/End?`, `Employees[]` (ravnopravni, nema primarnog),
  `PricingMode`, `PricingEmployeeId?`, Room?, `Resources[]`. `Service.DefaultDuration` je samo prijedlog.
- **Booking**: jedan Client u jednom Appointmentu, unique (Appointment, Client). Nema status, cijenu ni settlement.
- **BookingSegmentParticipation**: najmanja izvršna i komercijalna jedinica, unique (Booking, Segment). Vlasnik statusa,
  `StatusVersion`, dolaska, otkazivanja/no-showa, cijene, paketa i settlementa. Per-client operacije adresiraju `ParticipationId`.

## Posljedice
- Zabranjeno vraćati `Appointment.ServiceId/EmployeeId/RoomId`, `Booking.Status`, `Booking.Price` ili settlement na
  Booking, ili uvoditi "primarnog" Employeeja.
- Read modeli izvode Booking summary (`BookingStatusSummary`, uklj. `Mixed`) i raspon Appointmenta.
- Multi-segment izvršenje se ne spljoštava u flat DTO-ove.
