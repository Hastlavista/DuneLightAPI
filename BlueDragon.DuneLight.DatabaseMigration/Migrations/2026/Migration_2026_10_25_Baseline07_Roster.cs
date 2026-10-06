using BlueDragon.DuneLight.DatabaseMigration.Extensions;
using BlueDragon.DuneLight.DatabaseMigration.Models;

namespace BlueDragon.DuneLight.DatabaseMigration.Migrations._2026;

/// <summary>
/// Početna migracija (ADR-0022), korak 7/11: roster, predlošci radnog vremena i fond godišnjeg odmora.
/// Gradi isključivo trenutnu ciljnu shemu; ne seeda podatke.
/// </summary>
[DeveloperMigration(2026, 10, 25, Developer.SilvioHabazin, 7)]
public class Baseline07Roster : DuneLightMigration
{
    public override void Up()
    {
        // roster_types
        Execute.Sql(@"
            CREATE TABLE dunelight.roster_types (
                id uuid NOT NULL,
                organization_id uuid NOT NULL,
                name character varying(255) NOT NULL,
                color_hex character varying(7),
                counts_as_work boolean NOT NULL,
                is_absence boolean NOT NULL,
                requires_time boolean NOT NULL,
                is_active boolean DEFAULT true NOT NULL,
                sort_order integer DEFAULT 0 NOT NULL,
                created_at timestamp with time zone NOT NULL,
                created_by uuid,
                updated_at timestamp with time zone,
                updated_by uuid,
                deducts_from_leave_fund boolean DEFAULT false NOT NULL
            );

            ALTER TABLE dunelight.roster_types ADD CONSTRAINT pk_roster_types PRIMARY KEY (id);

            CREATE UNIQUE INDEX ux_roster_types_org_name_active ON dunelight.roster_types USING btree (organization_id, lower(TRIM(BOTH FROM name))) WHERE (is_active = true);

            ALTER TABLE dunelight.roster_types ADD CONSTRAINT fk_roster_types_organization_id FOREIGN KEY (organization_id) REFERENCES dunelight.organizations(id);");

        // roster_entries
        Execute.Sql(@"
            CREATE TABLE dunelight.roster_entries (
                id uuid NOT NULL,
                organization_id uuid NOT NULL,
                employee_id uuid NOT NULL,
                roster_type_id uuid NOT NULL,
                date_from date NOT NULL,
                date_to date,
                start_time time without time zone,
                end_time time without time zone,
                duration_hours numeric(5,2),
                note text,
                created_at timestamp with time zone NOT NULL,
                created_by uuid,
                updated_at timestamp with time zone,
                updated_by uuid,
                is_override boolean DEFAULT false NOT NULL
            );

            ALTER TABLE dunelight.roster_entries ADD CONSTRAINT pk_roster_entries PRIMARY KEY (id);

            CREATE INDEX ix_roster_entries_org_employee_date ON dunelight.roster_entries USING btree (organization_id, employee_id, date_from);

            ALTER TABLE dunelight.roster_entries ADD CONSTRAINT fk_roster_entries_employee_id FOREIGN KEY (employee_id) REFERENCES dunelight.employees(id);

            ALTER TABLE dunelight.roster_entries ADD CONSTRAINT fk_roster_entries_organization_id FOREIGN KEY (organization_id) REFERENCES dunelight.organizations(id);

            ALTER TABLE dunelight.roster_entries ADD CONSTRAINT fk_roster_entries_roster_type_id FOREIGN KEY (roster_type_id) REFERENCES dunelight.roster_types(id);");

        // roster_audit_log
        Execute.Sql(@"
            CREATE TABLE dunelight.roster_audit_log (
                id uuid NOT NULL,
                roster_entry_id uuid NOT NULL,
                change_type character varying(30) NOT NULL,
                old_value text,
                new_value text,
                changed_at timestamp with time zone NOT NULL,
                changed_by uuid
            );

            ALTER TABLE dunelight.roster_audit_log ADD CONSTRAINT pk_roster_audit_log PRIMARY KEY (id);

            CREATE INDEX ix_roster_audit_log_roster_entry_id ON dunelight.roster_audit_log USING btree (roster_entry_id);");

        // working_hours_templates
        Execute.Sql(@"
            CREATE TABLE dunelight.working_hours_templates (
                id uuid NOT NULL,
                organization_id uuid NOT NULL,
                employee_id uuid,
                company_id uuid,
                cycle_type character varying(20) NOT NULL,
                anchor_date date NOT NULL,
                created_at timestamp with time zone NOT NULL,
                created_by uuid,
                updated_at timestamp with time zone,
                updated_by uuid,
                CONSTRAINT ck_working_hours_templates_owner CHECK ((((employee_id IS NOT NULL) AND (company_id IS NULL)) OR ((employee_id IS NULL) AND (company_id IS NOT NULL))))
            );

            ALTER TABLE dunelight.working_hours_templates ADD CONSTRAINT pk_working_hours_templates PRIMARY KEY (id);

            CREATE UNIQUE INDEX ux_working_hours_templates_company ON dunelight.working_hours_templates USING btree (company_id) WHERE (company_id IS NOT NULL);

            CREATE UNIQUE INDEX ux_working_hours_templates_employee ON dunelight.working_hours_templates USING btree (employee_id) WHERE (employee_id IS NOT NULL);

            ALTER TABLE dunelight.working_hours_templates ADD CONSTRAINT fk_working_hours_templates_company_id FOREIGN KEY (company_id) REFERENCES dunelight.companies(id);

            ALTER TABLE dunelight.working_hours_templates ADD CONSTRAINT fk_working_hours_templates_employee_id FOREIGN KEY (employee_id) REFERENCES dunelight.employees(id);

            ALTER TABLE dunelight.working_hours_templates ADD CONSTRAINT fk_working_hours_templates_organization_id FOREIGN KEY (organization_id) REFERENCES dunelight.organizations(id);");

        // working_hours_intervals
        Execute.Sql(@"
            CREATE TABLE dunelight.working_hours_intervals (
                id uuid NOT NULL,
                working_hours_template_id uuid NOT NULL,
                cycle_week_index integer NOT NULL,
                day_of_week character varying(10) NOT NULL,
                start_time time without time zone NOT NULL,
                end_time time without time zone NOT NULL
            );

            ALTER TABLE dunelight.working_hours_intervals ADD CONSTRAINT pk_working_hours_intervals PRIMARY KEY (id);

            CREATE INDEX ix_working_hours_intervals_template_id ON dunelight.working_hours_intervals USING btree (working_hours_template_id);

            ALTER TABLE dunelight.working_hours_intervals ADD CONSTRAINT fk_working_hours_intervals_template_id FOREIGN KEY (working_hours_template_id) REFERENCES dunelight.working_hours_templates(id) ON DELETE CASCADE;");

        // leave_funds
        Execute.Sql(@"
            CREATE TABLE dunelight.leave_funds (
                id uuid NOT NULL,
                organization_id uuid NOT NULL,
                employee_id uuid NOT NULL,
                fund_year integer NOT NULL,
                opened_at timestamp with time zone NOT NULL,
                expires_at timestamp with time zone NOT NULL,
                allocated_days integer NOT NULL,
                used_days integer DEFAULT 0 NOT NULL,
                created_at timestamp with time zone NOT NULL,
                created_by uuid,
                updated_at timestamp with time zone,
                updated_by uuid
            );

            ALTER TABLE dunelight.leave_funds ADD CONSTRAINT pk_leave_funds PRIMARY KEY (id);

            CREATE UNIQUE INDEX ux_leave_funds_employee_year ON dunelight.leave_funds USING btree (organization_id, employee_id, fund_year);

            ALTER TABLE dunelight.leave_funds ADD CONSTRAINT fk_leave_funds_employee_id FOREIGN KEY (employee_id) REFERENCES dunelight.employees(id);

            ALTER TABLE dunelight.leave_funds ADD CONSTRAINT fk_leave_funds_organization_id FOREIGN KEY (organization_id) REFERENCES dunelight.organizations(id);");

        // leave_fund_usages
        Execute.Sql(@"
            CREATE TABLE dunelight.leave_fund_usages (
                id uuid NOT NULL,
                organization_id uuid NOT NULL,
                roster_entry_id uuid NOT NULL,
                leave_fund_id uuid NOT NULL,
                days integer NOT NULL,
                created_at timestamp with time zone NOT NULL
            );

            ALTER TABLE dunelight.leave_fund_usages ADD CONSTRAINT pk_leave_fund_usages PRIMARY KEY (id);

            CREATE INDEX ix_leave_fund_usages_leave_fund_id ON dunelight.leave_fund_usages USING btree (leave_fund_id);

            CREATE INDEX ix_leave_fund_usages_roster_entry_id ON dunelight.leave_fund_usages USING btree (roster_entry_id);

            ALTER TABLE dunelight.leave_fund_usages ADD CONSTRAINT fk_leave_fund_usages_leave_fund_id FOREIGN KEY (leave_fund_id) REFERENCES dunelight.leave_funds(id);

            ALTER TABLE dunelight.leave_fund_usages ADD CONSTRAINT fk_leave_fund_usages_roster_entry_id FOREIGN KEY (roster_entry_id) REFERENCES dunelight.roster_entries(id) ON DELETE CASCADE;");
    }
}
