using BlueDragon.DuneLight.DatabaseMigration.Extensions;
using BlueDragon.DuneLight.DatabaseMigration.Models;

namespace BlueDragon.DuneLight.DatabaseMigration.Migrations._2026;

/// <summary>
/// P2 (faza 2E) — cjenovna pogodnost članarine i opći mehanizam prilagodbi cijene (Q1, Q2, P2_PLAN §10.4):
/// - membership_plan_price_benefits: pravila pogodnosti verzije plana — opseg izričit (AllServices | Service; prazna usluga nikad
///   ne znači "sve"), tip PercentOff | AmountOff | FixedPrice; najviše jedno pravilo po usluzi i jedno AllServices po verziji.
/// - booking_segment_participations: primijenjena prilagodba (tip, izvor, snapshot pravila) i evaluacija svih kandidata (jsonb) —
///   snapshot iz trenutka cijene; AdjustmentAmount = predložena − osnovna cijena.
/// - participation_membership_coverages: zadnja automatska promjena cijene (stara, nova, događaj, vrijeme), razlog zaštite od
///   automatske promjene (ručni iznos, već plaćeno) i oznaka da promjena čeka (sudjelovanje je bilo zaključano).
/// </summary>
[DeveloperMigration(2026, 10, 27, Developer.SilvioHabazin, 12)]
public class P2MembershipPriceBenefits : DuneLightMigration
{
    public override void Up()
    {
        Execute.Sql(@"
            CREATE TABLE dunelight.membership_plan_price_benefits (
                id uuid NOT NULL,
                organization_id uuid NOT NULL,
                membership_plan_version_id uuid NOT NULL,
                scope character varying(20) NOT NULL,
                service_id uuid,
                benefit_type character varying(20) NOT NULL,
                value numeric(10,2) NOT NULL,
                CONSTRAINT pk_membership_plan_price_benefits PRIMARY KEY (id),
                CONSTRAINT fk_membership_plan_price_benefits_version_organization FOREIGN KEY (membership_plan_version_id, organization_id) REFERENCES dunelight.membership_plan_versions(id, organization_id) ON DELETE RESTRICT,
                CONSTRAINT fk_membership_plan_price_benefits_service FOREIGN KEY (service_id) REFERENCES dunelight.services(id) ON DELETE RESTRICT,
                CONSTRAINT ck_membership_plan_price_benefits_scope CHECK (((((scope)::text = 'AllServices'::text) AND (service_id IS NULL)) OR (((scope)::text = 'Service'::text) AND (service_id IS NOT NULL)))),
                CONSTRAINT ck_membership_plan_price_benefits_type CHECK (((benefit_type)::text IN ('PercentOff', 'AmountOff', 'FixedPrice'))),
                CONSTRAINT ck_membership_plan_price_benefits_value CHECK (((((benefit_type)::text = 'PercentOff'::text) AND (value > (0)::numeric) AND (value <= (100)::numeric))
                    OR (((benefit_type)::text = 'AmountOff'::text) AND (value > (0)::numeric))
                    OR (((benefit_type)::text = 'FixedPrice'::text) AND (value >= (0)::numeric))))
            );
            CREATE UNIQUE INDEX ux_membership_plan_price_benefits_service ON dunelight.membership_plan_price_benefits (membership_plan_version_id, service_id)
                WHERE (service_id IS NOT NULL);
            CREATE UNIQUE INDEX ux_membership_plan_price_benefits_all ON dunelight.membership_plan_price_benefits (membership_plan_version_id)
                WHERE ((scope)::text = 'AllServices'::text);
            CREATE INDEX ix_membership_plan_price_benefits_service_ref ON dunelight.membership_plan_price_benefits (service_id) WHERE (service_id IS NOT NULL);");

        Execute.Sql(@"
            ALTER TABLE dunelight.booking_segment_participations
                ADD COLUMN adjustment_type character varying(20),
                ADD COLUMN adjustment_source_id uuid,
                ADD COLUMN adjustment_rule_snapshot jsonb,
                ADD COLUMN adjustment_evaluation jsonb,
                ADD CONSTRAINT ck_booking_segment_participations_adjustment CHECK ((((adjustment_type IS NULL) AND (adjustment_source_id IS NULL) AND (adjustment_rule_snapshot IS NULL))
                    OR ((adjustment_type IN ('Membership', 'ClientTag', 'ClientGroup', 'Promo')) AND (adjustment_source_id IS NOT NULL) AND (adjustment_amount IS NOT NULL))));

            ALTER TABLE dunelight.participation_membership_coverages
                ADD COLUMN last_price_change_old_amount numeric(10,2),
                ADD COLUMN last_price_change_new_amount numeric(10,2),
                ADD COLUMN last_price_change_event character varying(30),
                ADD COLUMN last_price_change_at timestamp with time zone,
                ADD COLUMN price_protected_reason character varying(20),
                ADD COLUMN price_stale boolean NOT NULL DEFAULT false,
                ADD CONSTRAINT ck_participation_membership_coverages_price_change CHECK ((((last_price_change_at IS NULL) AND (last_price_change_old_amount IS NULL) AND (last_price_change_new_amount IS NULL) AND (last_price_change_event IS NULL))
                    OR ((last_price_change_at IS NOT NULL) AND (last_price_change_old_amount IS NOT NULL) AND (last_price_change_new_amount IS NOT NULL) AND (last_price_change_event IS NOT NULL)))),
                ADD CONSTRAINT ck_participation_membership_coverages_price_protected CHECK (((price_protected_reason IS NULL) OR ((price_protected_reason)::text IN ('ManualAmount', 'AlreadyPaid'))));");
    }
}
