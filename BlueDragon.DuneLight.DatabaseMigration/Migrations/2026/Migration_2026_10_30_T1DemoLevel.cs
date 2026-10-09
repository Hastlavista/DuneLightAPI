using BlueDragon.DuneLight.DatabaseMigration.Extensions;
using BlueDragon.DuneLight.DatabaseMigration.Models;

namespace BlueDragon.DuneLight.DatabaseMigration.Migrations._2026;

/// <summary>
/// T1-4 — PRIVREMENO (uklanja se prije go-livea zajedno s tablicom test_tool_organizations): razina demo organizacije
/// ("Basic" = Osnova, "Full" = Puni demo), da reset stvori organizaciju iste razine. Postojeći demo redovi ostaju bez razine
/// (tretiraju se kao "Full", što je bio jedini seed do sada). Ništa se ne seeda (ADR-0022).
/// </summary>
[DeveloperMigration(2026, 10, 30, Developer.SilvioHabazin, 3)]
public class T1DemoLevel : DuneLightMigration
{
    public override void Up()
    {
        Execute.Sql(@"
            ALTER TABLE dunelight.test_tool_organizations ADD COLUMN demo_level character varying(20);
            ALTER TABLE dunelight.test_tool_organizations ADD CONSTRAINT ck_test_tool_organizations_demo_level
                CHECK (demo_level IS NULL OR demo_level IN ('Basic', 'Full'));");
    }
}
