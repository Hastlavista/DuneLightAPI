using BlueDragon.DuneLight.DatabaseMigration.Extensions;
using BlueDragon.DuneLight.DatabaseMigration.Models;

namespace BlueDragon.DuneLight.DatabaseMigration.Migrations._2026;

/// <summary>
/// Početna migracija (ADR-0022), korak 4/11: vrste angažmana i zaposlenici.
/// Gradi isključivo trenutnu ciljnu shemu; ne seeda podatke.
/// </summary>
[DeveloperMigration(2026, 10, 25, Developer.SilvioHabazin, 4)]
public class Baseline04Employees : DuneLightMigration
{
    public override void Up()
    {
        // engagement_types
        Execute.Sql(@"
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

            ALTER TABLE dunelight.engagement_types ADD CONSTRAINT pk_engagement_types PRIMARY KEY (id);

            CREATE UNIQUE INDEX ux_engagement_types_org_name_active ON dunelight.engagement_types USING btree (organization_id, name) WHERE (is_active = true);

            ALTER TABLE dunelight.engagement_types ADD CONSTRAINT fk_engagement_types_organization_id FOREIGN KEY (organization_id) REFERENCES dunelight.organizations(id);");

        // employees
        Execute.Sql(@"
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

            ALTER TABLE dunelight.employees ADD CONSTRAINT pk_employees PRIMARY KEY (id);

            CREATE INDEX ix_employees_organization_id ON dunelight.employees USING btree (organization_id);

            CREATE UNIQUE INDEX ux_employees_user_id ON dunelight.employees USING btree (user_id);

            ALTER TABLE dunelight.employees ADD CONSTRAINT fk_employees_engagement_type_id FOREIGN KEY (engagement_type_id) REFERENCES dunelight.engagement_types(id);

            ALTER TABLE dunelight.employees ADD CONSTRAINT fk_employees_organization_id FOREIGN KEY (organization_id) REFERENCES dunelight.organizations(id);

            ALTER TABLE dunelight.employees ADD CONSTRAINT fk_employees_user_id FOREIGN KEY (user_id) REFERENCES dunelight.users(id);");

        // employee_companies
        Execute.Sql(@"
            CREATE TABLE dunelight.employee_companies (
                id uuid CONSTRAINT employee_locations_id_not_null NOT NULL,
                employee_id uuid CONSTRAINT employee_locations_employee_id_not_null NOT NULL,
                company_id uuid CONSTRAINT employee_locations_location_id_not_null NOT NULL,
                is_primary boolean DEFAULT false CONSTRAINT employee_locations_is_primary_not_null NOT NULL
            );

            ALTER TABLE dunelight.employee_companies ADD CONSTRAINT pk_employee_companies PRIMARY KEY (id);

            CREATE UNIQUE INDEX ux_employee_companies_employee_company ON dunelight.employee_companies USING btree (employee_id, company_id);

            CREATE UNIQUE INDEX ux_employee_companies_primary ON dunelight.employee_companies USING btree (employee_id) WHERE (is_primary = true);

            ALTER TABLE dunelight.employee_companies ADD CONSTRAINT fk_employee_companies_company_id FOREIGN KEY (company_id) REFERENCES dunelight.companies(id);

            ALTER TABLE dunelight.employee_companies ADD CONSTRAINT fk_employee_companies_employee_id FOREIGN KEY (employee_id) REFERENCES dunelight.employees(id) ON DELETE CASCADE;");

        // employee_services
        Execute.Sql(@"
            CREATE TABLE dunelight.employee_services (
                id uuid NOT NULL,
                employee_id uuid NOT NULL,
                service_id uuid NOT NULL
            );

            ALTER TABLE dunelight.employee_services ADD CONSTRAINT pk_employee_services PRIMARY KEY (id);

            CREATE UNIQUE INDEX ux_employee_services_employee_service ON dunelight.employee_services USING btree (employee_id, service_id);

            ALTER TABLE dunelight.employee_services ADD CONSTRAINT fk_employee_services_employee_id FOREIGN KEY (employee_id) REFERENCES dunelight.employees(id) ON DELETE CASCADE;

            ALTER TABLE dunelight.employee_services ADD CONSTRAINT fk_employee_services_service_id FOREIGN KEY (service_id) REFERENCES dunelight.services(id);");

        // employee_audit_log
        Execute.Sql(@"
            CREATE TABLE dunelight.employee_audit_log (
                id uuid NOT NULL,
                employee_id uuid NOT NULL,
                change_type character varying(20) NOT NULL,
                old_value character varying(255),
                new_value character varying(255),
                changed_at timestamp with time zone NOT NULL,
                changed_by uuid
            );

            ALTER TABLE dunelight.employee_audit_log ADD CONSTRAINT pk_employee_audit_log PRIMARY KEY (id);

            CREATE INDEX ix_employee_audit_log_employee_id ON dunelight.employee_audit_log USING btree (employee_id);

            ALTER TABLE dunelight.employee_audit_log ADD CONSTRAINT fk_employee_audit_log_employee_id FOREIGN KEY (employee_id) REFERENCES dunelight.employees(id);");

        // employee_leave_settings
        Execute.Sql(@"
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

            ALTER TABLE dunelight.employee_leave_settings ADD CONSTRAINT pk_employee_leave_settings PRIMARY KEY (id);

            CREATE UNIQUE INDEX ux_employee_leave_settings_employee_id ON dunelight.employee_leave_settings USING btree (employee_id);

            ALTER TABLE dunelight.employee_leave_settings ADD CONSTRAINT fk_employee_leave_settings_employee_id FOREIGN KEY (employee_id) REFERENCES dunelight.employees(id);

            ALTER TABLE dunelight.employee_leave_settings ADD CONSTRAINT fk_employee_leave_settings_organization_id FOREIGN KEY (organization_id) REFERENCES dunelight.organizations(id);");
    }
}
