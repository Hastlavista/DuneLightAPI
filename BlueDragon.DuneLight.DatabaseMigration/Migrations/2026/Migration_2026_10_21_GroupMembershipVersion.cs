using BlueDragon.DuneLight.DatabaseMigration.Extensions;
using BlueDragon.DuneLight.DatabaseMigration.Models;

namespace BlueDragon.DuneLight.DatabaseMigration.Migrations._2026;

/// <summary>
/// Phase M1F.1 — groups.membership_version: revizija aktivnog članstva i odabira predložaka grupe. Svaka izmjena članstva
/// (AddMember, RemoveMember, promjena odabira predložaka) je inkrementira jednim UPDATE-om koji ujedno zaključava redak
/// grupe; generiranje occurrencea pamti reviziju iz koje je sastavilo planove i pod FOR SHARE lockom provjerava da je još
/// važeća (inače ponovno sastavlja). Time generiranje i izmjena članstva iste grupe serijaliziraju u jednom od dva legalna
/// redoslijeda. Razvojna baza: postojeće grupe počinju od 0.
/// </summary>
[DeveloperMigration(2026, 10, 21, Developer.SilvioHabazin, 0)]
public class GroupMembershipVersion : DuneLightMigration
{
    public override void Up()
    {
        Execute.Sql($@"
            ALTER TABLE dunelight.{Tables.Groups}
                ADD COLUMN membership_version bigint NOT NULL DEFAULT 0,
                ADD CONSTRAINT ck_groups_membership_version CHECK (membership_version >= 0);");
    }

    public override void Down()
    {
        Execute.Sql($@"
            ALTER TABLE dunelight.{Tables.Groups}
                DROP CONSTRAINT ck_groups_membership_version,
                DROP COLUMN membership_version;");
    }
}
