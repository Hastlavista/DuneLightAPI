using BlueDragon.DuneLight.DatabaseMigration.Extensions;
using BlueDragon.DuneLight.DatabaseMigration.Models;

namespace BlueDragon.DuneLight.DatabaseMigration.Migrations._2026;

/// <summary>
/// K1-8 (bug b, P-1) — članstvo "stoji" dok su sve poslovnice opsega plana neaktivne: sustavna pauza (source CompanyClosure)
/// koju otvara i zatvara obnova; otvorena sustavna pauza nema planirani kraj. Novi razlog završetka članstva za otkaz tijekom
/// stajanja (završava odmah, bez roka i obveze).
/// </summary>
[DeveloperMigration(2026, 10, 28, Developer.SilvioHabazin, 3)]
public class K1MembershipCompanyClosure : DuneLightMigration
{
    public override void Up()
    {
        Execute.Sql(@"
            ALTER TABLE dunelight.membership_pauses
                ADD COLUMN source character varying(20) NOT NULL DEFAULT 'Client',
                ALTER COLUMN planned_ends_on DROP NOT NULL,
                DROP CONSTRAINT ck_membership_pauses_range,
                DROP CONSTRAINT ck_membership_pauses_actual,
                ADD CONSTRAINT ck_membership_pauses_source CHECK (source IN ('Client', 'CompanyClosure')),
                ADD CONSTRAINT ck_membership_pauses_range CHECK ((planned_ends_on IS NULL AND source = 'CompanyClosure') OR planned_ends_on >= starts_on),
                ADD CONSTRAINT ck_membership_pauses_actual CHECK (actual_ends_on IS NULL OR (actual_ends_on >= starts_on AND (planned_ends_on IS NULL OR actual_ends_on <= planned_ends_on)));

            ALTER TABLE dunelight.client_memberships DROP CONSTRAINT ck_client_memberships_end;
            ALTER TABLE dunelight.client_memberships ADD CONSTRAINT ck_client_memberships_end CHECK ((((ends_on IS NULL) AND (end_reason IS NULL)) OR ((ends_on IS NOT NULL) AND (end_reason IN ('Cancelled', 'EndOverride', 'PlanDeactivated', 'NonPayment', 'CancelledDuringCompanyClosure')))));");
    }
}
