using BlueDragon.DuneLight.DatabaseMigration.Extensions;
using BlueDragon.DuneLight.DatabaseMigration.Models;

namespace BlueDragon.DuneLight.DatabaseMigration.Migrations._2026;

/// <summary>
/// P2 (faza 2A), korak 1/2: katalog planova članarina — profil, nepromjenjive verzije uvjeta, pokrivene usluge, odabrane
/// poslovnice i limiti korištenja (docs/p2/P2_DECISION_RECORD.md). Sve tablice nose organization_id; djeca se vežu složenim
/// FK (id, organization_id) na verziju, kao kod politika otkazivanja. Ne seeda ništa (ADR-0022).
/// </summary>
[DeveloperMigration(2026, 10, 27, Developer.SilvioHabazin, 0)]
public class P2MembershipPlans : DuneLightMigration
{
    public override void Up()
    {
        // membership_plans
        Execute.Sql(@"
            CREATE TABLE dunelight.membership_plans (
                id uuid NOT NULL,
                organization_id uuid NOT NULL,
                name character varying(255) NOT NULL,
                description character varying(2000),
                is_active boolean NOT NULL,
                max_active_memberships integer,
                created_at timestamp with time zone NOT NULL,
                created_by uuid,
                updated_at timestamp with time zone,
                updated_by uuid,
                CONSTRAINT ck_membership_plans_max_active CHECK (((max_active_memberships IS NULL) OR (max_active_memberships >= 1)))
            );

            ALTER TABLE dunelight.membership_plans ADD CONSTRAINT pk_membership_plans PRIMARY KEY (id);

            CREATE UNIQUE INDEX ux_membership_plans_id_organization ON dunelight.membership_plans USING btree (id, organization_id);

            CREATE UNIQUE INDEX ux_membership_plans_organization_active_name ON dunelight.membership_plans USING btree (organization_id, lower(btrim((name)::text))) WHERE is_active;

            ALTER TABLE dunelight.membership_plans ADD CONSTRAINT fk_membership_plans_organization_id FOREIGN KEY (organization_id) REFERENCES dunelight.organizations(id);");

        // membership_plan_versions
        Execute.Sql(@"
            CREATE TABLE dunelight.membership_plan_versions (
                id uuid NOT NULL,
                organization_id uuid NOT NULL,
                membership_plan_id uuid NOT NULL,
                version integer NOT NULL,
                price numeric(10,2) NOT NULL,
                start_fee numeric(10,2) NOT NULL,
                billing_interval character varying(20) NOT NULL,
                renewal_anchor character varying(20) NOT NULL,
                company_scope character varying(20) NOT NULL,
                minimum_commitment_periods integer,
                cancellation_notice_days integer,
                pause_allowed boolean NOT NULL,
                max_pause_days integer,
                max_pause_periods integer,
                max_pauses_per_12_months integer,
                pause_extends_period boolean NOT NULL,
                created_at timestamp with time zone NOT NULL,
                created_by uuid,
                CONSTRAINT ck_membership_plan_versions_version CHECK ((version >= 1)),
                CONSTRAINT ck_membership_plan_versions_amounts CHECK (((price >= (0)::numeric) AND (start_fee >= (0)::numeric))),
                CONSTRAINT ck_membership_plan_versions_billing_interval CHECK ((billing_interval IN ('Monthly', 'Yearly'))),
                CONSTRAINT ck_membership_plan_versions_renewal_anchor CHECK ((renewal_anchor IN ('PurchaseDate', 'CalendarMonth'))),
                CONSTRAINT ck_membership_plan_versions_calendar_monthly CHECK ((((renewal_anchor)::text <> 'CalendarMonth'::text) OR ((billing_interval)::text = 'Monthly'::text))),
                CONSTRAINT ck_membership_plan_versions_company_scope CHECK ((company_scope IN ('AllCompanies', 'SelectedCompanies'))),
                CONSTRAINT ck_membership_plan_versions_commitment CHECK (((minimum_commitment_periods IS NULL) OR (minimum_commitment_periods >= 1))),
                CONSTRAINT ck_membership_plan_versions_notice CHECK (((cancellation_notice_days IS NULL) OR (cancellation_notice_days >= 1))),
                CONSTRAINT ck_membership_plan_versions_pause_limits CHECK ((((max_pause_days IS NULL) OR (max_pause_days >= 1)) AND ((max_pause_periods IS NULL) OR (max_pause_periods >= 1)) AND ((max_pauses_per_12_months IS NULL) OR (max_pauses_per_12_months >= 1)))),
                CONSTRAINT ck_membership_plan_versions_pause_disabled CHECK ((pause_allowed OR ((max_pause_days IS NULL) AND (max_pause_periods IS NULL) AND (max_pauses_per_12_months IS NULL) AND (NOT pause_extends_period)))),
                CONSTRAINT ck_membership_plan_versions_pause_by_anchor CHECK (((((renewal_anchor)::text = 'PurchaseDate'::text) AND (max_pause_periods IS NULL)) OR (((renewal_anchor)::text = 'CalendarMonth'::text) AND (max_pause_days IS NULL) AND (NOT pause_extends_period))))
            );

            ALTER TABLE dunelight.membership_plan_versions ADD CONSTRAINT pk_membership_plan_versions PRIMARY KEY (id);

            CREATE UNIQUE INDEX ux_membership_plan_versions_id_organization ON dunelight.membership_plan_versions USING btree (id, organization_id);

            CREATE UNIQUE INDEX ux_membership_plan_versions_plan_version ON dunelight.membership_plan_versions USING btree (membership_plan_id, version);

            ALTER TABLE dunelight.membership_plan_versions ADD CONSTRAINT fk_membership_plan_versions_plan_organization FOREIGN KEY (membership_plan_id, organization_id) REFERENCES dunelight.membership_plans(id, organization_id) ON DELETE RESTRICT;");

        // membership_plan_version_services
        Execute.Sql(@"
            CREATE TABLE dunelight.membership_plan_version_services (
                id uuid NOT NULL,
                organization_id uuid NOT NULL,
                membership_plan_version_id uuid NOT NULL,
                service_id uuid NOT NULL
            );

            ALTER TABLE dunelight.membership_plan_version_services ADD CONSTRAINT pk_membership_plan_version_services PRIMARY KEY (id);

            CREATE UNIQUE INDEX ux_membership_plan_version_services_version_service ON dunelight.membership_plan_version_services USING btree (membership_plan_version_id, service_id);

            CREATE INDEX ix_membership_plan_version_services_service ON dunelight.membership_plan_version_services USING btree (service_id);

            ALTER TABLE dunelight.membership_plan_version_services ADD CONSTRAINT fk_membership_plan_version_services_version_organization FOREIGN KEY (membership_plan_version_id, organization_id) REFERENCES dunelight.membership_plan_versions(id, organization_id) ON DELETE RESTRICT;

            ALTER TABLE dunelight.membership_plan_version_services ADD CONSTRAINT fk_membership_plan_version_services_service_id FOREIGN KEY (service_id) REFERENCES dunelight.services(id) ON DELETE RESTRICT;");

        // membership_plan_version_companies
        Execute.Sql(@"
            CREATE TABLE dunelight.membership_plan_version_companies (
                id uuid NOT NULL,
                organization_id uuid NOT NULL,
                membership_plan_version_id uuid NOT NULL,
                company_id uuid NOT NULL
            );

            ALTER TABLE dunelight.membership_plan_version_companies ADD CONSTRAINT pk_membership_plan_version_companies PRIMARY KEY (id);

            CREATE UNIQUE INDEX ux_membership_plan_version_companies_version_company ON dunelight.membership_plan_version_companies USING btree (membership_plan_version_id, company_id);

            CREATE INDEX ix_membership_plan_version_companies_company ON dunelight.membership_plan_version_companies USING btree (company_id);

            ALTER TABLE dunelight.membership_plan_version_companies ADD CONSTRAINT fk_membership_plan_version_companies_version_organization FOREIGN KEY (membership_plan_version_id, organization_id) REFERENCES dunelight.membership_plan_versions(id, organization_id) ON DELETE RESTRICT;

            ALTER TABLE dunelight.membership_plan_version_companies ADD CONSTRAINT fk_membership_plan_version_companies_company_id FOREIGN KEY (company_id) REFERENCES dunelight.companies(id) ON DELETE RESTRICT;");

        // membership_plan_usage_limits
        Execute.Sql(@"
            CREATE TABLE dunelight.membership_plan_usage_limits (
                id uuid NOT NULL,
                organization_id uuid NOT NULL,
                membership_plan_version_id uuid NOT NULL,
                service_id uuid,
                usage_window character varying(20) NOT NULL,
                max_uses integer NOT NULL,
                CONSTRAINT ck_membership_plan_usage_limits_window CHECK ((usage_window IN ('Period', 'Day', 'Week', 'Month', 'Quarter'))),
                CONSTRAINT ck_membership_plan_usage_limits_max_uses CHECK ((max_uses >= 1))
            );

            ALTER TABLE dunelight.membership_plan_usage_limits ADD CONSTRAINT pk_membership_plan_usage_limits PRIMARY KEY (id);

            CREATE UNIQUE INDEX ux_membership_plan_usage_limits_scope_window ON dunelight.membership_plan_usage_limits USING btree (membership_plan_version_id, COALESCE(service_id, '00000000-0000-0000-0000-000000000000'::uuid), usage_window);

            CREATE INDEX ix_membership_plan_usage_limits_service ON dunelight.membership_plan_usage_limits USING btree (service_id) WHERE (service_id IS NOT NULL);

            ALTER TABLE dunelight.membership_plan_usage_limits ADD CONSTRAINT fk_membership_plan_usage_limits_version_organization FOREIGN KEY (membership_plan_version_id, organization_id) REFERENCES dunelight.membership_plan_versions(id, organization_id) ON DELETE RESTRICT;

            ALTER TABLE dunelight.membership_plan_usage_limits ADD CONSTRAINT fk_membership_plan_usage_limits_service_id FOREIGN KEY (service_id) REFERENCES dunelight.services(id) ON DELETE RESTRICT;");
    }
}
