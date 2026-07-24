using System;
using BlueDragon.DuneLight.DatabaseMigration.Extensions;
using BlueDragon.DuneLight.DatabaseMigration.Models;
using FluentMigrator;

namespace BlueDragon.DuneLight.DatabaseMigration.Migrations._2026;

[DeveloperMigration(2026, 07, 21, Developer.SilvioHabazin, 37)]
public class CreateRosterTypesTable : DuneLightMigration
{
    public override void Up()
    {
        Create.Table(Tables.RosterTypes)
            .InSchema(Tables.Schemas.DuneLight)
            .WithColumn("id").AsGuid().NotNullable().PrimaryKey("pk_roster_types")
            .WithColumn("organization_id").AsGuid().NotNullable()
            .WithColumn("name").AsString(255).NotNullable()
            .WithColumn("color_hex").AsString(7).Nullable()
            .WithColumn("counts_as_work").AsBoolean().NotNullable()
            .WithColumn("is_absence").AsBoolean().NotNullable()
            .WithColumn("requires_time").AsBoolean().NotNullable()
            .WithColumn("is_active").AsBoolean().NotNullable().WithDefaultValue(true)
            .WithColumn("sort_order").AsInt32().NotNullable().WithDefaultValue(0)
            .WithColumn("created_at").AsDateTimeOffset().NotNullable()
            .WithColumn("created_by").AsGuid().Nullable()
            .WithColumn("updated_at").AsDateTimeOffset().Nullable()
            .WithColumn("updated_by").AsGuid().Nullable();

        Create.ForeignKey("fk_roster_types_organization_id")
            .FromTable(Tables.RosterTypes).InSchema(Tables.Schemas.DuneLight).ForeignColumn("organization_id")
            .ToTable(Tables.Organizations).InSchema(Tables.Schemas.DuneLight).PrimaryColumn("id");

        Execute.Sql(
            "CREATE UNIQUE INDEX ux_roster_types_organization_name " +
            "ON dunelight.roster_types (organization_id, name) WHERE is_active = true;");
    }
}

[DeveloperMigration(2026, 07, 21, Developer.SilvioHabazin, 38)]
public class CreateRosterEntriesTable : DuneLightMigration
{
    public override void Up()
    {
        Create.Table(Tables.RosterEntries)
            .InSchema(Tables.Schemas.DuneLight)
            .WithColumn("id").AsGuid().NotNullable().PrimaryKey("pk_roster_entries")
            .WithColumn("organization_id").AsGuid().NotNullable()
            .WithColumn("employee_id").AsGuid().NotNullable()
            .WithColumn("roster_type_id").AsGuid().NotNullable()
            .WithColumn("date_from").AsDateTimeOffset().NotNullable()
            .WithColumn("date_to").AsDateTimeOffset().Nullable()
            .WithColumn("start_time").AsTime().Nullable()
            .WithColumn("end_time").AsTime().Nullable()
            .WithColumn("duration_hours").AsDecimal(5, 2).Nullable()
            .WithColumn("note").AsCustom("text").Nullable()
            .WithColumn("created_at").AsDateTimeOffset().NotNullable()
            .WithColumn("created_by").AsGuid().Nullable()
            .WithColumn("updated_at").AsDateTimeOffset().Nullable()
            .WithColumn("updated_by").AsGuid().Nullable();

        Create.ForeignKey("fk_roster_entries_organization_id")
            .FromTable(Tables.RosterEntries).InSchema(Tables.Schemas.DuneLight).ForeignColumn("organization_id")
            .ToTable(Tables.Organizations).InSchema(Tables.Schemas.DuneLight).PrimaryColumn("id");

        Create.ForeignKey("fk_roster_entries_employee_id")
            .FromTable(Tables.RosterEntries).InSchema(Tables.Schemas.DuneLight).ForeignColumn("employee_id")
            .ToTable(Tables.Employees).InSchema(Tables.Schemas.DuneLight).PrimaryColumn("id");

        Create.ForeignKey("fk_roster_entries_roster_type_id")
            .FromTable(Tables.RosterEntries).InSchema(Tables.Schemas.DuneLight).ForeignColumn("roster_type_id")
            .ToTable(Tables.RosterTypes).InSchema(Tables.Schemas.DuneLight).PrimaryColumn("id");

        Create.Index("ix_roster_entries_org_employee_date")
            .OnTable(Tables.RosterEntries).InSchema(Tables.Schemas.DuneLight)
            .OnColumn("organization_id").Ascending()
            .OnColumn("employee_id").Ascending()
            .OnColumn("date_from").Ascending();
    }
}

