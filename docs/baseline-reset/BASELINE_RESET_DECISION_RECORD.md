# Baseline reset — Decision Record

> Segment čišćenja: shema se gradi od nule jednom početnom (baseline) migracijom, a globalni capability/template sustav
> u bazi zamjenjuje se statičkim katalogom u C#. Odluke: [ADR-0022](../decisions/0022-pocetna-migracija-bez-seeda.md),
> [ADR-0023](../decisions/0023-katalog-autorizacije-u-kodu.md), [ADR-0024](../decisions/0024-produkcijska-baza-do-go-livea.md).

## Opseg

| # | Stavka | ADR | Status |
|---|---|---|---|
| 1 | Sve dosadašnje migracije zamijeniti početnom migracijom trenutne ciljne sheme, u malim koracima | 0022 | Implementirano 2026-10-06 |
| 2 | Migracije ne seedaju ništa; prazna baza nema nijedan redak | 0022 | Implementirano 2026-10-06 |
| 3 | Katalog grantova i capabilityja isključivo u C#; ukloniti capability/template sustav iz baze | 0023 | Implementirano 2026-10-06 |
| 4 | Registracija: Admin GrantGroup sa SVIM grantovima iz kataloga + zadani RosterTypeovi, u istoj transakciji | 0023 | Implementirano 2026-10-06 |
| 5 | Dijagnostički endpoint grantova zamijeniti testovima konzistentnosti kataloga | 0023 | Implementirano 2026-10-06 |

## Zaključana pravila
- **Seed baze = ništa.** Nijedna migracija ne upisuje podatke (ni katalog, ni predloške, ni RosterTypeove).
- **Inicijalizacija organizacije** (aplikacijski kod, u transakciji registracije): Admin GrantGroup sa svim grantovima
  iz C# kataloga + dodjela prvog Usera toj grupi + zadani RosterTypeovi (`SeedDefaultTypes`, definirani u kodu). To su
  obični tenant podaci te organizacije; za njih nema predložaka, verzija ni upgrade infrastrukture.
- **Jedini perzistirani autorizacijski izvor istine je `GrantGroupGrant`.** Capability odabiri, snapshotovi,
  provenance i authoring-state se ne spremaju; authoring-state se izvodi iz grantova.
- Admin grupa je obična organizacijska grupa (smije se preimenovati, mijenjati joj grantove i obrisati); "Admin = svi
  grantovi" vrijedi pri kreiranju. Stabilni sistemski identitet (`grant_groups.system_key = 'admin'`) postoji samo da
  buduće migracije mogu nove grantove dodati inicijalnim Admin grupama — nikad za autorizaciju.
- Runtime autorizacija ostaje isključivo `User → UserGrantGroup → GrantGroup → GrantGroupGrant → grant` (ADR-0004);
  nema Admin/Owner bypassa ni autorizacije po imenu grupe.

## Nalazi audita capability/template sustava (2026-10-06)

| Dio | Svrha | Ishod |
|---|---|---|
| `capability_definitions`, `capability_definition_grants` | Katalog capabilityja u bazi (verzije, deprecated) | Seli se u C# (`CapabilityCatalog`), tablice uklonjene |
| `default_role_templates` (+ capabilities, grants) | Predlošci Admin/Trener/Recepcija, v1–v6 | Uklonjeno; Admin = svi grantovi iz koda |
| `grant_group_capability_snapshots`, `grant_group_template_grants` | Provenance za template upgrade i authoring-state | Uklonjeno; authoring-state se izvodi iz grantova |
| `grant_group_template_upgrade_audit_log`, upgrade planner/servis/endpointi | Nadogradnja predložaka | Uklonjeno |
| Capability-based editor (create/update/authoring-state) | Grupiranje grantova (scope model, osjetljivost) za UI | **Zadržano**, katalog iz koda, bez verzija; sprema preko `GrantGroupService` |
| `GrantDiagnosticsService` + `/api/_internal/diagnostics/grants` | Konzistentnost kataloga vs. `RequireGrant` + drift predložaka | Endpoint uklonjen; korisne provjere postaju testovi |
| `DefaultGrantGroups` (Trener/Recepcija), `DefaultGrantGroupDriftChecker` | Samo dijagnostika | Uklonjeno |
| Workforce `Role` (`roles`, `user_role_assignments`) | Poslovna oznaka, nije dio ovog sustava | Netaknuto |

## Namjerne promjene ponašanja / API-ja (frontend)

| Područje | Prije | Poslije |
|---|---|---|
| `GET /api/permissions/role-templates[/{key}]` | Predlošci iz baze | Uklonjeno |
| `GET /api/permissions/capabilities[/{key}]` | Iz baze, s `id`, `version`, `isActive`, `deprecatedAt` | Iz C# kataloga, bez verzija/id-a |
| `POST/PUT .../grant-groups/capability-based` | `capabilitySelections[].capabilityVersion` obavezan | Polje uklonjeno; samo `capabilityKey` + `selectedScope` |
| `GET .../grant-groups/{id}/authoring-state` | Iz snapshotova; `templateSource*`, `hasCapabilityMetadata`, `isCustomized` | Izvedeno iz grantova; ta polja uklonjena |
| `GET .../grant-groups/{id}/template-match`, `template-upgrade-*` | Postoje | Uklonjeno |
| `GET /api/_internal/diagnostics/grants` | Development-only izvještaj | Uklonjeno (zamijenjeno testovima) |
| `GET /api/grants` | `key`, `module`, `description` | + `displayName` |

