using BlueDragon.DuneLight.DatabaseMigration.Extensions;
using BlueDragon.DuneLight.DatabaseMigration.Models;

namespace BlueDragon.DuneLight.DatabaseMigration.Migrations._2026;

/// <summary>
/// P2 (faza 2C): grant memberships.charges.write-off (otpis zaduženja članarine) dodaje se SAMO inicijalnim Admin grupama
/// (grant_groups.system_key = 'admin', ADR-0023). Idempotentno.
/// </summary>
[DeveloperMigration(2026, 10, 27, Developer.SilvioHabazin, 7)]
public class P2ChargeWriteOffGrant : DuneLightMigration
{
    public override void Up()
    {
        Execute.Sql(@"
            INSERT INTO dunelight.grant_group_grants (id, grant_group_id, grant_key)
            SELECT gen_random_uuid(), g.id, 'memberships.charges.write-off'
            FROM dunelight.grant_groups g
            WHERE g.system_key = 'admin'
            ON CONFLICT (grant_group_id, grant_key) DO NOTHING;");
    }
}
