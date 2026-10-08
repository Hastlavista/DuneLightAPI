using BlueDragon.DuneLight.DatabaseMigration.Extensions;
using BlueDragon.DuneLight.DatabaseMigration.Models;

namespace BlueDragon.DuneLight.DatabaseMigration.Migrations._2026;

/// <summary>
/// P2 (pregled 2B): (1) istisnuta izmjena plana — kad klijentova promjena plana istisne zakazanu izmjenu plana (ili izmjena
/// stigne dok klijentova promjena čeka), izmjena se pamti s IZVORNIM datumom, pa povlačenje promjene vraća točno stanje kao da
/// je nikad nije bilo; (2) trajna oznaka da izmjena plana nije primijenjena na članstvo (preklapanje, Q10) dok se ne riješi.
/// Nova migracija jer je 20261027000003 već primijenjena.
/// </summary>
[DeveloperMigration(2026, 10, 27, Developer.SilvioHabazin, 5)]
public class P2PlanUpdateTracking : DuneLightMigration
{
    public override void Up()
    {
        Execute.Sql(@"
            ALTER TABLE dunelight.client_memberships ADD COLUMN displaced_plan_version_id uuid;
            ALTER TABLE dunelight.client_memberships ADD COLUMN displaced_effective_on date;
            ALTER TABLE dunelight.client_memberships ADD COLUMN plan_update_skipped_version_id uuid;
            ALTER TABLE dunelight.client_memberships ADD COLUMN plan_update_skipped_reason character varying(60);
            ALTER TABLE dunelight.client_memberships ADD COLUMN plan_update_skipped_at timestamp with time zone;

            ALTER TABLE dunelight.client_memberships ADD CONSTRAINT ck_client_memberships_displaced CHECK ((((displaced_plan_version_id IS NULL) AND (displaced_effective_on IS NULL)) OR ((displaced_plan_version_id IS NOT NULL) AND (displaced_effective_on IS NOT NULL))));

            ALTER TABLE dunelight.client_memberships ADD CONSTRAINT ck_client_memberships_plan_update_skipped CHECK ((((plan_update_skipped_version_id IS NULL) AND (plan_update_skipped_reason IS NULL) AND (plan_update_skipped_at IS NULL)) OR ((plan_update_skipped_version_id IS NOT NULL) AND (plan_update_skipped_reason IS NOT NULL) AND (plan_update_skipped_at IS NOT NULL))));

            ALTER TABLE dunelight.client_memberships ADD CONSTRAINT fk_client_memberships_displaced_version FOREIGN KEY (displaced_plan_version_id) REFERENCES dunelight.membership_plan_versions(id) ON DELETE RESTRICT;

            ALTER TABLE dunelight.client_memberships ADD CONSTRAINT fk_client_memberships_plan_update_skipped_version FOREIGN KEY (plan_update_skipped_version_id) REFERENCES dunelight.membership_plan_versions(id) ON DELETE RESTRICT;

            CREATE INDEX ix_client_memberships_plan_update_skipped ON dunelight.client_memberships USING btree (organization_id, membership_plan_id) WHERE (plan_update_skipped_version_id IS NOT NULL);");
    }
}
