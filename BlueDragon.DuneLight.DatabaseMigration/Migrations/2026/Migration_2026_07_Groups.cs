using System;
using BlueDragon.DuneLight.DatabaseMigration.Extensions;
using BlueDragon.DuneLight.DatabaseMigration.Models;
using FluentMigrator;

namespace BlueDragon.DuneLight.DatabaseMigration.Migrations._2026;

[DeveloperMigration(2026, 07, 21, Developer.SilvioHabazin, 29)]
public class CreateGroupsTable : DuneLightMigration
{
    public override void Up()
    {
        Create.Table(Tables.Groups)
            .InSchema(Tables.Schemas.DuneLight)
            .WithColumn("id").AsGuid().NotNullable().PrimaryKey("pk_groups")
            .WithColumn("organization_id").AsGuid().NotNullable()
            .WithColumn("name").AsString(255).NotNullable()
            .WithColumn("service_id").AsGuid().NotNullable()
            .WithColumn("location_id").AsGuid().NotNullable()
            .WithColumn("capacity").AsInt32().NotNullable()
            .WithColumn("default_trainer_id").AsGuid().Nullable()
            .WithColumn("is_active").AsBoolean().NotNullable().WithDefaultValue(true)
            .WithColumn("note").AsCustom("text").Nullable()
            .WithColumn("created_at").AsDateTimeOffset().NotNullable()
            .WithColumn("created_by").AsGuid().Nullable()
            .WithColumn("updated_at").AsDateTimeOffset().Nullable()
            .WithColumn("updated_by").AsGuid().Nullable();

        Create.ForeignKey("fk_groups_organization_id")
            .FromTable(Tables.Groups).InSchema(Tables.Schemas.DuneLight).ForeignColumn("organization_id")
            .ToTable(Tables.Organizations).InSchema(Tables.Schemas.DuneLight).PrimaryColumn("id");

        Create.ForeignKey("fk_groups_service_id")
            .FromTable(Tables.Groups).InSchema(Tables.Schemas.DuneLight).ForeignColumn("service_id")
            .ToTable(Tables.Services).InSchema(Tables.Schemas.DuneLight).PrimaryColumn("id");

        Create.ForeignKey("fk_groups_location_id")
            .FromTable(Tables.Groups).InSchema(Tables.Schemas.DuneLight).ForeignColumn("location_id")
            .ToTable(Tables.Locations).InSchema(Tables.Schemas.DuneLight).PrimaryColumn("id");

        Create.ForeignKey("fk_groups_default_trainer_id")
            .FromTable(Tables.Groups).InSchema(Tables.Schemas.DuneLight).ForeignColumn("default_trainer_id")
            .ToTable(Tables.Employees).InSchema(Tables.Schemas.DuneLight).PrimaryColumn("id");

        Create.Index("ix_groups_organization_id")
            .OnTable(Tables.Groups).InSchema(Tables.Schemas.DuneLight)
            .OnColumn("organization_id").Ascending();
    }
}

[DeveloperMigration(2026, 07, 21, Developer.SilvioHabazin, 30)]
public class CreateGroupSlotsTable : DuneLightMigration
{
    public override void Up()
    {
        Create.Table(Tables.GroupSlots)
            .InSchema(Tables.Schemas.DuneLight)
            .WithColumn("id").AsGuid().NotNullable().PrimaryKey("pk_group_slots")
            .WithColumn("group_id").AsGuid().NotNullable()
            .WithColumn("day_of_week").AsString(20).NotNullable()
            .WithColumn("start_time").AsTime().NotNullable()
            .WithColumn("is_active").AsBoolean().NotNullable().WithDefaultValue(true)
            .WithColumn("created_at").AsDateTimeOffset().NotNullable();

        Create.ForeignKey("fk_group_slots_group_id")
            .FromTable(Tables.GroupSlots).InSchema(Tables.Schemas.DuneLight).ForeignColumn("group_id")
            .ToTable(Tables.Groups).InSchema(Tables.Schemas.DuneLight).PrimaryColumn("id")
            .OnDelete(System.Data.Rule.Cascade);

        Create.Index("ix_group_slots_group_active")
            .OnTable(Tables.GroupSlots).InSchema(Tables.Schemas.DuneLight)
            .OnColumn("group_id").Ascending()
            .OnColumn("is_active").Ascending();
    }
}

