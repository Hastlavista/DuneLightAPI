# ADR-0009: Cijene po Participationu i SegmentPricingMode

- **Status:** Prihvaćeno
- **Datum:** 2026-10-05 (Decision Log v1 #25–#27; implementirano u D3B2 i M1G)

## Kontekst
Klijenti u istom Segmentu mogu imati različite komercijalne uvjete, pa cijena ne može biti na Appointmentu ni Bookingu.
Uz više Employeeja na Segmentu bilo je neodlučeno čija se cijena koristi (Decision Log #27); automatski izbor
(prvi, najjeftiniji, prosjek...) bio bi nedeterminističan i skriven.

## Razmotrene opcije
1. **Automatski izbor Employeeja za cijenu** - zgodno, ali skriveno pravilo koje klijent nije odobrio.
2. **Eksplicitni izvor cijene na Segmentu** (`SegmentPricingMode` + `PricingEmployeeId`) - pozivatelj bira kad nije jednoznačno.

## Odluka
- Cijena je po Participationu.
- `SegmentPricingMode`: `Standard | Employee`, uz `PricingEmployeeId?`:
  - 0 Employeeja → Standard, null;
  - 1 Employee → Employee mode, automatski taj Employee;
  - 2+ Employeeja → pozivatelj MORA eksplicitno odabrati (Employee mode s jednim od njih, ili Standard). Nikad automatski
    odabir. Promjena skupa koja ostavlja 2+ traži ponovnu potvrdu.
- Prednost u Employee modu: Employee+Company+Service → Employee+Service → Company+Service → Organization-wide →
  `Service.DefaultPrice`. U Standard modu: Company+Service → Organization-wide → DefaultPrice (preskače Employee razine).
- `PricingEmployeeId` znači samo "čija se cijena koristi" — nije primarni Employee ni korisnik provizije.
- Participation snapshotira base amount, izvor, mode, `PricingEmployeeId`, suggested/final cijenu i ručni override.
  Povijesni izvor cijene se nikad ne izvodi iz trenutnog stanja Segmenta.
- Ručna cijena: `PATCH /api/participations/{id}/price`, samo Confirmed, ne ispod već plaćenog iznosa, s audit zapisom.

## Posljedice
- Algoritam je u `PriceResolutionService` (čist, bez baze); orkestracija u `PricingService`; pravila izvora u
  `Utils/SegmentPricingSource.cs`.
- `PricingEmployeeId ∈ Segment.Employees` se provjerava samo u servisu (dug A).
- Sloj komercijalnih prilagodbi (membership/tag/promo) ne postoji; `AdjustmentAmount` se ne piše; prioritet je otvoren
  (Decision Log #28, dug G) — ne izmišljati pravila slaganja.
- Checkout snapshot cijene može odstupiti nakon repricinga (dug F).
