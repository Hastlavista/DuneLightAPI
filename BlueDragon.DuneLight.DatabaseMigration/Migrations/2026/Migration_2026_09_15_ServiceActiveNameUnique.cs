using BlueDragon.DuneLight.DatabaseMigration.Extensions;
using BlueDragon.DuneLight.DatabaseMigration.Models;

namespace BlueDragon.DuneLight.DatabaseMigration.Migrations._2026;

/// <summary>
/// Uslugin postojeći ux_services_org_name_active (CreateServicesTable, Migration_2026_07_Catalog) uspoređuje
/// TOČAN string — "Sports Massage" i "sports massage" su trenutno dopuštene kao dvije različite aktivne
/// usluge. Ovaj indeks zamjenjuje ga normaliziranim (trim + case-insensitive) inačicom, isti obrazac kao
/// ux_companies_org_name_active/ux_rooms_org_company_name_active (Migration_2026_09_15_CompanyActiveNameUnique/
/// RoomActiveNameUnique).
///
/// OPREZ prije pokretanja na bazi s postojećim podacima: ako već postoje dvije aktivne Service u istoj
/// Organization s istim normaliziranim nazivom, ovaj CREATE UNIQUE INDEX će pasti — takve zapise treba ručno
/// preimenovati/deaktivirati prije primjene migracije.
/// </summary>
[DeveloperMigration(2026, 09, 15, Developer.SilvioHabazin, 2)]
public class AddServiceActiveNameUniqueIndex : DuneLightMigration
{
    public override void Up()
    {
        Execute.Sql("DROP INDEX IF EXISTS dunelight.ux_services_org_name_active;");

        Execute.Sql(
            "CREATE UNIQUE INDEX ux_services_org_name_active " +
            "ON dunelight.services (organization_id, lower(trim(name))) WHERE is_active = true;");
    }
}
