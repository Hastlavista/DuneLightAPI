using System;
using BlueDragon.DuneLight.DatabaseMigration.Extensions;
using BlueDragon.DuneLight.DatabaseMigration.Models;
using FluentMigrator;

namespace BlueDragon.DuneLight.DatabaseMigration.Migrations.Baseline;

/// <summary>
/// Baseline 002 — PermissionsAndCapabilities. Kreira KONAČNU strukturu odjednom (nema create->rename/alter povijesti, nema backfilla).
/// Tablice: capability_definitions, capability_definition_grants, default_role_templates, default_role_template_capabilities, default_role_template_grants, grant_groups, grant_group_grants, user_grant_groups, grant_group_capability_snapshots, grant_group_template_grants, grant_group_template_upgrade_audit_log.
/// </summary>
[DeveloperMigration(2026, 09, 29, Developer.SilvioHabazin, 2)]
public class Baseline002PermissionsAndCapabilities : DuneLightMigration
{
    public override void Up()
    {
        Execute.Sql(Ddl);
        SeedReferenceData();
    }

    private const string Ddl = """
CREATE TABLE dunelight.capability_definitions (
    id uuid NOT NULL,
    key character varying(150) NOT NULL,
    version integer NOT NULL,
    category_key character varying(50) NOT NULL,
    scope_model character varying(40) NOT NULL,
    sensitivity character varying(40) NOT NULL,
    is_active boolean DEFAULT true NOT NULL,
    deprecated_at timestamp with time zone,
    created_at timestamp with time zone NOT NULL,
    created_by uuid,
    CONSTRAINT ck_capability_definitions_version_positive CHECK ((version > 0))
);

ALTER TABLE ONLY dunelight.capability_definitions
    ADD CONSTRAINT pk_capability_definitions PRIMARY KEY (id);

CREATE UNIQUE INDEX ux_capability_definitions_key_version ON dunelight.capability_definitions USING btree (key, version);

CREATE TABLE dunelight.capability_definition_grants (
    id uuid NOT NULL,
    capability_definition_id uuid NOT NULL,
    grant_key character varying(100) NOT NULL,
    role character varying(40) NOT NULL
);

ALTER TABLE ONLY dunelight.capability_definition_grants
    ADD CONSTRAINT pk_capability_definition_grants PRIMARY KEY (id);

CREATE UNIQUE INDEX ux_capability_definition_grants_capability_grant ON dunelight.capability_definition_grants USING btree (capability_definition_id, grant_key);

CREATE TABLE dunelight.default_role_templates (
    id uuid NOT NULL,
    key character varying(150) NOT NULL,
    version integer NOT NULL,
    display_name_hr character varying(255) NOT NULL,
    is_active boolean DEFAULT true NOT NULL,
    created_at timestamp with time zone NOT NULL,
    created_by uuid,
    CONSTRAINT ck_default_role_templates_version_positive CHECK ((version > 0))
);

ALTER TABLE ONLY dunelight.default_role_templates
    ADD CONSTRAINT pk_default_role_templates PRIMARY KEY (id);

CREATE UNIQUE INDEX ux_default_role_templates_key_version ON dunelight.default_role_templates USING btree (key, version);

CREATE TABLE dunelight.default_role_template_capabilities (
    id uuid NOT NULL,
    default_role_template_id uuid NOT NULL,
    capability_definition_id uuid NOT NULL,
    selected_scope character varying(40) NOT NULL
);

ALTER TABLE ONLY dunelight.default_role_template_capabilities
    ADD CONSTRAINT pk_default_role_template_capabilities PRIMARY KEY (id);

CREATE UNIQUE INDEX ux_default_role_template_capabilities_template_capability ON dunelight.default_role_template_capabilities USING btree (default_role_template_id, capability_definition_id);

CREATE TABLE dunelight.default_role_template_grants (
    id uuid NOT NULL,
    default_role_template_id uuid NOT NULL,
    grant_key character varying(100) NOT NULL,
    reason character varying(40) NOT NULL
);

ALTER TABLE ONLY dunelight.default_role_template_grants
    ADD CONSTRAINT pk_default_role_template_grants PRIMARY KEY (id);

CREATE UNIQUE INDEX ux_default_role_template_grants_template_grant ON dunelight.default_role_template_grants USING btree (default_role_template_id, grant_key);

CREATE TABLE dunelight.grant_groups (
    id uuid NOT NULL,
    organization_id uuid NOT NULL,
    name character varying(255) NOT NULL,
    created_at timestamp with time zone,
    created_by uuid,
    updated_at timestamp with time zone,
    updated_by uuid
);

ALTER TABLE ONLY dunelight.grant_groups
    ADD CONSTRAINT pk_grant_groups PRIMARY KEY (id);

CREATE UNIQUE INDEX ux_grant_groups_organization_name ON dunelight.grant_groups USING btree (organization_id, name);

CREATE TABLE dunelight.grant_group_grants (
    id uuid NOT NULL,
    grant_group_id uuid NOT NULL,
    grant_key character varying(100) NOT NULL
);

ALTER TABLE ONLY dunelight.grant_group_grants
    ADD CONSTRAINT pk_grant_group_grants PRIMARY KEY (id);

CREATE UNIQUE INDEX ux_grant_group_grants_group_key ON dunelight.grant_group_grants USING btree (grant_group_id, grant_key);

CREATE TABLE dunelight.user_grant_groups (
    id uuid NOT NULL,
    user_id uuid NOT NULL,
    grant_group_id uuid NOT NULL
);

ALTER TABLE ONLY dunelight.user_grant_groups
    ADD CONSTRAINT pk_user_grant_groups PRIMARY KEY (id);

CREATE UNIQUE INDEX ux_user_grant_groups_user_group ON dunelight.user_grant_groups USING btree (user_id, grant_group_id);

CREATE TABLE dunelight.grant_group_capability_snapshots (
    id uuid NOT NULL,
    grant_group_id uuid NOT NULL,
    capability_definition_id uuid NOT NULL,
    selected_scope character varying(40) NOT NULL,
    source_template_key character varying(150),
    source_template_version integer,
    applied_at timestamp with time zone NOT NULL,
    applied_by uuid
);

ALTER TABLE ONLY dunelight.grant_group_capability_snapshots
    ADD CONSTRAINT pk_grant_group_capability_snapshots PRIMARY KEY (id);

CREATE UNIQUE INDEX ux_grant_group_capability_snapshots_group_capability ON dunelight.grant_group_capability_snapshots USING btree (grant_group_id, capability_definition_id);

CREATE TABLE dunelight.grant_group_template_grants (
    id uuid NOT NULL,
    grant_group_id uuid NOT NULL,
    grant_key character varying(100) NOT NULL,
    source_template_key character varying(150) NOT NULL,
    source_template_version integer NOT NULL,
    applied_at timestamp with time zone NOT NULL,
    applied_by uuid
);

ALTER TABLE ONLY dunelight.grant_group_template_grants
    ADD CONSTRAINT pk_grant_group_template_grants PRIMARY KEY (id);

CREATE UNIQUE INDEX ux_grant_group_template_grants_group_grant ON dunelight.grant_group_template_grants USING btree (grant_group_id, grant_key);

CREATE TABLE dunelight.grant_group_template_upgrade_audit_log (
    id uuid NOT NULL,
    grant_group_id uuid NOT NULL,
    organization_id uuid NOT NULL,
    source_template_key character varying(150) NOT NULL,
    source_template_version integer NOT NULL,
    target_template_key character varying(150) NOT NULL,
    target_template_version integer NOT NULL,
    capability_changes_json text NOT NULL,
    conflict_resolutions_json text NOT NULL,
    applied_at timestamp with time zone NOT NULL,
    applied_by uuid
);

ALTER TABLE ONLY dunelight.grant_group_template_upgrade_audit_log
    ADD CONSTRAINT pk_grant_group_template_upgrade_audit_log PRIMARY KEY (id);

CREATE INDEX ix_grant_group_template_upgrade_audit_log_grant_group_id ON dunelight.grant_group_template_upgrade_audit_log USING btree (grant_group_id);

ALTER TABLE ONLY dunelight.capability_definition_grants
    ADD CONSTRAINT fk_capability_definition_grants_capability_definition_id FOREIGN KEY (capability_definition_id) REFERENCES dunelight.capability_definitions(id) ON DELETE CASCADE;

ALTER TABLE ONLY dunelight.default_role_template_capabilities
    ADD CONSTRAINT fk_default_role_template_capabilities_capability_id FOREIGN KEY (capability_definition_id) REFERENCES dunelight.capability_definitions(id);

ALTER TABLE ONLY dunelight.default_role_template_capabilities
    ADD CONSTRAINT fk_default_role_template_capabilities_template_id FOREIGN KEY (default_role_template_id) REFERENCES dunelight.default_role_templates(id) ON DELETE CASCADE;

ALTER TABLE ONLY dunelight.default_role_template_grants
    ADD CONSTRAINT fk_default_role_template_grants_template_id FOREIGN KEY (default_role_template_id) REFERENCES dunelight.default_role_templates(id) ON DELETE CASCADE;

ALTER TABLE ONLY dunelight.grant_groups
    ADD CONSTRAINT fk_grant_groups_organization_id FOREIGN KEY (organization_id) REFERENCES dunelight.organizations(id);

ALTER TABLE ONLY dunelight.grant_group_grants
    ADD CONSTRAINT fk_grant_group_grants_grant_group_id FOREIGN KEY (grant_group_id) REFERENCES dunelight.grant_groups(id) ON DELETE CASCADE;

ALTER TABLE ONLY dunelight.user_grant_groups
    ADD CONSTRAINT fk_user_grant_groups_grant_group_id FOREIGN KEY (grant_group_id) REFERENCES dunelight.grant_groups(id) ON DELETE CASCADE;

ALTER TABLE ONLY dunelight.user_grant_groups
    ADD CONSTRAINT fk_user_grant_groups_user_id FOREIGN KEY (user_id) REFERENCES dunelight.users(id) ON DELETE CASCADE;

ALTER TABLE ONLY dunelight.grant_group_capability_snapshots
    ADD CONSTRAINT fk_grant_group_capability_snapshots_capability_id FOREIGN KEY (capability_definition_id) REFERENCES dunelight.capability_definitions(id);

ALTER TABLE ONLY dunelight.grant_group_capability_snapshots
    ADD CONSTRAINT fk_grant_group_capability_snapshots_grant_group_id FOREIGN KEY (grant_group_id) REFERENCES dunelight.grant_groups(id) ON DELETE CASCADE;

ALTER TABLE ONLY dunelight.grant_group_template_grants
    ADD CONSTRAINT fk_grant_group_template_grants_grant_group_id FOREIGN KEY (grant_group_id) REFERENCES dunelight.grant_groups(id) ON DELETE CASCADE;
""";

