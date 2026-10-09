# ADR-0031: K1 — dorade rasporeda i klijenata (validacija prošlosti, dolazak, razlozi, vraćanje termina, zadani resursi, stajanje članstva)

- **Status:** Prihvaćeno, implementirano 2026-10-08 (migracije `20261028000000` – `20261028000003`)
- **Datum:** 2026-10-08
- **Izvor:** [K1 Decision Record](../k1/K1_DECISION_RECORD.md), [povratne informacije klijenta](../klijent/POVRATNE_INFORMACIJE_v1.md)

## Kontekst
Klijent je validirao vodič poslovnih pravila. Dio odgovora traži male dorade postojećih tokova, a dvije od njih diraju
ranije odluke: P1 pravilo "otkaz studija (Business) traži razlog" i P2 matematiku perioda članarine (zatvorena poslovnica).

## Razmotrene opcije
1. **Zasebne odluke po stavci bez ADR-a** — dorade su male, ali promjene P1/P2 pravila ne bi bile vidljive u indeksu odluka.
2. **Jedan ADR za fazu K1** koji sažima namjerne promjene ponašanja i nove koncepte; detalji su u zapisu faze.

## Odluka
Opcija 2. Zaključano:
- **Validacija prošlosti (K1-1):** "Upiši odrađeno" provjerava radnu snagu i za prošli početak (kao ADR-0008), uz isti override.
- **Dolazak (K1-2):** uska naredba `PATCH/DELETE api/participations/{id}/arrival`, grant `appointments.arrival.mark`.
  - Metapodatak samo uz Confirmed/Completed (DB CHECK).
  - Izostanak/otkaz ga briše uz trajni trag (`AppointmentAuditLog` "Arrival").
  - Nije uvjet za odradu.
- **Broj člana (K1-3):** automatski (najveći + 1) pod advisory lockom organizacije; ručni ≥ 1, jedinstven.
  - Skok > 1000 traži potvrdu.
  - Utrka → `DUPLICATE_MEMBER_NUMBER`.
- **Šifrarnik razloga (K1-4):** `cancellation_reasons` po organizaciji (događaji otkaz klijenta / otkaz studija / izostanak).
  - Snapshot naziva na sudjelovanju i otkazanom terminu.
  - Obaveznost po postavci organizacije (vrijedi samo uz aktivnu šifru).
  - **Mijenja P1:** razlog otkaza studija = slobodni tekst ILI šifra. Otpis i korekcija i dalje traže tekst. Bez utjecaja na
    politiku naplate.
- **Vrati termin (K1-5):** `POST api/appointments/{id}/restore` vraća samo sudjelovanja otkazana otkazom termina.
  - Prepoznaju se po initiatoru Business i istom trenutku otkaza kao termin; otkaz termina i kaskada dijele timestamp.
  - Sve ili ništa uz tvrde provjere; pokriće članarinom ponovno evaluirano.
  - Lista čekanja se ne vraća (upozorenje s popisom).
- **Zadani resursi usluge (K1-6):** `service_default_resources`; primjenjuju se kad zahtjev ne navodi resurse (`Resources = null`).
  - Promjena usluge ih zamjenjuje zadanima nove usluge.
  - Predložak grupe ih kopira pri kreiranju.
  - Kapacitet ostaje tvrd (ADR-0008); ponavljajući niz pri sudaru navodi sve datume (`RESOURCE_CAPACITY_EXCEEDED`,
    `details.conflicts` s resursom, kapacitetom i zauzetošću).
- **Generiranje grupa (K1-7):** grupe neaktivnih poslovnica i datumi praznika se preskaču i navode
  (`GenerateGroupAppointmentsResult.Skipped`); praznik se uz `OverrideAvailability` generira s upozorenjem.
- **Stajanje članstva (K1-8):** kad su sve poslovnice opsega verzije plana neaktivne, obnova na granici otvara **sustavnu pauzu**
  (`membership_pauses.source = CompanyClosure`, bez kraja) i ne otvara periode ni zaduženja.
  - Ponovna aktivacija je zatvara:
    - od datuma kupnje: preskočeni razmak, novi period od dana aktivacije, tekući period ostaje kakav jest;
    - kalendarski: preskočeni mjeseci do kraja mjeseca aktivacije (Q47 ručno).
  - Ne troši klijentove limite pauza; dok traje nema nove pauze.
  - Prikaz je zaseban od klijentove pauze: stanje članstva `StandingStill` (+ `StandingStillSince`), razlog nepokrivenosti
    termina `MembershipStandingCompanyClosed`.
  - **Mijenja P2:** otkaz tijekom stajanja djeluje odmah, bez roka i obveze, razlog `CancelledDuringCompanyClosure`.
- **Upozorenje "nije pokriveno" (K1-9):** `PARTICIPATION_NOT_COVERED` / `PARTICIPATION_PACKAGE_AVAILABLE` pri dolasku i odradi kad je
  dug > 0.

## Posljedice
- Karakterizacijski testovi s oznakom `CHANGED in K1`:
  - `CompleteNow` za prošlost (`TargetCommandTests`, `AppointmentWorkforceAvailabilityCharacterizationTests`);
  - praznik pri generiranju grupe (`GroupOccurrenceGenerationCharacterizationTests`).
- `AppointmentSegmentDefinitionRequest.Resources` i `GroupSegmentTemplateRequest.Resources` su sada nullable (izostavljeno =
  zadano). Frontend koji želi "bez resursa" šalje praznu listu.
- `MembershipPauseSpan.IsSystem`: matematika perioda (`MembershipPeriodCalendar`) ostaje jedino mjesto granica. Otvorena sustavna
  pauza ne ulazi u periode (`MembershipTimelines.PeriodPauseSpans`), ali ulazi u stanje i pokriće (`PauseSpans`).
- Novi grantovi `appointments.arrival.mark` i `catalog.cancellation-reasons.manage` (samo Admin, ADR-0023).
- Za K2 zabilježeno:
  - override radnog vremena kod grupa prelazi na zaseban grant;
  - "vrati termin" prelazi na grant korekcije.
