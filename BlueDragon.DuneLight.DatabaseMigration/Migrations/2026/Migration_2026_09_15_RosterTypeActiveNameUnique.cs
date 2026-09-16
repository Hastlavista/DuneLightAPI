using BlueDragon.DuneLight.DatabaseMigration.Extensions;
using BlueDragon.DuneLight.DatabaseMigration.Models;

namespace BlueDragon.DuneLight.DatabaseMigration.Migrations._2026;

/// <summary>
/// RosterType (CreateRosterTypesTable, Migration_2026_07_Roster) je i dalje uspoređivao TOČAN string —
/// zamjenjuje ux_roster_types_organization_name normaliziranim (trim + case-insensitive) indeksom, isti
/// obrazac kao ux_services_org_name_active/ux_companies_org_name_active/ux_rooms_org_company_name_active.
///
/// OPREZ prije pokretanja na bazi s postojećim podacima: ako već postoje dvije aktivne RosterType u istoj
/// Organization s istim normaliziranim nazivom, ovaj CREATE UNIQUE INDEX će pasti — takve zapise treba ručno
/// preimenovati/deaktivirati prije primjene migracije.
/// </summary>
[DeveloperMigration(2026, 09, 15, Developer.SilvioHabazin, 7)]
public class AddRosterTypeActiveNameUniqueIndex : DuneLightMigration
{
    public override void Up()
    {
        Execute.Sql("DROP INDEX IF EXISTS dunelight.ux_roster_types_organization_name;");

        Execute.Sql(
            "CREATE UNIQUE INDEX ux_roster_types_org_name_active " +
            "ON dunelight.roster_types (organization_id, lower(trim(name))) WHERE is_active = true;");
    }
}
