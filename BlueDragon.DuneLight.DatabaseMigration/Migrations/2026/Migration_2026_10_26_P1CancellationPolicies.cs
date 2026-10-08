using BlueDragon.DuneLight.DatabaseMigration.Extensions;
using BlueDragon.DuneLight.DatabaseMigration.Models;

namespace BlueDragon.DuneLight.DatabaseMigration.Migrations._2026;

/// <summary>
/// P1 (ADR-0015), korak 1/3: imenovani profili politike otkazivanja, nepromjenjive verzije i dodjele po scopeu. Zadana
/// politika organizacije je profil s is_organization_default (točno jedan po organizaciji); nastaje pri registraciji
/// organizacije (aplikacijski kod) — migracija ne seeda ništa (ADR-0022; postojećih organizacija nema, P1 decision log).
/// </summary>
[DeveloperMigration(2026, 10, 26, Developer.SilvioHabazin, 0)]
public class P1CancellationPolicies : DuneLightMigration
{
    public override void Up()
    {
        // cancellation_policies
        Execute.Sql(@"
            CREATE TABLE dunelight.cancellation_policies (
                id uuid NOT NULL,
                organization_id uuid NOT NULL,
                name character varying(255) NOT NULL,
                is_active boolean NOT NULL,
                is_organization_default boolean NOT NULL,
                created_at timestamp with time zone NOT NULL,
                created_by uuid,
                updated_at timestamp with time zone,
                updated_by uuid,
                CONSTRAINT ck_cancellation_policies_default_is_active CHECK (((NOT is_organization_default) OR is_active))
            );

            ALTER TABLE dunelight.cancellation_policies ADD CONSTRAINT pk_cancellation_policies PRIMARY KEY (id);

            CREATE UNIQUE INDEX ux_cancellation_policies_id_organization ON dunelight.cancellation_policies USING btree (id, organization_id);

            CREATE UNIQUE INDEX ux_cancellation_policies_organization_default ON dunelight.cancellation_policies USING btree (organization_id) WHERE is_organization_default;

            CREATE UNIQUE INDEX ux_cancellation_policies_organization_active_name ON dunelight.cancellation_policies USING btree (organization_id, lower((name)::text)) WHERE is_active;

            ALTER TABLE dunelight.cancellation_policies ADD CONSTRAINT fk_cancellation_policies_organization_id FOREIGN KEY (organization_id) REFERENCES dunelight.organizations(id);");

        // cancellation_policy_versions
        Execute.Sql(@"
            CREATE TABLE dunelight.cancellation_policy_versions (
                id uuid NOT NULL,
                organization_id uuid NOT NULL,
                cancellation_policy_id uuid NOT NULL,
                version integer NOT NULL,
                cancellation_window_minutes integer NOT NULL,
                late_cancellation_fee_type character varying(20) NOT NULL,
                late_cancellation_fee_value numeric(10,2),
                late_cancellation_package_action character varying(20) NOT NULL,
                no_show_fee_type character varying(20) NOT NULL,
                no_show_fee_value numeric(10,2),
                no_show_package_action character varying(20) NOT NULL,
                created_at timestamp with time zone NOT NULL,
                created_by uuid,
                CONSTRAINT ck_cancellation_policy_versions_version CHECK ((version >= 1)),
                CONSTRAINT ck_cancellation_policy_versions_window CHECK ((cancellation_window_minutes >= 0)),
                CONSTRAINT ck_cancellation_policy_versions_late_fee_type CHECK ((late_cancellation_fee_type IN ('None', 'Fixed', 'Percentage'))),
                CONSTRAINT ck_cancellation_policy_versions_no_show_fee_type CHECK ((no_show_fee_type IN ('None', 'Fixed', 'Percentage'))),
                CONSTRAINT ck_cancellation_policy_versions_late_package_action CHECK ((late_cancellation_package_action IN ('None', 'ConsumeUnit'))),
                CONSTRAINT ck_cancellation_policy_versions_no_show_package_action CHECK ((no_show_package_action IN ('None', 'ConsumeUnit'))),
                CONSTRAINT ck_cancellation_policy_versions_late_fee_value CHECK (((((late_cancellation_fee_type)::text = 'None'::text) AND (late_cancellation_fee_value IS NULL)) OR (((late_cancellation_fee_type)::text = 'Fixed'::text) AND (late_cancellation_fee_value >= (0)::numeric)) OR (((late_cancellation_fee_type)::text = 'Percentage'::text) AND (late_cancellation_fee_value >= (0)::numeric) AND (late_cancellation_fee_value <= (100)::numeric)))),
                CONSTRAINT ck_cancellation_policy_versions_no_show_fee_value CHECK (((((no_show_fee_type)::text = 'None'::text) AND (no_show_fee_value IS NULL)) OR (((no_show_fee_type)::text = 'Fixed'::text) AND (no_show_fee_value >= (0)::numeric)) OR (((no_show_fee_type)::text = 'Percentage'::text) AND (no_show_fee_value >= (0)::numeric) AND (no_show_fee_value <= (100)::numeric))))
            );

            ALTER TABLE dunelight.cancellation_policy_versions ADD CONSTRAINT pk_cancellation_policy_versions PRIMARY KEY (id);

            CREATE UNIQUE INDEX ux_cancellation_policy_versions_policy_version ON dunelight.cancellation_policy_versions USING btree (cancellation_policy_id, version);

            ALTER TABLE dunelight.cancellation_policy_versions ADD CONSTRAINT fk_cancellation_policy_versions_policy_organization FOREIGN KEY (cancellation_policy_id, organization_id) REFERENCES dunelight.cancellation_policies(id, organization_id) ON DELETE RESTRICT;");

        // cancellation_policy_assignments
        Execute.Sql(@"
            CREATE TABLE dunelight.cancellation_policy_assignments (
                id uuid NOT NULL,
                organization_id uuid NOT NULL,
                company_id uuid,
                service_id uuid,
                cancellation_policy_id uuid NOT NULL,
                created_at timestamp with time zone NOT NULL,
                created_by uuid,
                updated_at timestamp with time zone,
                updated_by uuid,
                CONSTRAINT ck_cancellation_policy_assignments_scope CHECK (((company_id IS NOT NULL) OR (service_id IS NOT NULL)))
            );

            ALTER TABLE dunelight.cancellation_policy_assignments ADD CONSTRAINT pk_cancellation_policy_assignments PRIMARY KEY (id);

            CREATE UNIQUE INDEX ux_cancellation_policy_assignments_company_service ON dunelight.cancellation_policy_assignments USING btree (organization_id, company_id, service_id) WHERE ((company_id IS NOT NULL) AND (service_id IS NOT NULL));

            CREATE UNIQUE INDEX ux_cancellation_policy_assignments_service ON dunelight.cancellation_policy_assignments USING btree (organization_id, service_id) WHERE (company_id IS NULL);

            CREATE UNIQUE INDEX ux_cancellation_policy_assignments_company ON dunelight.cancellation_policy_assignments USING btree (organization_id, company_id) WHERE (service_id IS NULL);

            CREATE INDEX ix_cancellation_policy_assignments_policy ON dunelight.cancellation_policy_assignments USING btree (cancellation_policy_id);

            ALTER TABLE dunelight.cancellation_policy_assignments ADD CONSTRAINT fk_cancellation_policy_assignments_policy_organization FOREIGN KEY (cancellation_policy_id, organization_id) REFERENCES dunelight.cancellation_policies(id, organization_id) ON DELETE RESTRICT;

            ALTER TABLE dunelight.cancellation_policy_assignments ADD CONSTRAINT fk_cancellation_policy_assignments_company_id FOREIGN KEY (company_id) REFERENCES dunelight.companies(id) ON DELETE RESTRICT;

            ALTER TABLE dunelight.cancellation_policy_assignments ADD CONSTRAINT fk_cancellation_policy_assignments_service_id FOREIGN KEY (service_id) REFERENCES dunelight.services(id) ON DELETE RESTRICT;

            ALTER TABLE dunelight.cancellation_policy_assignments ADD CONSTRAINT fk_cancellation_policy_assignments_organization_id FOREIGN KEY (organization_id) REFERENCES dunelight.organizations(id);");
    }
}
