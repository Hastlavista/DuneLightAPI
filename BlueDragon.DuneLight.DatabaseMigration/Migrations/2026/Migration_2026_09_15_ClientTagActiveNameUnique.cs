using BlueDragon.DuneLight.DatabaseMigration.Extensions;
using BlueDragon.DuneLight.DatabaseMigration.Models;

namespace BlueDragon.DuneLight.DatabaseMigration.Migrations._2026;

/// <summary>
/// ClientTagov postojeći ux_client_tags_org_name_active (CreateClientTagsTable, Migration_2026_07_Clients)
/// uspoređuje TOČAN string — "VIP" i "vip" su trenutno dopuštene kao dvije različite aktivne oznake. Ovaj
/// indeks zamjenjuje ga normaliziranim (trim + case-insensitive) inačicom, isti obrazac kao
/// ux_companies_org_name_active/ux_services_org_name_active/ux_rooms_org_company_name_active/
/// ux_packages_org_name_active. ClientTag je jedini šifrarnik iz Client domene koji ovaj zahvat izravno
/// dira — EngagementType/RosterType namjerno NISU normalizirani ovdje.
///
/// OPREZ prije pokretanja na bazi s postojećim podacima: ako već postoje dvije aktivne oznake u istoj
/// Organization s istim normaliziranim nazivom, ovaj CREATE UNIQUE INDEX će pasti — takve zapise treba ručno
/// preimenovati/deaktivirati prije primjene migracije.
/// </summary>
[DeveloperMigration(2026, 09, 15, Developer.SilvioHabazin, 6)]
public class AddClientTagActiveNameUniqueIndex : DuneLightMigration
{
    public override void Up()
    {
        Execute.Sql("DROP INDEX IF EXISTS dunelight.ux_client_tags_org_name_active;");

        Execute.Sql(
            "CREATE UNIQUE INDEX ux_client_tags_org_name_active " +
            "ON dunelight.client_tags (organization_id, lower(trim(name))) WHERE is_active = true;");
    }
}
