# ADR-0014: Uske poslovne naredbe umjesto generičkog CRUD-a

- **Status:** Prihvaćeno
- **Datum:** 2026-10-05 (Decision Log v1 #39; implementirano u M1E i M1H)

## Kontekst
Stari `PUT /api/appointments/{id}`, `PATCH .../move`, `CompleteExisting` i flat request modeli mijenjali su cijeli agregat
odjednom; karakterizacija je pokazala niz defekata (izmjena Completed termina, gubitak ručne cijene, predaja termina
drugom zaposleniku u own scopeu...).

## Razmotrene opcije
1. **Jedan veliki Update agregata** - malo endpointa, ali miješa autorizaciju, validaciju i side-effecte.
2. **Uske naredbe po granici vlasništva** - Appointment metapodaci / Segment / Participation.

## Odluka
- Princip: uske poslovne naredbe, nikad generički `PUT` cijelog agregata.
- Appointments: create, CompleteNow, recurring create, GET, add Segment, add Client, edit note
  (`PATCH /api/appointments/{id}/note`), cancel, no-show, delete.
- Segments (`/api/segments/{id}`): time, service, employees, pricing-source, room, resources; delete.
- Participations (`/api/participations/{id}`): status, cancel, no-show, confirm, price, delete.
- Bookings: list, Booking-wide cancel, payments, Group guest add uz eksplicitni Segment.
- Groups: create, update metapodataka, SegmentTemplates, Slots, Members s odabirom templatea, generiranje, attendance,
  close-out (`PATCH /api/groups/appointments/{id}/complete`).
- **CompleteNow** (`POST /api/appointments/complete`) je legitimna namjenska naredba: jedan Segment, klijenti, atomarno
  završavanje i naplata u jednoj transakciji, preko istog lifecycle koda (StatusVersion = 1).
- Uklonjeno: `PUT /api/appointments/{id}`, `PATCH .../move`, `PATCH .../{id}/complete`, `POST /api/appointments/schedule`,
  Booking-adresirani confirm/no-show i stari flat request modeli.
- Promjena Companyja na Appointmentu NE postoji (ako zatreba: zaseban use case `MoveAppointmentToCompany`).
- Recurring create namjerno zadržava "flat" SERIES request — nije svaki flat request legacy.

## Posljedice
- Svaka naredba ima vlastitu autorizaciju (own/all po Segmentu), validaciju i audit.
- CompleteNow ne smije zaobilaziti lifecycle servise.
- Nove funkcionalnosti dodaju nove uske naredbe umjesto proširivanja postojećih.
