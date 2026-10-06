# ADR-0011: Group templatei i selektivno sudjelovanje članova

- **Status:** Prihvaćeno
- **Datum:** 2026-10-05 (zatvara Decision Log v1 #36; implementirano u M1F, M1F.1, M1G)

## Kontekst
Group je imao jednu uslugu, jednog DefaultTrainera i jednu sobu. Multi-segment model (ADR-0005) traži da generirani
termin bude običan Appointment, a bilo je otvoreno sudjeluju li svi članovi u svim Segmentima.

## Razmotrene opcije
1. **Svi članovi automatski u svim Segmentima** - jednostavno, ali ne odgovara stvarnim grupama s izbornim dijelovima.
2. **Član bira podskup templatea** - eksplicitno sudjelovanje po Segmentu.

## Odluka
- Group = metapodaci + recurrence Slots + `SegmentTemplates[]` + Members + odabir templatea po članu.
- `GroupSegmentTemplate`: Service, StartOffset, Duration, `Employees[]`, PricingMode/PricingEmployeeId, Room?,
  Resources[], Capacity. Templatei bez trenera su valjani. Nema Group DefaultTrainera (`groups.default_trainer_id` uklonjen).
- Član bira PODSKUP templatea (barem jedan). Generirani occurrence = jedan Appointment s jednim Segmentom po templateu;
  član dobiva jedan Booking i Participation samo za odabrane templatee.
- Attendance je po Appointment + Client + Segment. Waitlist je po Segmentu (`SegmentId` obavezan), jedinstven po
  (Segment, Client), FIFO promocija, bez soft-capacity overridea.
- Izmjene templatea utječu samo na BUDUĆE generiranje.
- Generiranje i promjene članstva: `groups.membership_version`, Group redak `FOR SHARE`, do 3 pokušaja, zatim
  `CONCURRENCY_CONFLICT`.
- Selektori se nikad ne izvode implicitno ("ako postoji samo jedan Segment, uzmi njega" je zabranjeno); nedostaje li
  selektor, vraća se 400.

## Posljedice
- Group guest add i attendance traže eksplicitni Segment.
- Identitet occurrencea se provodi samo na razini aplikacije (dug I); propagacija izmjena templatea na postojeće
  occurrence je otvorena (dug E, faza P5).
- Group close-out u own scopeu traži vlasništvo nad svim Segmentima (dug B).
- Target Architecture v1 spominje waitlist prioritet po tagovima i auto/manual mod — nije implementirano (samo FIFO).
