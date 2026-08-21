using BlueDragon.DuneLight.DatabaseMigration.Extensions;
using BlueDragon.DuneLight.DatabaseMigration.Models;
using FluentMigrator;

namespace BlueDragon.DuneLight.DatabaseMigration.Migrations._2026;

[DeveloperMigration(2026, 08, 20, Developer.SilvioHabazin, 0)]
public class CreateScheduleBreaksTable : DuneLightMigration
{
    public override void Up()
    {
        Create.Table(Tables.ScheduleBreaks)
            .InSchema(Tables.Schemas.DuneLight)
            .WithColumn("id").AsGuid().NotNullable().PrimaryKey("pk_schedule_breaks")
            .WithColumn("organization_id").AsGuid().NotNullable()
            .WithColumn("employee_id").AsGuid().NotNullable()
            .WithColumn("company_id").AsGuid().NotNullable()
            .WithColumn("starts_at").AsDateTimeOffset().NotNullable()
            .WithColumn("duration_minutes").AsInt32().NotNullable()
            .WithColumn("note").AsCustom("text").Nullable()
            .WithColumn("recurrence_group_id").AsGuid().Nullable()
            .WithColumn("created_at").AsDateTimeOffset().NotNullable()
            .WithColumn("created_by").AsGuid().Nullable()
            .WithColumn("updated_at").AsDateTimeOffset().Nullable()
            .WithColumn("updated_by").AsGuid().Nullable();

        Create.ForeignKey("fk_schedule_breaks_organization_id")
            .FromTable(Tables.ScheduleBreaks).InSchema(Tables.Schemas.DuneLight).ForeignColumn("organization_id")
            .ToTable(Tables.Organizations).InSchema(Tables.Schemas.DuneLight).PrimaryColumn("id");

        Create.ForeignKey("fk_schedule_breaks_employee_id")
            .FromTable(Tables.ScheduleBreaks).InSchema(Tables.Schemas.DuneLight).ForeignColumn("employee_id")
            .ToTable(Tables.Employees).InSchema(Tables.Schemas.DuneLight).PrimaryColumn("id");

        Create.ForeignKey("fk_schedule_breaks_company_id")
            .FromTable(Tables.ScheduleBreaks).InSchema(Tables.Schemas.DuneLight).ForeignColumn("company_id")
            .ToTable(Tables.Companies).InSchema(Tables.Schemas.DuneLight).PrimaryColumn("id");

        Create.Index("ix_schedule_breaks_org_employee_startsat")
            .OnTable(Tables.ScheduleBreaks).InSchema(Tables.Schemas.DuneLight)
            .OnColumn("organization_id").Ascending()
            .OnColumn("employee_id").Ascending()
            .OnColumn("starts_at").Ascending();

        Create.Index("ix_schedule_breaks_org_company_startsat")
            .OnTable(Tables.ScheduleBreaks).InSchema(Tables.Schemas.DuneLight)
            .OnColumn("organization_id").Ascending()
            .OnColumn("company_id").Ascending()
            .OnColumn("starts_at").Ascending();
    }
}
