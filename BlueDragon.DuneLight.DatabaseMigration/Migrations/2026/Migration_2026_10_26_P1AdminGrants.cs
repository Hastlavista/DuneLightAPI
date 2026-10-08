using BlueDragon.DuneLight.DatabaseMigration.Extensions;
using BlueDragon.DuneLight.DatabaseMigration.Models;

namespace BlueDragon.DuneLight.DatabaseMigration.Migrations._2026;

/// <summary>
/// P1 (ADR-0015/ADR-0018, D10), korak 3/3: novi grantovi kataloga (catalog.cancellation-policies.view/.manage,
/// appointments.policy.override) dodaju se SAMO inicijalnim Admin grupama (grant_groups.system_key = 'admin') — pravilo
/// ADR-0023 za uvođenje novog granta. Recepcija i treneri ih ne dobivaju automatski. Idempotentno.
/// </summary>
[DeveloperMigration(2026, 10, 26, Developer.SilvioHabazin, 2)]
public class P1AdminGrants : DuneLightMigration
{
    public override void Up()
    {
        Execute.Sql(@"
            INSERT INTO dunelight.grant_group_grants (id, grant_group_id, grant_key)
            SELECT gen_random_uuid(), g.id, k.grant_key
            FROM dunelight.grant_groups g
            CROSS JOIN (VALUES
                ('catalog.cancellation-policies.view'),
                ('catalog.cancellation-policies.manage'),
                ('appointments.policy.override')) AS k(grant_key)
            WHERE g.system_key = 'admin'
            ON CONFLICT (grant_group_id, grant_key) DO NOTHING;");
    }
}
