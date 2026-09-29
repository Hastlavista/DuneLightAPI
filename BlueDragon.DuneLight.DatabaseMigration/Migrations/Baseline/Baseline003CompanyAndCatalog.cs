using System;
using BlueDragon.DuneLight.DatabaseMigration.Extensions;
using BlueDragon.DuneLight.DatabaseMigration.Models;
using FluentMigrator;

namespace BlueDragon.DuneLight.DatabaseMigration.Migrations.Baseline;

/// <summary>
/// Baseline 003 — CompanyAndCatalog. Kreira KONAČNU strukturu odjednom (nema create->rename/alter povijesti, nema backfilla).
/// Tablice: companies, company_holidays, services, service_companies, packages, package_services, price_list_items, price_list_item_history, products, product_stock, rooms, resources.
/// </summary>
[DeveloperMigration(2026, 09, 29, Developer.SilvioHabazin, 3)]
public class Baseline003CompanyAndCatalog : DuneLightMigration
{
    public override void Up()
    {
        Execute.Sql(Ddl);
    }

    private const string Ddl = """
CREATE TABLE dunelight.companies (
    id uuid NOT NULL,
    organization_id uuid NOT NULL,
    name character varying(255) NOT NULL,
    address character varying(500),
    phone character varying(50),
    color_hex character varying(7),
    is_active boolean DEFAULT true NOT NULL,
    note text,
    sort_order integer DEFAULT 0 NOT NULL,
    created_at timestamp with time zone NOT NULL,
    created_by uuid,
    updated_at timestamp with time zone,
    updated_by uuid,
    country character varying(2) DEFAULT 'HR'::character varying NOT NULL
);

ALTER TABLE ONLY dunelight.companies
    ADD CONSTRAINT pk_companies PRIMARY KEY (id);

CREATE INDEX ix_companies_organization_id ON dunelight.companies USING btree (organization_id);

CREATE UNIQUE INDEX ux_companies_org_name_active ON dunelight.companies USING btree (organization_id, lower(TRIM(BOTH FROM name))) WHERE (is_active = true);

CREATE TABLE dunelight.company_holidays (
    id uuid NOT NULL,
    organization_id uuid NOT NULL,
    company_id uuid NOT NULL,
    date timestamp with time zone NOT NULL,
    name character varying(255) NOT NULL,
    is_auto_generated boolean NOT NULL,
    created_at timestamp with time zone NOT NULL,
    created_by uuid
);

ALTER TABLE ONLY dunelight.company_holidays
    ADD CONSTRAINT pk_company_holidays PRIMARY KEY (id);

CREATE UNIQUE INDEX ux_company_holidays_org_company_date ON dunelight.company_holidays USING btree (organization_id, company_id, date);

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

ALTER TABLE ONLY dunelight.services
    ADD CONSTRAINT pk_services PRIMARY KEY (id);

CREATE UNIQUE INDEX ux_services_org_name_active ON dunelight.services USING btree (organization_id, lower(TRIM(BOTH FROM name))) WHERE (is_active = true);

CREATE TABLE dunelight.service_companies (
    id uuid NOT NULL,
    service_id uuid NOT NULL,
    company_id uuid NOT NULL
);

ALTER TABLE ONLY dunelight.service_companies
    ADD CONSTRAINT pk_service_companies PRIMARY KEY (id);

CREATE INDEX ix_service_companies_company_id ON dunelight.service_companies USING btree (company_id);

CREATE UNIQUE INDEX ux_service_companies_service_company ON dunelight.service_companies USING btree (service_id, company_id);

CREATE TABLE dunelight.packages (
    id uuid NOT NULL,
    organization_id uuid NOT NULL,
    name character varying(255) NOT NULL,
    description text,
    entry_mode character varying(20) NOT NULL,
    total_entry_count integer,
    validity_type character varying(20) NOT NULL,
    validity_days integer,
    validity_fixed_date timestamp with time zone,
    default_price numeric(10,2) NOT NULL,
    is_active boolean DEFAULT true NOT NULL,
    sort_order integer DEFAULT 0 NOT NULL,
    created_at timestamp with time zone NOT NULL,
    created_by uuid,
    updated_at timestamp with time zone,
    updated_by uuid
);

ALTER TABLE ONLY dunelight.packages
    ADD CONSTRAINT pk_packages PRIMARY KEY (id);

CREATE UNIQUE INDEX ux_packages_org_name_active ON dunelight.packages USING btree (organization_id, lower(TRIM(BOTH FROM name))) WHERE (is_active = true);

CREATE TABLE dunelight.package_services (
    id uuid NOT NULL,
    package_id uuid NOT NULL,
    service_id uuid NOT NULL,
    entry_count integer
);

ALTER TABLE ONLY dunelight.package_services
    ADD CONSTRAINT pk_package_services PRIMARY KEY (id);

CREATE UNIQUE INDEX ux_package_services_package_service ON dunelight.package_services USING btree (package_id, service_id);

CREATE TABLE dunelight.price_list_items (
    id uuid NOT NULL,
    organization_id uuid NOT NULL,
    service_id uuid,
    package_id uuid,
    company_id uuid,
    price numeric(10,2) NOT NULL,
    valid_from timestamp with time zone NOT NULL,
    valid_to timestamp with time zone,
    is_active boolean DEFAULT true NOT NULL,
    created_at timestamp with time zone NOT NULL,
    created_by uuid,
    updated_at timestamp with time zone,
    updated_by uuid,
    CONSTRAINT ck_price_list_items_subject CHECK ((((service_id IS NOT NULL) AND (package_id IS NULL)) OR ((service_id IS NULL) AND (package_id IS NOT NULL))))
);

ALTER TABLE ONLY dunelight.price_list_items
    ADD CONSTRAINT pk_price_list_items PRIMARY KEY (id);

CREATE INDEX ix_price_list_items_subject_company ON dunelight.price_list_items USING btree (organization_id, service_id, package_id, company_id);

CREATE TABLE dunelight.price_list_item_history (
    id uuid NOT NULL,
    price_list_item_id uuid NOT NULL,
    old_price numeric(10,2) NOT NULL,
    new_price numeric(10,2) NOT NULL,
    changed_at timestamp with time zone NOT NULL,
    changed_by uuid,
    old_valid_from timestamp with time zone NOT NULL,
    new_valid_from timestamp with time zone NOT NULL,
    old_valid_to timestamp with time zone,
    new_valid_to timestamp with time zone
);

ALTER TABLE ONLY dunelight.price_list_item_history
    ADD CONSTRAINT pk_price_list_item_history PRIMARY KEY (id);

CREATE INDEX ix_price_list_item_history_price_list_item_id ON dunelight.price_list_item_history USING btree (price_list_item_id);

CREATE TABLE dunelight.products (
    id uuid NOT NULL,
    organization_id uuid NOT NULL,
    name character varying(255) NOT NULL,
    description text,
    sku character varying(100),
    default_price numeric(10,2) NOT NULL,
    is_active boolean DEFAULT true NOT NULL,
    created_at timestamp with time zone NOT NULL,
    created_by uuid,
    updated_at timestamp with time zone,
    updated_by uuid
);

ALTER TABLE ONLY dunelight.products
    ADD CONSTRAINT pk_products PRIMARY KEY (id);

CREATE INDEX ix_products_organization_id ON dunelight.products USING btree (organization_id);

CREATE UNIQUE INDEX ux_products_org_name_active ON dunelight.products USING btree (organization_id, lower(TRIM(BOTH FROM name))) WHERE (is_active = true);

CREATE UNIQUE INDEX ux_products_org_sku ON dunelight.products USING btree (organization_id, lower(TRIM(BOTH FROM sku))) WHERE ((sku IS NOT NULL) AND (TRIM(BOTH FROM sku) <> ''::text));

CREATE TABLE dunelight.product_stock (
    id uuid NOT NULL,
    organization_id uuid NOT NULL,
    product_id uuid NOT NULL,
    company_id uuid NOT NULL,
    quantity integer DEFAULT 0 NOT NULL,
    updated_at timestamp with time zone NOT NULL,
    CONSTRAINT ck_product_stock_quantity_non_negative CHECK ((quantity >= 0))
);

ALTER TABLE ONLY dunelight.product_stock
    ADD CONSTRAINT pk_product_stock PRIMARY KEY (id);

CREATE INDEX ix_product_stock_org_company ON dunelight.product_stock USING btree (organization_id, company_id);

CREATE UNIQUE INDEX ux_product_stock_product_company ON dunelight.product_stock USING btree (product_id, company_id);

CREATE TABLE dunelight.rooms (
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
    CONSTRAINT ck_rooms_capacity CHECK ((capacity >= 1))
);

ALTER TABLE ONLY dunelight.rooms
    ADD CONSTRAINT pk_rooms PRIMARY KEY (id);

CREATE INDEX ix_rooms_organization_company ON dunelight.rooms USING btree (organization_id, company_id);

CREATE UNIQUE INDEX ux_rooms_org_company_name_active ON dunelight.rooms USING btree (organization_id, company_id, lower(TRIM(BOTH FROM name))) WHERE (is_active = true);

CREATE TABLE dunelight.resources (
    id uuid NOT NULL,
    organization_id uuid NOT NULL,
    company_id uuid NOT NULL,
    name character varying(255) NOT NULL,
    capacity integer NOT NULL,
    is_active boolean DEFAULT true NOT NULL,
    created_at timestamp with time zone NOT NULL,
    created_by uuid,
    updated_at timestamp with time zone,
    updated_by uuid,
    CONSTRAINT ck_resources_capacity CHECK ((capacity >= 1))
);

ALTER TABLE ONLY dunelight.resources
    ADD CONSTRAINT pk_resources PRIMARY KEY (id);

CREATE INDEX ix_resources_organization_company ON dunelight.resources USING btree (organization_id, company_id);

CREATE UNIQUE INDEX ux_resources_org_company_name_active ON dunelight.resources USING btree (organization_id, company_id, lower(TRIM(BOTH FROM name))) WHERE (is_active = true);

ALTER TABLE ONLY dunelight.companies
    ADD CONSTRAINT fk_companies_organization_id FOREIGN KEY (organization_id) REFERENCES dunelight.organizations(id);

ALTER TABLE ONLY dunelight.company_holidays
    ADD CONSTRAINT fk_company_holidays_company_id FOREIGN KEY (company_id) REFERENCES dunelight.companies(id);

ALTER TABLE ONLY dunelight.company_holidays
    ADD CONSTRAINT fk_company_holidays_organization_id FOREIGN KEY (organization_id) REFERENCES dunelight.organizations(id);

ALTER TABLE ONLY dunelight.services
    ADD CONSTRAINT fk_services_organization_id FOREIGN KEY (organization_id) REFERENCES dunelight.organizations(id);

ALTER TABLE ONLY dunelight.service_companies
    ADD CONSTRAINT fk_service_companies_company_id FOREIGN KEY (company_id) REFERENCES dunelight.companies(id) ON DELETE CASCADE;

ALTER TABLE ONLY dunelight.service_companies
    ADD CONSTRAINT fk_service_companies_service_id FOREIGN KEY (service_id) REFERENCES dunelight.services(id) ON DELETE CASCADE;

ALTER TABLE ONLY dunelight.packages
    ADD CONSTRAINT fk_packages_organization_id FOREIGN KEY (organization_id) REFERENCES dunelight.organizations(id);

ALTER TABLE ONLY dunelight.package_services
    ADD CONSTRAINT fk_package_services_package_id FOREIGN KEY (package_id) REFERENCES dunelight.packages(id) ON DELETE CASCADE;

ALTER TABLE ONLY dunelight.package_services
    ADD CONSTRAINT fk_package_services_service_id FOREIGN KEY (service_id) REFERENCES dunelight.services(id);

ALTER TABLE ONLY dunelight.price_list_items
    ADD CONSTRAINT fk_price_list_items_company_id FOREIGN KEY (company_id) REFERENCES dunelight.companies(id);

ALTER TABLE ONLY dunelight.price_list_items
    ADD CONSTRAINT fk_price_list_items_organization_id FOREIGN KEY (organization_id) REFERENCES dunelight.organizations(id);

ALTER TABLE ONLY dunelight.price_list_items
    ADD CONSTRAINT fk_price_list_items_package_id FOREIGN KEY (package_id) REFERENCES dunelight.packages(id);

ALTER TABLE ONLY dunelight.price_list_items
    ADD CONSTRAINT fk_price_list_items_service_id FOREIGN KEY (service_id) REFERENCES dunelight.services(id);

ALTER TABLE ONLY dunelight.price_list_item_history
    ADD CONSTRAINT fk_price_list_item_history_price_list_item_id FOREIGN KEY (price_list_item_id) REFERENCES dunelight.price_list_items(id);

ALTER TABLE ONLY dunelight.products
    ADD CONSTRAINT fk_products_organization_id FOREIGN KEY (organization_id) REFERENCES dunelight.organizations(id);

ALTER TABLE ONLY dunelight.product_stock
    ADD CONSTRAINT fk_product_stock_company_id FOREIGN KEY (company_id) REFERENCES dunelight.companies(id);

ALTER TABLE ONLY dunelight.product_stock
    ADD CONSTRAINT fk_product_stock_product_id FOREIGN KEY (product_id) REFERENCES dunelight.products(id);

ALTER TABLE ONLY dunelight.rooms
    ADD CONSTRAINT fk_rooms_company_id FOREIGN KEY (company_id) REFERENCES dunelight.companies(id);

ALTER TABLE ONLY dunelight.rooms
    ADD CONSTRAINT fk_rooms_organization_id FOREIGN KEY (organization_id) REFERENCES dunelight.organizations(id);

ALTER TABLE ONLY dunelight.resources
    ADD CONSTRAINT fk_resources_organization_id FOREIGN KEY (organization_id) REFERENCES dunelight.organizations(id);

ALTER TABLE ONLY dunelight.resources
    ADD CONSTRAINT fk_resources_company_id FOREIGN KEY (company_id) REFERENCES dunelight.companies(id);
""";
}