## Implementation decision log

Format: datum — pitanje/kontekst — odgovor — posljedica.

- 2026-10-06 — Dropati sve migracije i napraviti nove prema trenutnom stanju koda? — Da; korisnik ručno dropa shemu
  `dunelight` (lokalno i na produkcijskoj bazi) i pokreće nove migracije. — ADR-0022.
- 2026-10-06 — Što seedati (capability katalog, predlošci)? — Ništa. Baza sadrži samo podatke koje stvori aplikacija;
  katalog grantova/capabilityja je u C#; prvi korisnik organizacije dobiva Admin grupu sa svim grantovima, bez
  predloška. — ADR-0023.
- 2026-10-06 — Testovi cutover migracija (AppointmentSegment / BookingParticipation Lifecycle / Pricing)? — Obrisati.
- 2026-10-06 — Postoje li druge baze s primijenjenim migracijama? — Da, produkcijska; korisnik je sam dropa i pokreće
  migration projekt. Claude samo kreira nove migracije.
- 2026-10-06 — Struktura novih migracija? — Po manjim koracima (chunkovima), ne sve u jednoj klasi.
- 2026-10-06 — `SeedDefaultTypes` pri registraciji? — Zadržati; to je inicijalizacija organizacije, ne seed baze.
  Migracije ne seedaju RosterTypeove; zadane vrijednosti ostaju u kodu, bez template/version infrastrukture.
- 2026-10-06 — Capability-based uređivanje grupa? — Zadržati grupiranje, ali katalog capabilityja u kodu (bez baze,
  verzija i predložaka).
- 2026-10-06 — Smije li se Admin grupa uređivati? — Da; to je organizacijska grupa, "Admin = svi grantovi" vrijedi samo
  inicijalno (+ buduće migracije novih grantova dodaju grant inicijalnim Admin grupama).
- 2026-10-06 — Dijagnostika grantova? — Zamijeniti testovima: dupli ključ u katalogu, `RequireGrant` s nepoznatim
  ključem, grant iz kataloga koji se nigdje ne koristi (namjerne iznimke eksplicitno popisane, test se ne slabi
  globalno). Endpoint, servis i DTO-ovi se brišu.
- 2026-10-06 — Gdje čuvati capability odabir grupe? — Nigdje; izvodi se iz grantova (najveći opseg čiji je skup
  grantova sadržan u grupi), ostatak su ručni grantovi. Bez tablice odabira. Prihvaćeno da View + ručno dodani Manage
  grantovi idući put izgledaju kao Manage.
- 2026-10-06 — Smije li Claude obrisati lokalnu shemu `dunelight` i pokrenuti nove migracije radi testova? — Ne; korisnik
  to radi sam. — Claude je početnu migraciju provjerio u zasebnoj bazi `dunelight_baseline_check` (pg_dump usporedba
  objekt po objekt sa starom shemom: jedine razlike su uklonjene capability/template tablice i novi
  `grant_groups.system_key` + `ux_grant_groups_organization_system_key`).

- 2026-10-06 — Produkcijska baza (ADR-0003 traži novi ADR za prvi produkcijski deploy): ima li stvarne podatke? — Ne,
  samo test/demo. Do kada se smije resetirati? — Nema roka; do go-livea koji proglašava korisnik. Pravila nakon
  go-livea? — Zasad samo: svaka migracija se prije produkcije testira. — ADR-0024; ostala pravila (backup, izmjena
  stupaca s podacima, Down/forward-fix) otvorena do go-live ADR-a.
- 2026-10-06 — ADR-0024 posljedica "produkcija ne smije dobiti stvarne podatke dok go-live ADR ne postoji" (Claude je dodao
  bez izričitog odabira) — zadržati? — Da. — Pravilo ostaje u ADR-0024.

## Implementacijske napomene
- Početna migracija je generirana iz `pg_dump --schema-only` stare (potpuno migrirane) sheme, podijeljena u 12 koraka
  (`Baseline00_Schema` … `Baseline11_OutboxAndNotifications`), svaka tablica u svom `Execute.Sql` bloku; FK-ovi
  upućuju samo na tablice iz istog ili ranijeg koraka. CHECK-ovi s popisom vrijednosti pišu se kao `col IN (...)` da
  `pg_get_constraintdef` ostane identičan (schema testovi uspoređuju točan tekst).
- `grant_groups.system_key` varchar(50) NULL + parcijalni unique (organization_id, system_key) WHERE NOT NULL.
- Grantovi u `Grants.Catalog` dobili su `DisplayName` (hrvatski naziv za prikaz).
- Testovi: `AuthorizationCatalogConsistencyTests` (zamjena za dijagnostiku; namjerna iznimka: `groups.capacity.override`
  se provjerava u `GroupCapacityGuard`), `CapabilityAuthoringTests`, `RegistrationBootstrapTests` (registracija od
  kraja do kraja). Obrisani: template-upgrade, seed i cutover-migracijski testovi.
- Rezultat 2026-10-06 nakon što je korisnik migrirao lokalnu bazu (12 koraka, zadnja verzija `20261025000011`, nijedan
  redak u `grant_groups`): build bez grešaka, testovi 1077/1077.
