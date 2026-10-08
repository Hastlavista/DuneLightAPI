using BlueDragon.DuneLight.DatabaseMigration.Extensions;
using BlueDragon.DuneLight.DatabaseMigration.Models;

namespace BlueDragon.DuneLight.DatabaseMigration.Migrations._2026;

/// <summary>
/// P2 (faza 2F) — usklađivanje provizija s Vagaro modelom (decision record 2026-10-08 "kopiramo Vagaro model", ADR-0030).
/// Migracija 20261027000013 je već primijenjena lokalno, pa se ukinuti dijelovi uklanjaju ovdje:
/// - ukida se nadjačavanje po načinu plaćanja (commission_rule_source_overrides) i prekidač "oduzmi pokriće paketom";
///   "oduzmi pokriće članarinom" postaje "oduzmi popuste članstva" (commission_deduct_membership_discounts);
/// - snapshot na zapisu provizije prati iste postavke; nadjačavanje se više ne bilježi, a uz zapis se sprema objašnjenje izbora
///   pravila (primijenjeno, zašto, neprimijenjena pravila).
/// Novo:
/// - opće pravilo zaposlenika za sve individualne usluge (predmet AllServices) kao tiered model s jednom razinom bez praga
///   (commission_rule_tiers, from_revenue = 0) — faza Payroll dodaje razine po prometu bez promjene modela;
/// - "Bez provizije" (calculation_type None) kao izričit izbor pravila za uslugu;
/// - deaktivacija verzije ima datum (deactivated_from): od tog datuma pravilo ne postoji, bez povratka na stariju verziju; zato je
///   jedinstvenost verzija (zaposlenik, vrsta, predmet, datum važenja) neovisna o aktivnosti;
/// - ishod jednokratne evaluacije prve prodaje članarine i njezina osnovica (naknadna dodjela korisnika kad ga nije bilo).
/// </summary>
[DeveloperMigration(2026, 10, 27, Developer.SilvioHabazin, 14)]
public class P2CommissionsVagaroAlignment : DuneLightMigration
{
    public override void Up()
    {
        Execute.Sql(@"
            DROP TABLE dunelight.commission_rule_source_overrides;

            ALTER TABLE dunelight.organization_settings RENAME COLUMN commission_deduct_membership_coverage TO commission_deduct_membership_discounts;
            ALTER TABLE dunelight.organization_settings DROP COLUMN commission_deduct_package_coverage;

            ALTER TABLE dunelight.commission_entries RENAME COLUMN deduct_membership_coverage TO deduct_membership_discounts;
            ALTER TABLE dunelight.commission_entries DROP COLUMN deduct_package_coverage;
            ALTER TABLE dunelight.commission_entries DROP COLUMN override_applied;
            ALTER TABLE dunelight.commission_entries ADD COLUMN applied_rule_scope character varying(20);
            ALTER TABLE dunelight.commission_entries ADD COLUMN rule_evaluation jsonb;
            ALTER TABLE dunelight.commission_entries ADD CONSTRAINT ck_commission_entries_applied_rule_scope
                CHECK ((applied_rule_scope IS NULL) OR ((applied_rule_scope)::text IN ('Subject', 'AllServices')));

            ALTER TABLE dunelight.client_memberships ADD COLUMN first_sale_commission_outcome character varying(20);
            ALTER TABLE dunelight.client_memberships ADD COLUMN first_sale_base_amount numeric(10,2);
            ALTER TABLE dunelight.client_memberships ADD CONSTRAINT ck_client_memberships_first_sale_commission_outcome
                CHECK ((first_sale_commission_outcome IS NULL) OR ((first_sale_commission_outcome)::text IN ('Earned', 'NoRecipient', 'NoRule', 'ZeroBase')));");

        Execute.Sql(@"
            ALTER TABLE dunelight.commission_rules ADD COLUMN deactivated_from date;
            ALTER TABLE dunelight.commission_rules ALTER COLUMN calculation_type DROP NOT NULL;
            ALTER TABLE dunelight.commission_rules ALTER COLUMN value DROP NOT NULL;

            ALTER TABLE dunelight.commission_rules DROP CONSTRAINT ck_commission_rules_value;
            ALTER TABLE dunelight.commission_rules ADD CONSTRAINT ck_commission_rules_value CHECK (
                (((subject_type)::text = 'AllServices'::text) AND (calculation_type IS NULL) AND (value IS NULL))
                OR (((subject_type)::text <> 'AllServices'::text) AND (
                    (((calculation_type)::text = 'Percentage'::text) AND (value >= (0)::numeric) AND (value <= (100)::numeric))
                    OR (((calculation_type)::text = 'Fixed'::text) AND (value >= (0)::numeric))
                    OR (((calculation_type)::text = 'None'::text) AND ((subject_type)::text = 'Service'::text) AND (value = (0)::numeric)))));

            ALTER TABLE dunelight.commission_rules DROP CONSTRAINT ck_commission_rules_subject;
            ALTER TABLE dunelight.commission_rules ADD CONSTRAINT ck_commission_rules_subject CHECK (
                (((subject_type)::text = 'Service'::text) AND (service_id IS NOT NULL) AND (product_id IS NULL) AND (package_id IS NULL) AND (membership_plan_id IS NULL))
                OR (((subject_type)::text = 'Product'::text) AND (product_id IS NOT NULL) AND (service_id IS NULL) AND (package_id IS NULL) AND (membership_plan_id IS NULL))
                OR (((subject_type)::text = 'Package'::text) AND (package_id IS NOT NULL) AND (service_id IS NULL) AND (product_id IS NULL) AND (membership_plan_id IS NULL))
                OR (((subject_type)::text = 'MembershipPlan'::text) AND (membership_plan_id IS NOT NULL) AND (service_id IS NULL) AND (product_id IS NULL) AND (package_id IS NULL))
                OR (((subject_type)::text = 'AllServices'::text) AND (service_id IS NULL) AND (product_id IS NULL) AND (package_id IS NULL) AND (membership_plan_id IS NULL)));
            ALTER TABLE dunelight.commission_rules DROP CONSTRAINT ck_commission_rules_kind;
            ALTER TABLE dunelight.commission_rules ADD CONSTRAINT ck_commission_rules_kind CHECK (
                (((kind)::text = 'Performance'::text) AND ((subject_type)::text IN ('Service', 'AllServices')))
                OR (((kind)::text = 'Sale'::text) AND ((subject_type)::text IN ('Product', 'Package', 'MembershipPlan'))));

            DROP INDEX dunelight.ux_commission_rules_employee_kind_subject_effective;
            CREATE UNIQUE INDEX ux_commission_rules_employee_kind_subject_effective ON dunelight.commission_rules USING btree (
                organization_id, employee_id, kind, subject_type,
                COALESCE(service_id, '00000000-0000-0000-0000-000000000000'::uuid),
                COALESCE(product_id, '00000000-0000-0000-0000-000000000000'::uuid),
                COALESCE(package_id, '00000000-0000-0000-0000-000000000000'::uuid),
                COALESCE(membership_plan_id, '00000000-0000-0000-0000-000000000000'::uuid),
                effective_from);

            CREATE TABLE dunelight.commission_rule_tiers (
                id uuid NOT NULL,
                organization_id uuid NOT NULL,
                commission_rule_id uuid NOT NULL,
                from_revenue numeric(12,2) DEFAULT 0 NOT NULL,
                calculation_type character varying(20) NOT NULL,
                value numeric(10,2) NOT NULL,
                CONSTRAINT pk_commission_rule_tiers PRIMARY KEY (id),
                CONSTRAINT fk_commission_rule_tiers_rule FOREIGN KEY (commission_rule_id) REFERENCES dunelight.commission_rules(id) ON DELETE CASCADE,
                CONSTRAINT fk_commission_rule_tiers_organization FOREIGN KEY (organization_id) REFERENCES dunelight.organizations(id),
                CONSTRAINT ck_commission_rule_tiers_from_revenue CHECK ((from_revenue >= (0)::numeric)),
                CONSTRAINT ck_commission_rule_tiers_value CHECK (
                    (((calculation_type)::text = 'Percentage'::text) AND (value >= (0)::numeric) AND (value <= (100)::numeric))
                    OR (((calculation_type)::text = 'Fixed'::text) AND (value >= (0)::numeric)))
            );
            CREATE UNIQUE INDEX ux_commission_rule_tiers_rule_from_revenue ON dunelight.commission_rule_tiers (commission_rule_id, from_revenue);");
    }
}
