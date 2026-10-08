using BlueDragon.DuneLight.DatabaseMigration.Extensions;
using BlueDragon.DuneLight.DatabaseMigration.Models;

namespace BlueDragon.DuneLight.DatabaseMigration.Migrations._2026;

/// <summary>
/// P2 (faza 2D) — pokriće sudjelovanja članarinom:
/// - membership_usages: ledger korištenja (Claim −1 / Release +1 s referencom na claim, nikad brisanje); aktivan claim je
///   najviše jedan po sudjelovanju. Datum usluge u kalendaru poslovnice termina (prozori limita, Q17) i u kalendaru organizacije
///   (pripadnost periodu, Q22).
/// - participation_membership_coverages: projekcija odluke o pokriću po sudjelovanju (stanje, razlog, događaj koji ju je
///   promijenio, iscrpljeni limit) — jedan pisac (IMembershipCoverageService), čita je settlement i read model.
///   Obje tablice namjerno NEMAJU FK na booking_segment_participations: FK bi pri pisanju uzimao KEY SHARE lock na
///   sudjelovanje, a strana članstva (pauza, obnova, dug) piše pokriće tuđih sudjelovanja pod lockom članstva — to bi s
///   prijelazom sudjelovanja (lock sudjelovanja → lock članstva) stvorilo deadlock. Brisanje netaknutog sudjelovanja prije
///   brisanja oslobađa claim i briše projekciju (IMembershipCoverageService.ReleaseForRemoval).
/// - P1 (Q31): MembershipAction po događaju u verziji politike (default ForfeitCredit) i snapshot na posljedici.
/// - organization_settings.membership_limit_exceeded_behavior (Q4, default FallbackToNextSource).
/// </summary>
[DeveloperMigration(2026, 10, 27, Developer.SilvioHabazin, 9)]
public class P2MembershipCoverage : DuneLightMigration
{
    public override void Up()
    {
        Execute.Sql(@"
            CREATE TABLE dunelight.membership_usages (
                id uuid NOT NULL,
                organization_id uuid NOT NULL,
                client_membership_id uuid NOT NULL,
                participation_id uuid NOT NULL,
                service_id uuid NOT NULL,
                company_id uuid NOT NULL,
                service_date date NOT NULL,
                period_date date NOT NULL,
                entry_type character varying(20) NOT NULL,
                units smallint NOT NULL,
                reverses_usage_id uuid,
                is_active boolean NOT NULL,
                released_at timestamp with time zone,
                release_reason character varying(40),
                created_at timestamp with time zone NOT NULL,
                created_by uuid,
                CONSTRAINT pk_membership_usages PRIMARY KEY (id),
                CONSTRAINT fk_membership_usages_organization FOREIGN KEY (organization_id) REFERENCES dunelight.organizations (id),
                CONSTRAINT fk_membership_usages_membership FOREIGN KEY (client_membership_id) REFERENCES dunelight.client_memberships (id),
                CONSTRAINT fk_membership_usages_reverses FOREIGN KEY (reverses_usage_id) REFERENCES dunelight.membership_usages (id),
                CONSTRAINT ck_membership_usages_entry CHECK ((
                    ((entry_type)::text = 'Claim'::text AND units = -1 AND reverses_usage_id IS NULL
                        AND ((is_active AND released_at IS NULL AND release_reason IS NULL)
                             OR (NOT is_active AND released_at IS NOT NULL AND release_reason IS NOT NULL)))
                    OR ((entry_type)::text = 'Release'::text AND units = 1 AND reverses_usage_id IS NOT NULL AND NOT is_active
                        AND release_reason IS NOT NULL)))
            );
            CREATE UNIQUE INDEX ux_membership_usages_active_claim ON dunelight.membership_usages (participation_id)
                WHERE (((entry_type)::text = 'Claim'::text) AND is_active);
            CREATE INDEX ix_membership_usages_membership_active ON dunelight.membership_usages (client_membership_id)
                WHERE is_active;
            CREATE INDEX ix_membership_usages_participation ON dunelight.membership_usages (participation_id);");

        Execute.Sql(@"
            CREATE TABLE dunelight.participation_membership_coverages (
                participation_id uuid NOT NULL,
                organization_id uuid NOT NULL,
                client_membership_id uuid,
                status character varying(30) NOT NULL,
                reason character varying(40) NOT NULL,
                changed_by_event character varying(30) NOT NULL,
                active_usage_id uuid,
                limit_window character varying(20),
                limit_service_id uuid,
                limit_max_uses integer,
                limit_used integer,
                expected_period_starts_on date,
                evaluated_at timestamp with time zone NOT NULL,
                evaluated_by uuid,
                CONSTRAINT pk_participation_membership_coverages PRIMARY KEY (participation_id),
                CONSTRAINT fk_participation_membership_coverages_organization FOREIGN KEY (organization_id) REFERENCES dunelight.organizations (id),
                CONSTRAINT fk_participation_membership_coverages_membership FOREIGN KEY (client_membership_id) REFERENCES dunelight.client_memberships (id),
                CONSTRAINT fk_participation_membership_coverages_usage FOREIGN KEY (active_usage_id) REFERENCES dunelight.membership_usages (id),
                CONSTRAINT ck_participation_membership_coverages_status CHECK (((status)::text IN ('Covered', 'NotCovered', 'PendingEvaluation', 'Released'))),
                CONSTRAINT ck_participation_membership_coverages_usage CHECK (((((status)::text = 'Covered'::text) AND (active_usage_id IS NOT NULL)) OR (((status)::text <> 'Covered'::text) AND (active_usage_id IS NULL)))),
                CONSTRAINT ck_participation_membership_coverages_limit CHECK ((((reason)::text = 'LimitReached'::text) = (limit_window IS NOT NULL)))
            );
            CREATE INDEX ix_participation_membership_coverages_membership ON dunelight.participation_membership_coverages (client_membership_id);");

        Execute.Sql(@"
            ALTER TABLE dunelight.cancellation_policy_versions
                ADD COLUMN late_cancellation_membership_action character varying(30) NOT NULL DEFAULT 'ForfeitCredit',
                ADD COLUMN no_show_membership_action character varying(30) NOT NULL DEFAULT 'ForfeitCredit',
                ADD CONSTRAINT ck_cancellation_policy_versions_late_membership_action CHECK ((late_cancellation_membership_action IN ('ForfeitCredit', 'ReturnCreditChargeFee'))),
                ADD CONSTRAINT ck_cancellation_policy_versions_no_show_membership_action CHECK ((no_show_membership_action IN ('ForfeitCredit', 'ReturnCreditChargeFee')));

            ALTER TABLE dunelight.participation_policy_consequences
                ADD COLUMN membership_action character varying(30),
                ADD COLUMN client_membership_id uuid,
                ADD COLUMN membership_usage_id uuid,
                ADD COLUMN membership_credit_forfeited boolean NOT NULL DEFAULT false,
                ADD CONSTRAINT fk_participation_policy_consequences_membership FOREIGN KEY (client_membership_id) REFERENCES dunelight.client_memberships (id),
                ADD CONSTRAINT fk_participation_policy_consequences_usage FOREIGN KEY (membership_usage_id) REFERENCES dunelight.membership_usages (id),
                ADD CONSTRAINT ck_participation_policy_consequences_membership CHECK ((
                    (membership_action IS NULL AND client_membership_id IS NULL AND membership_usage_id IS NULL AND NOT membership_credit_forfeited)
                    OR (membership_action IN ('ForfeitCredit', 'ReturnCreditChargeFee') AND client_membership_id IS NOT NULL)));

            ALTER TABLE dunelight.organization_settings
                ADD COLUMN membership_limit_exceeded_behavior character varying(30) NOT NULL DEFAULT 'FallbackToNextSource',
                ADD CONSTRAINT ck_organization_settings_membership_limit_behavior CHECK ((membership_limit_exceeded_behavior IN ('FallbackToNextSource', 'Reject')));");
    }
}
