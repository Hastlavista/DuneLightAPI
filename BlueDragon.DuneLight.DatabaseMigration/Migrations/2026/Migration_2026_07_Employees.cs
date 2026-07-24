using System;
using System.Security.Cryptography;
using System.Text;
using BlueDragon.DuneLight.DatabaseMigration.Extensions;
using BlueDragon.DuneLight.DatabaseMigration.Models;
using FluentMigrator;

namespace BlueDragon.DuneLight.DatabaseMigration.Migrations._2026;

[DeveloperMigration(2026, 07, 20, Developer.SilvioHabazin, 10)]
public class AddIsActiveToUsers : DuneLightMigration
{
    public override void Up()
    {
        Alter.Table(Tables.Users)
            .InSchema(Tables.Schemas.DuneLight)
            .AddColumn("is_active").AsBoolean().NotNullable().WithDefaultValue(true);
    }
}

[DeveloperMigration(2026, 07, 20, Developer.SilvioHabazin, 11)]
public class CreateEngagementTypesTable : DuneLightMigration
{
    public override void Up()
    {
        Create.Table(Tables.EngagementTypes)
            .InSchema(Tables.Schemas.DuneLight)
            .WithColumn("id").AsGuid().NotNullable().PrimaryKey("pk_engagement_types")
            .WithColumn("organization_id").AsGuid().NotNullable()
            .WithColumn("name").AsString(255).NotNullable()
            .WithColumn("is_active").AsBoolean().NotNullable().WithDefaultValue(true)
            .WithColumn("sort_order").AsInt32().NotNullable().WithDefaultValue(0)
            .WithColumn("created_at").AsDateTimeOffset().NotNullable()
            .WithColumn("created_by").AsGuid().Nullable()
            .WithColumn("updated_at").AsDateTimeOffset().Nullable()
            .WithColumn("updated_by").AsGuid().Nullable();

        Create.ForeignKey("fk_engagement_types_organization_id")
            .FromTable(Tables.EngagementTypes).InSchema(Tables.Schemas.DuneLight).ForeignColumn("organization_id")
            .ToTable(Tables.Organizations).InSchema(Tables.Schemas.DuneLight).PrimaryColumn("id");

        Execute.Sql(
            "CREATE UNIQUE INDEX ux_engagement_types_org_name_active " +
            "ON dunelight.engagement_types (organization_id, name) WHERE is_active = true;");
    }
}

[DeveloperMigration(2026, 07, 20, Developer.SilvioHabazin, 12)]
public class CreateEmployeesTable : DuneLightMigration
{
    public override void Up()
    {
        Create.Table(Tables.Employees)
            .InSchema(Tables.Schemas.DuneLight)
            .WithColumn("id").AsGuid().NotNullable().PrimaryKey("pk_employees")
            .WithColumn("organization_id").AsGuid().NotNullable()
            .WithColumn("first_name").AsString(255).NotNullable()
            .WithColumn("last_name").AsString(255).NotNullable()
            .WithColumn("phone").AsString(50).Nullable()
            .WithColumn("email").AsString(255).Nullable()
            .WithColumn("date_of_birth").AsDateTimeOffset().Nullable()
            .WithColumn("address").AsCustom("text").Nullable()
            .WithColumn("oib").AsString(11).Nullable()
            .WithColumn("note").AsCustom("text").Nullable()
            .WithColumn("compensation_note").AsCustom("text").Nullable()
            .WithColumn("color_hex").AsString(7).Nullable()
            .WithColumn("sort_order").AsInt32().NotNullable().WithDefaultValue(0)
            .WithColumn("employment_start_date").AsDateTimeOffset().NotNullable()
            .WithColumn("employment_end_date").AsDateTimeOffset().Nullable()
            .WithColumn("engagement_type_id").AsGuid().NotNullable()
            .WithColumn("is_active").AsBoolean().NotNullable().WithDefaultValue(true)
            .WithColumn("user_id").AsGuid().NotNullable()
            .WithColumn("created_at").AsDateTimeOffset().NotNullable()
            .WithColumn("created_by").AsGuid().Nullable()
            .WithColumn("updated_at").AsDateTimeOffset().Nullable()
            .WithColumn("updated_by").AsGuid().Nullable();

        Create.ForeignKey("fk_employees_organization_id")
            .FromTable(Tables.Employees).InSchema(Tables.Schemas.DuneLight).ForeignColumn("organization_id")
            .ToTable(Tables.Organizations).InSchema(Tables.Schemas.DuneLight).PrimaryColumn("id");

        Create.ForeignKey("fk_employees_engagement_type_id")
            .FromTable(Tables.Employees).InSchema(Tables.Schemas.DuneLight).ForeignColumn("engagement_type_id")
            .ToTable(Tables.EngagementTypes).InSchema(Tables.Schemas.DuneLight).PrimaryColumn("id");

        Create.ForeignKey("fk_employees_user_id")
            .FromTable(Tables.Employees).InSchema(Tables.Schemas.DuneLight).ForeignColumn("user_id")
            .ToTable(Tables.Users).InSchema(Tables.Schemas.DuneLight).PrimaryColumn("id");

        Create.Index("ux_employees_user_id")
            .OnTable(Tables.Employees).InSchema(Tables.Schemas.DuneLight)
            .OnColumn("user_id").Ascending()
            .WithOptions().Unique();

        Create.Index("ix_employees_organization_id")
            .OnTable(Tables.Employees).InSchema(Tables.Schemas.DuneLight)
            .OnColumn("organization_id").Ascending();
    }
}

