using BlueDragon.DuneLight.DatabaseMigration.Extensions;
using BlueDragon.DuneLight.DatabaseMigration.Models;

namespace BlueDragon.DuneLight.DatabaseMigration.Migrations._2026;

/// <summary>
/// P1 (ADR-0016/ADR-0017), korak 2/3: strukturirani metapodaci otkazivanja/izostanka na sudjelovanju (uvijek odgovaraju
/// statusu), ledger posljedica politike, okidač potrošnje paketa (izvršenje usluge / kazna politike) i uklanjanje
/// organization_settings.cancellation_cutoff_minutes. Čista ciljna shema (ADR-0003), bez backfilla.
/// </summary>
[DeveloperMigration(2026, 10, 26, Developer.SilvioHabazin, 1)]
public class P1ParticipationConsequences : DuneLightMigration
{
    public override void Up()
    {
        // booking_segment_participations — metapodaci otkazivanja (samo Cancelled) i izostanka (samo NoShow)
        Execute.Sql(@"
            ALTER TABLE dunelight.booking_segment_participations
                ADD COLUMN cancellation_initiator character varying(20),
                ADD COLUMN cancelled_at timestamp with time zone,
                ADD COLUMN cancelled_by uuid,
                ADD COLUMN cancellation_policy_id uuid,
                ADD COLUMN cancellation_policy_version integer,
                ADD COLUMN applied_cancellation_window_minutes integer,
                ADD COLUMN no_show_at timestamp with time zone,
                ADD COLUMN no_show_by uuid,
                ADD COLUMN no_show_reason text;

            ALTER TABLE dunelight.booking_segment_participations ADD CONSTRAINT ck_booking_segment_participations_cancellation_initiator CHECK (((cancellation_initiator IS NULL) OR (cancellation_initiator IN ('Client', 'Business', 'System'))));

            ALTER TABLE dunelight.booking_segment_participations ADD CONSTRAINT ck_booking_segment_participations_cancellation_metadata CHECK (((((status)::text = 'Cancelled'::text) AND (cancellation_initiator IS NOT NULL) AND (cancelled_at IS NOT NULL)) OR (((status)::text <> 'Cancelled'::text) AND (cancellation_initiator IS NULL) AND (cancelled_at IS NULL) AND (cancelled_by IS NULL) AND (cancellation_reason IS NULL))));

            ALTER TABLE dunelight.booking_segment_participations ADD CONSTRAINT ck_booking_segment_participations_client_classification CHECK (((((cancellation_initiator)::text = 'Client'::text) AND (is_late_cancellation IS NOT NULL) AND (cancellation_policy_id IS NOT NULL) AND (cancellation_policy_version IS NOT NULL) AND (applied_cancellation_window_minutes IS NOT NULL)) OR (((cancellation_initiator IS NULL) OR ((cancellation_initiator)::text <> 'Client'::text)) AND (is_late_cancellation IS NULL) AND (cancellation_policy_id IS NULL) AND (cancellation_policy_version IS NULL) AND (applied_cancellation_window_minutes IS NULL))));

            ALTER TABLE dunelight.booking_segment_participations ADD CONSTRAINT ck_booking_segment_participations_no_show_metadata CHECK (((((status)::text = 'NoShow'::text) AND (no_show_at IS NOT NULL)) OR (((status)::text <> 'NoShow'::text) AND (no_show_at IS NULL) AND (no_show_by IS NULL) AND (no_show_reason IS NULL))));

            ALTER TABLE dunelight.booking_segment_participations ADD CONSTRAINT fk_booking_segment_participations_cancellation_policy_id FOREIGN KEY (cancellation_policy_id) REFERENCES dunelight.cancellation_policies(id) ON DELETE RESTRICT;");

        // participation_policy_consequences — nepromjenjiv ledger posljedica politike
        Execute.Sql(@"
            CREATE TABLE dunelight.participation_policy_consequences (
                id uuid NOT NULL,
                organization_id uuid NOT NULL,
                booking_segment_participation_id uuid NOT NULL,
                source_version integer NOT NULL,
                event character varying(30) NOT NULL,
                fee_type character varying(20) NOT NULL,
                configured_fee_value numeric(10,2),
                fee_base_amount numeric(10,2) NOT NULL,
                calculated_fee_amount numeric(10,2) NOT NULL,
                was_fee_capped boolean NOT NULL,
                cancellation_policy_id uuid NOT NULL,
                cancellation_policy_version integer NOT NULL,
                package_action character varying(20) NOT NULL,
                package_unit_consumed boolean NOT NULL,
                client_package_id uuid,
                status character varying(20) NOT NULL,
                created_at timestamp with time zone NOT NULL,
                created_by uuid,
                reversed_at timestamp with time zone,
                reversed_by uuid,
                reversal_reason text,
                waived_at timestamp with time zone,
                waived_by uuid,
                waiver_reason text,
                CONSTRAINT ck_participation_policy_consequences_event CHECK ((event IN ('LateCancellation', 'NoShow'))),
                CONSTRAINT ck_participation_policy_consequences_fee_type CHECK ((fee_type IN ('None', 'Fixed', 'Percentage'))),
                CONSTRAINT ck_participation_policy_consequences_package_action CHECK ((package_action IN ('None', 'ConsumeUnit'))),
                CONSTRAINT ck_participation_policy_consequences_status CHECK ((status IN ('Active', 'Waived', 'Reversed'))),
                CONSTRAINT ck_participation_policy_consequences_amounts CHECK (((fee_base_amount >= (0)::numeric) AND (calculated_fee_amount >= (0)::numeric) AND (calculated_fee_amount <= fee_base_amount))),
                CONSTRAINT ck_participation_policy_consequences_source_version CHECK ((source_version >= 1)),
                CONSTRAINT ck_participation_policy_consequences_package CHECK ((((package_unit_consumed = true) AND (client_package_id IS NOT NULL) AND ((package_action)::text = 'ConsumeUnit'::text)) OR ((package_unit_consumed = false) AND (client_package_id IS NULL)))),
                CONSTRAINT ck_participation_policy_consequences_state CHECK (((((status)::text = 'Active'::text) AND (reversed_at IS NULL) AND (reversed_by IS NULL) AND (reversal_reason IS NULL) AND (waived_at IS NULL) AND (waived_by IS NULL) AND (waiver_reason IS NULL)) OR (((status)::text = 'Reversed'::text) AND (reversed_at IS NOT NULL) AND (reversal_reason IS NOT NULL) AND (waived_at IS NULL) AND (waived_by IS NULL) AND (waiver_reason IS NULL)) OR (((status)::text = 'Waived'::text) AND (waived_at IS NOT NULL) AND (waiver_reason IS NOT NULL) AND (reversed_at IS NULL) AND (reversed_by IS NULL) AND (reversal_reason IS NULL))))
            );

            ALTER TABLE dunelight.participation_policy_consequences ADD CONSTRAINT pk_participation_policy_consequences PRIMARY KEY (id);

            CREATE UNIQUE INDEX ux_participation_policy_consequences_participation_source_version ON dunelight.participation_policy_consequences USING btree (booking_segment_participation_id, source_version);

            CREATE UNIQUE INDEX ux_participation_policy_consequences_active_participation ON dunelight.participation_policy_consequences USING btree (booking_segment_participation_id) WHERE ((status)::text = 'Active'::text);

            CREATE UNIQUE INDEX ux_participation_policy_consequences_id_participation ON dunelight.participation_policy_consequences USING btree (id, booking_segment_participation_id);

            ALTER TABLE dunelight.participation_policy_consequences ADD CONSTRAINT fk_participation_policy_consequences_participation_organization FOREIGN KEY (booking_segment_participation_id, organization_id) REFERENCES dunelight.booking_segment_participations(id, organization_id) ON DELETE RESTRICT;

            ALTER TABLE dunelight.participation_policy_consequences ADD CONSTRAINT fk_participation_policy_consequences_policy_organization FOREIGN KEY (cancellation_policy_id, organization_id) REFERENCES dunelight.cancellation_policies(id, organization_id) ON DELETE RESTRICT;

            ALTER TABLE dunelight.participation_policy_consequences ADD CONSTRAINT fk_participation_policy_consequences_client_package_id FOREIGN KEY (client_package_id) REFERENCES dunelight.client_packages(id) ON DELETE RESTRICT;");

        // package_consumptions — okidač potrošnje i veza na posljedicu politike (D6)
        Execute.Sql(@"
            ALTER TABLE dunelight.package_consumptions
                ADD COLUMN ""trigger"" character varying(30) DEFAULT 'ServiceCompletion'::character varying NOT NULL,
                ADD COLUMN participation_policy_consequence_id uuid;

            ALTER TABLE dunelight.package_consumptions ADD CONSTRAINT ck_package_consumptions_trigger CHECK (((((""trigger"")::text = 'ServiceCompletion'::text) AND (participation_policy_consequence_id IS NULL)) OR (((""trigger"")::text = 'PolicyConsequence'::text) AND (participation_policy_consequence_id IS NOT NULL) AND (units = 1))));

            ALTER TABLE dunelight.package_consumptions DROP CONSTRAINT ck_package_consumptions_reversal_reason;

            ALTER TABLE dunelight.package_consumptions ADD CONSTRAINT ck_package_consumptions_reversal_reason CHECK (((reversal_reason IS NULL) OR (reversal_reason IN ('Cancellation', 'NoShow', 'CompletionCorrection', 'PolicyConsequenceReversed', 'PolicyConsequenceWaived'))));

            CREATE UNIQUE INDEX ux_package_consumptions_policy_consequence ON dunelight.package_consumptions USING btree (participation_policy_consequence_id) WHERE (participation_policy_consequence_id IS NOT NULL);

            ALTER TABLE dunelight.package_consumptions ADD CONSTRAINT fk_package_consumptions_policy_consequence FOREIGN KEY (participation_policy_consequence_id, booking_segment_participation_id) REFERENCES dunelight.participation_policy_consequences(id, booking_segment_participation_id) ON DELETE RESTRICT;");

        // organization_settings — P1 (D1): rok otkazivanja je dio verzije politike
        Execute.Sql(@"
            ALTER TABLE dunelight.organization_settings DROP COLUMN cancellation_cutoff_minutes;");
    }
}
