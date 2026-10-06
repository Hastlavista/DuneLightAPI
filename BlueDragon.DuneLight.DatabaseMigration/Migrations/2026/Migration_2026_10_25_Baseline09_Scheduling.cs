using BlueDragon.DuneLight.DatabaseMigration.Extensions;
using BlueDragon.DuneLight.DatabaseMigration.Models;

namespace BlueDragon.DuneLight.DatabaseMigration.Migrations._2026;

/// <summary>
/// Početna migracija (ADR-0022), korak 9/11: termini, segmenti, bookingi, participationi, lista čekanja i pauze.
/// Gradi isključivo trenutnu ciljnu shemu; ne seeda podatke.
/// </summary>
[DeveloperMigration(2026, 10, 25, Developer.SilvioHabazin, 9)]
public class Baseline09Scheduling : DuneLightMigration
{
    public override void Up()
    {
        // appointments
        Execute.Sql(@"
            CREATE TABLE dunelight.appointments (
                id uuid NOT NULL,
                organization_id uuid NOT NULL,
                form character varying(20) NOT NULL,
                company_id uuid CONSTRAINT appointments_location_id_not_null NOT NULL,
                status character varying(20) NOT NULL,
                note text,
                group_id uuid,
                recurrence_group_id uuid,
                created_at timestamp with time zone NOT NULL,
                created_by uuid,
                updated_at timestamp with time zone,
                updated_by uuid,
                group_slot_id uuid,
                cancellation_reason character varying(500),
                cancelled_at timestamp with time zone,
                cancelled_by uuid,
                closed_out_at timestamp with time zone,
                closed_out_by uuid,
                CONSTRAINT ck_appointments_cancelled_by CHECK (((cancelled_by IS NULL) OR (cancelled_at IS NOT NULL))),
                CONSTRAINT ck_appointments_closed_out CHECK ((((closed_out_by IS NULL) OR (closed_out_at IS NOT NULL)) AND ((closed_out_at IS NULL) OR ((form)::text = 'Group'::text)))),
                CONSTRAINT ck_appointments_status CHECK ((status IN ('Scheduled', 'Cancelled', 'Closed')))
            );

            ALTER TABLE dunelight.appointments ADD CONSTRAINT pk_appointments PRIMARY KEY (id);

            ALTER TABLE dunelight.appointments ADD CONSTRAINT fk_appointments_company_id FOREIGN KEY (company_id) REFERENCES dunelight.companies(id);

            ALTER TABLE dunelight.appointments ADD CONSTRAINT fk_appointments_group_id FOREIGN KEY (group_id) REFERENCES dunelight.groups(id);

            ALTER TABLE dunelight.appointments ADD CONSTRAINT fk_appointments_group_slot_id FOREIGN KEY (group_slot_id) REFERENCES dunelight.group_slots(id);

            ALTER TABLE dunelight.appointments ADD CONSTRAINT fk_appointments_organization_id FOREIGN KEY (organization_id) REFERENCES dunelight.organizations(id);");

        // appointment_segments
        Execute.Sql(@"
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
                updated_at timestamp with time zone,
                group_segment_template_id uuid,
                pricing_mode character varying(32) NOT NULL,
                pricing_employee_id uuid,
                CONSTRAINT ck_appointment_segments_actual_range CHECK (((actual_end IS NULL) OR ((actual_start IS NOT NULL) AND (actual_end >= actual_start)))),
                CONSTRAINT ck_appointment_segments_planned_range CHECK ((planned_end > planned_start)),
                CONSTRAINT ck_appointment_segments_pricing_source CHECK (((((pricing_mode)::text = 'Standard'::text) AND (pricing_employee_id IS NULL)) OR (((pricing_mode)::text = 'Employee'::text) AND (pricing_employee_id IS NOT NULL))))
            );

            ALTER TABLE dunelight.appointment_segments ADD CONSTRAINT pk_appointment_segments PRIMARY KEY (id);

            CREATE INDEX ix_appointment_segments_appointment_id ON dunelight.appointment_segments USING btree (appointment_id);

            CREATE INDEX ix_appointment_segments_organization_planned ON dunelight.appointment_segments USING btree (organization_id, planned_start, planned_end);

            CREATE INDEX ix_appointment_segments_room_planned ON dunelight.appointment_segments USING btree (room_id, planned_start, planned_end);

            CREATE INDEX ix_appointment_segments_service_id ON dunelight.appointment_segments USING btree (service_id);

            CREATE UNIQUE INDEX ux_appointment_segments_appointment_template ON dunelight.appointment_segments USING btree (appointment_id, group_segment_template_id) WHERE (group_segment_template_id IS NOT NULL);

            ALTER TABLE dunelight.appointment_segments ADD CONSTRAINT fk_appointment_segments_appointment_id FOREIGN KEY (appointment_id) REFERENCES dunelight.appointments(id) ON DELETE CASCADE;

            ALTER TABLE dunelight.appointment_segments ADD CONSTRAINT fk_appointment_segments_group_segment_template_id FOREIGN KEY (group_segment_template_id) REFERENCES dunelight.group_segment_templates(id) ON DELETE RESTRICT;

            ALTER TABLE dunelight.appointment_segments ADD CONSTRAINT fk_appointment_segments_organization_id FOREIGN KEY (organization_id) REFERENCES dunelight.organizations(id);

            ALTER TABLE dunelight.appointment_segments ADD CONSTRAINT fk_appointment_segments_pricing_employee_id FOREIGN KEY (pricing_employee_id) REFERENCES dunelight.employees(id) ON DELETE RESTRICT;

            ALTER TABLE dunelight.appointment_segments ADD CONSTRAINT fk_appointment_segments_room_id FOREIGN KEY (room_id) REFERENCES dunelight.rooms(id);

            ALTER TABLE dunelight.appointment_segments ADD CONSTRAINT fk_appointment_segments_service_id FOREIGN KEY (service_id) REFERENCES dunelight.services(id);");

        // appointment_segment_employees
        Execute.Sql(@"
            CREATE TABLE dunelight.appointment_segment_employees (
                appointment_segment_id uuid NOT NULL,
                employee_id uuid NOT NULL
            );

            ALTER TABLE dunelight.appointment_segment_employees ADD CONSTRAINT pk_appointment_segment_employees PRIMARY KEY (appointment_segment_id, employee_id);

            CREATE INDEX ix_appointment_segment_employees_employee_id ON dunelight.appointment_segment_employees USING btree (employee_id);

            ALTER TABLE dunelight.appointment_segment_employees ADD CONSTRAINT fk_appointment_segment_employees_appointment_segment_id FOREIGN KEY (appointment_segment_id) REFERENCES dunelight.appointment_segments(id) ON DELETE CASCADE;

            ALTER TABLE dunelight.appointment_segment_employees ADD CONSTRAINT fk_appointment_segment_employees_employee_id FOREIGN KEY (employee_id) REFERENCES dunelight.employees(id);");

        // appointment_segment_resources
        Execute.Sql(@"
            CREATE TABLE dunelight.appointment_segment_resources (
                appointment_segment_id uuid NOT NULL,
                resource_id uuid NOT NULL,
                quantity_required integer NOT NULL,
                CONSTRAINT ck_appointment_segment_resources_quantity_positive CHECK ((quantity_required > 0))
            );

            ALTER TABLE dunelight.appointment_segment_resources ADD CONSTRAINT pk_appointment_segment_resources PRIMARY KEY (appointment_segment_id, resource_id);

            CREATE INDEX ix_appointment_segment_resources_resource_id ON dunelight.appointment_segment_resources USING btree (resource_id);

            ALTER TABLE dunelight.appointment_segment_resources ADD CONSTRAINT fk_appointment_segment_resources_appointment_segment_id FOREIGN KEY (appointment_segment_id) REFERENCES dunelight.appointment_segments(id) ON DELETE CASCADE;

            ALTER TABLE dunelight.appointment_segment_resources ADD CONSTRAINT fk_appointment_segment_resources_resource_id FOREIGN KEY (resource_id) REFERENCES dunelight.resources(id);");

        // bookings
        Execute.Sql(@"
            CREATE TABLE dunelight.bookings (
                id uuid NOT NULL,
                organization_id uuid NOT NULL,
                appointment_id uuid NOT NULL,
                client_id uuid NOT NULL,
                note text,
                created_at timestamp with time zone NOT NULL,
                updated_at timestamp with time zone,
                updated_by uuid
            );

            ALTER TABLE dunelight.bookings ADD CONSTRAINT pk_bookings PRIMARY KEY (id);

            CREATE INDEX ix_bookings_org_client ON dunelight.bookings USING btree (organization_id, client_id);

            CREATE UNIQUE INDEX ux_bookings_appointment_client ON dunelight.bookings USING btree (appointment_id, client_id);

            ALTER TABLE dunelight.bookings ADD CONSTRAINT fk_bookings_appointment_id FOREIGN KEY (appointment_id) REFERENCES dunelight.appointments(id) ON DELETE CASCADE;

            ALTER TABLE dunelight.bookings ADD CONSTRAINT fk_bookings_client_id FOREIGN KEY (client_id) REFERENCES dunelight.clients(id);

            ALTER TABLE dunelight.bookings ADD CONSTRAINT fk_bookings_organization_id FOREIGN KEY (organization_id) REFERENCES dunelight.organizations(id);");

        // booking_segment_participations
        Execute.Sql(@"
            CREATE TABLE dunelight.booking_segment_participations (
                id uuid NOT NULL,
                organization_id uuid NOT NULL,
                booking_id uuid NOT NULL,
                appointment_segment_id uuid NOT NULL,
                status character varying(20) NOT NULL,
                status_version integer NOT NULL,
                arrived_at timestamp with time zone,
                arrived_by uuid,
                cancellation_reason text,
                is_late_cancellation boolean,
                base_amount numeric(10,2),
                base_amount_source character varying(32),
                adjustment_amount numeric(10,2),
                suggested_amount numeric(10,2) NOT NULL,
                amount numeric(10,2) NOT NULL,
                is_amount_manually_overridden boolean CONSTRAINT booking_segment_participati_is_amount_manually_overrid_not_null NOT NULL,
                created_at timestamp with time zone NOT NULL,
                updated_at timestamp with time zone,
                pricing_mode character varying(32),
                pricing_employee_id uuid,
                CONSTRAINT ck_booking_segment_participations_amounts_non_negative CHECK (((base_amount >= (0)::numeric) AND (suggested_amount >= (0)::numeric) AND (amount >= (0)::numeric))),
                CONSTRAINT ck_booking_segment_participations_arrival CHECK (((arrived_by IS NULL) OR (arrived_at IS NOT NULL))),
                CONSTRAINT ck_booking_segment_participations_base_amount_source CHECK ((base_amount_source IN ('EmployeeCompanySpecific', 'EmployeeAllCompanies', 'CompanySpecific', 'AllCompanies', 'Default'))),
                CONSTRAINT ck_booking_segment_participations_pricing_source CHECK ((((pricing_mode IS NULL) AND (pricing_employee_id IS NULL) AND (base_amount IS NULL)) OR (((pricing_mode)::text = 'Standard'::text) AND (pricing_employee_id IS NULL) AND (base_amount IS NOT NULL)) OR (((pricing_mode)::text = 'Employee'::text) AND (pricing_employee_id IS NOT NULL) AND (base_amount IS NOT NULL)))),
                CONSTRAINT ck_booking_segment_participations_status CHECK ((status IN ('Confirmed', 'Completed', 'Cancelled', 'NoShow'))),
                CONSTRAINT ck_booking_segment_participations_status_version CHECK ((status_version >= 0))
            );

            ALTER TABLE dunelight.booking_segment_participations ADD CONSTRAINT pk_booking_segment_participations PRIMARY KEY (id);

            CREATE INDEX ix_booking_segment_participations_appointment_segment_id ON dunelight.booking_segment_participations USING btree (appointment_segment_id);

            CREATE UNIQUE INDEX ux_booking_segment_participations_booking_segment ON dunelight.booking_segment_participations USING btree (booking_id, appointment_segment_id);

            CREATE UNIQUE INDEX ux_booking_segment_participations_id_booking_organization ON dunelight.booking_segment_participations USING btree (id, booking_id, organization_id);

            CREATE UNIQUE INDEX ux_booking_segment_participations_id_organization ON dunelight.booking_segment_participations USING btree (id, organization_id);

            ALTER TABLE dunelight.booking_segment_participations ADD CONSTRAINT fk_booking_segment_participations_appointment_segment_id FOREIGN KEY (appointment_segment_id) REFERENCES dunelight.appointment_segments(id);

            ALTER TABLE dunelight.booking_segment_participations ADD CONSTRAINT fk_booking_segment_participations_booking_id FOREIGN KEY (booking_id) REFERENCES dunelight.bookings(id);

            ALTER TABLE dunelight.booking_segment_participations ADD CONSTRAINT fk_booking_segment_participations_organization_id FOREIGN KEY (organization_id) REFERENCES dunelight.organizations(id);

            ALTER TABLE dunelight.booking_segment_participations ADD CONSTRAINT fk_booking_segment_participations_pricing_employee_id FOREIGN KEY (pricing_employee_id) REFERENCES dunelight.employees(id) ON DELETE RESTRICT;");

        // waitlist_entries
        Execute.Sql(@"
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
                updated_at timestamp with time zone,
                appointment_segment_id uuid NOT NULL
            );

            ALTER TABLE dunelight.waitlist_entries ADD CONSTRAINT pk_waitlist_entries PRIMARY KEY (id);

            CREATE INDEX ix_waitlist_entries_appointment_status_joined ON dunelight.waitlist_entries USING btree (appointment_id, status, joined_at);

            CREATE INDEX ix_waitlist_entries_org_client ON dunelight.waitlist_entries USING btree (organization_id, client_id);

            CREATE INDEX ix_waitlist_entries_segment_status_joined ON dunelight.waitlist_entries USING btree (appointment_segment_id, status, joined_at);

            CREATE UNIQUE INDEX ux_waitlist_entries_segment_client_waiting ON dunelight.waitlist_entries USING btree (appointment_segment_id, client_id) WHERE ((status)::text = 'Waiting'::text);

            ALTER TABLE dunelight.waitlist_entries ADD CONSTRAINT fk_waitlist_entries_appointment_id FOREIGN KEY (appointment_id) REFERENCES dunelight.appointments(id) ON DELETE CASCADE;

            ALTER TABLE dunelight.waitlist_entries ADD CONSTRAINT fk_waitlist_entries_appointment_segment_id FOREIGN KEY (appointment_segment_id) REFERENCES dunelight.appointment_segments(id) ON DELETE CASCADE;

            ALTER TABLE dunelight.waitlist_entries ADD CONSTRAINT fk_waitlist_entries_client_id FOREIGN KEY (client_id) REFERENCES dunelight.clients(id);

            ALTER TABLE dunelight.waitlist_entries ADD CONSTRAINT fk_waitlist_entries_organization_id FOREIGN KEY (organization_id) REFERENCES dunelight.organizations(id);

            ALTER TABLE dunelight.waitlist_entries ADD CONSTRAINT fk_waitlist_entries_promoted_booking_id FOREIGN KEY (promoted_booking_id) REFERENCES dunelight.bookings(id);");

        // appointment_audit_log
        Execute.Sql(@"
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

            ALTER TABLE dunelight.appointment_audit_log ADD CONSTRAINT pk_appointment_audit_log PRIMARY KEY (id);

            CREATE INDEX ix_appointment_audit_log_appointment_id ON dunelight.appointment_audit_log USING btree (appointment_id);

            CREATE INDEX ix_appointment_audit_log_booking_id ON dunelight.appointment_audit_log USING btree (booking_id);

            CREATE INDEX ix_appointment_audit_log_participation_id ON dunelight.appointment_audit_log USING btree (booking_segment_participation_id);

            CREATE INDEX ix_appointment_audit_log_waitlist_entry_id ON dunelight.appointment_audit_log USING btree (waitlist_entry_id);

            ALTER TABLE dunelight.appointment_audit_log ADD CONSTRAINT fk_appointment_audit_log_appointment_id FOREIGN KEY (appointment_id) REFERENCES dunelight.appointments(id) ON DELETE CASCADE;

            ALTER TABLE dunelight.appointment_audit_log ADD CONSTRAINT fk_appointment_audit_log_booking_id FOREIGN KEY (booking_id) REFERENCES dunelight.bookings(id) ON DELETE CASCADE;

            ALTER TABLE dunelight.appointment_audit_log ADD CONSTRAINT fk_appointment_audit_log_participation FOREIGN KEY (booking_segment_participation_id) REFERENCES dunelight.booking_segment_participations(id) ON DELETE CASCADE;

            ALTER TABLE dunelight.appointment_audit_log ADD CONSTRAINT fk_appointment_audit_log_waitlist_entry_id FOREIGN KEY (waitlist_entry_id) REFERENCES dunelight.waitlist_entries(id) ON DELETE CASCADE;");

        // schedule_breaks
        Execute.Sql(@"
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

            ALTER TABLE dunelight.schedule_breaks ADD CONSTRAINT pk_schedule_breaks PRIMARY KEY (id);

            CREATE INDEX ix_schedule_breaks_org_company_startsat ON dunelight.schedule_breaks USING btree (organization_id, company_id, starts_at);

            CREATE INDEX ix_schedule_breaks_org_employee_startsat ON dunelight.schedule_breaks USING btree (organization_id, employee_id, starts_at);

            ALTER TABLE dunelight.schedule_breaks ADD CONSTRAINT fk_schedule_breaks_company_id FOREIGN KEY (company_id) REFERENCES dunelight.companies(id);

            ALTER TABLE dunelight.schedule_breaks ADD CONSTRAINT fk_schedule_breaks_employee_id FOREIGN KEY (employee_id) REFERENCES dunelight.employees(id);

            ALTER TABLE dunelight.schedule_breaks ADD CONSTRAINT fk_schedule_breaks_organization_id FOREIGN KEY (organization_id) REFERENCES dunelight.organizations(id);");
    }
}