    /// <summary>Jedina generacija referentnih podataka — vidi CapabilityReferenceData (konačno stanje, verzija 1).</summary>
    private void SeedReferenceData()
    {
        DateTimeOffset now = DateTimeOffset.UtcNow;

        foreach (CapabilityReferenceData.CapabilitySeed capability in CapabilityReferenceData.Capabilities)
        {
            Guid capabilityId = CapabilityReferenceData.CapabilityId(capability.Key);

            Insert.IntoTable("capability_definitions").InSchema("dunelight").Row(new
            {
                id = capabilityId,
                key = capability.Key,
                version = CapabilityReferenceData.CapabilityVersion,
                category_key = capability.CategoryKey,
                scope_model = capability.ScopeModel,
                sensitivity = capability.Sensitivity,
                is_active = true,
                created_at = now
            });

            foreach (CapabilityReferenceData.CapabilityGrantSeed grant in capability.Grants)
            {
                Insert.IntoTable("capability_definition_grants").InSchema("dunelight").Row(new
                {
                    id = Guid.NewGuid(),
                    capability_definition_id = capabilityId,
                    grant_key = grant.GrantKey,
                    role = grant.Role
                });
            }
        }

        foreach (CapabilityReferenceData.TemplateSeed template in CapabilityReferenceData.Templates)
        {
            Guid templateId = CapabilityReferenceData.TemplateId(template.Key);

            Insert.IntoTable("default_role_templates").InSchema("dunelight").Row(new
            {
                id = templateId,
                key = template.Key,
                version = CapabilityReferenceData.TemplateVersion,
                display_name_hr = template.DisplayNameHr,
                is_active = true,
                created_at = now
            });

            foreach (CapabilityReferenceData.TemplateCapabilitySeed selection in template.Selections)
            {
                Insert.IntoTable("default_role_template_capabilities").InSchema("dunelight").Row(new
                {
                    id = Guid.NewGuid(),
                    default_role_template_id = templateId,
                    capability_definition_id = CapabilityReferenceData.CapabilityId(selection.CapabilityKey),
                    selected_scope = selection.SelectedScope
                });
            }

            foreach (string grantKey in template.CompatibilityExtraGrants)
            {
                Insert.IntoTable("default_role_template_grants").InSchema("dunelight").Row(new
                {
                    id = Guid.NewGuid(),
                    default_role_template_id = templateId,
                    grant_key = grantKey,
                    reason = "CompatibilityExtra"
                });
            }
        }
    }
}
