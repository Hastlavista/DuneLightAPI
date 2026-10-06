using BlueDragon.DuneLight.DatabaseMigration.Extensions;
using BlueDragon.DuneLight.DatabaseMigration.Models;

namespace BlueDragon.DuneLight.DatabaseMigration.Migrations._2026;

/// <summary>
/// Početna migracija (ADR-0022), korak 8/11: grupe, slotovi, segment templatei i članstva.
/// Gradi isključivo trenutnu ciljnu shemu; ne seeda podatke.
/// </summary>
[DeveloperMigration(2026, 10, 25, Developer.SilvioHabazin, 8)]
public class Baseline08Groups : DuneLightMigration
{
    public override void Up()
    {
        // groups
        Execute.Sql(@"
            CREATE TABLE dunelight.groups (
                id uuid NOT NULL,
                organization_id uuid NOT NULL,
                name character varying(255) NOT NULL,
                company_id uuid CONSTRAINT groups_location_id_not_null NOT NULL,
                is_active boolean DEFAULT true NOT NULL,
                note text,
                created_at timestamp with time zone NOT NULL,
                created_by uuid,
                updated_at timestamp with time zone,
                updated_by uuid,
                membership_version bigint DEFAULT 0 NOT NULL,
                CONSTRAINT ck_groups_membership_version CHECK ((membership_version >= 0))
            );

            ALTER TABLE dunelight.groups ADD CONSTRAINT pk_groups PRIMARY KEY (id);

            CREATE INDEX ix_groups_organization_id ON dunelight.groups USING btree (organization_id);

            ALTER TABLE dunelight.groups ADD CONSTRAINT fk_groups_company_id FOREIGN KEY (company_id) REFERENCES dunelight.companies(id);

            ALTER TABLE dunelight.groups ADD CONSTRAINT fk_groups_organization_id FOREIGN KEY (organization_id) REFERENCES dunelight.organizations(id);");

        // group_slots
        Execute.Sql(@"
            CREATE TABLE dunelight.group_slots (
                id uuid NOT NULL,
                group_id uuid NOT NULL,
                day_of_week character varying(20) NOT NULL,
                start_time time without time zone NOT NULL,
                is_active boolean DEFAULT true NOT NULL,
                created_at timestamp with time zone NOT NULL
            );

            ALTER TABLE dunelight.group_slots ADD CONSTRAINT pk_group_slots PRIMARY KEY (id);

            CREATE INDEX ix_group_slots_group_active ON dunelight.group_slots USING btree (group_id, is_active);

            ALTER TABLE dunelight.group_slots ADD CONSTRAINT fk_group_slots_group_id FOREIGN KEY (group_id) REFERENCES dunelight.groups(id) ON DELETE CASCADE;");

        // group_segment_templates
        Execute.Sql(@"
            CREATE TABLE dunelight.group_segment_templates (
                id uuid NOT NULL,
                group_id uuid NOT NULL,
                service_id uuid NOT NULL,
                start_offset_minutes integer NOT NULL,
                duration_minutes integer NOT NULL,
                room_id uuid,
                capacity integer NOT NULL,
                created_at timestamp with time zone NOT NULL,
                updated_at timestamp with time zone,
                pricing_mode character varying(32) NOT NULL,
                pricing_employee_id uuid,
                CONSTRAINT ck_group_segment_templates_capacity CHECK ((capacity >= 1)),
                CONSTRAINT ck_group_segment_templates_duration CHECK (((duration_minutes > 0) AND (duration_minutes <= 1440))),
                CONSTRAINT ck_group_segment_templates_offset CHECK (((start_offset_minutes >= 0) AND (start_offset_minutes < 1440))),
                CONSTRAINT ck_group_segment_templates_pricing_source CHECK (((((pricing_mode)::text = 'Standard'::text) AND (pricing_employee_id IS NULL)) OR (((pricing_mode)::text = 'Employee'::text) AND (pricing_employee_id IS NOT NULL))))
            );

            ALTER TABLE dunelight.group_segment_templates ADD CONSTRAINT pk_group_segment_templates PRIMARY KEY (id);

            ALTER TABLE dunelight.group_segment_templates ADD CONSTRAINT ux_group_segment_templates_id_group UNIQUE (id, group_id);

            CREATE INDEX ix_group_segment_templates_group_id ON dunelight.group_segment_templates USING btree (group_id);

            ALTER TABLE dunelight.group_segment_templates ADD CONSTRAINT fk_group_segment_templates_group_id FOREIGN KEY (group_id) REFERENCES dunelight.groups(id) ON DELETE CASCADE;

            ALTER TABLE dunelight.group_segment_templates ADD CONSTRAINT fk_group_segment_templates_pricing_employee_id FOREIGN KEY (pricing_employee_id) REFERENCES dunelight.employees(id) ON DELETE RESTRICT;

            ALTER TABLE dunelight.group_segment_templates ADD CONSTRAINT fk_group_segment_templates_room_id FOREIGN KEY (room_id) REFERENCES dunelight.rooms(id) ON DELETE RESTRICT;

            ALTER TABLE dunelight.group_segment_templates ADD CONSTRAINT fk_group_segment_templates_service_id FOREIGN KEY (service_id) REFERENCES dunelight.services(id) ON DELETE RESTRICT;");

        // group_segment_template_employees
        Execute.Sql(@"
            CREATE TABLE dunelight.group_segment_template_employees (
                group_segment_template_id uuid CONSTRAINT group_segment_template_emplo_group_segment_template_id_not_null NOT NULL,
                employee_id uuid NOT NULL
            );

            ALTER TABLE dunelight.group_segment_template_employees ADD CONSTRAINT pk_group_segment_template_employees PRIMARY KEY (group_segment_template_id, employee_id);

            CREATE INDEX ix_group_segment_template_employees_employee ON dunelight.group_segment_template_employees USING btree (employee_id);

            ALTER TABLE dunelight.group_segment_template_employees ADD CONSTRAINT fk_group_segment_template_employees_employee FOREIGN KEY (employee_id) REFERENCES dunelight.employees(id) ON DELETE RESTRICT;

            ALTER TABLE dunelight.group_segment_template_employees ADD CONSTRAINT fk_group_segment_template_employees_template FOREIGN KEY (group_segment_template_id) REFERENCES dunelight.group_segment_templates(id) ON DELETE CASCADE;");

        // group_segment_template_resources
        Execute.Sql(@"
            CREATE TABLE dunelight.group_segment_template_resources (
                group_segment_template_id uuid CONSTRAINT group_segment_template_resou_group_segment_template_id_not_null NOT NULL,
                resource_id uuid NOT NULL,
                quantity_required integer NOT NULL,
                CONSTRAINT ck_group_segment_template_resources_quantity CHECK ((quantity_required > 0))
            );

            ALTER TABLE dunelight.group_segment_template_resources ADD CONSTRAINT pk_group_segment_template_resources PRIMARY KEY (group_segment_template_id, resource_id);

            ALTER TABLE dunelight.group_segment_template_resources ADD CONSTRAINT fk_group_segment_template_resources_resource FOREIGN KEY (resource_id) REFERENCES dunelight.resources(id) ON DELETE RESTRICT;

            ALTER TABLE dunelight.group_segment_template_resources ADD CONSTRAINT fk_group_segment_template_resources_template FOREIGN KEY (group_segment_template_id) REFERENCES dunelight.group_segment_templates(id) ON DELETE CASCADE;");

        // group_members
        Execute.Sql(@"
            CREATE TABLE dunelight.group_members (
                id uuid NOT NULL,
                group_id uuid NOT NULL,
                client_id uuid NOT NULL,
                joined_at timestamp with time zone NOT NULL,
                is_active boolean DEFAULT true NOT NULL,
                created_at timestamp with time zone NOT NULL
            );

            ALTER TABLE dunelight.group_members ADD CONSTRAINT pk_group_members PRIMARY KEY (id);

            ALTER TABLE dunelight.group_members ADD CONSTRAINT ux_group_members_id_group UNIQUE (id, group_id);

            CREATE UNIQUE INDEX ux_group_members_group_client_active ON dunelight.group_members USING btree (group_id, client_id) WHERE (is_active = true);

            ALTER TABLE dunelight.group_members ADD CONSTRAINT fk_group_members_client_id FOREIGN KEY (client_id) REFERENCES dunelight.clients(id);

            ALTER TABLE dunelight.group_members ADD CONSTRAINT fk_group_members_group_id FOREIGN KEY (group_id) REFERENCES dunelight.groups(id) ON DELETE CASCADE;");

        // group_member_segment_templates
        Execute.Sql(@"
            CREATE TABLE dunelight.group_member_segment_templates (
                group_member_id uuid NOT NULL,
                group_segment_template_id uuid CONSTRAINT group_member_segment_templat_group_segment_template_id_not_null NOT NULL,
                group_id uuid NOT NULL
            );

            ALTER TABLE dunelight.group_member_segment_templates ADD CONSTRAINT pk_group_member_segment_templates PRIMARY KEY (group_member_id, group_segment_template_id);

            CREATE INDEX ix_group_member_segment_templates_template ON dunelight.group_member_segment_templates USING btree (group_segment_template_id);

            ALTER TABLE dunelight.group_member_segment_templates ADD CONSTRAINT fk_group_member_segment_templates_member FOREIGN KEY (group_member_id, group_id) REFERENCES dunelight.group_members(id, group_id) ON DELETE CASCADE;

            ALTER TABLE dunelight.group_member_segment_templates ADD CONSTRAINT fk_group_member_segment_templates_template FOREIGN KEY (group_segment_template_id, group_id) REFERENCES dunelight.group_segment_templates(id, group_id) ON DELETE RESTRICT;");

        // group_audit_log
        Execute.Sql(@"
            CREATE TABLE dunelight.group_audit_log (
                id uuid NOT NULL,
                group_id uuid NOT NULL,
                change_type character varying(30) NOT NULL,
                old_value character varying(255),
                new_value character varying(255),
                changed_at timestamp with time zone NOT NULL,
                changed_by uuid
            );

            ALTER TABLE dunelight.group_audit_log ADD CONSTRAINT pk_group_audit_log PRIMARY KEY (id);

            CREATE INDEX ix_group_audit_log_group_id ON dunelight.group_audit_log USING btree (group_id);

            ALTER TABLE dunelight.group_audit_log ADD CONSTRAINT fk_group_audit_log_group_id FOREIGN KEY (group_id) REFERENCES dunelight.groups(id) ON DELETE CASCADE;");
    }
}
