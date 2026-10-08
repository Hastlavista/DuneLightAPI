using BlueDragon.DuneLight.DatabaseMigration.Extensions;
using BlueDragon.DuneLight.DatabaseMigration.Models;

namespace BlueDragon.DuneLight.DatabaseMigration.Migrations._2026;

/// <summary>
/// P2 (pregled 2A): kad je pauza na planu dopuštena, najveće trajanje pauze je obavezno — max_pause_days za planove
/// "od datuma kupnje", max_pause_periods za kalendarske planove. max_pauses_per_12_months smije ostati NULL. Razlog:
/// neograničena pauza + minimalna obveza koja se broji samo izvan pauze = obveza se može izbjegavati neograničeno.
/// Nova migracija jer je 20261027000000 već primijenjena.
/// </summary>
[DeveloperMigration(2026, 10, 27, Developer.SilvioHabazin, 2)]
public class P2PauseLimitRequired : DuneLightMigration
{
    public override void Up()
    {
        Execute.Sql(@"
            ALTER TABLE dunelight.membership_plan_versions ADD CONSTRAINT ck_membership_plan_versions_pause_limit_required CHECK (((NOT pause_allowed) OR (((renewal_anchor)::text = 'PurchaseDate'::text) AND (max_pause_days IS NOT NULL)) OR (((renewal_anchor)::text = 'CalendarMonth'::text) AND (max_pause_periods IS NOT NULL))));");
    }
}
