using BlueDragon.DuneLight.DatabaseMigration.Extensions;
using BlueDragon.DuneLight.DatabaseMigration.Models;
using FluentMigrator;

namespace BlueDragon.DuneLight.DatabaseMigration.Migrations._2026;

[DeveloperMigration(2026, 08, 26, Developer.SilvioHabazin, 0)]
public class CreateCompanyHolidaysTable : DuneLightMigration
{
    public override void Up()
    {
        Create.Table(Tables.CompanyHolidays)
            .InSchema(Tables.Schemas.DuneLight)
            .WithColumn("id").AsGuid().NotNullable().PrimaryKey("pk_company_holidays")
            .WithColumn("organization_id").AsGuid().NotNullable()
            .WithColumn("company_id").AsGuid().NotNullable()
            .WithColumn("date").AsDateTimeOffset().NotNullable()
            .WithColumn("name").AsString(255).NotNullable()
            .WithColumn("is_auto_generated").AsBoolean().NotNullable()
            .WithColumn("created_at").AsDateTimeOffset().NotNullable()
            .WithColumn("created_by").AsGuid().Nullable();

        Create.ForeignKey("fk_company_holidays_organization_id")
            .FromTable(Tables.CompanyHolidays).InSchema(Tables.Schemas.DuneLight).ForeignColumn("organization_id")
            .ToTable(Tables.Organizations).InSchema(Tables.Schemas.DuneLight).PrimaryColumn("id");

        Create.ForeignKey("fk_company_holidays_company_id")
            .FromTable(Tables.CompanyHolidays).InSchema(Tables.Schemas.DuneLight).ForeignColumn("company_id")
            .ToTable(Tables.Companies).InSchema(Tables.Schemas.DuneLight).PrimaryColumn("id");

        Create.Index("ux_company_holidays_org_company_date")
            .OnTable(Tables.CompanyHolidays).InSchema(Tables.Schemas.DuneLight)
            .OnColumn("organization_id").Ascending()
            .OnColumn("company_id").Ascending()
            .OnColumn("date").Ascending()
            .WithOptions().Unique();
    }
}
