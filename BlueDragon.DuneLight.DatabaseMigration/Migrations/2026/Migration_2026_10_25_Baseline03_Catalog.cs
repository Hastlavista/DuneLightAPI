using BlueDragon.DuneLight.DatabaseMigration.Extensions;
using BlueDragon.DuneLight.DatabaseMigration.Models;

namespace BlueDragon.DuneLight.DatabaseMigration.Migrations._2026;

/// <summary>
/// Početna migracija (ADR-0022), korak 3/11: poslovnice, prostorije, resursi, usluge i paketi.
/// Gradi isključivo trenutnu ciljnu shemu; ne seeda podatke.
/// </summary>
[DeveloperMigration(2026, 10, 25, Developer.SilvioHabazin, 3)]
public class Baseline03Catalog : DuneLightMigration
{
    public override void Up()
    {
        // companies
        Execute.Sql(@"
            CREATE TABLE dunelight.companies (
                id uuid CONSTRAINT locations_id_not_null NOT NULL,
                organization_id uuid CONSTRAINT locations_organization_id_not_null NOT NULL,
                name character varying(255) CONSTRAINT locations_name_not_null NOT NULL,
                address character varying(500),
                phone character varying(50),
                color_hex character varying(7),
                is_active boolean DEFAULT true CONSTRAINT locations_is_active_not_null NOT NULL,
                note text,
                sort_order integer DEFAULT 0 CONSTRAINT locations_sort_order_not_null NOT NULL,
                created_at timestamp with time zone CONSTRAINT locations_created_at_not_null NOT NULL,
                created_by uuid,
                updated_at timestamp with time zone,
                updated_by uuid,
                country character varying(2) DEFAULT 'HR'::character varying NOT NULL,
                time_zone character varying(64)
            );

            ALTER TABLE dunelight.companies ADD CONSTRAINT pk_companies PRIMARY KEY (id);

            CREATE INDEX ix_companies_organization_id ON dunelight.companies USING btree (organization_id);

            CREATE UNIQUE INDEX ux_companies_org_name_active ON dunelight.companies USING btree (organization_id, lower(TRIM(BOTH FROM name))) WHERE (is_active = true);

            ALTER TABLE dunelight.companies ADD CONSTRAINT fk_companies_organization_id FOREIGN KEY (organization_id) REFERENCES dunelight.organizations(id);");

        // company_holidays
        Execute.Sql(@"
            CREATE TABLE dunelight.company_holidays (
                id uuid NOT NULL,
                organization_id uuid NOT NULL,
                company_id uuid NOT NULL,
                date date NOT NULL,
                name character varying(255) NOT NULL,
                is_auto_generated boolean NOT NULL,
                created_at timestamp with time zone NOT NULL,
                created_by uuid
            );

            ALTER TABLE dunelight.company_holidays ADD CONSTRAINT pk_company_holidays PRIMARY KEY (id);

            CREATE UNIQUE INDEX ux_company_holidays_org_company_date ON dunelight.company_holidays USING btree (organization_id, company_id, date);

            ALTER TABLE dunelight.company_holidays ADD CONSTRAINT fk_company_holidays_company_id FOREIGN KEY (company_id) REFERENCES dunelight.companies(id);

            ALTER TABLE dunelight.company_holidays ADD CONSTRAINT fk_company_holidays_organization_id FOREIGN KEY (organization_id) REFERENCES dunelight.organizations(id);");

        // rooms
        Execute.Sql(@"
            CREATE TABLE dunelight.rooms (
                id uuid NOT NULL,
                organization_id uuid NOT NULL,
                company_id uuid NOT NULL,
                name character varying(255) NOT NULL,
                is_active boolean DEFAULT true NOT NULL,
                note text,
                sort_order integer DEFAULT 0 NOT NULL,
                created_at timestamp with time zone NOT NULL,
                created_by uuid,
                updated_at timestamp with time zone,
                updated_by uuid,
                capacity integer NOT NULL,
                CONSTRAINT ck_rooms_capacity_positive CHECK ((capacity >= 1))
            );

            ALTER TABLE dunelight.rooms ADD CONSTRAINT pk_rooms PRIMARY KEY (id);

            CREATE INDEX ix_rooms_organization_company ON dunelight.rooms USING btree (organization_id, company_id);

            CREATE UNIQUE INDEX ux_rooms_org_company_name_active ON dunelight.rooms USING btree (organization_id, company_id, lower(TRIM(BOTH FROM name))) WHERE (is_active = true);

            ALTER TABLE dunelight.rooms ADD CONSTRAINT fk_rooms_company_id FOREIGN KEY (company_id) REFERENCES dunelight.companies(id);

            ALTER TABLE dunelight.rooms ADD CONSTRAINT fk_rooms_organization_id FOREIGN KEY (organization_id) REFERENCES dunelight.organizations(id);");

        // resources
        Execute.Sql(@"
            CREATE TABLE dunelight.resources (
                id uuid NOT NULL,
                organization_id uuid NOT NULL,
                company_id uuid NOT NULL,
                name character varying(255) NOT NULL,
                capacity integer NOT NULL,
                is_active boolean DEFAULT true NOT NULL,
                note text,
                sort_order integer DEFAULT 0 NOT NULL,
                created_at timestamp with time zone NOT NULL,
                created_by uuid,
                updated_at timestamp with time zone,
                updated_by uuid,
                CONSTRAINT ck_resources_capacity_positive CHECK ((capacity >= 1))
            );

            ALTER TABLE dunelight.resources ADD CONSTRAINT pk_resources PRIMARY KEY (id);

            CREATE INDEX ix_resources_organization_company ON dunelight.resources USING btree (organization_id, company_id);

            CREATE UNIQUE INDEX ux_resources_org_company_name_active ON dunelight.resources USING btree (organization_id, company_id, lower(TRIM(BOTH FROM name))) WHERE (is_active = true);

            ALTER TABLE dunelight.resources ADD CONSTRAINT fk_resources_company_id FOREIGN KEY (company_id) REFERENCES dunelight.companies(id);

            ALTER TABLE dunelight.resources ADD CONSTRAINT fk_resources_organization_id FOREIGN KEY (organization_id) REFERENCES dunelight.organizations(id);");

        // services
        Execute.Sql(@"
            CREATE TABLE dunelight.services (
                id uuid NOT NULL,
                organization_id uuid NOT NULL,
                name character varying(255) NOT NULL,
                default_duration_minutes integer NOT NULL,
                default_price numeric(10,2) NOT NULL,
                description text,
                is_active boolean DEFAULT true NOT NULL,
                sort_order integer DEFAULT 0 NOT NULL,
                created_at timestamp with time zone NOT NULL,
                created_by uuid,
                updated_at timestamp with time zone,
                updated_by uuid,
                execution_mode character varying(20) NOT NULL,
                color_hex character varying(7)
            );

            ALTER TABLE dunelight.services ADD CONSTRAINT pk_services PRIMARY KEY (id);

            CREATE UNIQUE INDEX ux_services_org_name_active ON dunelight.services USING btree (organization_id, lower(TRIM(BOTH FROM name))) WHERE (is_active = true);

            ALTER TABLE dunelight.services ADD CONSTRAINT fk_services_organization_id FOREIGN KEY (organization_id) REFERENCES dunelight.organizations(id);");

        // service_companies
        Execute.Sql(@"
            CREATE TABLE dunelight.service_companies (
                id uuid NOT NULL,
                service_id uuid NOT NULL,
                company_id uuid NOT NULL
            );

            ALTER TABLE dunelight.service_companies ADD CONSTRAINT pk_service_companies PRIMARY KEY (id);

            CREATE INDEX ix_service_companies_company_id ON dunelight.service_companies USING btree (company_id);

            CREATE UNIQUE INDEX ux_service_companies_service_company ON dunelight.service_companies USING btree (service_id, company_id);

            ALTER TABLE dunelight.service_companies ADD CONSTRAINT fk_service_companies_company_id FOREIGN KEY (company_id) REFERENCES dunelight.companies(id) ON DELETE CASCADE;

            ALTER TABLE dunelight.service_companies ADD CONSTRAINT fk_service_companies_service_id FOREIGN KEY (service_id) REFERENCES dunelight.services(id) ON DELETE CASCADE;");

        // packages
        Execute.Sql(@"
            CREATE TABLE dunelight.packages (
                id uuid NOT NULL,
                organization_id uuid NOT NULL,
                name character varying(255) NOT NULL,
                description text,
                entry_mode character varying(20) NOT NULL,
                total_entry_count integer,
                validity_type character varying(20) NOT NULL,
                validity_days integer,
                validity_fixed_date date,
                default_price numeric(10,2) NOT NULL,
                is_active boolean DEFAULT true NOT NULL,
                sort_order integer DEFAULT 0 NOT NULL,
                created_at timestamp with time zone NOT NULL,
                created_by uuid,
                updated_at timestamp with time zone,
                updated_by uuid
            );

            ALTER TABLE dunelight.packages ADD CONSTRAINT pk_packages PRIMARY KEY (id);

            CREATE UNIQUE INDEX ux_packages_org_name_active ON dunelight.packages USING btree (organization_id, lower(TRIM(BOTH FROM name))) WHERE (is_active = true);

            ALTER TABLE dunelight.packages ADD CONSTRAINT fk_packages_organization_id FOREIGN KEY (organization_id) REFERENCES dunelight.organizations(id);");

        // package_services
        Execute.Sql(@"
            CREATE TABLE dunelight.package_services (
                id uuid NOT NULL,
                package_id uuid NOT NULL,
                service_id uuid NOT NULL,
                entry_count integer
            );

            ALTER TABLE dunelight.package_services ADD CONSTRAINT pk_package_services PRIMARY KEY (id);

            CREATE UNIQUE INDEX ux_package_services_package_service ON dunelight.package_services USING btree (package_id, service_id);

            ALTER TABLE dunelight.package_services ADD CONSTRAINT fk_package_services_package_id FOREIGN KEY (package_id) REFERENCES dunelight.packages(id) ON DELETE CASCADE;

            ALTER TABLE dunelight.package_services ADD CONSTRAINT fk_package_services_service_id FOREIGN KEY (service_id) REFERENCES dunelight.services(id);");
    }
}
