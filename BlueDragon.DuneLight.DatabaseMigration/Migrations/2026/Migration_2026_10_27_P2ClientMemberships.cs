using BlueDragon.DuneLight.DatabaseMigration.Extensions;
using BlueDragon.DuneLight.DatabaseMigration.Models;

namespace BlueDragon.DuneLight.DatabaseMigration.Migrations._2026;

/// <summary>
/// P2 (faza 2B): članstva klijenata, pauze i povijest naredbi (docs/p2). Uvjeti članstva su nepromjenjiva verzija plana
/// (složeni FK na membership_plan_versions); stanje se izvodi u aplikaciji. Uz to postavka organizacije za rok najave izmjene
/// plana (Q14, default 30 dana). Ne seeda ništa (ADR-0022).
/// </summary>
[DeveloperMigration(2026, 10, 27, Developer.SilvioHabazin, 3)]
public class P2ClientMemberships : DuneLightMigration
{
    public override void Up()
    {
        // organization_settings — Q14 rok najave
        Execute.Sql(@"
            ALTER TABLE dunelight.organization_settings ADD COLUMN membership_change_notice_days integer DEFAULT 30 NOT NULL;

            ALTER TABLE dunelight.organization_settings ADD CONSTRAINT ck_organization_settings_membership_change_notice_days CHECK (((membership_change_notice_days >= 0) AND (membership_change_notice_days <= 365)));");

        // client_memberships
        Execute.Sql(@"
            CREATE TABLE dunelight.client_memberships (
                id uuid NOT NULL,
                organization_id uuid NOT NULL,
                client_id uuid NOT NULL,
                membership_plan_id uuid NOT NULL,
                plan_version_id uuid NOT NULL,
                starts_on date NOT NULL,
                anchor_day integer NOT NULL,
                sold_company_id uuid NOT NULL,
                sold_via character varying(20) NOT NULL,
                sold_by uuid,
                proposed_sale_commission_employee_id uuid,
                pending_plan_version_id uuid,
                pending_effective_on date,
                pending_source character varying(30),
                cancellation_requested_at timestamp with time zone,
                cancellation_requested_by uuid,
                cancellation_reason character varying(500),
                ends_on date,
                end_reason character varying(30),
                voided_at timestamp with time zone,
                voided_by uuid,
                void_reason character varying(500),
                created_at timestamp with time zone NOT NULL,
                created_by uuid,
                updated_at timestamp with time zone,
                updated_by uuid,
                CONSTRAINT ck_client_memberships_anchor_day CHECK (((anchor_day >= 1) AND (anchor_day <= 31))),
                CONSTRAINT ck_client_memberships_sold_via CHECK ((sold_via IN ('Staff', 'Online'))),
                CONSTRAINT ck_client_memberships_pending CHECK ((((pending_plan_version_id IS NULL) AND (pending_effective_on IS NULL) AND (pending_source IS NULL)) OR ((pending_plan_version_id IS NOT NULL) AND (pending_effective_on IS NOT NULL) AND (pending_source IN ('ClientPlanChange', 'PlanUpdate'))))),
                CONSTRAINT ck_client_memberships_end CHECK ((((ends_on IS NULL) AND (end_reason IS NULL)) OR ((ends_on IS NOT NULL) AND (end_reason IN ('Cancelled', 'EndOverride', 'PlanDeactivated'))))),
                CONSTRAINT ck_client_memberships_ends_after_start CHECK (((ends_on IS NULL) OR (ends_on >= starts_on))),
                CONSTRAINT ck_client_memberships_cancelled_has_request CHECK ((((end_reason)::text <> 'Cancelled'::text) OR (end_reason IS NULL) OR (cancellation_requested_at IS NOT NULL))),
                CONSTRAINT ck_client_memberships_void CHECK ((((voided_at IS NULL) AND (void_reason IS NULL)) OR ((voided_at IS NOT NULL) AND (void_reason IS NOT NULL))))
            );

            ALTER TABLE dunelight.client_memberships ADD CONSTRAINT pk_client_memberships PRIMARY KEY (id);

            CREATE UNIQUE INDEX ux_client_memberships_id_organization ON dunelight.client_memberships USING btree (id, organization_id);

            CREATE INDEX ix_client_memberships_org_client ON dunelight.client_memberships USING btree (organization_id, client_id);

            CREATE INDEX ix_client_memberships_plan ON dunelight.client_memberships USING btree (membership_plan_id);

            ALTER TABLE dunelight.client_memberships ADD CONSTRAINT fk_client_memberships_organization_id FOREIGN KEY (organization_id) REFERENCES dunelight.organizations(id);

            ALTER TABLE dunelight.client_memberships ADD CONSTRAINT fk_client_memberships_client_id FOREIGN KEY (client_id) REFERENCES dunelight.clients(id) ON DELETE RESTRICT;

            ALTER TABLE dunelight.client_memberships ADD CONSTRAINT fk_client_memberships_plan_organization FOREIGN KEY (membership_plan_id, organization_id) REFERENCES dunelight.membership_plans(id, organization_id) ON DELETE RESTRICT;

            ALTER TABLE dunelight.client_memberships ADD CONSTRAINT fk_client_memberships_version_organization FOREIGN KEY (plan_version_id, organization_id) REFERENCES dunelight.membership_plan_versions(id, organization_id) ON DELETE RESTRICT;

            ALTER TABLE dunelight.client_memberships ADD CONSTRAINT fk_client_memberships_pending_version FOREIGN KEY (pending_plan_version_id) REFERENCES dunelight.membership_plan_versions(id) ON DELETE RESTRICT;

            ALTER TABLE dunelight.client_memberships ADD CONSTRAINT fk_client_memberships_sold_company_id FOREIGN KEY (sold_company_id) REFERENCES dunelight.companies(id) ON DELETE RESTRICT;");

        // membership_pauses
        Execute.Sql(@"
            CREATE TABLE dunelight.membership_pauses (
                id uuid NOT NULL,
                organization_id uuid NOT NULL,
                client_membership_id uuid NOT NULL,
                kind character varying(20) NOT NULL,
                starts_on date NOT NULL,
                planned_ends_on date NOT NULL,
                actual_ends_on date,
                reason character varying(500),
                cancelled_at timestamp with time zone,
                cancelled_by uuid,
                cancellation_reason character varying(30),
                created_at timestamp with time zone NOT NULL,
                created_by uuid,
                updated_at timestamp with time zone,
                updated_by uuid,
                CONSTRAINT ck_membership_pauses_kind CHECK ((kind IN ('Days', 'SkipPeriods'))),
                CONSTRAINT ck_membership_pauses_range CHECK ((planned_ends_on >= starts_on)),
                CONSTRAINT ck_membership_pauses_actual CHECK (((actual_ends_on IS NULL) OR ((actual_ends_on >= starts_on) AND (actual_ends_on <= planned_ends_on)))),
                CONSTRAINT ck_membership_pauses_cancelled CHECK ((((cancelled_at IS NULL) AND (cancellation_reason IS NULL)) OR ((cancelled_at IS NOT NULL) AND (actual_ends_on IS NULL) AND (cancellation_reason IN ('Withdrawn', 'MembershipCancellation', 'MembershipEnded', 'MembershipVoided')))))
            );

            ALTER TABLE dunelight.membership_pauses ADD CONSTRAINT pk_membership_pauses PRIMARY KEY (id);

            CREATE INDEX ix_membership_pauses_membership ON dunelight.membership_pauses USING btree (client_membership_id);

            ALTER TABLE dunelight.membership_pauses ADD CONSTRAINT fk_membership_pauses_membership_organization FOREIGN KEY (client_membership_id, organization_id) REFERENCES dunelight.client_memberships(id, organization_id) ON DELETE RESTRICT;");

        // client_membership_audit_log
        Execute.Sql(@"
            CREATE TABLE dunelight.client_membership_audit_log (
                id uuid NOT NULL,
                organization_id uuid NOT NULL,
                client_membership_id uuid NOT NULL,
                change_type character varying(60) NOT NULL,
                old_value text,
                new_value text,
                reason character varying(500),
                changed_at timestamp with time zone NOT NULL,
                changed_by uuid
            );

            ALTER TABLE dunelight.client_membership_audit_log ADD CONSTRAINT pk_client_membership_audit_log PRIMARY KEY (id);

            CREATE INDEX ix_client_membership_audit_log_membership ON dunelight.client_membership_audit_log USING btree (client_membership_id);

            ALTER TABLE dunelight.client_membership_audit_log ADD CONSTRAINT fk_client_membership_audit_log_membership_organization FOREIGN KEY (client_membership_id, organization_id) REFERENCES dunelight.client_memberships(id, organization_id) ON DELETE RESTRICT;");
    }
}