[DeveloperMigration(2026, 07, 21, Developer.SilvioHabazin, 31)]
public class CreateGroupMembersTable : DuneLightMigration
{
    public override void Up()
    {
        Create.Table(Tables.GroupMembers)
            .InSchema(Tables.Schemas.DuneLight)
            .WithColumn("id").AsGuid().NotNullable().PrimaryKey("pk_group_members")
            .WithColumn("group_id").AsGuid().NotNullable()
            .WithColumn("client_id").AsGuid().NotNullable()
            .WithColumn("joined_at").AsDateTimeOffset().NotNullable()
            .WithColumn("is_active").AsBoolean().NotNullable().WithDefaultValue(true)
            .WithColumn("created_at").AsDateTimeOffset().NotNullable();

        Create.ForeignKey("fk_group_members_group_id")
            .FromTable(Tables.GroupMembers).InSchema(Tables.Schemas.DuneLight).ForeignColumn("group_id")
            .ToTable(Tables.Groups).InSchema(Tables.Schemas.DuneLight).PrimaryColumn("id")
            .OnDelete(System.Data.Rule.Cascade);

        Create.ForeignKey("fk_group_members_client_id")
            .FromTable(Tables.GroupMembers).InSchema(Tables.Schemas.DuneLight).ForeignColumn("client_id")
            .ToTable(Tables.Clients).InSchema(Tables.Schemas.DuneLight).PrimaryColumn("id");

        // Partial unique index (dopušta ponovni upis nakon napuštanja grupe) — vidi GroupMember.cs.
        Execute.Sql(
            "CREATE UNIQUE INDEX ux_group_members_group_client_active " +
            "ON dunelight.group_members (group_id, client_id) WHERE is_active = true;");
    }
}

[DeveloperMigration(2026, 07, 21, Developer.SilvioHabazin, 32)]
public class CreateGroupAuditLogTable : DuneLightMigration
{
    public override void Up()
    {
        Create.Table(Tables.GroupAuditLog)
            .InSchema(Tables.Schemas.DuneLight)
            .WithColumn("id").AsGuid().NotNullable().PrimaryKey("pk_group_audit_log")
            .WithColumn("group_id").AsGuid().NotNullable()
            .WithColumn("change_type").AsString(30).NotNullable()
            .WithColumn("old_value").AsString(255).Nullable()
            .WithColumn("new_value").AsString(255).Nullable()
            .WithColumn("changed_at").AsDateTimeOffset().NotNullable()
            .WithColumn("changed_by").AsGuid().Nullable();

        Create.ForeignKey("fk_group_audit_log_group_id")
            .FromTable(Tables.GroupAuditLog).InSchema(Tables.Schemas.DuneLight).ForeignColumn("group_id")
            .ToTable(Tables.Groups).InSchema(Tables.Schemas.DuneLight).PrimaryColumn("id")
            .OnDelete(System.Data.Rule.Cascade);

        Create.Index("ix_group_audit_log_group_id")
            .OnTable(Tables.GroupAuditLog).InSchema(Tables.Schemas.DuneLight)
            .OnColumn("group_id").Ascending();
    }
}

[DeveloperMigration(2026, 07, 21, Developer.SilvioHabazin, 33)]
public class AlterAppointmentsForGroups : DuneLightMigration
{
    public override void Up()
    {
        Alter.Table(Tables.Appointments)
            .InSchema(Tables.Schemas.DuneLight)
            .AddColumn("group_slot_id").AsGuid().Nullable();

        Create.ForeignKey("fk_appointments_group_id")
            .FromTable(Tables.Appointments).InSchema(Tables.Schemas.DuneLight).ForeignColumn("group_id")
            .ToTable(Tables.Groups).InSchema(Tables.Schemas.DuneLight).PrimaryColumn("id");

        Create.ForeignKey("fk_appointments_group_slot_id")
            .FromTable(Tables.Appointments).InSchema(Tables.Schemas.DuneLight).ForeignColumn("group_slot_id")
            .ToTable(Tables.GroupSlots).InSchema(Tables.Schemas.DuneLight).PrimaryColumn("id");

        // Sigurnosna mreža na DB razini za idempotentno generiranje — vidi GroupService.Generate.
        Execute.Sql(
            "CREATE UNIQUE INDEX ux_appointments_group_slot_startsat " +
            "ON dunelight.appointments (group_slot_id, starts_at) WHERE group_slot_id IS NOT NULL;");
    }
}