[DeveloperMigration(2026, 07, 20, Developer.SilvioHabazin, 13)]
public class CreateEmployeeLocationsTable : DuneLightMigration
{
    public override void Up()
    {
        Create.Table(Tables.EmployeeLocations)
            .InSchema(Tables.Schemas.DuneLight)
            .WithColumn("id").AsGuid().NotNullable().PrimaryKey("pk_employee_locations")
            .WithColumn("employee_id").AsGuid().NotNullable()
            .WithColumn("location_id").AsGuid().NotNullable()
            .WithColumn("is_primary").AsBoolean().NotNullable().WithDefaultValue(false);

        Create.ForeignKey("fk_employee_locations_employee_id")
            .FromTable(Tables.EmployeeLocations).InSchema(Tables.Schemas.DuneLight).ForeignColumn("employee_id")
            .ToTable(Tables.Employees).InSchema(Tables.Schemas.DuneLight).PrimaryColumn("id")
            .OnDelete(System.Data.Rule.Cascade);

        Create.ForeignKey("fk_employee_locations_location_id")
            .FromTable(Tables.EmployeeLocations).InSchema(Tables.Schemas.DuneLight).ForeignColumn("location_id")
            .ToTable(Tables.Locations).InSchema(Tables.Schemas.DuneLight).PrimaryColumn("id");

        Create.Index("ux_employee_locations_employee_location")
            .OnTable(Tables.EmployeeLocations).InSchema(Tables.Schemas.DuneLight)
            .OnColumn("employee_id").Ascending()
            .OnColumn("location_id").Ascending()
            .WithOptions().Unique();

        // Točno jedna matična lokacija po zaposleniku.
        Execute.Sql(
            "CREATE UNIQUE INDEX ux_employee_locations_primary " +
            "ON dunelight.employee_locations (employee_id) WHERE is_primary = true;");
    }
}

[DeveloperMigration(2026, 07, 20, Developer.SilvioHabazin, 14)]
public class CreateEmployeeServicesTable : DuneLightMigration
{
    public override void Up()
    {
        Create.Table(Tables.EmployeeServices)
            .InSchema(Tables.Schemas.DuneLight)
            .WithColumn("id").AsGuid().NotNullable().PrimaryKey("pk_employee_services")
            .WithColumn("employee_id").AsGuid().NotNullable()
            .WithColumn("service_id").AsGuid().NotNullable();

        Create.ForeignKey("fk_employee_services_employee_id")
            .FromTable(Tables.EmployeeServices).InSchema(Tables.Schemas.DuneLight).ForeignColumn("employee_id")
            .ToTable(Tables.Employees).InSchema(Tables.Schemas.DuneLight).PrimaryColumn("id")
            .OnDelete(System.Data.Rule.Cascade);

        Create.ForeignKey("fk_employee_services_service_id")
            .FromTable(Tables.EmployeeServices).InSchema(Tables.Schemas.DuneLight).ForeignColumn("service_id")
            .ToTable(Tables.Services).InSchema(Tables.Schemas.DuneLight).PrimaryColumn("id");

        Create.Index("ux_employee_services_employee_service")
            .OnTable(Tables.EmployeeServices).InSchema(Tables.Schemas.DuneLight)
            .OnColumn("employee_id").Ascending()
            .OnColumn("service_id").Ascending()
            .WithOptions().Unique();
    }
}

