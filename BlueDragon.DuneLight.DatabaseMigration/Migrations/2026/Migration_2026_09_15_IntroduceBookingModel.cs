using BlueDragon.DuneLight.DatabaseMigration.Extensions;
using BlueDragon.DuneLight.DatabaseMigration.Models;

namespace BlueDragon.DuneLight.DatabaseMigration.Migrations._2026;

/// <summary>
/// Uvodi Booking kao prvorazredni Klijent↔Appointment entitet, zamjenjujući appointment_clients
/// (individualni termini) i appointment_attendances (grupni termini) — dvije paralelne, djelomično
/// redundantne strukture za istu stvar. Vidi Booking.cs (Infrastructure) za punu domensku napomenu.
///
/// Appointment.Status gubi NoShow (termin kao okvir/resurs nikad nije "izostao", samo pojedini Booking na
/// njemu) — postojeći Appointment redci sa status='NoShow' prelaze na 'Cancelled' (Migration 2/3 niže), dok
/// njihov konkretan Booking ispravno nasljeđuje 'NoShow' iz izvornog appointment_clients retka.
/// </summary>
[DeveloperMigration(2026, 09, 15, Developer.SilvioHabazin, 8)]
public class CreateBookingsTable : DuneLightMigration
{
    public override void Up()
    {
        Create.Table(Tables.Bookings)
            .InSchema(Tables.Schemas.DuneLight)
            .WithColumn("id").AsGuid().NotNullable().PrimaryKey("pk_bookings")
            .WithColumn("organization_id").AsGuid().NotNullable()
            .WithColumn("appointment_id").AsGuid().NotNullable()
            .WithColumn("client_id").AsGuid().NotNullable()
            .WithColumn("status").AsString(20).NotNullable()
            .WithColumn("client_package_id").AsGuid().Nullable()
            .WithColumn("coverage_type").AsString(20).Nullable()
            .WithColumn("package_entry_deducted").AsBoolean().NotNullable().WithDefaultValue(false)
            .WithColumn("package_entry_returned").AsBoolean().NotNullable().WithDefaultValue(false)
            .WithColumn("package_entry_returned_at").AsDateTimeOffset().Nullable()
            .WithColumn("package_entry_returned_by").AsGuid().Nullable()
            .WithColumn("note").AsCustom("text").Nullable()
            .WithColumn("cancellation_reason").AsCustom("text").Nullable()
            .WithColumn("created_at").AsDateTimeOffset().NotNullable()
            .WithColumn("updated_at").AsDateTimeOffset().Nullable()
            .WithColumn("updated_by").AsGuid().Nullable();

        Create.ForeignKey("fk_bookings_organization_id")
            .FromTable(Tables.Bookings).InSchema(Tables.Schemas.DuneLight).ForeignColumn("organization_id")
            .ToTable(Tables.Organizations).InSchema(Tables.Schemas.DuneLight).PrimaryColumn("id");

        Create.ForeignKey("fk_bookings_appointment_id")
            .FromTable(Tables.Bookings).InSchema(Tables.Schemas.DuneLight).ForeignColumn("appointment_id")
            .ToTable(Tables.Appointments).InSchema(Tables.Schemas.DuneLight).PrimaryColumn("id")
            .OnDelete(System.Data.Rule.Cascade);

        Create.ForeignKey("fk_bookings_client_id")
            .FromTable(Tables.Bookings).InSchema(Tables.Schemas.DuneLight).ForeignColumn("client_id")
            .ToTable(Tables.Clients).InSchema(Tables.Schemas.DuneLight).PrimaryColumn("id");

        Create.ForeignKey("fk_bookings_client_package_id")
            .FromTable(Tables.Bookings).InSchema(Tables.Schemas.DuneLight).ForeignColumn("client_package_id")
            .ToTable(Tables.ClientPackages).InSchema(Tables.Schemas.DuneLight).PrimaryColumn("id");

        Create.Index("ux_bookings_appointment_client")
            .OnTable(Tables.Bookings).InSchema(Tables.Schemas.DuneLight)
            .OnColumn("appointment_id").Ascending()
            .OnColumn("client_id").Ascending()
            .WithOptions().Unique();

        Create.Index("ix_bookings_org_client")
            .OnTable(Tables.Bookings).InSchema(Tables.Schemas.DuneLight)
            .OnColumn("organization_id").Ascending()
            .OnColumn("client_id").Ascending();
    }
}

