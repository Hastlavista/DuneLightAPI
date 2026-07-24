using System;
using BlueDragon.DuneLight.DatabaseMigration.Extensions;
using BlueDragon.DuneLight.DatabaseMigration.Models;
using FluentMigrator;

namespace BlueDragon.DuneLight.DatabaseMigration.Migrations._2026;

/// <summary>
/// Katalog (Services/ServiceCategories/Packages) nema dosad seed podataka — potrebni su kao
/// preduvjet da seed termina/paketa ima na što referencirati.
/// </summary>
[DeveloperMigration(2026, 07, 21, Developer.SilvioHabazin, 21)]
public class SeedCatalogForAppointments : DuneLightMigration
{
    private static readonly Guid OrganizationId = Guid.Parse("00000000-0000-0000-0000-000000000001");
    private static readonly Guid AdminUserId = Guid.Parse("00000000-0000-0000-0000-000000000002");

    private static readonly Guid CategoryIndividual = Guid.NewGuid();
    private static readonly Guid ServiceIndividual = Guid.NewGuid();
    private static readonly Guid ServiceIndividualPar = Guid.NewGuid();
    private static readonly Guid PackageTen = Guid.NewGuid();

    public override void Up()
    {
        DateTimeOffset now = DateTimeOffset.UtcNow;

        Insert.IntoTable(Tables.ServiceCategories).InSchema(Tables.Schemas.DuneLight).Row(new
        {
            id = CategoryIndividual,
            organization_id = OrganizationId,
            name = "Individualni treninzi",
            execution_mode = "Individual",
            color_hex = "#2E86AB",
            requires_client = true,
            counts_toward_revenue = true,
            description = (string)null,
            is_active = true,
            sort_order = 0,
            created_at = now,
            created_by = AdminUserId
        });

        Insert.IntoTable(Tables.Services).InSchema(Tables.Schemas.DuneLight).Row(new
        {
            id = ServiceIndividual,
            organization_id = OrganizationId,
            name = "Individualni trening",
            service_category_id = CategoryIndividual,
            default_duration_minutes = 60,
            default_price = 25.00m,
            description = (string)null,
            is_active = true,
            sort_order = 0,
            created_at = now,
            created_by = AdminUserId
        });

        Insert.IntoTable(Tables.Services).InSchema(Tables.Schemas.DuneLight).Row(new
        {
            id = ServiceIndividualPar,
            organization_id = OrganizationId,
            name = "Individualni trening u paru",
            service_category_id = CategoryIndividual,
            default_duration_minutes = 60,
            default_price = 35.00m,
            description = "Duo varijanta individualnog treninga — svaki klijent naplaćuje/skida ulazak sa svog paketa.",
            is_active = true,
            sort_order = 1,
            created_at = now,
            created_by = AdminUserId
        });

        Insert.IntoTable(Tables.Packages).InSchema(Tables.Schemas.DuneLight).Row(new
        {
            id = PackageTen,
            organization_id = OrganizationId,
            name = "Paket 10 individualnih",
            description = (string)null,
            entry_mode = "SharedPool",
            total_entry_count = 10,
            validity_type = "DayCount",
            validity_days = 90,
            validity_fixed_date = (DateTimeOffset?)null,
            default_price = 220.00m,
            is_active = true,
            sort_order = 0,
            created_at = now,
            created_by = AdminUserId
        });

        Insert.IntoTable(Tables.PackageServices).InSchema(Tables.Schemas.DuneLight).Row(new
        {
            id = Guid.NewGuid(),
            package_id = PackageTen,
            service_id = ServiceIndividual,
            entry_count = (int?)null
        });
    }
}

