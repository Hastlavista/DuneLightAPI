using System;
using BlueDragon.DuneLight.DatabaseMigration.Extensions;
using BlueDragon.DuneLight.DatabaseMigration.Models;
using FluentMigrator;

namespace BlueDragon.DuneLight.DatabaseMigration.Migrations.Baseline;

/// <summary>
/// Baseline 001 — IdentityAndOrganization. Kreira KONAČNU strukturu odjednom (nema create->rename/alter povijesti, nema backfilla).
/// Tablice: organizations, organization_settings, organization_branding_audit_log, users, platform_accounts.
/// </summary>
[DeveloperMigration(2026, 09, 29, Developer.SilvioHabazin, 1)]
public class Baseline001IdentityAndOrganization : DuneLightMigration
{
    public override void Up()
    {
        Execute.Sql("CREATE SCHEMA IF NOT EXISTS dunelight;");
        Execute.Sql(Ddl);
    }

    private const string Ddl = """
CREATE TABLE dunelight.organizations (
    id uuid NOT NULL,
    name character varying(255) NOT NULL,
    slug character varying(255) NOT NULL,
    created_at timestamp with time zone NOT NULL,
    logo character varying(511),
    favicon character varying(511),
    primary_color character varying(7),
    secondary_color character varying(7),
    surface_color character varying(7)
);

ALTER TABLE ONLY dunelight.organizations
    ADD CONSTRAINT pk_organizations PRIMARY KEY (id);

CREATE UNIQUE INDEX uq_organizations_slug ON dunelight.organizations USING btree (slug);

CREATE TABLE dunelight.organization_settings (
    id uuid NOT NULL,
    organization_id uuid NOT NULL,
    cancellation_cutoff_minutes integer NOT NULL,
    created_at timestamp with time zone NOT NULL,
    created_by uuid,
    updated_at timestamp with time zone,
    updated_by uuid
);

ALTER TABLE ONLY dunelight.organization_settings
    ADD CONSTRAINT pk_organization_settings PRIMARY KEY (id);

CREATE UNIQUE INDEX ux_organization_settings_organization_id ON dunelight.organization_settings USING btree (organization_id);

CREATE TABLE dunelight.organization_branding_audit_log (
    id uuid NOT NULL,
    organization_id uuid NOT NULL,
    change_type character varying(30) NOT NULL,
    old_value character varying(511),
    new_value character varying(511),
    changed_at timestamp with time zone NOT NULL,
    changed_by uuid
);

ALTER TABLE ONLY dunelight.organization_branding_audit_log
    ADD CONSTRAINT pk_organization_branding_audit_log PRIMARY KEY (id);

CREATE INDEX ix_organization_branding_audit_log_organization_id ON dunelight.organization_branding_audit_log USING btree (organization_id);

CREATE TABLE dunelight.users (
    id uuid NOT NULL,
    organization_id uuid NOT NULL,
    email character varying(255) NOT NULL,
    password_hash character varying(255) NOT NULL,
    api_key character varying(255) NOT NULL,
    role character varying(20) NOT NULL,
    created_at timestamp with time zone NOT NULL,
    is_active boolean DEFAULT true NOT NULL,
    must_change_credentials_on_first_login boolean DEFAULT false NOT NULL,
    pin_hash character varying(255)
);

ALTER TABLE ONLY dunelight.users
    ADD CONSTRAINT pk_users PRIMARY KEY (id);

CREATE UNIQUE INDEX uq_users_api_key ON dunelight.users USING btree (api_key);

CREATE UNIQUE INDEX uq_users_organization_id_email ON dunelight.users USING btree (organization_id, email);

CREATE TABLE dunelight.platform_accounts (
    id uuid NOT NULL,
    email character varying(255) NOT NULL,
    password_hash character varying(255) NOT NULL,
    is_active boolean DEFAULT true NOT NULL,
    created_at timestamp with time zone NOT NULL
);

ALTER TABLE ONLY dunelight.platform_accounts
    ADD CONSTRAINT pk_platform_accounts PRIMARY KEY (id);

CREATE UNIQUE INDEX uq_platform_accounts_email ON dunelight.platform_accounts USING btree (email);

ALTER TABLE ONLY dunelight.organization_settings
    ADD CONSTRAINT fk_organization_settings_organization_id FOREIGN KEY (organization_id) REFERENCES dunelight.organizations(id) ON DELETE CASCADE;

ALTER TABLE ONLY dunelight.organization_branding_audit_log
    ADD CONSTRAINT fk_organization_branding_audit_log_organization_id FOREIGN KEY (organization_id) REFERENCES dunelight.organizations(id);

ALTER TABLE ONLY dunelight.users
    ADD CONSTRAINT fk_users_organization_id FOREIGN KEY (organization_id) REFERENCES dunelight.organizations(id);
""";
}