/// <summary>
/// Prebacuje postojeće podatke iz appointment_clients/appointment_attendances u bookings (ID redaka se
/// PREUZIMA 1:1 — appointment_clients.id/appointment_attendances.id postaju bookings.id — nema potrebe za
/// mapping tablicom, a i eventualne postojeće FK/audit reference po starom ID-u ostaju smislene). Status
/// mapiranje: appointment_clients nasljeđuje status iz roditeljskog Appointment.status u trenutku migracije
/// (Scheduled→Confirmed, Completed→Completed, Cancelled→Cancelled, NoShow→NoShow — Appointment.status se
/// popravlja NoShow→Cancelled tek NAKON ovog inserta, vidi FixAppointmentStatusAfterBookingMigration ispod).
/// appointment_attendances nasljeđuje status iz attended (true→Completed, false→NoShow, NULL→Confirmed).
/// </summary>
[DeveloperMigration(2026, 09, 15, Developer.SilvioHabazin, 9)]
public class MigrateAppointmentClientsAndAttendancesToBookings : DuneLightMigration
{
    public override void Up()
    {
        Execute.Sql(@"
            INSERT INTO dunelight.bookings
                (id, organization_id, appointment_id, client_id, status, client_package_id, coverage_type,
                 package_entry_deducted, package_entry_returned, package_entry_returned_at, package_entry_returned_by,
                 note, cancellation_reason, created_at, updated_at, updated_by)
            SELECT
                ac.id, a.organization_id, ac.appointment_id, ac.client_id,
                CASE a.status
                    WHEN 'Scheduled' THEN 'Confirmed'
                    WHEN 'Completed' THEN 'Completed'
                    WHEN 'Cancelled' THEN 'Cancelled'
                    WHEN 'NoShow' THEN 'NoShow'
                    ELSE 'Confirmed'
                END,
                ac.client_package_id, NULL,
                ac.package_entry_deducted, ac.package_entry_returned, ac.package_entry_returned_at, ac.package_entry_returned_by,
                NULL, NULL, ac.created_at, NULL, NULL
            FROM dunelight.appointment_clients ac
            JOIN dunelight.appointments a ON a.id = ac.appointment_id;");

        Execute.Sql(@"
            INSERT INTO dunelight.bookings
                (id, organization_id, appointment_id, client_id, status, client_package_id, coverage_type,
                 package_entry_deducted, package_entry_returned, package_entry_returned_at, package_entry_returned_by,
                 note, cancellation_reason, created_at, updated_at, updated_by)
            SELECT
                aa.id, a.organization_id, aa.appointment_id, aa.client_id,
                CASE WHEN aa.attended = true THEN 'Completed' WHEN aa.attended = false THEN 'NoShow' ELSE 'Confirmed' END,
                aa.client_package_id, aa.coverage_type,
                aa.package_entry_deducted, aa.package_entry_returned, aa.package_entry_returned_at, aa.package_entry_returned_by,
                aa.note, NULL, aa.created_at, NULL, NULL
            FROM dunelight.appointment_attendances aa
            JOIN dunelight.appointments a ON a.id = aa.appointment_id;");
    }
}

/// <summary>Appointment.Status gubi NoShow (vidi AppointmentStatus.cs) — jedina rupa preostala nakon
/// migracije podataka gore je sam Appointment.status='NoShow' (Booking retci su već ispravno preuzeli
/// NoShow gore). Occurrence koji je bio no-showan tretira se ubuduće kao zatvoren/Cancelled na razini
/// termina, dok konkretan Booking i dalje ispravno pokazuje NoShow.</summary>
[DeveloperMigration(2026, 09, 15, Developer.SilvioHabazin, 10)]
public class FixAppointmentStatusAfterBookingMigration : DuneLightMigration
{
    public override void Up()
    {
        Execute.Sql("UPDATE dunelight.appointments SET status = 'Cancelled' WHERE status = 'NoShow';");
    }
}

/// <summary>Dodaje appointment_audit_log.booking_id — audit na razini jednog Bookinga (BookingStatus/
/// BookingPackageEntryDeduct/BookingPackageEntryReturn) sad ide u istu tablicu kao i audit na razini cijelog
/// termina (Amount/Status/EmployeeId), razlikovano po tome je li booking_id popunjen. Vidi
/// AppointmentAuditLog.cs domensku napomenu — namjerno bez zasebnog BookingAuditLog.</summary>
[DeveloperMigration(2026, 09, 15, Developer.SilvioHabazin, 11)]
public class AddBookingIdToAppointmentAuditLog : DuneLightMigration
{
    public override void Up()
    {
        Alter.Table(Tables.AppointmentAuditLog)
            .InSchema(Tables.Schemas.DuneLight)
            .AddColumn("booking_id").AsGuid().Nullable();

        Create.ForeignKey("fk_appointment_audit_log_booking_id")
            .FromTable(Tables.AppointmentAuditLog).InSchema(Tables.Schemas.DuneLight).ForeignColumn("booking_id")
            .ToTable(Tables.Bookings).InSchema(Tables.Schemas.DuneLight).PrimaryColumn("id")
            .OnDelete(System.Data.Rule.Cascade);

        Create.Index("ix_appointment_audit_log_booking_id")
            .OnTable(Tables.AppointmentAuditLog).InSchema(Tables.Schemas.DuneLight)
            .OnColumn("booking_id").Ascending();
    }
}

/// <summary>Uklanja stare tablice tek NAKON što su podaci prebačeni gore — razvojna faza dopušta breaking
/// migraciju (vidi spec), ali podaci se svejedno čuvaju/prenose, ne bacaju.</summary>
[DeveloperMigration(2026, 09, 15, Developer.SilvioHabazin, 12)]
public class DropLegacyAppointmentClientAndAttendanceTables : DuneLightMigration
{
    public override void Up()
    {
        Delete.Table(Tables.AppointmentClients).InSchema(Tables.Schemas.DuneLight);
        Delete.Table(Tables.AppointmentAttendances).InSchema(Tables.Schemas.DuneLight);
    }
}