[DeveloperMigration(2026, 07, 21, Developer.SilvioHabazin, 22)]
public class CreateClientPackagesTable : DuneLightMigration
{
    public override void Up()
    {
        Create.Table(Tables.ClientPackages)
            .InSchema(Tables.Schemas.DuneLight)
            .WithColumn("id").AsGuid().NotNullable().PrimaryKey("pk_client_packages")
            .WithColumn("organization_id").AsGuid().NotNullable()
            .WithColumn("client_id").AsGuid().NotNullable()
            .WithColumn("package_id").AsGuid().NotNullable()
            .WithColumn("purchase_date").AsDateTimeOffset().NotNullable()
            .WithColumn("paid_price").AsDecimal(10, 2).NotNullable()
            .WithColumn("entry_mode").AsString(20).NotNullable()
            .WithColumn("total_entry_count").AsInt32().Nullable()
            .WithColumn("remaining_shared_entries").AsInt32().Nullable()
            .WithColumn("validity_type").AsString(20).NotNullable()
            .WithColumn("expiry_date").AsDateTimeOffset().NotNullable()
            .WithColumn("status").AsString(20).NotNullable()
            .WithColumn("created_at").AsDateTimeOffset().NotNullable()
            .WithColumn("created_by").AsGuid().Nullable()
            .WithColumn("updated_at").AsDateTimeOffset().Nullable()
            .WithColumn("updated_by").AsGuid().Nullable();

        Create.ForeignKey("fk_client_packages_organization_id")
            .FromTable(Tables.ClientPackages).InSchema(Tables.Schemas.DuneLight).ForeignColumn("organization_id")
            .ToTable(Tables.Organizations).InSchema(Tables.Schemas.DuneLight).PrimaryColumn("id");

        Create.ForeignKey("fk_client_packages_client_id")
            .FromTable(Tables.ClientPackages).InSchema(Tables.Schemas.DuneLight).ForeignColumn("client_id")
            .ToTable(Tables.Clients).InSchema(Tables.Schemas.DuneLight).PrimaryColumn("id");

        Create.ForeignKey("fk_client_packages_package_id")
            .FromTable(Tables.ClientPackages).InSchema(Tables.Schemas.DuneLight).ForeignColumn("package_id")
            .ToTable(Tables.Packages).InSchema(Tables.Schemas.DuneLight).PrimaryColumn("id");

        Create.Index("ix_client_packages_org_client")
            .OnTable(Tables.ClientPackages).InSchema(Tables.Schemas.DuneLight)
            .OnColumn("organization_id").Ascending()
            .OnColumn("client_id").Ascending();
    }
}

[DeveloperMigration(2026, 07, 21, Developer.SilvioHabazin, 23)]
public class CreateClientPackageServiceEntriesTable : DuneLightMigration
{
    public override void Up()
    {
        Create.Table(Tables.ClientPackageServiceEntries)
            .InSchema(Tables.Schemas.DuneLight)
            .WithColumn("id").AsGuid().NotNullable().PrimaryKey("pk_client_package_service_entries")
            .WithColumn("client_package_id").AsGuid().NotNullable()
            .WithColumn("service_id").AsGuid().NotNullable()
            .WithColumn("total_entries").AsInt32().Nullable()
            .WithColumn("remaining_entries").AsInt32().Nullable();

        Create.ForeignKey("fk_client_package_service_entries_client_package_id")
            .FromTable(Tables.ClientPackageServiceEntries).InSchema(Tables.Schemas.DuneLight).ForeignColumn("client_package_id")
            .ToTable(Tables.ClientPackages).InSchema(Tables.Schemas.DuneLight).PrimaryColumn("id")
            .OnDelete(System.Data.Rule.Cascade);

        Create.ForeignKey("fk_client_package_service_entries_service_id")
            .FromTable(Tables.ClientPackageServiceEntries).InSchema(Tables.Schemas.DuneLight).ForeignColumn("service_id")
            .ToTable(Tables.Services).InSchema(Tables.Schemas.DuneLight).PrimaryColumn("id");

        Create.Index("ux_client_package_service_entries_package_service")
            .OnTable(Tables.ClientPackageServiceEntries).InSchema(Tables.Schemas.DuneLight)
            .OnColumn("client_package_id").Ascending()
            .OnColumn("service_id").Ascending()
            .WithOptions().Unique();
    }
}

