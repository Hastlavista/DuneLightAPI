using System;
using BlueDragon.DuneLight.DatabaseMigration.Extensions;
using BlueDragon.DuneLight.DatabaseMigration.Models;
using FluentMigrator;

namespace BlueDragon.DuneLight.DatabaseMigration.Migrations.Baseline;

/// <summary>
/// Baseline 004 — WorkforceAndRoster. Kreira KONAČNU strukturu odjednom (nema create->rename/alter povijesti, nema backfilla).
/// Tablice: engagement_types, employees, employee_companies, employee_services, employee_audit_log, roles, user_role_assignments, employee_leave_settings, roster_types, roster_entries, roster_audit_log, leave_funds, leave_fund_usages, working_hours_templates, working_hours_intervals, schedule_breaks.
/// </summary>
[DeveloperMigration(2026, 09, 29, Developer.SilvioHabazin, 4)]
public class Baseline004WorkforceAndRoster : DuneLightMigration
{
    public override void Up()
    {
        Execute.Sql(Ddl);
    }

    private const string Ddl = """
CREATE TABLE dunelight.engagement_types (
    id uuid NOT NULL,
    organization_id uuid NOT NULL,
    name character varying(255) NOT NULL,
    is_active boolean DEFAULT true NOT NULL,
    sort_order integer DEFAULT 0 NOT NULL,
    created_at timestamp with time zone NOT NULL,
    created_by uuid,
    updated_at timestamp with time zone,
    updated_by uuid
);

ALTER TABLE ONLY dunelight.engagement_types
    ADD CONSTRAINT pk_engagement_types PRIMARY KEY (id);

CREATE UNIQUE INDEX ux_engagement_types_org_name_active ON dunelight.engagement_types USING btree (organization_id, name) WHERE (is_active = true);

CREATE TABLE dunelight.employees (
    id uuid NOT NULL,
    organization_id uuid NOT NULL,
    first_name character varying(255) NOT NULL,
    last_name character varying(255) NOT NULL,
    phone character varying(50),
    email character varying(255),
    date_of_birth timestamp with time zone,
    address text,
    oib character varying(11),
    note text,
    compensation_note text,
    color_hex character varying(7),
    sort_order integer DEFAULT 0 NOT NULL,
    employment_start_date timestamp with time zone NOT NULL,
    employment_end_date timestamp with time zone,
    engagement_type_id uuid NOT NULL,
    is_active boolean DEFAULT true NOT NULL,
    user_id uuid NOT NULL,
    created_at timestamp with time zone NOT NULL,
    created_by uuid,
    updated_at timestamp with time zone,
    updated_by uuid
);

ALTER TABLE ONLY dunelight.employees
    ADD CONSTRAINT pk_employees PRIMARY KEY (id);

CREATE INDEX ix_employees_organization_id ON dunelight.employees USING btree (organization_id);

CREATE UNIQUE INDEX ux_employees_user_id ON dunelight.employees USING btree (user_id);

CREATE TABLE dunelight.employee_companies (
    id uuid NOT NULL,
    employee_id uuid NOT NULL,
    company_id uuid NOT NULL,
    is_primary boolean DEFAULT false NOT NULL
);

ALTER TABLE ONLY dunelight.employee_companies
    ADD CONSTRAINT pk_employee_companies PRIMARY KEY (id);

CREATE UNIQUE INDEX ux_employee_companies_employee_company ON dunelight.employee_companies USING btree (employee_id, company_id);

CREATE UNIQUE INDEX ux_employee_companies_primary ON dunelight.employee_companies USING btree (employee_id) WHERE (is_primary = true);

CREATE TABLE dunelight.employee_services (
    id uuid NOT NULL,
    employee_id uuid NOT NULL,
    service_id uuid NOT NULL
);

ALTER TABLE ONLY dunelight.employee_services
    ADD CONSTRAINT pk_employee_services PRIMARY KEY (id);

CREATE UNIQUE INDEX ux_employee_services_employee_service ON dunelight.employee_services USING btree (employee_id, service_id);

CREATE TABLE dunelight.employee_audit_log (
    id uuid NOT NULL,
    employee_id uuid NOT NULL,
    change_type character varying(20) NOT NULL,
    old_value character varying(255),
    new_value character varying(255),
    changed_at timestamp with time zone NOT NULL,
    changed_by uuid
);

ALTER TABLE ONLY dunelight.employee_audit_log
    ADD CONSTRAINT pk_employee_audit_log PRIMARY KEY (id);

CREATE INDEX ix_employee_audit_log_employee_id ON dunelight.employee_audit_log USING btree (employee_id);

CREATE TABLE dunelight.roles (
    id uuid NOT NULL,
    organization_id uuid NOT NULL,
    name character varying(255) NOT NULL,
    created_at timestamp with time zone,
    created_by uuid
);

ALTER TABLE ONLY dunelight.roles
    ADD CONSTRAINT pk_roles PRIMARY KEY (id);

CREATE UNIQUE INDEX ux_roles_organization_name ON dunelight.roles USING btree (organization_id, name);

CREATE TABLE dunelight.user_role_assignments (
    id uuid NOT NULL,
    user_id uuid NOT NULL,
    role_id uuid NOT NULL
);

ALTER TABLE ONLY dunelight.user_role_assignments
    ADD CONSTRAINT pk_user_role_assignments PRIMARY KEY (id);

CREATE UNIQUE INDEX ux_user_role_assignments_user_role ON dunelight.user_role_assignments USING btree (user_id, role_id);

CREATE TABLE dunelight.employee_leave_settings (
    id uuid NOT NULL,
    organization_id uuid NOT NULL,
    employee_id uuid NOT NULL,
    annual_days integer NOT NULL,
    renewal_month integer NOT NULL,
    renewal_day integer NOT NULL,
    carryover_expiry_month integer NOT NULL,
    carryover_expiry_day integer NOT NULL,
    created_at timestamp with time zone NOT NULL,
    created_by uuid,
    updated_at timestamp with time zone,
    updated_by uuid
);

ALTER TABLE ONLY dunelight.employee_leave_settings
    ADD CONSTRAINT pk_employee_leave_settings PRIMARY KEY (id);

CREATE UNIQUE INDEX ux_employee_leave_settings_employee_id ON dunelight.employee_leave_settings USING btree (employee_id);

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

ALTER TABLE ONLY dunelight.roster_types
    ADD CONSTRAINT pk_roster_types PRIMARY KEY (id);

CREATE UNIQUE INDEX ux_roster_types_org_name_active ON dunelight.roster_types USING btree (organization_id, lower(TRIM(BOTH FROM name))) WHERE (is_active = true);

CREATE TABLE dunelight.roster_entries (
    id uuid NOT NULL,
    organization_id uuid NOT NULL,
    employee_id uuid NOT NULL,
    roster_type_id uuid NOT NULL,
    date_from timestamp with time zone NOT NULL,
    date_to timestamp with time zone,
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

ALTER TABLE ONLY dunelight.roster_entries
    ADD CONSTRAINT pk_roster_entries PRIMARY KEY (id);

CREATE INDEX ix_roster_entries_org_employee_date ON dunelight.roster_entries USING btree (organization_id, employee_id, date_from);

CREATE TABLE dunelight.roster_audit_log (
    id uuid NOT NULL,
    roster_entry_id uuid NOT NULL,
    change_type character varying(30) NOT NULL,
    old_value text,
    new_value text,
    changed_at timestamp with time zone NOT NULL,
    changed_by uuid
);

ALTER TABLE ONLY dunelight.roster_audit_log
    ADD CONSTRAINT pk_roster_audit_log PRIMARY KEY (id);

CREATE INDEX ix_roster_audit_log_roster_entry_id ON dunelight.roster_audit_log USING btree (roster_entry_id);

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

ALTER TABLE ONLY dunelight.leave_funds
    ADD CONSTRAINT pk_leave_funds PRIMARY KEY (id);

CREATE UNIQUE INDEX ux_leave_funds_employee_year ON dunelight.leave_funds USING btree (organization_id, employee_id, fund_year);

CREATE TABLE dunelight.leave_fund_usages (
    id uuid NOT NULL,
    organization_id uuid NOT NULL,
    roster_entry_id uuid NOT NULL,
    leave_fund_id uuid NOT NULL,
    days integer NOT NULL,
    created_at timestamp with time zone NOT NULL
);

ALTER TABLE ONLY dunelight.leave_fund_usages
    ADD CONSTRAINT pk_leave_fund_usages PRIMARY KEY (id);

CREATE INDEX ix_leave_fund_usages_leave_fund_id ON dunelight.leave_fund_usages USING btree (leave_fund_id);

CREATE INDEX ix_leave_fund_usages_roster_entry_id ON dunelight.leave_fund_usages USING btree (roster_entry_id);

CREATE TABLE dunelight.working_hours_templates (
    id uuid NOT NULL,
    organization_id uuid NOT NULL,
    employee_id uuid,
    company_id uuid,
    cycle_type character varying(20) NOT NULL,
    anchor_date timestamp with time zone NOT NULL,
    created_at timestamp with time zone NOT NULL,
    created_by uuid,
    updated_at timestamp with time zone,
    updated_by uuid,
    CONSTRAINT ck_working_hours_templates_owner CHECK ((((employee_id IS NOT NULL) AND (company_id IS NULL)) OR ((employee_id IS NULL) AND (company_id IS NOT NULL))))
);

ALTER TABLE ONLY dunelight.working_hours_templates
    ADD CONSTRAINT pk_working_hours_templates PRIMARY KEY (id);

CREATE UNIQUE INDEX ux_working_hours_templates_company ON dunelight.working_hours_templates USING btree (company_id) WHERE (company_id IS NOT NULL);

CREATE UNIQUE INDEX ux_working_hours_templates_employee ON dunelight.working_hours_templates USING btree (employee_id) WHERE (employee_id IS NOT NULL);

CREATE TABLE dunelight.working_hours_intervals (
    id uuid NOT NULL,
    working_hours_template_id uuid NOT NULL,
    cycle_week_index integer NOT NULL,
    day_of_week character varying(10) NOT NULL,
    start_time time without time zone NOT NULL,
    end_time time without time zone NOT NULL
);

ALTER TABLE ONLY dunelight.working_hours_intervals
    ADD CONSTRAINT pk_working_hours_intervals PRIMARY KEY (id);

CREATE INDEX ix_working_hours_intervals_template_id ON dunelight.working_hours_intervals USING btree (working_hours_template_id);

CREATE TABLE dunelight.schedule_breaks (
    id uuid NOT NULL,
    organization_id uuid NOT NULL,
    employee_id uuid NOT NULL,
    company_id uuid NOT NULL,
    starts_at timestamp with time zone NOT NULL,
    duration_minutes integer NOT NULL,
    note text,
    recurrence_group_id uuid,
    created_at timestamp with time zone NOT NULL,
    created_by uuid,
    updated_at timestamp with time zone,
    updated_by uuid
);

ALTER TABLE ONLY dunelight.schedule_breaks
    ADD CONSTRAINT pk_schedule_breaks PRIMARY KEY (id);

CREATE INDEX ix_schedule_breaks_org_company_startsat ON dunelight.schedule_breaks USING btree (organization_id, company_id, starts_at);

CREATE INDEX ix_schedule_breaks_org_employee_startsat ON dunelight.schedule_breaks USING btree (organization_id, employee_id, starts_at);

ALTER TABLE ONLY dunelight.engagement_types
    ADD CONSTRAINT fk_engagement_types_organization_id FOREIGN KEY (organization_id) REFERENCES dunelight.organizations(id);

ALTER TABLE ONLY dunelight.employees
    ADD CONSTRAINT fk_employees_engagement_type_id FOREIGN KEY (engagement_type_id) REFERENCES dunelight.engagement_types(id);

ALTER TABLE ONLY dunelight.employees
    ADD CONSTRAINT fk_employees_organization_id FOREIGN KEY (organization_id) REFERENCES dunelight.organizations(id);

ALTER TABLE ONLY dunelight.employees
    ADD CONSTRAINT fk_employees_user_id FOREIGN KEY (user_id) REFERENCES dunelight.users(id);

ALTER TABLE ONLY dunelight.employee_companies
    ADD CONSTRAINT fk_employee_companies_company_id FOREIGN KEY (company_id) REFERENCES dunelight.companies(id);

ALTER TABLE ONLY dunelight.employee_companies
    ADD CONSTRAINT fk_employee_companies_employee_id FOREIGN KEY (employee_id) REFERENCES dunelight.employees(id) ON DELETE CASCADE;

ALTER TABLE ONLY dunelight.employee_services
    ADD CONSTRAINT fk_employee_services_employee_id FOREIGN KEY (employee_id) REFERENCES dunelight.employees(id) ON DELETE CASCADE;

ALTER TABLE ONLY dunelight.employee_services
    ADD CONSTRAINT fk_employee_services_service_id FOREIGN KEY (service_id) REFERENCES dunelight.services(id);

ALTER TABLE ONLY dunelight.employee_audit_log
    ADD CONSTRAINT fk_employee_audit_log_employee_id FOREIGN KEY (employee_id) REFERENCES dunelight.employees(id);

ALTER TABLE ONLY dunelight.roles
    ADD CONSTRAINT fk_roles_organization_id FOREIGN KEY (organization_id) REFERENCES dunelight.organizations(id);

ALTER TABLE ONLY dunelight.user_role_assignments
    ADD CONSTRAINT fk_user_role_assignments_role_id FOREIGN KEY (role_id) REFERENCES dunelight.roles(id) ON DELETE CASCADE;

ALTER TABLE ONLY dunelight.user_role_assignments
    ADD CONSTRAINT fk_user_role_assignments_user_id FOREIGN KEY (user_id) REFERENCES dunelight.users(id) ON DELETE CASCADE;

ALTER TABLE ONLY dunelight.employee_leave_settings
    ADD CONSTRAINT fk_employee_leave_settings_employee_id FOREIGN KEY (employee_id) REFERENCES dunelight.employees(id);

ALTER TABLE ONLY dunelight.employee_leave_settings
    ADD CONSTRAINT fk_employee_leave_settings_organization_id FOREIGN KEY (organization_id) REFERENCES dunelight.organizations(id);

ALTER TABLE ONLY dunelight.roster_types
    ADD CONSTRAINT fk_roster_types_organization_id FOREIGN KEY (organization_id) REFERENCES dunelight.organizations(id);

ALTER TABLE ONLY dunelight.roster_entries
    ADD CONSTRAINT fk_roster_entries_employee_id FOREIGN KEY (employee_id) REFERENCES dunelight.employees(id);

ALTER TABLE ONLY dunelight.roster_entries
    ADD CONSTRAINT fk_roster_entries_organization_id FOREIGN KEY (organization_id) REFERENCES dunelight.organizations(id);

ALTER TABLE ONLY dunelight.roster_entries
    ADD CONSTRAINT fk_roster_entries_roster_type_id FOREIGN KEY (roster_type_id) REFERENCES dunelight.roster_types(id);

ALTER TABLE ONLY dunelight.leave_funds
    ADD CONSTRAINT fk_leave_funds_employee_id FOREIGN KEY (employee_id) REFERENCES dunelight.employees(id);

ALTER TABLE ONLY dunelight.leave_funds
    ADD CONSTRAINT fk_leave_funds_organization_id FOREIGN KEY (organization_id) REFERENCES dunelight.organizations(id);

ALTER TABLE ONLY dunelight.leave_fund_usages
    ADD CONSTRAINT fk_leave_fund_usages_leave_fund_id FOREIGN KEY (leave_fund_id) REFERENCES dunelight.leave_funds(id);

ALTER TABLE ONLY dunelight.leave_fund_usages
    ADD CONSTRAINT fk_leave_fund_usages_roster_entry_id FOREIGN KEY (roster_entry_id) REFERENCES dunelight.roster_entries(id) ON DELETE CASCADE;

ALTER TABLE ONLY dunelight.working_hours_templates
    ADD CONSTRAINT fk_working_hours_templates_company_id FOREIGN KEY (company_id) REFERENCES dunelight.companies(id);

ALTER TABLE ONLY dunelight.working_hours_templates
    ADD CONSTRAINT fk_working_hours_templates_employee_id FOREIGN KEY (employee_id) REFERENCES dunelight.employees(id);

ALTER TABLE ONLY dunelight.working_hours_templates
    ADD CONSTRAINT fk_working_hours_templates_organization_id FOREIGN KEY (organization_id) REFERENCES dunelight.organizations(id);

ALTER TABLE ONLY dunelight.working_hours_intervals
    ADD CONSTRAINT fk_working_hours_intervals_template_id FOREIGN KEY (working_hours_template_id) REFERENCES dunelight.working_hours_templates(id) ON DELETE CASCADE;

ALTER TABLE ONLY dunelight.schedule_breaks
    ADD CONSTRAINT fk_schedule_breaks_company_id FOREIGN KEY (company_id) REFERENCES dunelight.companies(id);

ALTER TABLE ONLY dunelight.schedule_breaks
    ADD CONSTRAINT fk_schedule_breaks_employee_id FOREIGN KEY (employee_id) REFERENCES dunelight.employees(id);

ALTER TABLE ONLY dunelight.schedule_breaks
    ADD CONSTRAINT fk_schedule_breaks_organization_id FOREIGN KEY (organization_id) REFERENCES dunelight.organizations(id);
""";
}
