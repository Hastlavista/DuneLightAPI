using BlueDragon.DuneLight.DatabaseMigration.Extensions;
using BlueDragon.DuneLight.DatabaseMigration.Models;

namespace BlueDragon.DuneLight.DatabaseMigration.Migrations._2026;

/// <summary>
/// P2 (faza 2D, Q15.4/Q18/Q53/Q54) — dug uz postavku "blokiraj rezervaciju":
/// - grant appointments.membership-block.override (rezervacija unatoč blokadi) dodaje se SAMO inicijalnim Admin grupama
///   (grant_groups.system_key = 'admin', ADR-0023). Idempotentno.
/// - group_occurrence_membership_skips: član grupe u dugu preskočen pri generiranju (ili pridruživanju) budućeg segmenta
///   occurrencea; ostaje član grupe. Popis za recepciju i osnova za naknadno dodavanje nakon plaćanja duga (Q53): redom po
///   datumu dok ima mjesta; razrješenje (Added | CapacityFull | Conflict | AlreadyParticipating | NotApplicable) se bilježi.
/// </summary>
[DeveloperMigration(2026, 10, 27, Developer.SilvioHabazin, 10)]
public class P2MembershipDebtBlock : DuneLightMigration
{
    public override void Up()
    {
        Execute.Sql(@"
            INSERT INTO dunelight.grant_group_grants (id, grant_group_id, grant_key)
            SELECT gen_random_uuid(), g.id, 'appointments.membership-block.override'
            FROM dunelight.grant_groups g
            WHERE g.system_key = 'admin'
            ON CONFLICT (grant_group_id, grant_key) DO NOTHING;");

        Execute.Sql(@"
            CREATE TABLE dunelight.group_occurrence_membership_skips (
                id uuid NOT NULL,
                organization_id uuid NOT NULL,
                group_id uuid NOT NULL,
                appointment_id uuid NOT NULL,
                appointment_segment_id uuid NOT NULL,
                client_id uuid NOT NULL,
                client_membership_id uuid NOT NULL,
                skipped_at timestamp with time zone NOT NULL,
                skipped_by uuid,
                resolution character varying(30),
                resolved_at timestamp with time zone,
                participation_id uuid,
                CONSTRAINT pk_group_occurrence_membership_skips PRIMARY KEY (id),
                CONSTRAINT fk_group_occurrence_membership_skips_organization FOREIGN KEY (organization_id) REFERENCES dunelight.organizations (id),
                CONSTRAINT fk_group_occurrence_membership_skips_group FOREIGN KEY (group_id) REFERENCES dunelight.groups (id),
                CONSTRAINT fk_group_occurrence_membership_skips_appointment FOREIGN KEY (appointment_id) REFERENCES dunelight.appointments (id) ON DELETE CASCADE,
                CONSTRAINT fk_group_occurrence_membership_skips_segment FOREIGN KEY (appointment_segment_id) REFERENCES dunelight.appointment_segments (id) ON DELETE CASCADE,
                CONSTRAINT fk_group_occurrence_membership_skips_client FOREIGN KEY (client_id) REFERENCES dunelight.clients (id),
                CONSTRAINT fk_group_occurrence_membership_skips_membership FOREIGN KEY (client_membership_id) REFERENCES dunelight.client_memberships (id),
                CONSTRAINT ck_group_occurrence_membership_skips_resolution CHECK ((
                    (resolution IS NULL AND resolved_at IS NULL)
                    OR (resolution IN ('Added', 'CapacityFull', 'Conflict', 'AlreadyParticipating', 'NotApplicable') AND resolved_at IS NOT NULL)))
            );
            CREATE UNIQUE INDEX ux_group_occurrence_membership_skips_segment_client
                ON dunelight.group_occurrence_membership_skips (appointment_segment_id, client_id);
            CREATE INDEX ix_group_occurrence_membership_skips_open
                ON dunelight.group_occurrence_membership_skips (organization_id, client_id) WHERE resolution IS NULL;");
    }
}
