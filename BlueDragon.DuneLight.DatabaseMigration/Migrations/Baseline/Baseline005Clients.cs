using System;
using BlueDragon.DuneLight.DatabaseMigration.Extensions;
using BlueDragon.DuneLight.DatabaseMigration.Models;
using FluentMigrator;

namespace BlueDragon.DuneLight.DatabaseMigration.Migrations.Baseline;

/// <summary>
/// Baseline 005 — Clients. Kreira KONAČNU strukturu odjednom (nema create->rename/alter povijesti, nema backfilla).
/// Tablice: clients, client_tags, client_tag_assignments, client_packages, client_package_service_entries.
/// </summary>
[DeveloperMigration(2026, 09, 29, Developer.SilvioHabazin, 5)]
public class Baseline005Clients : DuneLightMigration
{
    public override void Up()
    {
        Execute.Sql(Ddl);
    }

    private const string Ddl = """
CREATE TABLE dunelight.clients (
    id uuid NOT NULL,
    organization_id uuid NOT NULL,
    member_number integer NOT NULL,
    first_name character varying(255) NOT NULL,
    last_name character varying(255) NOT NULL,
    date_of_birth timestamp with time zone,
    occupation character varying(255),
    phone character varying(255),
    email character varying(255),
    note text,
    health_note text,
    gdpr_consent_given boolean DEFAULT false NOT NULL,
    gdpr_consent_date timestamp with time zone,
    home_company_id uuid,
    home_trainer_id uuid,
    is_active boolean DEFAULT true NOT NULL,
    is_anonymized boolean DEFAULT false NOT NULL,
    anonymized_at timestamp with time zone,
    created_at timestamp with time zone NOT NULL,
    created_by uuid,
    updated_at timestamp with time zone,
    updated_by uuid,
    anonymized_by uuid
);

ALTER TABLE ONLY dunelight.clients
    ADD CONSTRAINT pk_clients PRIMARY KEY (id);

CREATE INDEX ix_clients_org_last_first_name ON dunelight.clients USING btree (organization_id, last_name, first_name);

CREATE UNIQUE INDEX ux_clients_org_member_number ON dunelight.clients USING btree (organization_id, member_number);

CREATE TABLE dunelight.client_tags (
    id uuid NOT NULL,
    organization_id uuid NOT NULL,
    name character varying(255) NOT NULL,
    color_hex character varying(7),
    is_active boolean DEFAULT true NOT NULL,
    sort_order integer DEFAULT 0 NOT NULL,
    created_at timestamp with time zone NOT NULL,
    created_by uuid,
    updated_at timestamp with time zone,
    updated_by uuid
);

ALTER TABLE ONLY dunelight.client_tags
    ADD CONSTRAINT pk_client_tags PRIMARY KEY (id);

CREATE UNIQUE INDEX ux_client_tags_org_name_active ON dunelight.client_tags USING btree (organization_id, lower(TRIM(BOTH FROM name))) WHERE (is_active = true);

CREATE TABLE dunelight.client_tag_assignments (
    id uuid NOT NULL,
    client_id uuid NOT NULL,
    tag_id uuid NOT NULL
);

ALTER TABLE ONLY dunelight.client_tag_assignments
    ADD CONSTRAINT pk_client_tag_assignments PRIMARY KEY (id);

CREATE UNIQUE INDEX ux_client_tag_assignments_client_tag ON dunelight.client_tag_assignments USING btree (client_id, tag_id);

CREATE TABLE dunelight.client_packages (
    id uuid NOT NULL,
    organization_id uuid NOT NULL,
    client_id uuid NOT NULL,
    package_id uuid NOT NULL,
    purchase_date timestamp with time zone NOT NULL,
    paid_price numeric(10,2) NOT NULL,
    entry_mode character varying(20) NOT NULL,
    total_entry_count integer,
    remaining_shared_entries integer,
    validity_type character varying(20) NOT NULL,
    expiry_date timestamp with time zone NOT NULL,
    status character varying(20) NOT NULL,
    created_at timestamp with time zone NOT NULL,
    created_by uuid,
    updated_at timestamp with time zone,
    updated_by uuid,
    cancelled_at timestamp with time zone,
    cancelled_by uuid
);

ALTER TABLE ONLY dunelight.client_packages
    ADD CONSTRAINT pk_client_packages PRIMARY KEY (id);

CREATE INDEX ix_client_packages_org_client ON dunelight.client_packages USING btree (organization_id, client_id);

CREATE TABLE dunelight.client_package_service_entries (
    id uuid NOT NULL,
    client_package_id uuid NOT NULL,
    service_id uuid NOT NULL,
    total_entries integer,
    remaining_entries integer
);

ALTER TABLE ONLY dunelight.client_package_service_entries
    ADD CONSTRAINT pk_client_package_service_entries PRIMARY KEY (id);

CREATE UNIQUE INDEX ux_client_package_service_entries_package_service ON dunelight.client_package_service_entries USING btree (client_package_id, service_id);

ALTER TABLE ONLY dunelight.clients
    ADD CONSTRAINT fk_clients_home_company_id FOREIGN KEY (home_company_id) REFERENCES dunelight.companies(id);

ALTER TABLE ONLY dunelight.clients
    ADD CONSTRAINT fk_clients_home_trainer_id FOREIGN KEY (home_trainer_id) REFERENCES dunelight.employees(id);

ALTER TABLE ONLY dunelight.clients
    ADD CONSTRAINT fk_clients_organization_id FOREIGN KEY (organization_id) REFERENCES dunelight.organizations(id);

ALTER TABLE ONLY dunelight.client_tags
    ADD CONSTRAINT fk_client_tags_organization_id FOREIGN KEY (organization_id) REFERENCES dunelight.organizations(id);

ALTER TABLE ONLY dunelight.client_tag_assignments
    ADD CONSTRAINT fk_client_tag_assignments_client_id FOREIGN KEY (client_id) REFERENCES dunelight.clients(id) ON DELETE CASCADE;

ALTER TABLE ONLY dunelight.client_tag_assignments
    ADD CONSTRAINT fk_client_tag_assignments_tag_id FOREIGN KEY (tag_id) REFERENCES dunelight.client_tags(id);

ALTER TABLE ONLY dunelight.client_packages
    ADD CONSTRAINT fk_client_packages_client_id FOREIGN KEY (client_id) REFERENCES dunelight.clients(id);

ALTER TABLE ONLY dunelight.client_packages
    ADD CONSTRAINT fk_client_packages_organization_id FOREIGN KEY (organization_id) REFERENCES dunelight.organizations(id);

ALTER TABLE ONLY dunelight.client_packages
    ADD CONSTRAINT fk_client_packages_package_id FOREIGN KEY (package_id) REFERENCES dunelight.packages(id);

ALTER TABLE ONLY dunelight.client_package_service_entries
    ADD CONSTRAINT fk_client_package_service_entries_client_package_id FOREIGN KEY (client_package_id) REFERENCES dunelight.client_packages(id) ON DELETE CASCADE;

ALTER TABLE ONLY dunelight.client_package_service_entries
    ADD CONSTRAINT fk_client_package_service_entries_service_id FOREIGN KEY (service_id) REFERENCES dunelight.services(id);
""";
}
