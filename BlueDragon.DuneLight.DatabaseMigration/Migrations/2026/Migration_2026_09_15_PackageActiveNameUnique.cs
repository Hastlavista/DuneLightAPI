using BlueDragon.DuneLight.DatabaseMigration.Extensions;
using BlueDragon.DuneLight.DatabaseMigration.Models;

namespace BlueDragon.DuneLight.DatabaseMigration.Migrations._2026;

/// <summary>
/// Package dosad nije imao uniqueness zaštitu na Name — isti normalizirani (trim + case-insensitive)
/// obrazac kao ux_companies_org_name_active/ux_rooms_org_company_name_active/ux_services_org_name_active
/// (Migration_2026_09_15_CompanyActiveNameUnique/RoomActiveNameUnique/ServiceActiveNameUnique).
///
/// OPREZ prije pokretanja na bazi s postojećim podacima: ako već postoje dva aktivna Package u istoj
/// Organization s istim normaliziranim nazivom, ovaj CREATE UNIQUE INDEX će pasti — takve zapise treba ručno
/// preimenovati/deaktivirati prije primjene migracije.
/// </summary>
[DeveloperMigration(2026, 09, 15, Developer.SilvioHabazin, 5)]
public class AddPackageActiveNameUniqueIndex : DuneLightMigration
{
    public override void Up()
    {
        Execute.Sql(
            "CREATE UNIQUE INDEX ux_packages_org_name_active " +
            "ON dunelight.packages (organization_id, lower(trim(name))) WHERE is_active = true;");
    }
}
