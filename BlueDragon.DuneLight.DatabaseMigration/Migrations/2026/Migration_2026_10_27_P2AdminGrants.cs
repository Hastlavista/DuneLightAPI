using BlueDragon.DuneLight.DatabaseMigration.Extensions;
using BlueDragon.DuneLight.DatabaseMigration.Models;

namespace BlueDragon.DuneLight.DatabaseMigration.Migrations._2026;

/// <summary>
/// P2 (faza 2A), korak 2/2: novi grantovi kataloga članarina (catalog.memberships.view/.manage) dodaju se SAMO inicijalnim
/// Admin grupama (grant_groups.system_key = 'admin') — pravilo ADR-0023 za uvođenje novog granta. Ostale grupe ih ne dobivaju
/// automatski. Idempotentno.
/// </summary>
[DeveloperMigration(2026, 10, 27, Developer.SilvioHabazin, 1)]
public class P2AdminGrants : DuneLightMigration
{
    public override void Up()
    {
        Execute.Sql(@"
            INSERT INTO dunelight.grant_group_grants (id, grant_group_id, grant_key)
            SELECT gen_random_uuid(), g.id, k.grant_key
            FROM dunelight.grant_groups g
            CROSS JOIN (VALUES
                ('catalog.memberships.view'),
                ('catalog.memberships.manage')) AS k(grant_key)
            WHERE g.system_key = 'admin'
            ON CONFLICT (grant_group_id, grant_key) DO NOTHING;");
    }
}