[DeveloperMigration(2026, 07, 21, Developer.SilvioHabazin, 34)]
public class AlterAppointmentAttendancesForGroups : DuneLightMigration
{
    public override void Up()
    {
        Alter.Table(Tables.AppointmentAttendances)
            .InSchema(Tables.Schemas.DuneLight)
            .AddColumn("coverage_type").AsString(20).Nullable();

        Alter.Table(Tables.AppointmentAttendances)
            .InSchema(Tables.Schemas.DuneLight)
            .AddColumn("package_entry_returned").AsBoolean().NotNullable().WithDefaultValue(false);

        Alter.Table(Tables.AppointmentAttendances)
            .InSchema(Tables.Schemas.DuneLight)
            .AddColumn("package_entry_returned_at").AsDateTimeOffset().Nullable();

        Alter.Table(Tables.AppointmentAttendances)
            .InSchema(Tables.Schemas.DuneLight)
            .AddColumn("package_entry_returned_by").AsGuid().Nullable();

        Alter.Table(Tables.AppointmentAttendances)
            .InSchema(Tables.Schemas.DuneLight)
            .AddColumn("note").AsCustom("text").Nullable();
    }
}

[DeveloperMigration(2026, 07, 21, Developer.SilvioHabazin, 35)]
public class SeedGroupsMinimal : DuneLightMigration
{
    private static readonly Guid OrganizationId = Guid.Parse("00000000-0000-0000-0000-000000000001");
    private static readonly Guid AdminUserId = Guid.Parse("00000000-0000-0000-0000-000000000002");

    private static readonly Guid CategoryGroup = Guid.NewGuid();
    private static readonly Guid ServiceGroupTraining = Guid.NewGuid();
    private static readonly Guid PackageGroupTen = Guid.NewGuid();
    private static readonly Guid PackageGroupMonthly = Guid.NewGuid();

    private static readonly Guid GroupMorningYoga = Guid.NewGuid();
    private static readonly Guid GroupEveningFunctional = Guid.NewGuid();

    private static readonly Guid SlotMonday = Guid.NewGuid();
    private static readonly Guid SlotWednesday = Guid.NewGuid();
    private static readonly Guid SlotTuesday = Guid.NewGuid();

    private static readonly Guid ClientPackageIvanaGroupTen = Guid.NewGuid();
    private static readonly Guid ClientPackagePetarMonthly = Guid.NewGuid();

    private static readonly Guid AppointmentMonday = Guid.NewGuid();
    private static readonly Guid AppointmentWednesday = Guid.NewGuid();
    private static readonly Guid AppointmentTuesday = Guid.NewGuid();

