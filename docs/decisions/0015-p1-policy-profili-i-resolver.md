# ADR-0015: P1 — Imenovani verzionirani policy profili i resolver

- **Status:** Prihvaćeno, nije implementirano
- **Datum:** 2026-10-05 (P1 Decision Record, odluke D1, D11, D13)

> Izvor: [P1 Decision Record](../p1/P1_DECISION_RECORD.md) (2026-10-05).
> Za P1 detalje record ima prednost pred ovim sažetkom. Pravilo ponovnog otvaranja: D1–D13 se otvaraju samo ako dokaz iz
> koda pokaže da je zaključano pravilo nemoguće ili nesigurno implementirati.

## Kontekst
Danas postoji samo jedan prozor otkazivanja po organizaciji (`organization_settings.cancellation_cutoff_minutes`,
default 1440) i nikakve konfigurabilne posljedice. Klijent traži politike koje se razlikuju po poslovnici i usluzi.

## Razmotrene opcije
1. **Proširiti jednu org postavku** - nema razlike po Company/Service ni povijesti verzija.
2. **Imenovani profili s nepromjenjivim verzijama i dodjelama po scopeu** - jedan centralni resolver.

## Odluka
- **D1** `CancellationPolicy` (Id, OrganizationId, Name, IsActive) + nepromjenjiva `CancellationPolicyVersion` (pravila iz
  ADR-0016/0017) + `CancellationPolicyAssignment` (CompanyId?, ServiceId?, PolicyId) sa scopeovima Company+Service,
  Service, Company. Jedan resolver `ResolveCancellationPolicy(organizationId, companyId, serviceId)`, prednost:
  Company+Service → Service → Company → Organization default. Ulazi: `Appointment.CompanyId` i Segment `ServiceId`.
  Membership, Client Tags i paket NISU dimenzije.
- **D11** Politika se razrješava jednom, u trenutku događaja (unutar lifecycle transakcije), uzima se najnovija verzija
  profila i snapshotira PolicyId + Version. Nema pinanja pri kreiranju ni grandfatheringa. Kreiranje verzije = objava
  (nema Draft/EffectiveFrom). Dodjele pokazuju na profil, ne na verziju. Profil koji je default ili ima dodjelu ne može se
  deaktivirati (`CANCELLATION_POLICY_IN_USE`); neaktivan se ne može dodijeliti. Svaka Organization uvijek ima valjan
  neutralni default (prozor 1440 min; LateCancellation i NoShow: FeeType None + PackageAction None), stvoren pri
  registraciji i P1 cutoverom.
- **D13** Allowances (prvo kasno otkazivanje besplatno, N mjesečno...), iznimke po Client Tagovima i konfigurabilni obavezni
  razlog su IZVAN P1. Waiver (ADR-0018) je P1 mehanizam iznimke.

## Posljedice
- Nove tablice: `cancellation_policies`, `cancellation_policy_versions` (unique policy+version),
  `cancellation_policy_assignments` (unique po scopeu). Org default na `organization_settings` ili kao default dodjela
  (implementacijski izbor).
- Uklanja se `organization_settings.cancellation_cutoff_minutes`, `PUT /api/organization/settings/cancellation-cutoff`,
  `OrganizationSettingsService.UpdateCancellationCutoff` i default konstanta.
- Novi grantovi (samo Admin template): `catalog.cancellation-policies.view`, `catalog.cancellation-policies.manage`.
- Namjerna promjena: neutralni default nije financijski identičan legacy kodu (NoShow s None/None ima Due 0).
- Dug: grandfathering / EffectiveFrom / zakazana objava; tierovi; tag iznimke; allowances.
