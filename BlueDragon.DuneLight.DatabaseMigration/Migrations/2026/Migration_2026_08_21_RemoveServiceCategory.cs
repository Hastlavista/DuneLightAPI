using BlueDragon.DuneLight.DatabaseMigration.Extensions;
using BlueDragon.DuneLight.DatabaseMigration.Models;
using FluentMigrator;

namespace BlueDragon.DuneLight.DatabaseMigration.Migrations._2026;

/// <summary>
/// ServiceCategory prestaje postojati kao zaseban entitet — ExecutionMode i ColorHex sele se
/// direktno na Service. RequiresClient/CountsTowardRevenue nestaju bez zamjene (postaju
/// podrazumijevana istina: svaka usluga zahtijeva klijenta i ulazi u promet).
/// </summary>
[DeveloperMigration(2026, 08, 21, Developer.SilvioHabazin, 0)]
public class AddExecutionModeAndColorHexToServices : DuneLightMigration
{
    public override void Up()
    {
        Alter.Table(Tables.Services)
            .InSchema(Tables.Schemas.DuneLight)
            .AddColumn("execution_mode").AsString(20).Nullable()
            .AddColumn("color_hex").AsString(7).Nullable();

        Execute.Sql(
            "UPDATE dunelight.services s " +
            "SET execution_mode = sc.execution_mode, color_hex = sc.color_hex " +
            "FROM dunelight.service_categories sc " +
            "WHERE s.service_category_id = sc.id;");

        Alter.Table(Tables.Services)
            .InSchema(Tables.Schemas.DuneLight)
            .AlterColumn("execution_mode").AsString(20).NotNullable();
    }
}

[DeveloperMigration(2026, 08, 21, Developer.SilvioHabazin, 1)]
public class DropServiceCategoryFromServices : DuneLightMigration
{
    public override void Up()
    {
        Delete.ForeignKey("fk_services_service_category_id")
            .OnTable(Tables.Services).InSchema(Tables.Schemas.DuneLight);

        Delete.Column("service_category_id").FromTable(Tables.Services).InSchema(Tables.Schemas.DuneLight);
    }
}

[DeveloperMigration(2026, 08, 21, Developer.SilvioHabazin, 2)]
public class DropServiceCategoriesTable : DuneLightMigration
{
    public override void Up()
    {
        Execute.Sql("DROP INDEX IF EXISTS dunelight.ux_service_categories_org_name_active;");

        Delete.ForeignKey("fk_service_categories_organization_id")
            .OnTable(Tables.ServiceCategories).InSchema(Tables.Schemas.DuneLight);

        Delete.Table(Tables.ServiceCategories).InSchema(Tables.Schemas.DuneLight);
    }
}
