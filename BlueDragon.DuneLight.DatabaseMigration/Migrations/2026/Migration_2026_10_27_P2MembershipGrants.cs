using BlueDragon.DuneLight.DatabaseMigration.Extensions;
using BlueDragon.DuneLight.DatabaseMigration.Models;

namespace BlueDragon.DuneLight.DatabaseMigration.Migrations._2026;

/// <summary>
/// P2 (faza 2B): granularni grantovi članstava (po radnji) i izdvojeni catalog.memberships.deactivate dodaju se SAMO
/// inicijalnim Admin grupama (grant_groups.system_key = 'admin', ADR-0023); ostalim grupama ih studio dodjeljuje kroz editor.
/// Idempotentno.
/// </summary>
[DeveloperMigration(2026, 10, 27, Developer.SilvioHabazin, 4)]
public class P2MembershipGrants : DuneLightMigration
{
    public override void Up()
    {
        Execute.Sql(@"
            INSERT INTO dunelight.grant_group_grants (id, grant_group_id, grant_key)
            SELECT gen_random_uuid(), g.id, k.grant_key
            FROM dunelight.grant_groups g
            CROSS JOIN (VALUES
                ('catalog.memberships.deactivate'),
                ('clients.memberships.view'),
                ('clients.memberships.sell'),
                ('clients.memberships.cancel'),
                ('clients.memberships.pause'),
                ('clients.memberships.plan-change'),
                ('clients.memberships.end-override'),
                ('clients.memberships.void-sale')) AS k(grant_key)
            WHERE g.system_key = 'admin'
            ON CONFLICT (grant_group_id, grant_key) DO NOTHING;");
    }
}
