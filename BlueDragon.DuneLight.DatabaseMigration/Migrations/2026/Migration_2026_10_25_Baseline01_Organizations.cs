using BlueDragon.DuneLight.DatabaseMigration.Extensions;
using BlueDragon.DuneLight.DatabaseMigration.Models;

namespace BlueDragon.DuneLight.DatabaseMigration.Migrations._2026;

/// <summary>
/// Početna migracija (ADR-0022), korak 1/11: organizacije, korisnički računi, platformski računi.
/// Gradi isključivo trenutnu ciljnu shemu; ne seeda podatke.
/// </summary>
[DeveloperMigration(2026, 10, 25, Developer.SilvioHabazin, 1)]
public class Baseline01Organizations : DuneLightMigration
{
    public override void Up()
    {
        // organizations
        Execute.Sql(@"
            CREATE TABLE dunelight.organizations (
                id uuid NOT NULL,
                name character varying(255) NOT NULL,
                slug character varying(255) NOT NULL,
                created_at timestamp with time zone NOT NULL,
                logo character varying(511),
                favicon character varying(511),
                primary_color character varying(7),
                secondary_color character varying(7),
                surface_color character varying(7),
                time_zone character varying(64) DEFAULT 'Europe/Zagreb'::character varying NOT NULL
            );

            ALTER TABLE dunelight.organizations ADD CONSTRAINT pk_organizations PRIMARY KEY (id);

            CREATE UNIQUE INDEX uq_organizations_slug ON dunelight.organizations USING btree (slug);");

        // organization_settings
        Execute.Sql(@"
            CREATE TABLE dunelight.organization_settings (
                id uuid NOT NULL,
                organization_id uuid NOT NULL,
                cancellation_cutoff_minutes integer NOT NULL,
                created_at timestamp with time zone NOT NULL,
                created_by uuid,
                updated_at timestamp with time zone,
                updated_by uuid,
                package_consumption_timing character varying(30) DEFAULT 'OnCompletion'::character varying NOT NULL,
                CONSTRAINT ck_organization_settings_package_consumption_timing CHECK (((package_consumption_timing)::text = 'OnCompletion'::text))
            );

            ALTER TABLE dunelight.organization_settings ADD CONSTRAINT pk_organization_settings PRIMARY KEY (id);

            CREATE UNIQUE INDEX ux_organization_settings_organization_id ON dunelight.organization_settings USING btree (organization_id);

            ALTER TABLE dunelight.organization_settings ADD CONSTRAINT fk_organization_settings_organization_id FOREIGN KEY (organization_id) REFERENCES dunelight.organizations(id) ON DELETE CASCADE;");

        // organization_branding_audit_log
        Execute.Sql(@"
            CREATE TABLE dunelight.organization_branding_audit_log (
                id uuid NOT NULL,
                organization_id uuid NOT NULL,
                change_type character varying(30) NOT NULL,
                old_value character varying(511),
                new_value character varying(511),
                changed_at timestamp with time zone NOT NULL,
                changed_by uuid
            );

            ALTER TABLE dunelight.organization_branding_audit_log ADD CONSTRAINT pk_organization_branding_audit_log PRIMARY KEY (id);

            CREATE INDEX ix_organization_branding_audit_log_organization_id ON dunelight.organization_branding_audit_log USING btree (organization_id);

            ALTER TABLE dunelight.organization_branding_audit_log ADD CONSTRAINT fk_organization_branding_audit_log_organization_id FOREIGN KEY (organization_id) REFERENCES dunelight.organizations(id);");

        // users
        Execute.Sql(@"
            CREATE TABLE dunelight.users (
                id uuid NOT NULL,
                organization_id uuid NOT NULL,
                email character varying(255) NOT NULL,
                password_hash character varying(255) NOT NULL,
                api_key character varying(255) NOT NULL,
                created_at timestamp with time zone NOT NULL,
                is_active boolean DEFAULT true NOT NULL,
                must_change_credentials_on_first_login boolean DEFAULT false NOT NULL,
                pin_hash character varying(255)
            );

            ALTER TABLE dunelight.users ADD CONSTRAINT pk_users PRIMARY KEY (id);

            CREATE UNIQUE INDEX uq_users_api_key ON dunelight.users USING btree (api_key);

            CREATE UNIQUE INDEX ux_users_organization_email ON dunelight.users USING btree (organization_id, lower((email)::text));

            ALTER TABLE dunelight.users ADD CONSTRAINT fk_users_organization_id FOREIGN KEY (organization_id) REFERENCES dunelight.organizations(id);");

        // platform_accounts
        Execute.Sql(@"
            CREATE TABLE dunelight.platform_accounts (
                id uuid NOT NULL,
                email character varying(255) NOT NULL,
                password_hash character varying(255) NOT NULL,
                is_active boolean DEFAULT true NOT NULL,
                created_at timestamp with time zone NOT NULL
            );

            ALTER TABLE dunelight.platform_accounts ADD CONSTRAINT pk_platform_accounts PRIMARY KEY (id);

            CREATE UNIQUE INDEX uq_platform_accounts_email ON dunelight.platform_accounts USING btree (email);");
    }
}
