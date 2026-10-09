using BlueDragon.DuneLight.DatabaseMigration.Extensions;
using BlueDragon.DuneLight.DatabaseMigration.Models;

namespace BlueDragon.DuneLight.DatabaseMigration.Migrations._2026;

/// <summary>
/// K1-4 (12.3) — šifrarnik razloga otkazivanja/izostanka: (1) tablica šifri po organizaciji (aktivni naziv jedinstven);
/// (2) šifra + snapshot naziva na sudjelovanju (otkaz samo uz Cancelled, izostanak samo uz NoShow) i na eksplicitno otkazanom
/// terminu; (3) postavke obaveznosti po događaju (default opcionalno); (4) grant catalog.cancellation-reasons.manage samo
/// Admin grupama (ADR-0023).
/// </summary>
[DeveloperMigration(2026, 10, 28, Developer.SilvioHabazin, 2)]
public class K1CancellationReasons : DuneLightMigration
{
    public override void Up()
    {
        Execute.Sql(@"
            CREATE TABLE dunelight.cancellation_reasons (
                id uuid NOT NULL,
                organization_id uuid NOT NULL,
                name character varying(255) NOT NULL,
                is_active boolean NOT NULL,
                sort_order integer NOT NULL,
                applies_to_client_cancellation boolean NOT NULL,
                applies_to_business_cancellation boolean NOT NULL,
                applies_to_no_show boolean NOT NULL,
                created_at timestamp with time zone NOT NULL,
                created_by uuid,
                updated_at timestamp with time zone,
                updated_by uuid,
                CONSTRAINT ck_cancellation_reasons_applies_to CHECK (applies_to_client_cancellation OR applies_to_business_cancellation OR applies_to_no_show)
            );

            ALTER TABLE dunelight.cancellation_reasons ADD CONSTRAINT pk_cancellation_reasons PRIMARY KEY (id);

            CREATE UNIQUE INDEX ux_cancellation_reasons_org_name_active ON dunelight.cancellation_reasons USING btree (organization_id, lower(trim(name))) WHERE is_active;

            ALTER TABLE dunelight.cancellation_reasons ADD CONSTRAINT fk_cancellation_reasons_organization_id FOREIGN KEY (organization_id) REFERENCES dunelight.organizations(id);");

        Execute.Sql(@"
            ALTER TABLE dunelight.booking_segment_participations
                ADD COLUMN cancellation_reason_code_id uuid,
                ADD COLUMN cancellation_reason_code_name character varying(255),
                ADD COLUMN no_show_reason_code_id uuid,
                ADD COLUMN no_show_reason_code_name character varying(255),
                ADD CONSTRAINT fk_booking_segment_participations_cancellation_reason_code_id FOREIGN KEY (cancellation_reason_code_id) REFERENCES dunelight.cancellation_reasons(id) ON DELETE RESTRICT,
                ADD CONSTRAINT fk_booking_segment_participations_no_show_reason_code_id FOREIGN KEY (no_show_reason_code_id) REFERENCES dunelight.cancellation_reasons(id) ON DELETE RESTRICT,
                ADD CONSTRAINT ck_booking_segment_participations_cancellation_reason_code CHECK (((cancellation_reason_code_id IS NULL) = (cancellation_reason_code_name IS NULL)) AND (cancellation_reason_code_id IS NULL OR status = 'Cancelled')),
                ADD CONSTRAINT ck_booking_segment_participations_no_show_reason_code CHECK (((no_show_reason_code_id IS NULL) = (no_show_reason_code_name IS NULL)) AND (no_show_reason_code_id IS NULL OR status = 'NoShow'));

            ALTER TABLE dunelight.appointments
                ADD COLUMN cancellation_reason_code_id uuid,
                ADD COLUMN cancellation_reason_code_name character varying(255),
                ADD CONSTRAINT fk_appointments_cancellation_reason_code_id FOREIGN KEY (cancellation_reason_code_id) REFERENCES dunelight.cancellation_reasons(id) ON DELETE RESTRICT,
                ADD CONSTRAINT ck_appointments_cancellation_reason_code CHECK (((cancellation_reason_code_id IS NULL) = (cancellation_reason_code_name IS NULL)) AND (cancellation_reason_code_id IS NULL OR cancelled_at IS NOT NULL));

            ALTER TABLE dunelight.organization_settings
                ADD COLUMN cancellation_reason_required_client boolean NOT NULL DEFAULT false,
                ADD COLUMN cancellation_reason_required_business boolean NOT NULL DEFAULT false,
                ADD COLUMN cancellation_reason_required_no_show boolean NOT NULL DEFAULT false;");

        Execute.Sql(@"
            INSERT INTO dunelight.grant_group_grants (id, grant_group_id, grant_key)
            SELECT gen_random_uuid(), g.id, 'catalog.cancellation-reasons.manage'
            FROM dunelight.grant_groups g
            WHERE g.system_key = 'admin'
            ON CONFLICT (grant_group_id, grant_key) DO NOTHING;");
    }
}