[DeveloperMigration(2026, 07, 20, Developer.SilvioHabazin, 15)]
public class CreateEmployeeAuditLogTable : DuneLightMigration
{
    public override void Up()
    {
        Create.Table(Tables.EmployeeAuditLog)
            .InSchema(Tables.Schemas.DuneLight)
            .WithColumn("id").AsGuid().NotNullable().PrimaryKey("pk_employee_audit_log")
            .WithColumn("employee_id").AsGuid().NotNullable()
            .WithColumn("change_type").AsString(20).NotNullable()
            .WithColumn("old_value").AsString(255).Nullable()
            .WithColumn("new_value").AsString(255).Nullable()
            .WithColumn("changed_at").AsDateTimeOffset().NotNullable()
            .WithColumn("changed_by").AsGuid().Nullable();

        // Bez ON DELETE CASCADE — postojanje zapisa blokira tvrdo brisanje zaposlenika (vidi EmployeeService.Delete).
        Create.ForeignKey("fk_employee_audit_log_employee_id")
            .FromTable(Tables.EmployeeAuditLog).InSchema(Tables.Schemas.DuneLight).ForeignColumn("employee_id")
            .ToTable(Tables.Employees).InSchema(Tables.Schemas.DuneLight).PrimaryColumn("id");

        Create.Index("ix_employee_audit_log_employee_id")
            .OnTable(Tables.EmployeeAuditLog).InSchema(Tables.Schemas.DuneLight)
            .OnColumn("employee_id").Ascending();
    }
}

[DeveloperMigration(2026, 07, 20, Developer.SilvioHabazin, 16)]
public class SeedEmployeesMinimal : DuneLightMigration
{
    // Isti fiksni GUID kao BlueDragon.DuneLight.Core.Shared.DevDefaults.OrganizationId/UserId —
    // dev-bypass principal (kad je Auth:Enabled = false) gleda točno ovu organizaciju/admina.
    private static readonly Guid OrganizationId = Guid.Parse("00000000-0000-0000-0000-000000000001");
    private static readonly Guid AdminUserId = Guid.Parse("00000000-0000-0000-0000-000000000002");

    private static readonly Guid LocationCenter = Guid.NewGuid();
    private static readonly Guid LocationRiverside = Guid.NewGuid();

    private static readonly Guid EngagementFullTime = Guid.NewGuid();
    private static readonly Guid EngagementPartTime = Guid.NewGuid();
    private static readonly Guid EngagementFreelance = Guid.NewGuid();
    private static readonly Guid EngagementExternal = Guid.NewGuid();

    private static readonly Guid TrainerUser1Id = Guid.NewGuid();
    private static readonly Guid TrainerUser2Id = Guid.NewGuid();

    private static readonly Guid AdminEmployeeId = Guid.NewGuid();
    private static readonly Guid TrainerEmployee1Id = Guid.NewGuid();
    private static readonly Guid TrainerEmployee2Id = Guid.NewGuid();