[DeveloperMigration(2026, 07, 21, Developer.SilvioHabazin, 39)]
public class CreateRosterAuditLogTable : DuneLightMigration
{
    public override void Up()
    {
        // Namjerno BEZ FK na roster_entries (za razliku od employee_audit_log/group_audit_log/appointment_audit_log,
        // koji cascade-aju uz roditelja) — roster dopušta trajno brisanje zapisa, a audit trag mora preživjeti to brisanje.
        Create.Table(Tables.RosterAuditLog)
            .InSchema(Tables.Schemas.DuneLight)
            .WithColumn("id").AsGuid().NotNullable().PrimaryKey("pk_roster_audit_log")
            .WithColumn("roster_entry_id").AsGuid().NotNullable()
            .WithColumn("change_type").AsString(30).NotNullable()
            .WithColumn("old_value").AsCustom("text").Nullable()
            .WithColumn("new_value").AsCustom("text").Nullable()
            .WithColumn("changed_at").AsDateTimeOffset().NotNullable()
            .WithColumn("changed_by").AsGuid().Nullable();

        Create.Index("ix_roster_audit_log_roster_entry_id")
            .OnTable(Tables.RosterAuditLog).InSchema(Tables.Schemas.DuneLight)
            .OnColumn("roster_entry_id").Ascending();
    }
}

[DeveloperMigration(2026, 07, 21, Developer.SilvioHabazin, 40)]
public class SeedRosterMinimal : DuneLightMigration
{
    private static readonly Guid OrganizationId = Guid.Parse("00000000-0000-0000-0000-000000000001");
    private static readonly Guid AdminUserId = Guid.Parse("00000000-0000-0000-0000-000000000002");

    private static readonly Guid TypeShift = Guid.NewGuid();
    private static readonly Guid TypeBowen = Guid.NewGuid();
    private static readonly Guid TypeRecDvok = Guid.NewGuid();
    private static readonly Guid TypeAnnualLeave = Guid.NewGuid();
    private static readonly Guid TypeSickLeave = Guid.NewGuid();
    private static readonly Guid TypeHoliday = Guid.NewGuid();

