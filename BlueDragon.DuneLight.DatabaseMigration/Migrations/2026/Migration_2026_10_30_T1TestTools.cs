using BlueDragon.DuneLight.DatabaseMigration.Extensions;
using BlueDragon.DuneLight.DatabaseMigration.Models;

namespace BlueDragon.DuneLight.DatabaseMigration.Migrations._2026;

/// <summary>
/// T1 — PRIVREMENI testni alati (uklanjaju se prije go-livea, zajedno s ovom tablicom): simulirani pomak poslovnog sata po
/// organizaciji (samo naprijed) i oznaka demo organizacije koju je stvorio seed. Nije dio poslovnog modela; postojeće
/// organizacije nemaju redak (pomak 0, nisu demo).
/// </summary>
[DeveloperMigration(2026, 10, 30, Developer.SilvioHabazin, 0)]
public class T1TestTools : DuneLightMigration
{
    public override void Up()
    {
        Execute.Sql(@"
            CREATE TABLE dunelight.test_tool_organizations (
                organization_id uuid PRIMARY KEY REFERENCES dunelight.organizations(id) ON DELETE CASCADE,
                is_demo boolean NOT NULL DEFAULT false,
                clock_offset_seconds bigint NOT NULL DEFAULT 0,
                clock_advanced_at timestamp with time zone,
                clock_advanced_by uuid,
                retired_at timestamp with time zone,
                created_at timestamp with time zone NOT NULL,
                CONSTRAINT ck_test_tool_organizations_offset CHECK (clock_offset_seconds >= 0));");
    }
}
