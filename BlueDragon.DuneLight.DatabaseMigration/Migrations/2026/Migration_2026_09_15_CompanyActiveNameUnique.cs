using BlueDragon.DuneLight.DatabaseMigration.Extensions;
using BlueDragon.DuneLight.DatabaseMigration.Models;

namespace BlueDragon.DuneLight.DatabaseMigration.Migrations._2026;

/// <summary>
/// Poslovno pravilo: unutar jedne Organization, dvije aktivne Company (poslovnice) ne smiju imati isti naziv
/// nakon normalizacije (trim + case-insensitive) — "Zagreb", "zagreb" i " Zagreb " se tretiraju kao isti naziv.
/// Isti obrazac kao ux_services_org_name_active/ux_service_categories_org_name_active (djelomični unique
/// indeks, samo među aktivnima), ali proširen s lower(trim(name)) jer je ovo prvi šifrarnik koji zahtijeva
/// normalizirano (case/whitespace-neovisno) uspoređivanje naziva — ostali šifrarnici namjerno NISU dirani u
/// ovom zahvatu (uspoređuju točan string), pa je ovo svjesna, privremena nekonzistentnost dok se ne odluči
/// hoće li se normalizacija širiti na cijeli sustav.
///
/// OPREZ prije pokretanja na bazi s postojećim podacima: ako već postoje dvije aktivne Company u istoj
/// Organization s istim normaliziranim nazivom, ovaj CREATE UNIQUE INDEX će pasti — takve zapise treba ručno
/// preimenovati/deaktivirati prije primjene migracije.
/// </summary>
[DeveloperMigration(2026, 09, 15, Developer.SilvioHabazin, 0)]
public class AddCompanyActiveNameUniqueIndex : DuneLightMigration
{
    public override void Up()
    {
        Execute.Sql(
            "CREATE UNIQUE INDEX ux_companies_org_name_active " +
            "ON dunelight.companies (organization_id, lower(trim(name))) WHERE is_active = true;");
    }
}
