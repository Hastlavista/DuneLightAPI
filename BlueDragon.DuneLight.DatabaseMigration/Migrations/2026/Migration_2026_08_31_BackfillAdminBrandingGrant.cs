using BlueDragon.DuneLight.DatabaseMigration.Extensions;
using BlueDragon.DuneLight.DatabaseMigration.Models;
using FluentMigrator;

namespace BlueDragon.DuneLight.DatabaseMigration.Migrations._2026;

/// <summary>
/// Backfill: dodaje novi organization.branding.manage grant postojećim "Admin" GrantGroup-ama. Nove
/// organizacije ga dobivaju automatski jer AuthService.Register čita DefaultGrantGroups.AdminGrants, koji
/// je izveden iz cijelog Grants.Catalog — ali postojeće grupe u bazi su snapshot s trenutka registracije pa
/// ih treba ručno dopuniti (isti obrazac kao ExpandDefaultReceptionGrants). Sigurno za ponovno pokretanje i
/// ne dira grupe koje je korisnik preimenovao iz "Admin".
/// </summary>
[DeveloperMigration(2026, 08, 31, Developer.SilvioHabazin, 1)]
public class BackfillAdminBrandingGrant : DuneLightMigration
{
    public override void Up()
    {
        Execute.Sql(@"
            INSERT INTO dunelight.grant_group_grants (id, grant_group_id, grant_key)
            SELECT (md5(random()::text || clock_timestamp()::text || gg.id::text || 'organization.branding.manage'))::uuid,
                   gg.id, 'organization.branding.manage'
            FROM dunelight.grant_groups gg
            WHERE gg.name = 'Admin'
              AND NOT EXISTS (
                  SELECT 1 FROM dunelight.grant_group_grants existing
                  WHERE existing.grant_group_id = gg.id AND existing.grant_key = 'organization.branding.manage'
              );
        ");
    }
}
