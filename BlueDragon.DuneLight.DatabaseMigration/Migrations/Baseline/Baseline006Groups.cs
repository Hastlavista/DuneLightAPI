using System;
using BlueDragon.DuneLight.DatabaseMigration.Extensions;
using BlueDragon.DuneLight.DatabaseMigration.Models;
using FluentMigrator;

namespace BlueDragon.DuneLight.DatabaseMigration.Migrations.Baseline;

/// <summary>
/// Baseline 006 — Groups. Kreira KONAČNU strukturu odjednom (nema create->rename/alter povijesti, nema backfilla).
/// Tablice: groups, group_slots, group_members, group_audit_log, group_segment_templates, group_segment_template_employees, group_segment_template_resources.
/// </summary>
[DeveloperMigration(2026, 09, 29, Developer.SilvioHabazin, 6)]
public class Baseline006Groups : DuneLightMigration
{
    public override void Up()
    {
        Execute.Sql(Ddl);
    }

    private const string Ddl = """
CREATE TABLE dunelight.groups (
    id uuid NOT NULL,
    organization_id uuid NOT NULL,
    name character varying(255) NOT NULL,
    company_id uuid NOT NULL,
    capacity integer NOT NULL,
    is_active boolean DEFAULT true NOT NULL,
    note text,
    created_at timestamp with time zone NOT NULL,
    created_by uuid,
    updated_at timestamp with time zone,
    updated_by uuid
);

ALTER TABLE ONLY dunelight.groups
    ADD CONSTRAINT pk_groups PRIMARY KEY (id);

CREATE INDEX ix_groups_organization_id ON dunelight.groups USING btree (organization_id);

CREATE TABLE dunelight.group_slots (
    id uuid NOT NULL,
    group_id uuid NOT NULL,
    day_of_week character varying(20) NOT NULL,
    start_time time without time zone NOT NULL,
    is_active boolean DEFAULT true NOT NULL,
    created_at timestamp with time zone NOT NULL
);

ALTER TABLE ONLY dunelight.group_slots
    ADD CONSTRAINT pk_group_slots PRIMARY KEY (id);

CREATE INDEX ix_group_slots_group_active ON dunelight.group_slots USING btree (group_id, is_active);

CREATE TABLE dunelight.group_members (
    id uuid NOT NULL,
    group_id uuid NOT NULL,
    client_id uuid NOT NULL,
    joined_at timestamp with time zone NOT NULL,
    is_active boolean DEFAULT true NOT NULL,
    created_at timestamp with time zone NOT NULL
);

ALTER TABLE ONLY dunelight.group_members
    ADD CONSTRAINT pk_group_members PRIMARY KEY (id);

CREATE UNIQUE INDEX ux_group_members_group_client_active ON dunelight.group_members USING btree (group_id, client_id) WHERE (is_active = true);

CREATE TABLE dunelight.group_audit_log (
    id uuid NOT NULL,
    group_id uuid NOT NULL,
    change_type character varying(30) NOT NULL,
    old_value character varying(255),
    new_value character varying(255),
    changed_at timestamp with time zone NOT NULL,
    changed_by uuid
);

ALTER TABLE ONLY dunelight.group_audit_log
    ADD CONSTRAINT pk_group_audit_log PRIMARY KEY (id);

CREATE INDEX ix_group_audit_log_group_id ON dunelight.group_audit_log USING btree (group_id);

CREATE TABLE dunelight.group_segment_templates (
    id uuid NOT NULL,
    group_id uuid NOT NULL,
    service_id uuid NOT NULL,
    offset_minutes integer NOT NULL,
    duration_minutes integer NOT NULL,
    room_id uuid,
    created_at timestamp with time zone NOT NULL,
    CONSTRAINT ck_group_segment_templates_offset CHECK ((offset_minutes >= 0)),
    CONSTRAINT ck_group_segment_templates_duration CHECK ((duration_minutes > 0))
);

ALTER TABLE ONLY dunelight.group_segment_templates
    ADD CONSTRAINT pk_group_segment_templates PRIMARY KEY (id);

CREATE INDEX ix_group_segment_templates_group ON dunelight.group_segment_templates USING btree (group_id);

CREATE TABLE dunelight.group_segment_template_employees (
    group_segment_template_id uuid NOT NULL,
    employee_id uuid NOT NULL
);

ALTER TABLE ONLY dunelight.group_segment_template_employees
    ADD CONSTRAINT pk_group_segment_template_employees PRIMARY KEY (group_segment_template_id, employee_id);

CREATE INDEX ix_group_segment_template_employees_employee ON dunelight.group_segment_template_employees USING btree (employee_id);

CREATE TABLE dunelight.group_segment_template_resources (
    group_segment_template_id uuid NOT NULL,
    resource_id uuid NOT NULL,
    quantity_required integer NOT NULL,
    CONSTRAINT ck_group_segment_template_resources_quantity CHECK ((quantity_required > 0))
);

ALTER TABLE ONLY dunelight.group_segment_template_resources
    ADD CONSTRAINT pk_group_segment_template_resources PRIMARY KEY (group_segment_template_id, resource_id);

CREATE INDEX ix_group_segment_template_resources_resource ON dunelight.group_segment_template_resources USING btree (resource_id);

ALTER TABLE ONLY dunelight.groups
    ADD CONSTRAINT fk_groups_company_id FOREIGN KEY (company_id) REFERENCES dunelight.companies(id);

ALTER TABLE ONLY dunelight.groups
    ADD CONSTRAINT fk_groups_organization_id FOREIGN KEY (organization_id) REFERENCES dunelight.organizations(id);

ALTER TABLE ONLY dunelight.group_slots
    ADD CONSTRAINT fk_group_slots_group_id FOREIGN KEY (group_id) REFERENCES dunelight.groups(id) ON DELETE CASCADE;

ALTER TABLE ONLY dunelight.group_members
    ADD CONSTRAINT fk_group_members_client_id FOREIGN KEY (client_id) REFERENCES dunelight.clients(id);

ALTER TABLE ONLY dunelight.group_members
    ADD CONSTRAINT fk_group_members_group_id FOREIGN KEY (group_id) REFERENCES dunelight.groups(id) ON DELETE CASCADE;

ALTER TABLE ONLY dunelight.group_audit_log
    ADD CONSTRAINT fk_group_audit_log_group_id FOREIGN KEY (group_id) REFERENCES dunelight.groups(id) ON DELETE CASCADE;

ALTER TABLE ONLY dunelight.group_segment_templates
    ADD CONSTRAINT fk_group_segment_templates_group_id FOREIGN KEY (group_id) REFERENCES dunelight.groups(id) ON DELETE CASCADE;

ALTER TABLE ONLY dunelight.group_segment_templates
    ADD CONSTRAINT fk_group_segment_templates_service_id FOREIGN KEY (service_id) REFERENCES dunelight.services(id);

ALTER TABLE ONLY dunelight.group_segment_templates
    ADD CONSTRAINT fk_group_segment_templates_room_id FOREIGN KEY (room_id) REFERENCES dunelight.rooms(id);

ALTER TABLE ONLY dunelight.group_segment_template_employees
    ADD CONSTRAINT fk_group_segment_template_employees_group_segment_template_id FOREIGN KEY (group_segment_template_id) REFERENCES dunelight.group_segment_templates(id) ON DELETE CASCADE;

ALTER TABLE ONLY dunelight.group_segment_template_employees
    ADD CONSTRAINT fk_group_segment_template_employees_employee_id FOREIGN KEY (employee_id) REFERENCES dunelight.employees(id);

ALTER TABLE ONLY dunelight.group_segment_template_resources
    ADD CONSTRAINT fk_group_segment_template_resources_group_segment_template_id FOREIGN KEY (group_segment_template_id) REFERENCES dunelight.group_segment_templates(id) ON DELETE CASCADE;

ALTER TABLE ONLY dunelight.group_segment_template_resources
    ADD CONSTRAINT fk_group_segment_template_resources_resource_id FOREIGN KEY (resource_id) REFERENCES dunelight.resources(id);
""";
}
