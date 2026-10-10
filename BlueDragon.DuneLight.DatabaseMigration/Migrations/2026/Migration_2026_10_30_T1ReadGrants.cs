using BlueDragon.DuneLight.DatabaseMigration.Extensions;
using BlueDragon.DuneLight.DatabaseMigration.Models;

namespace BlueDragon.DuneLight.DatabaseMigration.Migrations._2026;

/// <summary>
/// T1-11 — novi grantovi za čitanje (bez uređivanja): catalog.cancellation-reasons.view, organization.settings.view,
/// organization.branding.view i commissions.rules.view. Dodaju se SAMO Admin grupama (grant_groups.system_key = 'admin',
/// ADR-0023). Idempotentno (ON CONFLICT DO NOTHING). Ništa se ne seeda (ADR-0022).
/// </summary>
[DeveloperMigration(2026, 10, 30, Developer.SilvioHabazin, 4)]
public class T1ReadGrants : DuneLightMigration
{
    public override void Up()
    {
        Execute.Sql(@"
            INSERT INTO dunelight.grant_group_grants (id, grant_group_id, grant_key)
            SELECT gen_random_uuid(), g.id, k.grant_key
            FROM dunelight.grant_groups g
            CROSS JOIN (VALUES
                ('catalog.cancellation-reasons.view'),
                ('organization.settings.view'),
                ('organization.branding.view'),
                ('commissions.rules.view')) AS k(grant_key)
            WHERE g.system_key = 'admin'
            ON CONFLICT (grant_group_id, grant_key) DO NOTHING;");
    }
}
