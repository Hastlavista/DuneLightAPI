using BlueDragon.DuneLight.DatabaseMigration.Extensions;
using BlueDragon.DuneLight.DatabaseMigration.Models;

namespace BlueDragon.DuneLight.DatabaseMigration.Migrations._2026;

/// <summary>
/// P2 (faza 2F) — provizije, Vagaro model (ADR-0030; P2_PLAN §13, §18; decision record "Provjera provizija prije 2F"):
/// - commission_rules: vrsta (Performance | Sale; postojeća pravila za proizvode i pakete su Sale, za usluge Performance — nijedan
///   iznos se ne mijenja), datum od kad vrijedi (postojeća = '0001-01-01', bez početnog datuma), predmet MembershipPlan (samo Sale);
///   unique aktivnih pravila sada uključuje vrstu i datum važenja.
/// - commission_rule_source_overrides: nadjačavanje po načinu plaćanja (Direct | Package | Membership; Percentage | Fixed | None).
/// - organization_settings: postavke osnovice (oduzmi popuste / pokriće članarinom / pokriće paketom, default isključeno) i Q38
///   (default Never).
/// - checkout_items.sale_commission_employee_id: kome ide provizija na prodaju stavke (§18.1).
/// - client_memberships: proposed_sale_commission_employee_id → sale_commission_employee_id (jedini izvor korisnika provizije na
///   prvu prodaju) i first_sale_settled_at (Q42 evaluiran jednom).
/// - commission_entries: izvori MembershipSale i PolicyFee, snapshot izvora pokrića i primijenjenih postavki, razlog storna i veza
///   korekcije (Q50); idempotencija prodajnih izvora po (izvor, source_version).
/// Migracija ne seeda ništa (ADR-0022).
/// </summary>
[DeveloperMigration(2026, 10, 27, Developer.SilvioHabazin, 13)]
public class P2Commissions : DuneLightMigration
{
    public override void Up()
    {
        Execute.Sql(@"
            ALTER TABLE dunelight.client_memberships RENAME COLUMN proposed_sale_commission_employee_id TO sale_commission_employee_id;
            ALTER TABLE dunelight.client_memberships ADD COLUMN first_sale_settled_at timestamp with time zone;
            ALTER TABLE dunelight.client_memberships ADD CONSTRAINT fk_client_memberships_sale_commission_employee
                FOREIGN KEY (sale_commission_employee_id) REFERENCES dunelight.employees(id) ON DELETE RESTRICT;

            ALTER TABLE dunelight.checkout_items ADD COLUMN sale_commission_employee_id uuid;
            ALTER TABLE dunelight.checkout_items ADD CONSTRAINT fk_checkout_items_sale_commission_employee
                FOREIGN KEY (sale_commission_employee_id) REFERENCES dunelight.employees(id) ON DELETE RESTRICT;

            ALTER TABLE dunelight.organization_settings ADD COLUMN commission_deduct_discounts boolean DEFAULT false NOT NULL;
            ALTER TABLE dunelight.organization_settings ADD COLUMN commission_deduct_membership_coverage boolean DEFAULT false NOT NULL;
            ALTER TABLE dunelight.organization_settings ADD COLUMN commission_deduct_package_coverage boolean DEFAULT false NOT NULL;
            ALTER TABLE dunelight.organization_settings ADD COLUMN commission_late_cancellation character varying(20) DEFAULT 'Never' NOT NULL;
            ALTER TABLE dunelight.organization_settings ADD CONSTRAINT ck_organization_settings_commission_late_cancellation
                CHECK (((commission_late_cancellation)::text IN ('Never', 'WhenFeePaid')));");

        Execute.Sql(@"
            ALTER TABLE dunelight.commission_rules ADD COLUMN kind character varying(20) DEFAULT 'Performance' NOT NULL;
            UPDATE dunelight.commission_rules SET kind = 'Sale' WHERE (subject_type)::text IN ('Product', 'Package');
            ALTER TABLE dunelight.commission_rules ADD COLUMN effective_from date DEFAULT '0001-01-01' NOT NULL;
            ALTER TABLE dunelight.commission_rules ADD COLUMN membership_plan_id uuid;
            ALTER TABLE dunelight.commission_rules ADD CONSTRAINT fk_commission_rules_membership_plan_id
                FOREIGN KEY (membership_plan_id) REFERENCES dunelight.membership_plans(id);

            ALTER TABLE dunelight.commission_rules DROP CONSTRAINT ck_commission_rules_subject;
            ALTER TABLE dunelight.commission_rules ADD CONSTRAINT ck_commission_rules_subject CHECK (
                (((subject_type)::text = 'Service'::text) AND (service_id IS NOT NULL) AND (product_id IS NULL) AND (package_id IS NULL) AND (membership_plan_id IS NULL))
                OR (((subject_type)::text = 'Product'::text) AND (product_id IS NOT NULL) AND (service_id IS NULL) AND (package_id IS NULL) AND (membership_plan_id IS NULL))
                OR (((subject_type)::text = 'Package'::text) AND (package_id IS NOT NULL) AND (service_id IS NULL) AND (product_id IS NULL) AND (membership_plan_id IS NULL))
                OR (((subject_type)::text = 'MembershipPlan'::text) AND (membership_plan_id IS NOT NULL) AND (service_id IS NULL) AND (product_id IS NULL) AND (package_id IS NULL)));
            -- 2F: usluga = za odrađeno; proizvod, paket i plan članarine = za prodaju (provizija na prodaju usluge je odluka nakon 2F, Q52b).
            ALTER TABLE dunelight.commission_rules ADD CONSTRAINT ck_commission_rules_kind CHECK (
                (((kind)::text = 'Performance'::text) AND ((subject_type)::text = 'Service'::text))
                OR (((kind)::text = 'Sale'::text) AND ((subject_type)::text IN ('Product', 'Package', 'MembershipPlan'))));

            DROP INDEX dunelight.ux_commission_rules_employee_subject;
            CREATE UNIQUE INDEX ux_commission_rules_employee_kind_subject_effective ON dunelight.commission_rules USING btree (
                organization_id, employee_id, kind, subject_type,
                COALESCE(service_id, '00000000-0000-0000-0000-000000000000'::uuid),
                COALESCE(product_id, '00000000-0000-0000-0000-000000000000'::uuid),
                COALESCE(package_id, '00000000-0000-0000-0000-000000000000'::uuid),
                COALESCE(membership_plan_id, '00000000-0000-0000-0000-000000000000'::uuid),
                effective_from) WHERE (is_active = true);

            CREATE TABLE dunelight.commission_rule_source_overrides (
                id uuid NOT NULL,
                organization_id uuid NOT NULL,
                commission_rule_id uuid NOT NULL,
                payment_source character varying(20) NOT NULL,
                calculation_type character varying(20) NOT NULL,
                value numeric(10,2) NOT NULL,
                CONSTRAINT pk_commission_rule_source_overrides PRIMARY KEY (id),
                CONSTRAINT fk_commission_rule_source_overrides_rule FOREIGN KEY (commission_rule_id) REFERENCES dunelight.commission_rules(id) ON DELETE CASCADE,
                CONSTRAINT fk_commission_rule_source_overrides_organization FOREIGN KEY (organization_id) REFERENCES dunelight.organizations(id),
                CONSTRAINT ck_commission_rule_source_overrides_source CHECK (((payment_source)::text IN ('Direct', 'Package', 'Membership'))),
                CONSTRAINT ck_commission_rule_source_overrides_value CHECK (
                    (((calculation_type)::text = 'Percentage'::text) AND (value >= (0)::numeric) AND (value <= (100)::numeric))
                    OR (((calculation_type)::text = 'Fixed'::text) AND (value >= (0)::numeric))
                    OR (((calculation_type)::text = 'None'::text) AND (value = (0)::numeric)))
            );
            CREATE UNIQUE INDEX ux_commission_rule_source_overrides_rule_source ON dunelight.commission_rule_source_overrides (commission_rule_id, payment_source);");

        Execute.Sql(@"
            ALTER TABLE dunelight.commission_entries ADD COLUMN client_membership_id uuid;
            ALTER TABLE dunelight.commission_entries ADD COLUMN participation_policy_consequence_id uuid;
            ALTER TABLE dunelight.commission_entries ADD COLUMN payment_source character varying(20);
            ALTER TABLE dunelight.commission_entries ADD COLUMN coverage_source_id uuid;
            ALTER TABLE dunelight.commission_entries ADD COLUMN session_price_amount numeric(10,2);
            ALTER TABLE dunelight.commission_entries ADD COLUMN list_price_amount numeric(10,2);
            ALTER TABLE dunelight.commission_entries ADD COLUMN is_manual_price boolean;
            ALTER TABLE dunelight.commission_entries ADD COLUMN deduct_discounts boolean;
            ALTER TABLE dunelight.commission_entries ADD COLUMN deduct_membership_coverage boolean;
            ALTER TABLE dunelight.commission_entries ADD COLUMN deduct_package_coverage boolean;
            ALTER TABLE dunelight.commission_entries ADD COLUMN override_applied boolean DEFAULT false NOT NULL;
            ALTER TABLE dunelight.commission_entries ADD COLUMN was_capped boolean DEFAULT false NOT NULL;
            ALTER TABLE dunelight.commission_entries ADD COLUMN reversal_reason character varying(1000);
            ALTER TABLE dunelight.commission_entries ADD COLUMN correction_of_entry_id uuid;

            ALTER TABLE dunelight.commission_entries ADD CONSTRAINT fk_commission_entries_client_membership_id
                FOREIGN KEY (client_membership_id) REFERENCES dunelight.client_memberships(id) ON DELETE RESTRICT;
            ALTER TABLE dunelight.commission_entries ADD CONSTRAINT fk_commission_entries_policy_consequence_id
                FOREIGN KEY (participation_policy_consequence_id) REFERENCES dunelight.participation_policy_consequences(id) ON DELETE CASCADE;
            ALTER TABLE dunelight.commission_entries ADD CONSTRAINT fk_commission_entries_correction_of_entry_id
                FOREIGN KEY (correction_of_entry_id) REFERENCES dunelight.commission_entries(id);
            ALTER TABLE dunelight.commission_entries ADD CONSTRAINT ck_commission_entries_payment_source
                CHECK ((payment_source IS NULL) OR ((payment_source)::text IN ('Direct', 'Package', 'Membership')));

            ALTER TABLE dunelight.commission_entries DROP CONSTRAINT ck_commission_entries_source;
            ALTER TABLE dunelight.commission_entries ADD CONSTRAINT ck_commission_entries_source CHECK (
                (((source_type)::text = 'IndividualService'::text) AND (booking_segment_participation_id IS NOT NULL) AND (booking_id IS NOT NULL) AND (appointment_id IS NOT NULL)
                    AND (appointment_segment_id IS NULL) AND (checkout_item_id IS NULL) AND (client_membership_id IS NULL) AND (participation_policy_consequence_id IS NULL))
                OR (((source_type)::text = 'GroupService'::text) AND (appointment_id IS NOT NULL) AND (appointment_segment_id IS NOT NULL) AND (booking_id IS NULL)
                    AND (booking_segment_participation_id IS NULL) AND (checkout_item_id IS NULL) AND (client_membership_id IS NULL) AND (participation_policy_consequence_id IS NULL))
                OR (((source_type)::text IN ('ProductSale', 'PackageSale')) AND (checkout_item_id IS NOT NULL) AND (appointment_id IS NULL) AND (booking_id IS NULL)
                    AND (booking_segment_participation_id IS NULL) AND (appointment_segment_id IS NULL) AND (client_membership_id IS NULL) AND (participation_policy_consequence_id IS NULL))
                OR (((source_type)::text = 'MembershipSale'::text) AND (client_membership_id IS NOT NULL) AND (checkout_item_id IS NULL) AND (appointment_id IS NULL)
                    AND (booking_id IS NULL) AND (booking_segment_participation_id IS NULL) AND (appointment_segment_id IS NULL) AND (participation_policy_consequence_id IS NULL))
                OR (((source_type)::text = 'PolicyFee'::text) AND (participation_policy_consequence_id IS NOT NULL) AND (booking_segment_participation_id IS NOT NULL)
                    AND (booking_id IS NOT NULL) AND (appointment_id IS NOT NULL) AND (appointment_segment_id IS NULL) AND (checkout_item_id IS NULL) AND (client_membership_id IS NULL)));

            DROP INDEX dunelight.ux_commission_entries_checkout_item_id;
            CREATE UNIQUE INDEX ux_commission_entries_checkout_item_source_version ON dunelight.commission_entries USING btree (checkout_item_id, source_version)
                WHERE (checkout_item_id IS NOT NULL);
            DROP INDEX dunelight.ux_commission_entries_participation_employee_source_version;
            CREATE UNIQUE INDEX ux_commission_entries_participation_employee_source_version ON dunelight.commission_entries USING btree (booking_segment_participation_id, employee_id, source_version)
                WHERE ((source_type)::text = 'IndividualService'::text);
            CREATE UNIQUE INDEX ux_commission_entries_membership_sale_source_version ON dunelight.commission_entries USING btree (client_membership_id, source_version)
                WHERE ((source_type)::text = 'MembershipSale'::text);
            CREATE UNIQUE INDEX ux_commission_entries_policy_fee_employee_source_version ON dunelight.commission_entries USING btree (participation_policy_consequence_id, employee_id, source_version)
                WHERE ((source_type)::text = 'PolicyFee'::text);
            CREATE INDEX ix_commission_entries_org_reversed ON dunelight.commission_entries USING btree (organization_id, reversed_at)
                WHERE (reversed_at IS NOT NULL);");
    }
}
