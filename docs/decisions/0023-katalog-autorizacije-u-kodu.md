# ADR-0023: Katalog grantova i capabilityja u kodu; Admin grupa pri registraciji bez predložaka

- **Status:** Prihvaćeno, implementirano 2026-10-06
- **Datum:** 2026-10-06
- **Zapis segmenta:** [Baseline reset](../baseline-reset/BASELINE_RESET_DECISION_RECORD.md)
- **Mijenja:** dio ADR-0004 o "capability definicijama i default role templateima" kao autorskim metapodacima u bazi

## Kontekst
Grantovi su već definirani u kodu (`Grants.cs`), ali je uz njih postojao cijeli sustav u bazi: verzionirani
`capability_definitions`, `default_role_templates` (Admin/Trener/Recepcija v1–v6), snapshotovi i provenance po grupi,
template-upgrade planner, audit log nadogradnji i dijagnostika drifta. Sve to je seedano migracijama i služilo je samo
prikazu kataloga, kreiranju početnih grupa i nadogradnji predložaka. Registracija je Admin grupu gradila iz predloška
u bazi — prazna baza bi značila korisnika bez ikakvih prava.

## Razmotrene opcije
1. **Seedati samo katalog** - manje promjene, ali baza i dalje nosi globalne podatke koji nisu tenant podaci.
2. **Katalog u kodu, ništa u bazi** - jedan izvor istine (kod), baza sadrži samo podatke organizacija.

## Odluka
Opcija 2.
- **Katalog u C#**: `Grants.Catalog` (ključ, naziv za prikaz, modul, opis) i `CapabilityCatalog` (ključ, kategorija,
  scope model, osjetljivost, grantovi s ulogama). Bez verzija, deprecated stanja i tablica.
- **Capability je samo projekcija/editor nad grantovima**: pri spremanju se odabiri prevode u raw grantove i sprema se
  samo konačni `GrantGroupGrant` skup. Authoring-state se izvodi iz grantova: za svaki capability najveći opseg čiji je
  skup grantova u cijelosti sadržan u grupi; grantovi koje nijedan odabrani capability ne objašnjava su ručni.
- **Registracija** (ista transakcija kao Organization i prvi User): Admin GrantGroup sa SVIM grantovima iz kataloga,
  prvi User dodijeljen toj grupi, zadani RosterTypeovi (`SeedDefaultTypes`). Nema predloška.
- **Sistemski identitet**: `grant_groups.system_key` (`'admin'` za inicijalnu Admin grupu, jedinstven po organizaciji,
  inače NULL). Ne koristi se za autorizaciju; naziv "Admin" nije nositelj ničega.
- **Admin grupa je organizacijska**: smije se preimenovati, mijenjati joj grantove i obrisati.
- **Novi grantovi u budućim izdanjima**: migracija koja uvodi grant ga eksplicitno dodaje svim grupama sa
  `system_key = 'admin'`. Nema Admin bypassa.
- **Konzistentnost kataloga** provjeravaju testovi (dupli ključ, `RequireGrant` s nepoznatim ključem, grant koji se
  nigdje ne koristi uz eksplicitno popisane iznimke), ne runtime dijagnostički endpoint.

## Posljedice
- Invarijanta: inicijalna Admin grupa nove organizacije sadrži sve grantove kataloga; svaka migracija koja dodaje grant
  dodaje ga i postojećim sistemskim Admin grupama.
- Uklanjaju se tablice capability/template sustava, njihovi entiteti, handleri, servisi, endpointi
  (`role-templates`, `template-match`, `template-upgrade-*`, `/api/_internal/diagnostics/grants`) i error kodovi koje
  koriste samo oni.
- API promjene za frontend: vidi tablicu u zapisu segmenta.
- Runtime autorizacija nepromijenjena (ADR-0004): isključivo raw grantovi.
