# ADR-0006: Lifecycle Participationa i derivacija statusa Appointmenta

- **Status:** Prihvaćeno
- **Datum:** 2026-10-05 (Decision Log v1 #9–#17; implementirano u M1A / M1A.1)

## Kontekst
Status je morao prijeći s Bookinga/Appointmenta na Participation (ADR-0005), a Appointment i dalje treba operativni status
za kalendar i zatvaranje.

## Razmotrene opcije
1. **Pohranjeni status na sve tri razine** - jednostavno čitanje, ali konflikti između razina.
2. **Jedini mutable status na Participationu, ostalo izvedeno** - jedan izvor istine; eksplicitne činjenice tamo gdje
   izvod nije dovoljan.

## Odluka
- Participation: `Confirmed | Completed | Cancelled | NoShow`. "Arrived" NIJE status (`ArrivedAt/ArrivedBy` su metapodaci).
  LateCancelled je klasifikacija unutar Cancelled, ne status.
- Booking status je izveden (`Confirmed/Completed/Cancelled/NoShow/Mixed`).
- Appointment: `Scheduled | Cancelled | Closed` (nema Completed). Derivacija:
  1. postoji Confirmed → Scheduled;
  2. inače postoji Completed ili NoShow → Closed;
  3. inače → Cancelled SAMO ako je Appointment eksplicitno otkazan, inače Scheduled.
- Eksplicitno otkazivanje Appointmenta je zasebna činjenica (`CancelledAt/By`). Otkazivanje svih klijenata jednog po jednog
  NIJE eksplicitno otkazivanje. Korekcija natrag na Confirmed briše trenutno eksplicitno otkazivanje.
- Group close-out (`ClosedOutAt/By`) je zasebna činjenica, nije status.
- Closed znači operativno riješeno, ne plaćeno.

## Posljedice
- Derivacija je u `Infrastructure/Utils/AppointmentLifecycle.cs`; svaka promjena Participationa ponovno izvodi status.
- `StatusVersion` se povećava po promjeni statusa i služi kao `SourceVersion` za efekte (provizije, potrošnja paketa).
- Booking-wide Client cancel zadnjeg klijenta ostavlja Appointment Scheduled i slot zauzet (dug, vidi ADR-0016).
- P1 mijenja tranzicijsku matricu i metapodatke otkazivanja (ADR-0016, ADR-0018).
