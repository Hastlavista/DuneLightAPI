using System;
using BlueDragon.DuneLight.DatabaseMigration.Extensions;
using BlueDragon.DuneLight.DatabaseMigration.Models;
using FluentMigrator;

namespace BlueDragon.DuneLight.DatabaseMigration.Migrations.Baseline;

/// <summary>
/// Baseline 007 — AppointmentsAndBooking. Kreira KONAČNU strukturu odjednom (nema create->rename/alter povijesti, nema backfilla).
/// Tablice: appointments, appointment_segments, appointment_segment_employees, appointment_segment_resources, bookings, booking_segment_participations, waitlist_entries, appointment_audit_log.
/// </summary>
[DeveloperMigration(2026, 09, 29, Developer.SilvioHabazin, 7)]
public class Baseline007AppointmentsAndBooking : DuneLightMigration
{
    public override void Up()
    {
        Execute.Sql(Ddl);
    }

    private const string Ddl = """
CREATE TABLE dunelight.appointments (
    id uuid NOT NULL,
    organization_id uuid NOT NULL,
    company_id uuid NOT NULL,
    status character varying(20) NOT NULL,
    note text,
    group_id uuid,
    group_slot_id uuid,
    recurrence_group_id uuid,
    created_at timestamp with time zone NOT NULL,
    created_by uuid,
    updated_at timestamp with time zone,
    updated_by uuid,
    CONSTRAINT ck_appointments_status CHECK (((status)::text = ANY ((ARRAY['Scheduled'::character varying, 'Cancelled'::character varying, 'Closed'::character varying])::text[]))),
    CONSTRAINT ck_appointments_group_slot_requires_group CHECK (((group_slot_id IS NULL) OR (group_id IS NOT NULL)))
);

ALTER TABLE ONLY dunelight.appointments
    ADD CONSTRAINT pk_appointments PRIMARY KEY (id);

CREATE INDEX ix_appointments_org_company ON dunelight.appointments USING btree (organization_id, company_id);

CREATE INDEX ix_appointments_group_slot ON dunelight.appointments USING btree (group_slot_id) WHERE (group_slot_id IS NOT NULL);

CREATE INDEX ix_appointments_recurrence_group ON dunelight.appointments USING btree (recurrence_group_id) WHERE (recurrence_group_id IS NOT NULL);

CREATE TABLE dunelight.appointment_segments (
    id uuid NOT NULL,
    organization_id uuid NOT NULL,
    appointment_id uuid NOT NULL,
    service_id uuid NOT NULL,
    planned_start timestamp with time zone NOT NULL,
    planned_end timestamp with time zone NOT NULL,
    actual_start timestamp with time zone,
    actual_end timestamp with time zone,
    room_id uuid,
    created_at timestamp with time zone NOT NULL,
    created_by uuid,
    updated_at timestamp with time zone,
    updated_by uuid,
    CONSTRAINT ck_appointment_segments_planned CHECK ((planned_end > planned_start)),
    CONSTRAINT ck_appointment_segments_actual CHECK (((actual_end IS NULL) OR ((actual_start IS NOT NULL) AND (actual_end > actual_start))))
);

ALTER TABLE ONLY dunelight.appointment_segments
    ADD CONSTRAINT pk_appointment_segments PRIMARY KEY (id);

CREATE INDEX ix_appointment_segments_appointment ON dunelight.appointment_segments USING btree (appointment_id);

CREATE INDEX ix_appointment_segments_org_planned ON dunelight.appointment_segments USING btree (organization_id, planned_start, planned_end);

CREATE INDEX ix_appointment_segments_room_planned ON dunelight.appointment_segments USING btree (room_id, planned_start, planned_end) WHERE (room_id IS NOT NULL);

CREATE TABLE dunelight.appointment_segment_employees (
    appointment_segment_id uuid NOT NULL,
    employee_id uuid NOT NULL
);

ALTER TABLE ONLY dunelight.appointment_segment_employees
    ADD CONSTRAINT pk_appointment_segment_employees PRIMARY KEY (appointment_segment_id, employee_id);

CREATE INDEX ix_appointment_segment_employees_employee ON dunelight.appointment_segment_employees USING btree (employee_id);

CREATE TABLE dunelight.appointment_segment_resources (
    appointment_segment_id uuid NOT NULL,
    resource_id uuid NOT NULL,
    quantity_required integer NOT NULL,
    CONSTRAINT ck_appointment_segment_resources_quantity CHECK ((quantity_required > 0))
);

ALTER TABLE ONLY dunelight.appointment_segment_resources
    ADD CONSTRAINT pk_appointment_segment_resources PRIMARY KEY (appointment_segment_id, resource_id);

CREATE INDEX ix_appointment_segment_resources_resource ON dunelight.appointment_segment_resources USING btree (resource_id);

CREATE TABLE dunelight.bookings (
    id uuid NOT NULL,
    organization_id uuid NOT NULL,
    appointment_id uuid NOT NULL,
    client_id uuid NOT NULL,
    note text,
    created_at timestamp with time zone NOT NULL,
    created_by uuid,
    updated_at timestamp with time zone,
    updated_by uuid
);

ALTER TABLE ONLY dunelight.bookings
    ADD CONSTRAINT pk_bookings PRIMARY KEY (id);

CREATE UNIQUE INDEX ux_bookings_appointment_client ON dunelight.bookings USING btree (appointment_id, client_id);

CREATE INDEX ix_bookings_org_client ON dunelight.bookings USING btree (organization_id, client_id);

CREATE TABLE dunelight.booking_segment_participations (
    id uuid NOT NULL,
    organization_id uuid NOT NULL,
    booking_id uuid NOT NULL,
    appointment_segment_id uuid NOT NULL,
    status character varying(20) NOT NULL,
    status_version integer DEFAULT 0 NOT NULL,
    arrived_at timestamp with time zone,
    arrived_by uuid,
    cancellation_reason text,
    is_late_cancellation boolean,
    cancelled_at timestamp with time zone,
    cancelled_by uuid,
    suggested_price_snapshot numeric(10,2) NOT NULL,
    final_price_snapshot numeric(10,2) NOT NULL,
    is_price_manually_overridden boolean DEFAULT false NOT NULL,
    pricing_source_type character varying(30),
    pricing_source_id uuid,
    pricing_adjustment_type character varying(30),
    pricing_adjustment_value numeric(10,2),
    created_at timestamp with time zone NOT NULL,
    created_by uuid,
    updated_at timestamp with time zone,
    updated_by uuid,
    CONSTRAINT ck_booking_segment_participations_status CHECK (((status)::text = ANY ((ARRAY['Confirmed'::character varying, 'Completed'::character varying, 'Cancelled'::character varying, 'NoShow'::character varying])::text[]))),
    CONSTRAINT ck_booking_segment_participations_status_version CHECK ((status_version >= 0)),
    CONSTRAINT ck_booking_segment_participations_prices CHECK (((suggested_price_snapshot >= (0)::numeric) AND (final_price_snapshot >= (0)::numeric))),
    CONSTRAINT ck_booking_segment_participations_pricing_source CHECK (((pricing_source_id IS NULL) OR (pricing_source_type IS NOT NULL)))
);

ALTER TABLE ONLY dunelight.booking_segment_participations
    ADD CONSTRAINT pk_booking_segment_participations PRIMARY KEY (id);

CREATE UNIQUE INDEX ux_booking_segment_participations_booking_segment ON dunelight.booking_segment_participations USING btree (booking_id, appointment_segment_id);

CREATE INDEX ix_booking_segment_participations_segment ON dunelight.booking_segment_participations USING btree (appointment_segment_id);

CREATE INDEX ix_booking_segment_participations_org_status ON dunelight.booking_segment_participations USING btree (organization_id, status);

CREATE TABLE dunelight.waitlist_entries (
    id uuid NOT NULL,
    organization_id uuid NOT NULL,
    appointment_id uuid NOT NULL,
    client_id uuid NOT NULL,
    status character varying(20) NOT NULL,
    joined_at timestamp with time zone NOT NULL,
    promoted_at timestamp with time zone,
    promoted_booking_id uuid,
    cancelled_at timestamp with time zone,
    expired_reason character varying(60),
    created_at timestamp with time zone NOT NULL,
    updated_at timestamp with time zone
);

ALTER TABLE ONLY dunelight.waitlist_entries
    ADD CONSTRAINT pk_waitlist_entries PRIMARY KEY (id);

CREATE INDEX ix_waitlist_entries_appointment_status_joined ON dunelight.waitlist_entries USING btree (appointment_id, status, joined_at);

CREATE INDEX ix_waitlist_entries_org_client ON dunelight.waitlist_entries USING btree (organization_id, client_id);

CREATE UNIQUE INDEX ux_waitlist_entries_appointment_client_waiting ON dunelight.waitlist_entries USING btree (appointment_id, client_id) WHERE ((status)::text = 'Waiting'::text);

CREATE TABLE dunelight.appointment_audit_log (
    id uuid NOT NULL,
    appointment_id uuid NOT NULL,
    change_type character varying(30) NOT NULL,
    old_value character varying(255),
    new_value character varying(255),
    changed_at timestamp with time zone NOT NULL,
    changed_by uuid,
    booking_id uuid,
    waitlist_entry_id uuid,
    status_version integer,
    booking_segment_participation_id uuid
);

ALTER TABLE ONLY dunelight.appointment_audit_log
    ADD CONSTRAINT pk_appointment_audit_log PRIMARY KEY (id);

CREATE INDEX ix_appointment_audit_log_appointment_id ON dunelight.appointment_audit_log USING btree (appointment_id);

CREATE INDEX ix_appointment_audit_log_booking_id ON dunelight.appointment_audit_log USING btree (booking_id);

CREATE INDEX ix_appointment_audit_log_waitlist_entry_id ON dunelight.appointment_audit_log USING btree (waitlist_entry_id);

ALTER TABLE ONLY dunelight.appointments
    ADD CONSTRAINT fk_appointments_organization_id FOREIGN KEY (organization_id) REFERENCES dunelight.organizations(id);

ALTER TABLE ONLY dunelight.appointments
    ADD CONSTRAINT fk_appointments_company_id FOREIGN KEY (company_id) REFERENCES dunelight.companies(id);

ALTER TABLE ONLY dunelight.appointments
    ADD CONSTRAINT fk_appointments_group_id FOREIGN KEY (group_id) REFERENCES dunelight.groups(id);

ALTER TABLE ONLY dunelight.appointments
    ADD CONSTRAINT fk_appointments_group_slot_id FOREIGN KEY (group_slot_id) REFERENCES dunelight.group_slots(id);

ALTER TABLE ONLY dunelight.appointment_segments
    ADD CONSTRAINT fk_appointment_segments_organization_id FOREIGN KEY (organization_id) REFERENCES dunelight.organizations(id);

ALTER TABLE ONLY dunelight.appointment_segments
    ADD CONSTRAINT fk_appointment_segments_appointment_id FOREIGN KEY (appointment_id) REFERENCES dunelight.appointments(id) ON DELETE CASCADE;

ALTER TABLE ONLY dunelight.appointment_segments
    ADD CONSTRAINT fk_appointment_segments_service_id FOREIGN KEY (service_id) REFERENCES dunelight.services(id);

ALTER TABLE ONLY dunelight.appointment_segments
    ADD CONSTRAINT fk_appointment_segments_room_id FOREIGN KEY (room_id) REFERENCES dunelight.rooms(id);

ALTER TABLE ONLY dunelight.appointment_segment_employees
    ADD CONSTRAINT fk_appointment_segment_employees_appointment_segment_id FOREIGN KEY (appointment_segment_id) REFERENCES dunelight.appointment_segments(id) ON DELETE CASCADE;

ALTER TABLE ONLY dunelight.appointment_segment_employees
    ADD CONSTRAINT fk_appointment_segment_employees_employee_id FOREIGN KEY (employee_id) REFERENCES dunelight.employees(id);

ALTER TABLE ONLY dunelight.appointment_segment_resources
    ADD CONSTRAINT fk_appointment_segment_resources_appointment_segment_id FOREIGN KEY (appointment_segment_id) REFERENCES dunelight.appointment_segments(id) ON DELETE CASCADE;

ALTER TABLE ONLY dunelight.appointment_segment_resources
    ADD CONSTRAINT fk_appointment_segment_resources_resource_id FOREIGN KEY (resource_id) REFERENCES dunelight.resources(id);

ALTER TABLE ONLY dunelight.bookings
    ADD CONSTRAINT fk_bookings_organization_id FOREIGN KEY (organization_id) REFERENCES dunelight.organizations(id);

ALTER TABLE ONLY dunelight.bookings
    ADD CONSTRAINT fk_bookings_appointment_id FOREIGN KEY (appointment_id) REFERENCES dunelight.appointments(id) ON DELETE CASCADE;

ALTER TABLE ONLY dunelight.bookings
    ADD CONSTRAINT fk_bookings_client_id FOREIGN KEY (client_id) REFERENCES dunelight.clients(id);

ALTER TABLE ONLY dunelight.booking_segment_participations
    ADD CONSTRAINT fk_booking_segment_participations_organization_id FOREIGN KEY (organization_id) REFERENCES dunelight.organizations(id);

ALTER TABLE ONLY dunelight.booking_segment_participations
    ADD CONSTRAINT fk_booking_segment_participations_booking_id FOREIGN KEY (booking_id) REFERENCES dunelight.bookings(id) ON DELETE CASCADE;

ALTER TABLE ONLY dunelight.booking_segment_participations
    ADD CONSTRAINT fk_booking_segment_participations_appointment_segment_id FOREIGN KEY (appointment_segment_id) REFERENCES dunelight.appointment_segments(id) ON DELETE CASCADE;

ALTER TABLE ONLY dunelight.waitlist_entries
    ADD CONSTRAINT fk_waitlist_entries_appointment_id FOREIGN KEY (appointment_id) REFERENCES dunelight.appointments(id) ON DELETE CASCADE;

ALTER TABLE ONLY dunelight.waitlist_entries
    ADD CONSTRAINT fk_waitlist_entries_client_id FOREIGN KEY (client_id) REFERENCES dunelight.clients(id);

ALTER TABLE ONLY dunelight.waitlist_entries
    ADD CONSTRAINT fk_waitlist_entries_organization_id FOREIGN KEY (organization_id) REFERENCES dunelight.organizations(id);

ALTER TABLE ONLY dunelight.waitlist_entries
    ADD CONSTRAINT fk_waitlist_entries_promoted_booking_id FOREIGN KEY (promoted_booking_id) REFERENCES dunelight.bookings(id);

ALTER TABLE ONLY dunelight.appointment_audit_log
    ADD CONSTRAINT fk_appointment_audit_log_appointment_id FOREIGN KEY (appointment_id) REFERENCES dunelight.appointments(id) ON DELETE CASCADE;

ALTER TABLE ONLY dunelight.appointment_audit_log
    ADD CONSTRAINT fk_appointment_audit_log_booking_id FOREIGN KEY (booking_id) REFERENCES dunelight.bookings(id) ON DELETE CASCADE;

ALTER TABLE ONLY dunelight.appointment_audit_log
    ADD CONSTRAINT fk_appointment_audit_log_waitlist_entry_id FOREIGN KEY (waitlist_entry_id) REFERENCES dunelight.waitlist_entries(id) ON DELETE CASCADE;

ALTER TABLE ONLY dunelight.appointment_audit_log
    ADD CONSTRAINT fk_appointment_audit_log_booking_segment_participation_id FOREIGN KEY (booking_segment_participation_id) REFERENCES dunelight.booking_segment_participations(id) ON DELETE CASCADE;
""";
}