[DeveloperMigration(2026, 07, 21, Developer.SilvioHabazin, 24)]
public class CreateAppointmentsTable : DuneLightMigration
{
    public override void Up()
    {
        Create.Table(Tables.Appointments)
            .InSchema(Tables.Schemas.DuneLight)
            .WithColumn("id").AsGuid().NotNullable().PrimaryKey("pk_appointments")
            .WithColumn("organization_id").AsGuid().NotNullable()
            .WithColumn("form").AsString(20).NotNullable()
            .WithColumn("starts_at").AsDateTimeOffset().NotNullable()
            .WithColumn("duration_minutes").AsInt32().NotNullable()
            .WithColumn("service_id").AsGuid().NotNullable()
            .WithColumn("employee_id").AsGuid().NotNullable()
            .WithColumn("location_id").AsGuid().NotNullable()
            .WithColumn("amount").AsDecimal(10, 2).NotNullable()
            .WithColumn("suggested_amount").AsDecimal(10, 2).NotNullable()
            .WithColumn("is_amount_manually_overridden").AsBoolean().NotNullable().WithDefaultValue(false)
            .WithColumn("payment_method").AsString(20).Nullable()
            .WithColumn("is_paid").AsBoolean().NotNullable().WithDefaultValue(false)
            .WithColumn("status").AsString(20).NotNullable()
            .WithColumn("note").AsCustom("text").Nullable()
            .WithColumn("group_id").AsGuid().Nullable()
            .WithColumn("recurrence_group_id").AsGuid().Nullable()
            .WithColumn("created_at").AsDateTimeOffset().NotNullable()
            .WithColumn("created_by").AsGuid().Nullable()
            .WithColumn("updated_at").AsDateTimeOffset().Nullable()
            .WithColumn("updated_by").AsGuid().Nullable();

        Create.ForeignKey("fk_appointments_organization_id")
            .FromTable(Tables.Appointments).InSchema(Tables.Schemas.DuneLight).ForeignColumn("organization_id")
            .ToTable(Tables.Organizations).InSchema(Tables.Schemas.DuneLight).PrimaryColumn("id");

        Create.ForeignKey("fk_appointments_service_id")
            .FromTable(Tables.Appointments).InSchema(Tables.Schemas.DuneLight).ForeignColumn("service_id")
            .ToTable(Tables.Services).InSchema(Tables.Schemas.DuneLight).PrimaryColumn("id");

        Create.ForeignKey("fk_appointments_employee_id")
            .FromTable(Tables.Appointments).InSchema(Tables.Schemas.DuneLight).ForeignColumn("employee_id")
            .ToTable(Tables.Employees).InSchema(Tables.Schemas.DuneLight).PrimaryColumn("id");

        Create.ForeignKey("fk_appointments_location_id")
            .FromTable(Tables.Appointments).InSchema(Tables.Schemas.DuneLight).ForeignColumn("location_id")
            .ToTable(Tables.Locations).InSchema(Tables.Schemas.DuneLight).PrimaryColumn("id");

        // group_id namjerno bez FK-a — rezervirano za budući Group entitet koji još ne postoji.

        Create.Index("ix_appointments_org_location_startsat")
            .OnTable(Tables.Appointments).InSchema(Tables.Schemas.DuneLight)
            .OnColumn("organization_id").Ascending()
            .OnColumn("location_id").Ascending()
            .OnColumn("starts_at").Ascending();

        Create.Index("ix_appointments_org_employee_startsat")
            .OnTable(Tables.Appointments).InSchema(Tables.Schemas.DuneLight)
            .OnColumn("organization_id").Ascending()
            .OnColumn("employee_id").Ascending()
            .OnColumn("starts_at").Ascending();
    }
}

[DeveloperMigration(2026, 07, 21, Developer.SilvioHabazin, 25)]
public class CreateAppointmentClientsTable : DuneLightMigration
{
    public override void Up()
    {
        Create.Table(Tables.AppointmentClients)
            .InSchema(Tables.Schemas.DuneLight)
            .WithColumn("id").AsGuid().NotNullable().PrimaryKey("pk_appointment_clients")
            .WithColumn("appointment_id").AsGuid().NotNullable()
            .WithColumn("client_id").AsGuid().NotNullable()
            .WithColumn("client_package_id").AsGuid().Nullable()
            .WithColumn("package_entry_deducted").AsBoolean().NotNullable().WithDefaultValue(false)
            .WithColumn("package_entry_returned").AsBoolean().NotNullable().WithDefaultValue(false)
            .WithColumn("package_entry_returned_at").AsDateTimeOffset().Nullable()
            .WithColumn("package_entry_returned_by").AsGuid().Nullable()
            .WithColumn("created_at").AsDateTimeOffset().NotNullable();

        Create.ForeignKey("fk_appointment_clients_appointment_id")
            .FromTable(Tables.AppointmentClients).InSchema(Tables.Schemas.DuneLight).ForeignColumn("appointment_id")
            .ToTable(Tables.Appointments).InSchema(Tables.Schemas.DuneLight).PrimaryColumn("id")
            .OnDelete(System.Data.Rule.Cascade);

        Create.ForeignKey("fk_appointment_clients_client_id")
            .FromTable(Tables.AppointmentClients).InSchema(Tables.Schemas.DuneLight).ForeignColumn("client_id")
            .ToTable(Tables.Clients).InSchema(Tables.Schemas.DuneLight).PrimaryColumn("id");

        Create.ForeignKey("fk_appointment_clients_client_package_id")
            .FromTable(Tables.AppointmentClients).InSchema(Tables.Schemas.DuneLight).ForeignColumn("client_package_id")
            .ToTable(Tables.ClientPackages).InSchema(Tables.Schemas.DuneLight).PrimaryColumn("id");

        Create.Index("ux_appointment_clients_appointment_client")
            .OnTable(Tables.AppointmentClients).InSchema(Tables.Schemas.DuneLight)
            .OnColumn("appointment_id").Ascending()
            .OnColumn("client_id").Ascending()
            .WithOptions().Unique();
    }
}