    public override void Up()
    {
        DateTimeOffset now = DateTimeOffset.UtcNow;
        string org = OrganizationId.ToString();
        string admin = AdminUserId.ToString();

        DateTimeOffset monthStart = new DateTimeOffset(now.Year, now.Month, 1, 0, 0, 0, now.Offset);

        Insert.IntoTable(Tables.RosterTypes).InSchema(Tables.Schemas.DuneLight).Row(new
        {
            id = TypeShift,
            organization_id = OrganizationId,
            name = "Smjena",
            color_hex = "#3D5A80",
            counts_as_work = true,
            is_absence = false,
            requires_time = true,
            is_active = true,
            sort_order = 0,
            created_at = now,
            created_by = AdminUserId
        });

        Insert.IntoTable(Tables.RosterTypes).InSchema(Tables.Schemas.DuneLight).Row(new
        {
            id = TypeBowen,
            organization_id = OrganizationId,
            name = "Bowen",
            color_hex = "#98C1D9",
            counts_as_work = true,
            is_absence = false,
            requires_time = true,
            is_active = true,
            sort_order = 1,
            created_at = now,
            created_by = AdminUserId
        });

        Insert.IntoTable(Tables.RosterTypes).InSchema(Tables.Schemas.DuneLight).Row(new
        {
            id = TypeRecDvok,
            organization_id = OrganizationId,
            name = "Rec/dvok",
            color_hex = "#EE6C4D",
            counts_as_work = true,
            is_absence = false,
            requires_time = true,
            is_active = true,
            sort_order = 2,
            created_at = now,
            created_by = AdminUserId
        });

        Insert.IntoTable(Tables.RosterTypes).InSchema(Tables.Schemas.DuneLight).Row(new
        {
            id = TypeAnnualLeave,
            organization_id = OrganizationId,
            name = "Godišnji",
            color_hex = "#8FB339",
            counts_as_work = false,
            is_absence = true,
            requires_time = false,
            is_active = true,
            sort_order = 3,
            created_at = now,
            created_by = AdminUserId
        });

        Insert.IntoTable(Tables.RosterTypes).InSchema(Tables.Schemas.DuneLight).Row(new
        {
            id = TypeSickLeave,
            organization_id = OrganizationId,
            name = "Bolovanje",
            color_hex = "#C1666B",
            counts_as_work = false,
            is_absence = true,
            requires_time = false,
            is_active = true,
            sort_order = 4,
            created_at = now,
            created_by = AdminUserId
        });

        Insert.IntoTable(Tables.RosterTypes).InSchema(Tables.Schemas.DuneLight).Row(new
        {
            id = TypeHoliday,
            organization_id = OrganizationId,
            name = "Praznik",
            color_hex = "#8E44AD",
            counts_as_work = false,
            is_absence = true,
            requires_time = false,
            is_active = true,
            sort_order = 5,
            created_at = now,
            created_by = AdminUserId
        });

        // Marko: dvokratni radni dan (5. u mjesecu) — dva zasebna zapisa istog dana.
        InsertWorkEntry(Guid.NewGuid(), org, admin, "Marko", "Trener", TypeShift,
            monthStart.AddDays(4), new TimeSpan(7, 0, 0), new TimeSpan(12, 0, 0));
        InsertWorkEntry(Guid.NewGuid(), org, admin, "Marko", "Trener", TypeShift,
            monthStart.AddDays(4), new TimeSpan(17, 0, 0), new TimeSpan(19, 0, 0));

        // Marko: godišnji kao raspon (10.-14. u mjesecu) — jedan zapis pokriva 5 dana.
        Execute.Sql($@"
            INSERT INTO dunelight.roster_entries
                (id, organization_id, employee_id, roster_type_id, date_from, date_to,
                 start_time, end_time, duration_hours, note, created_at, created_by)
            VALUES
                ('{Guid.NewGuid()}', '{org}',
                 (SELECT id FROM dunelight.employees WHERE organization_id = '{org}' AND first_name = 'Marko' AND last_name = 'Trener'),
                 '{TypeAnnualLeave}', '{monthStart.AddDays(9):yyyy-MM-ddTHH:mm:sszzz}', '{monthStart.AddDays(13):yyyy-MM-ddTHH:mm:sszzz}',
                 NULL, NULL, NULL, NULL, '{now:yyyy-MM-ddTHH:mm:sszzz}', '{admin}');");

        // Iva: Bowen (7. u mjesecu, 09:00-11:00).
        InsertWorkEntry(Guid.NewGuid(), org, admin, "Iva", "Vanjska", TypeBowen,
            monthStart.AddDays(6), new TimeSpan(9, 0, 0), new TimeSpan(11, 0, 0));

        // Iva: otvoreno bolovanje (od 20. u mjesecu, datum do još nepoznat).
        Execute.Sql($@"
            INSERT INTO dunelight.roster_entries
                (id, organization_id, employee_id, roster_type_id, date_from, date_to,
                 start_time, end_time, duration_hours, note, created_at, created_by)
            VALUES
                ('{Guid.NewGuid()}', '{org}',
                 (SELECT id FROM dunelight.employees WHERE organization_id = '{org}' AND first_name = 'Iva' AND last_name = 'Vanjska'),
                 '{TypeSickLeave}', '{monthStart.AddDays(19):yyyy-MM-ddTHH:mm:sszzz}', NULL,
                 NULL, NULL, NULL, NULL, '{now:yyyy-MM-ddTHH:mm:sszzz}', '{admin}');");
    }

    private void InsertWorkEntry(
        Guid id, string org, string admin, string employeeFirstName, string employeeLastName,
        Guid rosterTypeId, DateTimeOffset date, TimeSpan startTime, TimeSpan endTime)
    {
        decimal durationHours = (decimal)(endTime - startTime).TotalHours;

        Execute.Sql($@"
            INSERT INTO dunelight.roster_entries
                (id, organization_id, employee_id, roster_type_id, date_from, date_to,
                 start_time, end_time, duration_hours, note, created_at, created_by)
            VALUES
                ('{id}', '{org}',
                 (SELECT id FROM dunelight.employees WHERE organization_id = '{org}' AND first_name = '{employeeFirstName}' AND last_name = '{employeeLastName}'),
                 '{rosterTypeId}', '{date:yyyy-MM-ddTHH:mm:sszzz}', NULL,
                 '{startTime:hh\:mm\:ss}', '{endTime:hh\:mm\:ss}', {durationHours.ToString(System.Globalization.CultureInfo.InvariantCulture)},
                 NULL, '{DateTimeOffset.UtcNow:yyyy-MM-ddTHH:mm:sszzz}', '{admin}');");
    }
}