    public override void Up()
    {
        DateTimeOffset now = DateTimeOffset.UtcNow;
        string org = OrganizationId.ToString();
        string admin = AdminUserId.ToString();

        // Ponedjeljak tekućeg tjedna (UTC datum), radi generiranja termina za "tekući tjedan" bez tvrdo kodiranog datuma.
        int diffToMonday = ((int)now.DayOfWeek + 6) % 7;
        DateTimeOffset monday = now.Date.AddDays(-diffToMonday);
        DateTimeOffset tuesday = monday.AddDays(1);
        DateTimeOffset wednesday = monday.AddDays(2);

        // Katalog dosad nema kategoriju/uslugu za grupne treninge — treba kao preduvjet za grupe.
        Insert.IntoTable(Tables.ServiceCategories).InSchema(Tables.Schemas.DuneLight).Row(new
        {
            id = CategoryGroup,
            organization_id = OrganizationId,
            name = "Grupni treninzi",
            execution_mode = "Group",
            color_hex = "#8E44AD",
            requires_client = true,
            counts_toward_revenue = true,
            description = (string)null,
            is_active = true,
            sort_order = 2,
            created_at = now,
            created_by = AdminUserId
        });

        Insert.IntoTable(Tables.Services).InSchema(Tables.Schemas.DuneLight).Row(new
        {
            id = ServiceGroupTraining,
            organization_id = OrganizationId,
            name = "Grupni trening",
            service_category_id = CategoryGroup,
            default_duration_minutes = 60,
            default_price = 12.00m,
            description = (string)null,
            is_active = true,
            sort_order = 2,
            created_at = now,
            created_by = AdminUserId
        });

        Insert.IntoTable(Tables.Packages).InSchema(Tables.Schemas.DuneLight).Row(new
        {
            id = PackageGroupTen,
            organization_id = OrganizationId,
            name = "Paket 10 grupnih",
            description = (string)null,
            entry_mode = "SharedPool",
            total_entry_count = 10,
            validity_type = "DayCount",
            validity_days = 60,
            validity_fixed_date = (DateTimeOffset?)null,
            default_price = 60.00m,
            is_active = true,
            sort_order = 1,
            created_at = now,
            created_by = AdminUserId
        });

        Insert.IntoTable(Tables.PackageServices).InSchema(Tables.Schemas.DuneLight).Row(new
        {
            id = Guid.NewGuid(),
            package_id = PackageGroupTen,
            service_id = ServiceGroupTraining,
            entry_count = (int?)null
        });

        // Neograničen/mjesečni paket — "MonthlyPackage" pokriće kod evidencije prisutnosti (null brojač = neograničeno).
        Insert.IntoTable(Tables.Packages).InSchema(Tables.Schemas.DuneLight).Row(new
        {
            id = PackageGroupMonthly,
            organization_id = OrganizationId,
            name = "Grupno neograničeno mjesečno",
            description = (string)null,
            entry_mode = "SharedPool",
            total_entry_count = (int?)null,
            validity_type = "EndOfMonth",
            validity_days = (int?)null,
            validity_fixed_date = (DateTimeOffset?)null,
            default_price = 45.00m,
            is_active = true,
            sort_order = 2,
            created_at = now,
            created_by = AdminUserId
        });

        Insert.IntoTable(Tables.PackageServices).InSchema(Tables.Schemas.DuneLight).Row(new
        {
            id = Guid.NewGuid(),
            package_id = PackageGroupMonthly,
            service_id = ServiceGroupTraining,
            entry_count = (int?)null
        });

        // Grupa 1: dva slota tjedno, lokacija Centar, zadani trener Marko.
        Execute.Sql($@"
            INSERT INTO dunelight.groups
                (id, organization_id, name, service_id, location_id, capacity, default_trainer_id, is_active, note, created_at, created_by)
            VALUES
                ('{GroupMorningYoga}', '{org}', 'Jutarnja joga', '{ServiceGroupTraining}',
                 (SELECT id FROM dunelight.locations WHERE organization_id = '{org}' AND name = 'Centar'),
                 8,
                 (SELECT id FROM dunelight.employees WHERE organization_id = '{org}' AND first_name = 'Marko' AND last_name = 'Trener'),
                 true, NULL, '{now:yyyy-MM-ddTHH:mm:sszzz}', '{admin}');");

        Insert.IntoTable(Tables.GroupSlots).InSchema(Tables.Schemas.DuneLight).Row(new
        {
            id = SlotMonday,
            group_id = GroupMorningYoga,
            day_of_week = "Monday",
            start_time = new TimeSpan(7, 0, 0),
            is_active = true,
            created_at = now
        });

        Insert.IntoTable(Tables.GroupSlots).InSchema(Tables.Schemas.DuneLight).Row(new
        {
            id = SlotWednesday,
            group_id = GroupMorningYoga,
            day_of_week = "Wednesday",
            start_time = new TimeSpan(7, 0, 0),
            is_active = true,
            created_at = now
        });

        // Grupa 2: jedan slot tjedno, lokacija Riverside, zadani trener Iva.
        Execute.Sql($@"
            INSERT INTO dunelight.groups
                (id, organization_id, name, service_id, location_id, capacity, default_trainer_id, is_active, note, created_at, created_by)
            VALUES
                ('{GroupEveningFunctional}', '{org}', 'Večernji funkcionalni', '{ServiceGroupTraining}',
                 (SELECT id FROM dunelight.locations WHERE organization_id = '{org}' AND name = 'Riverside'),
                 6,
                 (SELECT id FROM dunelight.employees WHERE organization_id = '{org}' AND first_name = 'Iva' AND last_name = 'Vanjska'),
                 true, NULL, '{now:yyyy-MM-ddTHH:mm:sszzz}', '{admin}');");

        Insert.IntoTable(Tables.GroupSlots).InSchema(Tables.Schemas.DuneLight).Row(new
        {
            id = SlotTuesday,
            group_id = GroupEveningFunctional,
            day_of_week = "Tuesday",
            start_time = new TimeSpan(19, 0, 0),
            is_active = true,
            created_at = now
        });

        // Članovi: Ivana (Paket 10 grupnih, SessionPackage) i Ana (bez paketa) u Jutarnjoj jogi;
        // Petar (Grupno neograničeno mjesečno, MonthlyPackage) i Marko Novak (bez paketa) u Večernjem funkcionalnom.
        Execute.Sql($@"
            INSERT INTO dunelight.group_members (id, group_id, client_id, joined_at, is_active, created_at)
            VALUES
                ('{Guid.NewGuid()}', '{GroupMorningYoga}',
                 (SELECT id FROM dunelight.clients WHERE organization_id = '{org}' AND member_number = 1),
                 '{now.AddDays(-30):yyyy-MM-ddTHH:mm:sszzz}', true, '{now:yyyy-MM-ddTHH:mm:sszzz}'),
                ('{Guid.NewGuid()}', '{GroupMorningYoga}',
                 (SELECT id FROM dunelight.clients WHERE organization_id = '{org}' AND member_number = 3),
                 '{now.AddDays(-14):yyyy-MM-ddTHH:mm:sszzz}', true, '{now:yyyy-MM-ddTHH:mm:sszzz}'),
                ('{Guid.NewGuid()}', '{GroupEveningFunctional}',
                 (SELECT id FROM dunelight.clients WHERE organization_id = '{org}' AND member_number = 2),
                 '{now.AddDays(-60):yyyy-MM-ddTHH:mm:sszzz}', true, '{now:yyyy-MM-ddTHH:mm:sszzz}'),
                ('{Guid.NewGuid()}', '{GroupEveningFunctional}',
                 (SELECT id FROM dunelight.clients WHERE organization_id = '{org}' AND member_number = 4),
                 '{now.AddDays(-5):yyyy-MM-ddTHH:mm:sszzz}', true, '{now:yyyy-MM-ddTHH:mm:sszzz}');");

        // Ivanin prodani paket "Paket 10 grupnih" — jedan ulazak već potrošen (vidi attendance niže), 9/10 preostalo.
        Execute.Sql($@"
            INSERT INTO dunelight.client_packages
                (id, organization_id, client_id, package_id, purchase_date, paid_price, entry_mode,
                 total_entry_count, remaining_shared_entries, validity_type, expiry_date, status, created_at, created_by)
            VALUES
                ('{ClientPackageIvanaGroupTen}', '{org}',
                 (SELECT id FROM dunelight.clients WHERE organization_id = '{org}' AND member_number = 1),
                 '{PackageGroupTen}',
                 '{now.AddDays(-20):yyyy-MM-ddTHH:mm:sszzz}', 60.00, 'SharedPool',
                 10, 9, 'DayCount', '{now.AddDays(40):yyyy-MM-ddTHH:mm:sszzz}', 'Active', '{now:yyyy-MM-ddTHH:mm:sszzz}', '{admin}');");

        Execute.Sql($@"
            INSERT INTO dunelight.client_package_service_entries (id, client_package_id, service_id, total_entries, remaining_entries)
            VALUES
                ('{Guid.NewGuid()}', '{ClientPackageIvanaGroupTen}', '{ServiceGroupTraining}', NULL, NULL);");

        // Petrov prodani paket "Grupno neograničeno mjesečno" — MonthlyPackage, ništa se ne broji.
        Execute.Sql($@"
            INSERT INTO dunelight.client_packages
                (id, organization_id, client_id, package_id, purchase_date, paid_price, entry_mode,
                 total_entry_count, remaining_shared_entries, validity_type, expiry_date, status, created_at, created_by)
            VALUES
                ('{ClientPackagePetarMonthly}', '{org}',
                 (SELECT id FROM dunelight.clients WHERE organization_id = '{org}' AND member_number = 2),
                 '{PackageGroupMonthly}',
                 '{now.AddDays(-5):yyyy-MM-ddTHH:mm:sszzz}', 45.00, 'SharedPool',
                 NULL, NULL, 'EndOfMonth', '{new DateTimeOffset(now.Year, now.Month, 1, 0, 0, 0, now.Offset).AddMonths(1).AddDays(-1):yyyy-MM-ddTHH:mm:sszzz}',
                 'Active', '{now:yyyy-MM-ddTHH:mm:sszzz}', '{admin}');");

        Execute.Sql($@"
            INSERT INTO dunelight.client_package_service_entries (id, client_package_id, service_id, total_entries, remaining_entries)
            VALUES
                ('{Guid.NewGuid()}', '{ClientPackagePetarMonthly}', '{ServiceGroupTraining}', NULL, NULL);");

        // Generirani termini za tekući tjedan (kao da je pokrenuto POST /api/groups/generate-appointments).
        InsertGroupAppointment(AppointmentMonday, org, admin, GroupMorningYoga, SlotMonday, monday.AddHours(7),
            "Marko", "Trener", "Centar");
        InsertGroupAppointment(AppointmentWednesday, org, admin, GroupMorningYoga, SlotWednesday, wednesday.AddHours(7),
            "Marko", "Trener", "Centar");
        InsertGroupAppointment(AppointmentTuesday, org, admin, GroupEveningFunctional, SlotTuesday, tuesday.AddHours(19),
            "Iva", "Vanjska", "Riverside");

        // Evidencija prisutnosti: Ivana na ponedjeljnom terminu (SessionPackage, ulazak skinut) i Petar na
        // utorkovom terminu (MonthlyPackage, ništa se ne skida).
        Execute.Sql($@"
            INSERT INTO dunelight.appointment_attendances
                (id, appointment_id, client_id, attended, coverage_type, client_package_id, package_entry_deducted,
                 package_entry_returned, note, created_at)
            VALUES
                ('{Guid.NewGuid()}', '{AppointmentMonday}',
                 (SELECT id FROM dunelight.clients WHERE organization_id = '{org}' AND member_number = 1),
                 true, 'SessionPackage', '{ClientPackageIvanaGroupTen}', true, false, NULL, '{now:yyyy-MM-ddTHH:mm:sszzz}');");

        Execute.Sql($@"
            INSERT INTO dunelight.appointment_attendances
                (id, appointment_id, client_id, attended, coverage_type, client_package_id, package_entry_deducted,
                 package_entry_returned, note, created_at)
            VALUES
                ('{Guid.NewGuid()}', '{AppointmentTuesday}',
                 (SELECT id FROM dunelight.clients WHERE organization_id = '{org}' AND member_number = 2),
                 true, 'MonthlyPackage', '{ClientPackagePetarMonthly}', false, false, NULL, '{now:yyyy-MM-ddTHH:mm:sszzz}');");
    }

    private void InsertGroupAppointment(
        Guid id, string org, string admin, Guid groupId, Guid groupSlotId, DateTimeOffset startsAt,
        string employeeFirstName, string employeeLastName, string locationName)
    {
        Execute.Sql($@"
            INSERT INTO dunelight.appointments
                (id, organization_id, form, starts_at, duration_minutes, service_id, employee_id, location_id,
                 amount, suggested_amount, is_amount_manually_overridden, payment_method, is_paid, status, note,
                 group_id, group_slot_id, created_at, created_by)
            VALUES
                ('{id}', '{org}', 'Group', '{startsAt:yyyy-MM-ddTHH:mm:sszzz}', 60,
                 '{ServiceGroupTraining}',
                 (SELECT id FROM dunelight.employees WHERE organization_id = '{org}' AND first_name = '{employeeFirstName}' AND last_name = '{employeeLastName}'),
                 (SELECT id FROM dunelight.locations WHERE organization_id = '{org}' AND name = '{locationName}'),
                 0, 0, false, NULL, false, 'Scheduled', NULL,
                 '{groupId}', '{groupSlotId}', '{DateTimeOffset.UtcNow:yyyy-MM-ddTHH:mm:sszzz}', '{admin}');");
    }
}

[DeveloperMigration(2026, 07, 21, Developer.SilvioHabazin, 36)]
public class AlterAppointmentEmployeeIdNullable : DuneLightMigration
{
    public override void Up()
    {
        // Grupni termin čija grupa nema zadanog trenera generira se bez trenera — dodjeljuje se ručno naknadno.
        // Individualni termini i dalje uvijek imaju trenera (poslovno pravilo živi u AppointmentService, ne u bazi).
        Alter.Table(Tables.Appointments)
            .InSchema(Tables.Schemas.DuneLight)
            .AlterColumn("employee_id").AsGuid().Nullable();
    }
}
