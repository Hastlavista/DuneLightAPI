# ADR-0008: Tvrda pravila preklapanja i kapaciteta Room/Resource; meki Group kapacitet

- **Status:** Prihvaćeno
- **Datum:** 2026-10-05 (Decision Log v1 #19–#23; implementirano u Phase C, M1C, M1D, M1D.1, M1F, M1G)

## Kontekst
Multi-segment i multi-employee model traži provjere preklapanja po Segmentu, a stari boolean `AllowConcurrentBookings`
nije odražavao fizički kapacitet prostora i opreme.

## Razmotrene opcije
1. **Boolean "dopusti paralelne rezervacije" po sobi** - nije stvarni kapacitet.
2. **Brojčani kapaciteti + tvrde blokade preklapanja, uz zaseban meki Group kapacitet.**

## Odluka
- Isti Employee na preklapajućim Segmentima: TVRDA BLOKADA. Isti Client na preklapajućim Participationima (i preko
  različitih Appointmenta): TVRDA BLOKADA. Intervali su polu-otvoreni `[start, end)`, susjedni su dopušteni.
- Confirmed i Completed zauzimaju klijentovo vrijeme; Cancelled i NoShow ne. Segment eksplicitno neotkazanog Appointmenta
  zauzima slot i bez aktivnih klijenata; kod eksplicitno otkazanog slot ostaje zauzet samo gdje postoji Completed/NoShow.
- Room: brojčani kapacitet u OSOBAMA = dodijeljeni Employees + Confirmed/Completed klijenti preklapajućih Segmenata.
- Resource: `QuantityRequired > 0` po Segmentu (ne množi se brojem Employeeja); zbroj preklapajućih ≤ `Resource.Capacity`.
- Vršno opterećenje se računa event-sweep algoritmom (`Utils/IntervalCapacity.cs`).
- Group kapacitet (po SegmentTemplateu) je MEKI: override uz `OverrideCapacity = true` + grant `groups.capacity.override`.
  Nikad ne zaobilazi Room/Resource kapacitet ni preklapanja.
- Prošli termini su dopušteni, ali se validiraju; rad izvan radnog vremena/praznika samo uz eksplicitni override grant
  gdje je podržano.
- EmployeeService: NULA eksplicitnih dodjela = Employee smije SVE usluge; jedna ili više = samo dodijeljene
  (`Utils/EmployeeServiceEligibility.cs`; ispravlja legacy "prazno = nijedna").
- Employee može raditi u više Companyja; točno jedna je primarna (`EmployeeCompany.IsPrimary`).
- `Service.ExecutionMode` (`Individual | Group`) se zaključava nakon korištenja.

## Posljedice
- Provjere su konkurentno sigurne: lockovi po subjektu rasporeda prije provjere (`SchedulingConflictGuard`).
- Izmjena kapaciteta ne smije ostaviti postojeće stanje iznad tvrdog kapaciteta (`CapacityChangeGuard`, M1D.1).
- Pauze (breaks) još ne koriste isti subject-lock model (dug J).
