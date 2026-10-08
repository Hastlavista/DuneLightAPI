using BlueDragon.DuneLight.DatabaseMigration.Extensions;
using BlueDragon.DuneLight.DatabaseMigration.Models;

namespace BlueDragon.DuneLight.DatabaseMigration.Migrations._2026;

/// <summary>
/// P2 (faza 2C): otvoreni periodi članstva, zaduženja (period + početna naknada) s projekcijom plaćenosti, stavka checkouta
/// tipa MembershipCharge (FK + lock zaduženja u otvorenom checkoutu), pravila duga organizacije (Q15, automatski završetak
/// nakon N neplaćenih perioda) i razlog završetka NonPayment. Ne seeda ništa (ADR-0022).
/// </summary>
[DeveloperMigration(2026, 10, 27, Developer.SilvioHabazin, 6)]
public class P2MembershipCharges : DuneLightMigration
{
    public override void Up()
    {
        // organization_settings — Q15 pravila duga
        Execute.Sql(@"
            ALTER TABLE dunelight.organization_settings ADD COLUMN membership_grace_days integer DEFAULT 7 NOT NULL;
            ALTER TABLE dunelight.organization_settings ADD COLUMN membership_debt_behavior character varying(20) DEFAULT 'StopCovering'::character varying NOT NULL;
            ALTER TABLE dunelight.organization_settings ADD COLUMN membership_auto_end_after_unpaid_periods integer;

            ALTER TABLE dunelight.organization_settings ADD CONSTRAINT ck_organization_settings_membership_grace_days CHECK (((membership_grace_days >= 0) AND (membership_grace_days <= 365)));
            ALTER TABLE dunelight.organization_settings ADD CONSTRAINT ck_organization_settings_membership_debt_behavior CHECK ((membership_debt_behavior IN ('KeepCovering', 'StopCovering', 'BlockBooking')));
            ALTER TABLE dunelight.organization_settings ADD CONSTRAINT ck_organization_settings_membership_auto_end CHECK (((membership_auto_end_after_unpaid_periods IS NULL) OR (membership_auto_end_after_unpaid_periods >= 1)));");

        // client_memberships — razlog završetka NonPayment; sidro trenutnih uvjeta (od kad se računaju granice perioda trenutnih
        // uvjeta; nakon promjene uvjeta na obnovi to je datum promjene, osim kad PurchaseDate s istim intervalom zadržava sidro).
        // Lokalna razvojna baza ima samo testne retke (ADR-0003): postojeći dobivaju starts_on.
        Execute.Sql(@"
            ALTER TABLE dunelight.client_memberships ADD COLUMN terms_anchor_on date;
            UPDATE dunelight.client_memberships SET terms_anchor_on = starts_on;
            ALTER TABLE dunelight.client_memberships ALTER COLUMN terms_anchor_on SET NOT NULL;
            ALTER TABLE dunelight.client_memberships ADD CONSTRAINT ck_client_memberships_terms_anchor CHECK ((terms_anchor_on >= starts_on));

            ALTER TABLE dunelight.client_memberships DROP CONSTRAINT ck_client_memberships_end;
            ALTER TABLE dunelight.client_memberships ADD CONSTRAINT ck_client_memberships_end CHECK ((((ends_on IS NULL) AND (end_reason IS NULL)) OR ((ends_on IS NOT NULL) AND (end_reason IN ('Cancelled', 'EndOverride', 'PlanDeactivated', 'NonPayment')))));");

        // membership_periods
        Execute.Sql(@"
            CREATE TABLE dunelight.membership_periods (
                id uuid NOT NULL,
                organization_id uuid NOT NULL,
                client_membership_id uuid NOT NULL,
                starts_on date NOT NULL,
                ends_on date NOT NULL,
                plan_version_id uuid NOT NULL,
                price numeric(10,2) NOT NULL,
                created_at timestamp with time zone NOT NULL,
                created_by uuid,
                CONSTRAINT ck_membership_periods_range CHECK ((ends_on >= starts_on)),
                CONSTRAINT ck_membership_periods_price CHECK ((price >= (0)::numeric))
            );

            ALTER TABLE dunelight.membership_periods ADD CONSTRAINT pk_membership_periods PRIMARY KEY (id);

            CREATE UNIQUE INDEX ux_membership_periods_id_organization ON dunelight.membership_periods USING btree (id, organization_id);

            CREATE UNIQUE INDEX ux_membership_periods_membership_start ON dunelight.membership_periods USING btree (client_membership_id, starts_on);

            ALTER TABLE dunelight.membership_periods ADD CONSTRAINT fk_membership_periods_membership_organization FOREIGN KEY (client_membership_id, organization_id) REFERENCES dunelight.client_memberships(id, organization_id) ON DELETE RESTRICT;

            ALTER TABLE dunelight.membership_periods ADD CONSTRAINT fk_membership_periods_version_organization FOREIGN KEY (plan_version_id, organization_id) REFERENCES dunelight.membership_plan_versions(id, organization_id) ON DELETE RESTRICT;");

        // membership_charges
        Execute.Sql(@"
            CREATE TABLE dunelight.membership_charges (
                id uuid NOT NULL,
                organization_id uuid NOT NULL,
                client_id uuid NOT NULL,
                client_membership_id uuid NOT NULL,
                period_id uuid,
                kind character varying(20) NOT NULL,
                description character varying(255) NOT NULL,
                amount numeric(10,2) NOT NULL,
                due_on date NOT NULL,
                lifecycle character varying(20) NOT NULL,
                settled_amount numeric(10,2) DEFAULT 0 NOT NULL,
                settlement_status character varying(20) DEFAULT 'Unpaid'::character varying NOT NULL,
                written_off_at timestamp with time zone,
                written_off_by uuid,
                write_off_reason character varying(500),
                voided_at timestamp with time zone,
                voided_by uuid,
                void_reason character varying(500),
                created_at timestamp with time zone NOT NULL,
                created_by uuid,
                CONSTRAINT ck_membership_charges_kind CHECK ((((kind)::text = 'Period'::text) AND (period_id IS NOT NULL)) OR (((kind)::text = 'StartFee'::text) AND (period_id IS NULL))),
                CONSTRAINT ck_membership_charges_amount CHECK ((amount > (0)::numeric)),
                CONSTRAINT ck_membership_charges_lifecycle CHECK ((lifecycle IN ('Open', 'WrittenOff', 'Voided'))),
                CONSTRAINT ck_membership_charges_settlement_status CHECK ((settlement_status IN ('Unpaid', 'PartiallyPaid', 'Paid'))),
                CONSTRAINT ck_membership_charges_settled CHECK (((settled_amount >= (0)::numeric) AND (settled_amount <= amount))),
                CONSTRAINT ck_membership_charges_written_off CHECK (((((lifecycle)::text = 'WrittenOff'::text) AND (written_off_at IS NOT NULL) AND (write_off_reason IS NOT NULL)) OR (((lifecycle)::text <> 'WrittenOff'::text) AND (written_off_at IS NULL)))),
                CONSTRAINT ck_membership_charges_voided CHECK (((((lifecycle)::text = 'Voided'::text) AND (voided_at IS NOT NULL) AND (void_reason IS NOT NULL)) OR (((lifecycle)::text <> 'Voided'::text) AND (voided_at IS NULL))))
            );

            ALTER TABLE dunelight.membership_charges ADD CONSTRAINT pk_membership_charges PRIMARY KEY (id);

            CREATE UNIQUE INDEX ux_membership_charges_id_organization ON dunelight.membership_charges USING btree (id, organization_id);

            CREATE UNIQUE INDEX ux_membership_charges_period ON dunelight.membership_charges USING btree (period_id) WHERE ((period_id IS NOT NULL) AND ((lifecycle)::text <> 'Voided'::text));

            CREATE UNIQUE INDEX ux_membership_charges_start_fee ON dunelight.membership_charges USING btree (client_membership_id) WHERE (((kind)::text = 'StartFee'::text) AND ((lifecycle)::text <> 'Voided'::text));

            CREATE INDEX ix_membership_charges_membership ON dunelight.membership_charges USING btree (client_membership_id);

            CREATE INDEX ix_membership_charges_unpaid ON dunelight.membership_charges USING btree (organization_id, due_on) WHERE (((lifecycle)::text = 'Open'::text) AND ((settlement_status)::text <> 'Paid'::text));

            ALTER TABLE dunelight.membership_charges ADD CONSTRAINT fk_membership_charges_membership_organization FOREIGN KEY (client_membership_id, organization_id) REFERENCES dunelight.client_memberships(id, organization_id) ON DELETE RESTRICT;

            ALTER TABLE dunelight.membership_charges ADD CONSTRAINT fk_membership_charges_period_organization FOREIGN KEY (period_id, organization_id) REFERENCES dunelight.membership_periods(id, organization_id) ON DELETE RESTRICT;

            ALTER TABLE dunelight.membership_charges ADD CONSTRAINT fk_membership_charges_client_id FOREIGN KEY (client_id) REFERENCES dunelight.clients(id) ON DELETE RESTRICT;");

        // checkout_items — stavka MembershipCharge
        Execute.Sql(@"
            ALTER TABLE dunelight.checkout_items ADD COLUMN membership_charge_id uuid;
            ALTER TABLE dunelight.checkout_items ADD COLUMN locks_membership_charge boolean DEFAULT false NOT NULL;

            ALTER TABLE dunelight.checkout_items DROP CONSTRAINT ck_checkout_items_quantity;
            ALTER TABLE dunelight.checkout_items ADD CONSTRAINT ck_checkout_items_quantity CHECK (((((type)::text = 'Booking'::text) AND (quantity = 1)) OR (((type)::text = 'Package'::text) AND (quantity = 1)) OR (((type)::text = 'Product'::text) AND (quantity >= 1)) OR (((type)::text = 'MembershipCharge'::text) AND (quantity = 1))));

            ALTER TABLE dunelight.checkout_items DROP CONSTRAINT ck_checkout_items_subject;
            ALTER TABLE dunelight.checkout_items ADD CONSTRAINT ck_checkout_items_subject CHECK (((((type)::text = 'Booking'::text) AND (booking_segment_participation_id IS NOT NULL) AND (package_id IS NULL) AND (product_id IS NULL) AND (membership_charge_id IS NULL)) OR (((type)::text = 'Package'::text) AND (package_id IS NOT NULL) AND (booking_segment_participation_id IS NULL) AND (product_id IS NULL) AND (membership_charge_id IS NULL)) OR (((type)::text = 'Product'::text) AND (product_id IS NOT NULL) AND (booking_segment_participation_id IS NULL) AND (package_id IS NULL) AND (membership_charge_id IS NULL)) OR (((type)::text = 'MembershipCharge'::text) AND (membership_charge_id IS NOT NULL) AND (booking_segment_participation_id IS NULL) AND (package_id IS NULL) AND (product_id IS NULL))));

            CREATE UNIQUE INDEX ux_checkout_items_locks_membership_charge ON dunelight.checkout_items USING btree (membership_charge_id) WHERE (locks_membership_charge = true);

            CREATE INDEX ix_checkout_items_membership_charge ON dunelight.checkout_items USING btree (membership_charge_id) WHERE (membership_charge_id IS NOT NULL);

            ALTER TABLE dunelight.checkout_items ADD CONSTRAINT fk_checkout_items_membership_charge_organization FOREIGN KEY (membership_charge_id, organization_id) REFERENCES dunelight.membership_charges(id, organization_id) ON DELETE RESTRICT;");
    }
}