[DeveloperMigration(2026, 07, 21, Developer.SilvioHabazin, 26)]
public class CreateAppointmentAttendancesTable : DuneLightMigration
{
    public override void Up()
    {
        Create.Table(Tables.AppointmentAttendances)
            .InSchema(Tables.Schemas.DuneLight)
            .WithColumn("id").AsGuid().NotNullable().PrimaryKey("pk_appointment_attendances")
            .WithColumn("appointment_id").AsGuid().NotNullable()
            .WithColumn("client_id").AsGuid().NotNullable()
            .WithColumn("attended").AsBoolean().Nullable()
            .WithColumn("client_package_id").AsGuid().Nullable()
            .WithColumn("package_entry_deducted").AsBoolean().NotNullable().WithDefaultValue(false)
            .WithColumn("created_at").AsDateTimeOffset().NotNullable();

        Create.ForeignKey("fk_appointment_attendances_appointment_id")
            .FromTable(Tables.AppointmentAttendances).InSchema(Tables.Schemas.DuneLight).ForeignColumn("appointment_id")
            .ToTable(Tables.Appointments).InSchema(Tables.Schemas.DuneLight).PrimaryColumn("id")
            .OnDelete(System.Data.Rule.Cascade);

        Create.ForeignKey("fk_appointment_attendances_client_id")
            .FromTable(Tables.AppointmentAttendances).InSchema(Tables.Schemas.DuneLight).ForeignColumn("client_id")
            .ToTable(Tables.Clients).InSchema(Tables.Schemas.DuneLight).PrimaryColumn("id");

        Create.ForeignKey("fk_appointment_attendances_client_package_id")
            .FromTable(Tables.AppointmentAttendances).InSchema(Tables.Schemas.DuneLight).ForeignColumn("client_package_id")
            .ToTable(Tables.ClientPackages).InSchema(Tables.Schemas.DuneLight).PrimaryColumn("id");

        Create.Index("ux_appointment_attendances_appointment_client")
            .OnTable(Tables.AppointmentAttendances).InSchema(Tables.Schemas.DuneLight)
            .OnColumn("appointment_id").Ascending()
            .OnColumn("client_id").Ascending()
            .WithOptions().Unique();
    }
}

[DeveloperMigration(2026, 07, 21, Developer.SilvioHabazin, 27)]
public class CreateAppointmentAuditLogTable : DuneLightMigration
{
    public override void Up()
    {
        Create.Table(Tables.AppointmentAuditLog)
            .InSchema(Tables.Schemas.DuneLight)
            .WithColumn("id").AsGuid().NotNullable().PrimaryKey("pk_appointment_audit_log")
            .WithColumn("appointment_id").AsGuid().NotNullable()
            .WithColumn("change_type").AsString(30).NotNullable()
            .WithColumn("old_value").AsString(255).Nullable()
            .WithColumn("new_value").AsString(255).Nullable()
            .WithColumn("changed_at").AsDateTimeOffset().NotNullable()
            .WithColumn("changed_by").AsGuid().Nullable();

        Create.ForeignKey("fk_appointment_audit_log_appointment_id")
            .FromTable(Tables.AppointmentAuditLog).InSchema(Tables.Schemas.DuneLight).ForeignColumn("appointment_id")
            .ToTable(Tables.Appointments).InSchema(Tables.Schemas.DuneLight).PrimaryColumn("id")
            .OnDelete(System.Data.Rule.Cascade);

        Create.Index("ix_appointment_audit_log_appointment_id")
            .OnTable(Tables.AppointmentAuditLog).InSchema(Tables.Schemas.DuneLight)
            .OnColumn("appointment_id").Ascending();
    }
}