    public override void Up()
    {
        DateTimeOffset now = DateTimeOffset.UtcNow;

        Insert.IntoTable(Tables.Organizations).InSchema(Tables.Schemas.DuneLight).Row(new
        {
            id = OrganizationId,
            name = "Demo Wellness Studio",
            slug = "demo-wellness-studio",
            created_at = now
        });

        Insert.IntoTable(Tables.Users).InSchema(Tables.Schemas.DuneLight).Row(new
        {
            id = AdminUserId,
            organization_id = OrganizationId,
            email = "admin@dunelight.local",
            password_hash = HashPassword("password123"),
            api_key = "dev-admin-api-key-00000000000000000001",
            role = "Admin",
            is_active = true,
            created_at = now
        });

        Insert.IntoTable(Tables.Users).InSchema(Tables.Schemas.DuneLight).Row(new
        {
            id = TrainerUser1Id,
            organization_id = OrganizationId,
            email = "marko@dunelight.local",
            password_hash = HashPassword("password123"),
            api_key = "dev-trainer-api-key-0000000000000001",
            role = "Member",
            is_active = true,
            created_at = now
        });

        Insert.IntoTable(Tables.Users).InSchema(Tables.Schemas.DuneLight).Row(new
        {
            id = TrainerUser2Id,
            organization_id = OrganizationId,
            email = "iva@dunelight.local",
            password_hash = HashPassword("password123"),
            api_key = "dev-trainer-api-key-0000000000000002",
            role = "Member",
            is_active = true,
            created_at = now
        });

        Insert.IntoTable(Tables.Locations).InSchema(Tables.Schemas.DuneLight).Row(new
        {
            id = LocationCenter,
            organization_id = OrganizationId,
            name = "Centar",
            address = "Ilica 1, Zagreb",
            phone = "+385 1 111 1111",
            color_hex = "#2E86AB",
            is_active = true,
            note = (string)null,
            sort_order = 0,
            created_at = now,
            created_by = AdminUserId
        });

        Insert.IntoTable(Tables.Locations).InSchema(Tables.Schemas.DuneLight).Row(new
        {
            id = LocationRiverside,
            organization_id = OrganizationId,
            name = "Riverside",
            address = "Savska cesta 100, Zagreb",
            phone = "+385 1 222 2222",
            color_hex = "#A23B72",
            is_active = true,
            note = (string)null,
            sort_order = 1,
            created_at = now,
            created_by = AdminUserId
        });

        (Guid Id, string Name, int Sort)[] engagementTypes =
        {
            (EngagementFullTime, "Puno radno vrijeme", 0),
            (EngagementPartTime, "Pola radnog vremena", 1),
            (EngagementFreelance, "Honorarno", 2),
            (EngagementExternal, "Vanjski suradnik", 3)
        };

        foreach ((Guid id, string name, int sort) in engagementTypes)
        {
            Insert.IntoTable(Tables.EngagementTypes).InSchema(Tables.Schemas.DuneLight).Row(new
            {
                id,
                organization_id = OrganizationId,
                name,
                is_active = true,
                sort_order = sort,
                created_at = now,
                created_by = AdminUserId
            });
        }

        Insert.IntoTable(Tables.Employees).InSchema(Tables.Schemas.DuneLight).Row(new
        {
            id = AdminEmployeeId,
            organization_id = OrganizationId,
            first_name = "Ana",
            last_name = "Vlasnik",
            phone = (string)null,
            email = "admin@dunelight.local",
            date_of_birth = (DateTimeOffset?)null,
            address = (string)null,
            oib = (string)null,
            note = (string)null,
            compensation_note = "Vlasnica, fiksna plaća — vodi se u financijama.",
            color_hex = "#3D5A80",
            sort_order = 0,
            employment_start_date = now,
            employment_end_date = (DateTimeOffset?)null,
            engagement_type_id = EngagementFullTime,
            is_active = true,
            user_id = AdminUserId,
            created_at = now,
            created_by = AdminUserId
        });

        Insert.IntoTable(Tables.Employees).InSchema(Tables.Schemas.DuneLight).Row(new
        {
            id = TrainerEmployee1Id,
            organization_id = OrganizationId,
            first_name = "Marko",
            last_name = "Trener",
            phone = (string)null,
            email = "marko@dunelight.local",
            date_of_birth = (DateTimeOffset?)null,
            address = (string)null,
            oib = (string)null,
            note = (string)null,
            compensation_note = "Fiksno 800 EUR + 15% od Bowen tretmana.",
            color_hex = "#98C1D9",
            sort_order = 1,
            employment_start_date = now,
            employment_end_date = (DateTimeOffset?)null,
            engagement_type_id = EngagementFullTime,
            is_active = true,
            user_id = TrainerUser1Id,
            created_at = now,
            created_by = AdminUserId
        });

        Insert.IntoTable(Tables.Employees).InSchema(Tables.Schemas.DuneLight).Row(new
        {
            id = TrainerEmployee2Id,
            organization_id = OrganizationId,
            first_name = "Iva",
            last_name = "Vanjska",
            phone = (string)null,
            email = "iva@dunelight.local",
            date_of_birth = (DateTimeOffset?)null,
            address = (string)null,
            oib = (string)null,
            note = (string)null,
            compensation_note = "Vanjski suradnik, 50% od masaže.",
            color_hex = "#EE6C4D",
            sort_order = 2,
            employment_start_date = now,
            employment_end_date = (DateTimeOffset?)null,
            engagement_type_id = EngagementExternal,
            is_active = true,
            user_id = TrainerUser2Id,
            created_at = now,
            created_by = AdminUserId
        });

        InsertEmployeeLocation(Guid.NewGuid(), AdminEmployeeId, LocationCenter, true);
        InsertEmployeeLocation(Guid.NewGuid(), TrainerEmployee1Id, LocationCenter, true);
        InsertEmployeeLocation(Guid.NewGuid(), TrainerEmployee2Id, LocationRiverside, true);
    }

    private void InsertEmployeeLocation(Guid id, Guid employeeId, Guid locationId, bool isPrimary)
    {
        Insert.IntoTable(Tables.EmployeeLocations).InSchema(Tables.Schemas.DuneLight).Row(new
        {
            id,
            employee_id = employeeId,
            location_id = locationId,
            is_primary = isPrimary
        });
    }

    private static string HashPassword(string password)
    {
        byte[] bytes = SHA256.HashData(Encoding.UTF8.GetBytes(password));
        return Convert.ToHexString(bytes).ToLower();
    }
}