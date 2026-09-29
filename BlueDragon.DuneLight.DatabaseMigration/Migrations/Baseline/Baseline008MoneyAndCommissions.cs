using System;
using BlueDragon.DuneLight.DatabaseMigration.Extensions;
using BlueDragon.DuneLight.DatabaseMigration.Models;
using FluentMigrator;

namespace BlueDragon.DuneLight.DatabaseMigration.Migrations.Baseline;

/// <summary>
/// Baseline 008 — MoneyAndCommissions. Kreira KONAČNU strukturu odjednom (nema create->rename/alter povijesti, nema backfilla).
/// Tablice: checkouts, checkout_items, payments, payment_allocations, checkout_audit_log, stock_movements, commission_rules, commission_entries.
/// </summary>
[DeveloperMigration(2026, 09, 29, Developer.SilvioHabazin, 8)]
public class Baseline008MoneyAndCommissions : DuneLightMigration
{
    public override void Up()
    {
        Execute.Sql(Ddl);
    }

    private const string Ddl = """
CREATE TABLE dunelight.checkouts (
    id uuid NOT NULL,
    organization_id uuid NOT NULL,
    company_id uuid NOT NULL,
    client_id uuid NOT NULL,
    status character varying(20) NOT NULL,
    created_at timestamp with time zone NOT NULL,
    created_by uuid,
    completed_at timestamp with time zone,
    completed_by uuid,
    cancelled_at timestamp with time zone,
    cancelled_by uuid,
    voided_at timestamp with time zone,
    voided_by uuid,
    void_reason text
);

ALTER TABLE ONLY dunelight.checkouts
    ADD CONSTRAINT pk_checkouts PRIMARY KEY (id);

CREATE INDEX ix_checkouts_org_client ON dunelight.checkouts USING btree (organization_id, client_id);

CREATE INDEX ix_checkouts_org_company_status ON dunelight.checkouts USING btree (organization_id, company_id, status);

CREATE TABLE dunelight.checkout_items (
    id uuid NOT NULL,
    organization_id uuid NOT NULL,
    checkout_id uuid NOT NULL,
    type character varying(20) NOT NULL,
    description text,
    unit_price numeric(10,2) NOT NULL,
    quantity integer DEFAULT 1 NOT NULL,
    amount numeric(10,2) NOT NULL,
    booking_segment_participation_id uuid,
    package_id uuid,
    client_package_id uuid,
    locks_participation boolean DEFAULT false NOT NULL,
    created_at timestamp with time zone NOT NULL,
    created_by uuid,
    product_id uuid,
    CONSTRAINT ck_checkout_items_quantity CHECK (((((type)::text = 'Participation'::text) AND (quantity = 1)) OR (((type)::text = 'Package'::text) AND (quantity = 1)) OR (((type)::text = 'Product'::text) AND (quantity >= 1)))),
    CONSTRAINT ck_checkout_items_subject CHECK (((((type)::text = 'Participation'::text) AND (booking_segment_participation_id IS NOT NULL) AND (package_id IS NULL) AND (product_id IS NULL)) OR (((type)::text = 'Package'::text) AND (package_id IS NOT NULL) AND (booking_segment_participation_id IS NULL) AND (product_id IS NULL)) OR (((type)::text = 'Product'::text) AND (product_id IS NOT NULL) AND (booking_segment_participation_id IS NULL) AND (package_id IS NULL))))
);

ALTER TABLE ONLY dunelight.checkout_items
    ADD CONSTRAINT pk_checkout_items PRIMARY KEY (id);

CREATE INDEX ix_checkout_items_org_checkout ON dunelight.checkout_items USING btree (organization_id, checkout_id);

CREATE UNIQUE INDEX ux_checkout_items_locks_participation ON dunelight.checkout_items USING btree (booking_segment_participation_id) WHERE (locks_participation = true);

CREATE TABLE dunelight.payments (
    id uuid NOT NULL,
    organization_id uuid NOT NULL,
    amount numeric(10,2) NOT NULL,
    method character varying(20) NOT NULL,
    status character varying(20) NOT NULL,
    note text,
    is_checkin_generated boolean DEFAULT false NOT NULL,
    created_at timestamp with time zone NOT NULL,
    created_by uuid,
    voided_at timestamp with time zone,
    voided_by uuid,
    void_reason text,
    checkout_id uuid NOT NULL
);

ALTER TABLE ONLY dunelight.payments
    ADD CONSTRAINT pk_payments PRIMARY KEY (id);

CREATE INDEX ix_payments_org_checkout ON dunelight.payments USING btree (organization_id, checkout_id);

CREATE INDEX ix_payments_org_completed_created_at ON dunelight.payments USING btree (organization_id, created_at) WHERE ((status)::text = 'Completed'::text);

CREATE TABLE dunelight.payment_allocations (
    id uuid NOT NULL,
    payment_id uuid NOT NULL,
    checkout_item_id uuid NOT NULL,
    amount numeric(10,2) NOT NULL,
    created_at timestamp with time zone NOT NULL
);

ALTER TABLE ONLY dunelight.payment_allocations
    ADD CONSTRAINT pk_payment_allocations PRIMARY KEY (id);

CREATE INDEX ix_payment_allocations_checkout_item_id ON dunelight.payment_allocations USING btree (checkout_item_id);

CREATE INDEX ix_payment_allocations_payment_id ON dunelight.payment_allocations USING btree (payment_id);

CREATE TABLE dunelight.checkout_audit_log (
    id uuid NOT NULL,
    checkout_id uuid NOT NULL,
    change_type character varying(30) NOT NULL,
    old_value text,
    new_value text,
    changed_at timestamp with time zone NOT NULL,
    changed_by uuid
);

ALTER TABLE ONLY dunelight.checkout_audit_log
    ADD CONSTRAINT pk_checkout_audit_log PRIMARY KEY (id);

CREATE INDEX ix_checkout_audit_log_checkout_id ON dunelight.checkout_audit_log USING btree (checkout_id);

CREATE TABLE dunelight.stock_movements (
    id uuid NOT NULL,
    organization_id uuid NOT NULL,
    product_id uuid NOT NULL,
    company_id uuid NOT NULL,
    type character varying(20) NOT NULL,
    quantity_delta integer NOT NULL,
    reason text,
    checkout_item_id uuid,
    related_company_id uuid,
    transfer_correlation_id uuid,
    created_at timestamp with time zone NOT NULL,
    created_by uuid
);

ALTER TABLE ONLY dunelight.stock_movements
    ADD CONSTRAINT pk_stock_movements PRIMARY KEY (id);

CREATE INDEX ix_stock_movements_org_product_created ON dunelight.stock_movements USING btree (organization_id, product_id, created_at);

CREATE UNIQUE INDEX ux_stock_movements_sale_checkout_item ON dunelight.stock_movements USING btree (checkout_item_id) WHERE ((type)::text = 'Sale'::text);

CREATE TABLE dunelight.commission_rules (
    id uuid NOT NULL,
    organization_id uuid NOT NULL,
    employee_id uuid NOT NULL,
    subject_type character varying(20) NOT NULL,
    service_id uuid,
    product_id uuid,
    package_id uuid,
    calculation_type character varying(20) NOT NULL,
    value numeric(10,2) NOT NULL,
    is_active boolean DEFAULT true NOT NULL,
    created_at timestamp with time zone NOT NULL,
    created_by uuid,
    updated_at timestamp with time zone,
    updated_by uuid,
    CONSTRAINT ck_commission_rules_subject CHECK (((((subject_type)::text = 'Service'::text) AND (service_id IS NOT NULL) AND (product_id IS NULL) AND (package_id IS NULL)) OR (((subject_type)::text = 'Product'::text) AND (product_id IS NOT NULL) AND (service_id IS NULL) AND (package_id IS NULL)) OR (((subject_type)::text = 'Package'::text) AND (package_id IS NOT NULL) AND (service_id IS NULL) AND (product_id IS NULL)))),
    CONSTRAINT ck_commission_rules_value CHECK (((value >= (0)::numeric) AND (((calculation_type)::text = 'Fixed'::text) OR (value <= (100)::numeric))))
);

ALTER TABLE ONLY dunelight.commission_rules
    ADD CONSTRAINT pk_commission_rules PRIMARY KEY (id);

CREATE INDEX ix_commission_rules_org_employee ON dunelight.commission_rules USING btree (organization_id, employee_id);

CREATE UNIQUE INDEX ux_commission_rules_employee_subject ON dunelight.commission_rules USING btree (organization_id, employee_id, subject_type, COALESCE(service_id, '00000000-0000-0000-0000-000000000000'::uuid), COALESCE(product_id, '00000000-0000-0000-0000-000000000000'::uuid), COALESCE(package_id, '00000000-0000-0000-0000-000000000000'::uuid)) WHERE (is_active = true);

CREATE TABLE dunelight.commission_entries (
    id uuid NOT NULL,
    organization_id uuid NOT NULL,
    employee_id uuid NOT NULL,
    company_id uuid NOT NULL,
    commission_rule_id uuid NOT NULL,
    source_type character varying(20) NOT NULL,
    booking_segment_participation_id uuid,
    appointment_segment_id uuid,
    checkout_item_id uuid,
    base_amount numeric(10,2) NOT NULL,
    calculation_type character varying(20) NOT NULL,
    rule_value numeric(10,2) NOT NULL,
    commission_amount numeric(10,2) NOT NULL,
    status character varying(20) NOT NULL,
    earned_at timestamp with time zone NOT NULL,
    created_at timestamp with time zone NOT NULL,
    source_version integer DEFAULT 0 NOT NULL,
    reversed_at timestamp with time zone,
    reversed_by uuid,
    CONSTRAINT ck_commission_entries_amount CHECK ((commission_amount >= (0)::numeric)),
    CONSTRAINT ck_commission_entries_source CHECK (((((source_type)::text = 'IndividualService'::text) AND (booking_segment_participation_id IS NOT NULL) AND (appointment_segment_id IS NULL) AND (checkout_item_id IS NULL)) OR (((source_type)::text = 'GroupService'::text) AND (appointment_segment_id IS NOT NULL) AND (booking_segment_participation_id IS NULL) AND (checkout_item_id IS NULL)) OR (((source_type)::text = 'ProductSale'::text) AND (checkout_item_id IS NOT NULL) AND (appointment_segment_id IS NULL) AND (booking_segment_participation_id IS NULL)) OR (((source_type)::text = 'PackageSale'::text) AND (checkout_item_id IS NOT NULL) AND (appointment_segment_id IS NULL) AND (booking_segment_participation_id IS NULL))))
);

ALTER TABLE ONLY dunelight.commission_entries
    ADD CONSTRAINT pk_commission_entries PRIMARY KEY (id);

CREATE INDEX ix_commission_entries_org_company_earned ON dunelight.commission_entries USING btree (organization_id, company_id, earned_at);

CREATE INDEX ix_commission_entries_org_employee_earned ON dunelight.commission_entries USING btree (organization_id, employee_id, earned_at);

CREATE UNIQUE INDEX ux_commission_entries_checkout_item_id ON dunelight.commission_entries USING btree (checkout_item_id) WHERE (checkout_item_id IS NOT NULL);

CREATE UNIQUE INDEX ux_commission_entries_participation_employee_source_version ON dunelight.commission_entries USING btree (booking_segment_participation_id, employee_id, source_version) WHERE (booking_segment_participation_id IS NOT NULL);

CREATE UNIQUE INDEX ux_commission_entries_group_segment_employee ON dunelight.commission_entries USING btree (appointment_segment_id, employee_id) WHERE ((source_type)::text = 'GroupService'::text);

ALTER TABLE ONLY dunelight.checkouts
    ADD CONSTRAINT fk_checkouts_client_id FOREIGN KEY (client_id) REFERENCES dunelight.clients(id);

ALTER TABLE ONLY dunelight.checkouts
    ADD CONSTRAINT fk_checkouts_company_id FOREIGN KEY (company_id) REFERENCES dunelight.companies(id);

ALTER TABLE ONLY dunelight.checkouts
    ADD CONSTRAINT fk_checkouts_organization_id FOREIGN KEY (organization_id) REFERENCES dunelight.organizations(id);

ALTER TABLE ONLY dunelight.checkout_items
    ADD CONSTRAINT fk_checkout_items_booking_segment_participation_id FOREIGN KEY (booking_segment_participation_id) REFERENCES dunelight.booking_segment_participations(id);

ALTER TABLE ONLY dunelight.checkout_items
    ADD CONSTRAINT fk_checkout_items_checkout_id FOREIGN KEY (checkout_id) REFERENCES dunelight.checkouts(id) ON DELETE CASCADE;

ALTER TABLE ONLY dunelight.checkout_items
    ADD CONSTRAINT fk_checkout_items_client_package_id FOREIGN KEY (client_package_id) REFERENCES dunelight.client_packages(id);

ALTER TABLE ONLY dunelight.checkout_items
    ADD CONSTRAINT fk_checkout_items_package_id FOREIGN KEY (package_id) REFERENCES dunelight.packages(id);

ALTER TABLE ONLY dunelight.checkout_items
    ADD CONSTRAINT fk_checkout_items_product_id FOREIGN KEY (product_id) REFERENCES dunelight.products(id);

ALTER TABLE ONLY dunelight.payments
    ADD CONSTRAINT fk_payments_checkout_id FOREIGN KEY (checkout_id) REFERENCES dunelight.checkouts(id) ON DELETE CASCADE;

ALTER TABLE ONLY dunelight.payments
    ADD CONSTRAINT fk_payments_organization_id FOREIGN KEY (organization_id) REFERENCES dunelight.organizations(id);

ALTER TABLE ONLY dunelight.payment_allocations
    ADD CONSTRAINT fk_payment_allocations_checkout_item_id FOREIGN KEY (checkout_item_id) REFERENCES dunelight.checkout_items(id);

ALTER TABLE ONLY dunelight.payment_allocations
    ADD CONSTRAINT fk_payment_allocations_payment_id FOREIGN KEY (payment_id) REFERENCES dunelight.payments(id) ON DELETE CASCADE;

ALTER TABLE ONLY dunelight.checkout_audit_log
    ADD CONSTRAINT fk_checkout_audit_log_checkout_id FOREIGN KEY (checkout_id) REFERENCES dunelight.checkouts(id) ON DELETE CASCADE;

ALTER TABLE ONLY dunelight.stock_movements
    ADD CONSTRAINT fk_stock_movements_checkout_item_id FOREIGN KEY (checkout_item_id) REFERENCES dunelight.checkout_items(id);

ALTER TABLE ONLY dunelight.stock_movements
    ADD CONSTRAINT fk_stock_movements_company_id FOREIGN KEY (company_id) REFERENCES dunelight.companies(id);

ALTER TABLE ONLY dunelight.stock_movements
    ADD CONSTRAINT fk_stock_movements_product_id FOREIGN KEY (product_id) REFERENCES dunelight.products(id);

ALTER TABLE ONLY dunelight.stock_movements
    ADD CONSTRAINT fk_stock_movements_related_company_id FOREIGN KEY (related_company_id) REFERENCES dunelight.companies(id);

ALTER TABLE ONLY dunelight.commission_rules
    ADD CONSTRAINT fk_commission_rules_employee_id FOREIGN KEY (employee_id) REFERENCES dunelight.employees(id);

ALTER TABLE ONLY dunelight.commission_rules
    ADD CONSTRAINT fk_commission_rules_organization_id FOREIGN KEY (organization_id) REFERENCES dunelight.organizations(id);

ALTER TABLE ONLY dunelight.commission_rules
    ADD CONSTRAINT fk_commission_rules_package_id FOREIGN KEY (package_id) REFERENCES dunelight.packages(id);

ALTER TABLE ONLY dunelight.commission_rules
    ADD CONSTRAINT fk_commission_rules_product_id FOREIGN KEY (product_id) REFERENCES dunelight.products(id);

ALTER TABLE ONLY dunelight.commission_rules
    ADD CONSTRAINT fk_commission_rules_service_id FOREIGN KEY (service_id) REFERENCES dunelight.services(id);

ALTER TABLE ONLY dunelight.commission_entries
    ADD CONSTRAINT fk_commission_entries_checkout_item_id FOREIGN KEY (checkout_item_id) REFERENCES dunelight.checkout_items(id);

ALTER TABLE ONLY dunelight.commission_entries
    ADD CONSTRAINT fk_commission_entries_commission_rule_id FOREIGN KEY (commission_rule_id) REFERENCES dunelight.commission_rules(id);

ALTER TABLE ONLY dunelight.commission_entries
    ADD CONSTRAINT fk_commission_entries_company_id FOREIGN KEY (company_id) REFERENCES dunelight.companies(id);

ALTER TABLE ONLY dunelight.commission_entries
    ADD CONSTRAINT fk_commission_entries_employee_id FOREIGN KEY (employee_id) REFERENCES dunelight.employees(id);

ALTER TABLE ONLY dunelight.commission_entries
    ADD CONSTRAINT fk_commission_entries_organization_id FOREIGN KEY (organization_id) REFERENCES dunelight.organizations(id);

ALTER TABLE ONLY dunelight.commission_entries
    ADD CONSTRAINT fk_commission_entries_booking_segment_participation_id FOREIGN KEY (booking_segment_participation_id) REFERENCES dunelight.booking_segment_participations(id) ON DELETE CASCADE;

ALTER TABLE ONLY dunelight.commission_entries
    ADD CONSTRAINT fk_commission_entries_appointment_segment_id FOREIGN KEY (appointment_segment_id) REFERENCES dunelight.appointment_segments(id) ON DELETE CASCADE;
""";
}