[DeveloperMigration(2026, 07, 21, Developer.SilvioHabazin, 28)]
public class SeedAppointmentsMinimal : DuneLightMigration
{
    private static readonly Guid OrganizationId = Guid.Parse("00000000-0000-0000-0000-000000000001");
    private static readonly Guid AdminUserId = Guid.Parse("00000000-0000-0000-0000-000000000002");

    private static readonly Guid ClientPackageIvana = Guid.NewGuid();

    public override void Up()
    {
        DateTimeOffset now = DateTimeOffset.UtcNow;
        string org = OrganizationId.ToString();
        string admin = AdminUserId.ToString();

        // Ivana kupuje "Paket 10 individualnih" — SharedPool, 10 ulazaka, 90 dana valjanosti.
        // Jedan ulazak je već potrošen (vidi termin niže plaćen iz paketa), pa je preostalo 9.
        DateTimeOffset purchaseDate = now.AddDays(-10);
        DateTimeOffset expiryDate = purchaseDate.AddDays(90);
        Execute.Sql($@"
            INSERT INTO dunelight.client_packages
                (id, organization_id, client_id, package_id, purchase_date, paid_price, entry_mode,
                 total_entry_count, remaining_shared_entries, validity_type, expiry_date, status, created_at, created_by)
            VALUES
                ('{ClientPackageIvana}', '{org}',
                 (SELECT id FROM dunelight.clients WHERE organization_id = '{org}' AND member_number = 1),
                 (SELECT id FROM dunelight.packages WHERE organization_id = '{org}' AND name = 'Paket 10 individualnih'),
                 '{purchaseDate:yyyy-MM-ddTHH:mm:sszzz}', 220.00, 'SharedPool',
                 10, 9, 'DayCount', '{expiryDate:yyyy-MM-ddTHH:mm:sszzz}', 'Active', '{now:yyyy-MM-ddTHH:mm:sszzz}', '{admin}');");

        Execute.Sql($@"
            INSERT INTO dunelight.client_package_service_entries (id, client_package_id, service_id, total_entries, remaining_entries)
            VALUES
                ('{Guid.NewGuid()}', '{ClientPackageIvana}',
                 (SELECT id FROM dunelight.services WHERE organization_id = '{org}' AND name = 'Individualni trening'),
                 NULL, NULL);");

        // Termin 1: Scheduled, Centar, Marko, Ivana — za 2 dana.
        Guid appt1 = Guid.NewGuid();
        InsertAppointment(appt1, org, admin, now.Date.AddDays(2).AddHours(17).AddMinutes(15), 60,
            "Individualni trening", "Marko", "Trener", "Centar",
            25.00m, 25.00m, false, null, false, "Scheduled");
        InsertAppointmentClient(Guid.NewGuid(), appt1, org, 1, null, false, false);

        // Termin 2: Completed, Riverside, Iva, Petar — prije 1 dana, plaćeno gotovinom.
        Guid appt2 = Guid.NewGuid();
        InsertAppointment(appt2, org, admin, now.Date.AddDays(-1).AddHours(9), 60,
            "Individualni trening", "Iva", "Vanjska", "Riverside",
            25.00m, 25.00m, false, "Cash", true, "Completed");
        InsertAppointmentClient(Guid.NewGuid(), appt2, org, 2, null, false, false);

        // Termin 3: Scheduled, Centar, Marko, duo Ivana+Petar — za 1 dan, usluga "u paru".
        Guid appt3 = Guid.NewGuid();
        InsertAppointment(appt3, org, admin, now.Date.AddDays(1).AddHours(18), 60,
            "Individualni trening u paru", "Marko", "Trener", "Centar",
            35.00m, 35.00m, false, null, false, "Scheduled");
        InsertAppointmentClient(Guid.NewGuid(), appt3, org, 1, null, false, false);
        InsertAppointmentClient(Guid.NewGuid(), appt3, org, 2, null, false, false);

        // Termin 4: Cancelled, Riverside, Iva, Ana Anić — prije 3 dana.
        Guid appt4 = Guid.NewGuid();
        InsertAppointment(appt4, org, admin, now.Date.AddDays(-3).AddHours(10), 60,
            "Individualni trening", "Iva", "Vanjska", "Riverside",
            25.00m, 25.00m, false, null, false, "Cancelled");
        InsertAppointmentClient(Guid.NewGuid(), appt4, org, 3, null, false, false);

        // Termin 5: Completed, Centar, Marko, Ivana — prije 2 dana, plaćeno iz paketa (ulazak već skinut gore).
        Guid appt5 = Guid.NewGuid();
        InsertAppointment(appt5, org, admin, now.Date.AddDays(-2).AddHours(16), 60,
            "Individualni trening", "Marko", "Trener", "Centar",
            25.00m, 25.00m, false, "Package", true, "Completed");
        InsertAppointmentClient(Guid.NewGuid(), appt5, org, 1, ClientPackageIvana, true, false);

        // Termin 6: Scheduled, Riverside, Iva, Marko Novak — za 3 dana.
        Guid appt6 = Guid.NewGuid();
        InsertAppointment(appt6, org, admin, now.Date.AddDays(3).AddHours(8), 60,
            "Individualni trening", "Iva", "Vanjska", "Riverside",
            25.00m, 25.00m, false, null, false, "Scheduled");
        InsertAppointmentClient(Guid.NewGuid(), appt6, org, 4, null, false, false);
    }

    private void InsertAppointment(
        Guid id, string org, string admin, DateTimeOffset startsAt, int durationMinutes,
        string serviceName, string employeeFirstName, string employeeLastName, string locationName,
        decimal amount, decimal suggestedAmount, bool isManuallyOverridden,
        string paymentMethod, bool isPaid, string status)
    {
        string paymentMethodSql = paymentMethod == null ? "NULL" : $"'{paymentMethod}'";

        Execute.Sql($@"
            INSERT INTO dunelight.appointments
                (id, organization_id, form, starts_at, duration_minutes, service_id, employee_id, location_id,
                 amount, suggested_amount, is_amount_manually_overridden, payment_method, is_paid, status, created_at, created_by)
            VALUES
                ('{id}', '{org}', 'Individual', '{startsAt:yyyy-MM-ddTHH:mm:sszzz}', {durationMinutes},
                 (SELECT id FROM dunelight.services WHERE organization_id = '{org}' AND name = '{serviceName}'),
                 (SELECT id FROM dunelight.employees WHERE organization_id = '{org}' AND first_name = '{employeeFirstName}' AND last_name = '{employeeLastName}'),
                 (SELECT id FROM dunelight.locations WHERE organization_id = '{org}' AND name = '{locationName}'),
                 {amount.ToString(System.Globalization.CultureInfo.InvariantCulture)},
                 {suggestedAmount.ToString(System.Globalization.CultureInfo.InvariantCulture)},
                 {(isManuallyOverridden ? "true" : "false")}, {paymentMethodSql}, {(isPaid ? "true" : "false")}, '{status}',
                 '{DateTimeOffset.UtcNow:yyyy-MM-ddTHH:mm:sszzz}', '{admin}');");
    }

    private void InsertAppointmentClient(
        Guid id, Guid appointmentId, string org, int clientMemberNumber,
        Guid? clientPackageId, bool packageEntryDeducted, bool packageEntryReturned)
    {
        string clientPackageSql = clientPackageId == null ? "NULL" : $"'{clientPackageId}'";

        Execute.Sql($@"
            INSERT INTO dunelight.appointment_clients
                (id, appointment_id, client_id, client_package_id, package_entry_deducted, package_entry_returned, created_at)
            VALUES
                ('{id}', '{appointmentId}',
                 (SELECT id FROM dunelight.clients WHERE organization_id = '{org}' AND member_number = {clientMemberNumber}),
                 {clientPackageSql}, {(packageEntryDeducted ? "true" : "false")}, {(packageEntryReturned ? "true" : "false")},
                 '{DateTimeOffset.UtcNow:yyyy-MM-ddTHH:mm:sszzz}');");
    }
}
