using BlueDragon.DuneLight.DatabaseMigration.Extensions;
using BlueDragon.DuneLight.DatabaseMigration.Models;
using FluentMigrator;

namespace BlueDragon.DuneLight.DatabaseMigration.Migrations._2026;

/// <summary>
/// Fond godišnjeg odmora — FAZA 2. EmployeeLeaveSettings (postavke po zaposleniku), LeaveFund (fond po
/// obračunskoj godini, otvara se lijeno — vidi LeaveFundHandler.GetOrCreateForYear), LeaveFundUsage (koliko
/// dana kojeg fonda je potrošeno za koji RosterEntry, omogućuje split preko dva fonda i točan povrat pri
/// brisanju/izmjeni — vidi RosterEntryService).
/// </summary>
[DeveloperMigration(2026, 08, 18, Developer.SilvioHabazin, 7)]
public class AddDeductsFromLeaveFundToRosterTypes : DuneLightMigration
{
    public override void Up()
    {
        Alter.Table(Tables.RosterTypes).InSchema(Tables.Schemas.DuneLight)
            .AddColumn("deducts_from_leave_fund").AsBoolean().NotNullable().WithDefaultValue(false);
    }
}

[DeveloperMigration(2026, 08, 18, Developer.SilvioHabazin, 8)]
public class CreateEmployeeLeaveSettingsTable : DuneLightMigration
{
    public override void Up()
    {
        Create.Table(Tables.EmployeeLeaveSettings)
            .InSchema(Tables.Schemas.DuneLight)
            .WithColumn("id").AsGuid().NotNullable().PrimaryKey("pk_employee_leave_settings")
            .WithColumn("organization_id").AsGuid().NotNullable()
            .WithColumn("employee_id").AsGuid().NotNullable()
            .WithColumn("annual_days").AsInt32().NotNullable()
            .WithColumn("renewal_month").AsInt32().NotNullable()
            .WithColumn("renewal_day").AsInt32().NotNullable()
            .WithColumn("carryover_expiry_month").AsInt32().NotNullable()
            .WithColumn("carryover_expiry_day").AsInt32().NotNullable()
            .WithColumn("created_at").AsDateTimeOffset().NotNullable()
            .WithColumn("created_by").AsGuid().Nullable()
            .WithColumn("updated_at").AsDateTimeOffset().Nullable()
            .WithColumn("updated_by").AsGuid().Nullable();

        Create.ForeignKey("fk_employee_leave_settings_organization_id")
            .FromTable(Tables.EmployeeLeaveSettings).InSchema(Tables.Schemas.DuneLight).ForeignColumn("organization_id")
            .ToTable(Tables.Organizations).InSchema(Tables.Schemas.DuneLight).PrimaryColumn("id");

        Create.ForeignKey("fk_employee_leave_settings_employee_id")
            .FromTable(Tables.EmployeeLeaveSettings).InSchema(Tables.Schemas.DuneLight).ForeignColumn("employee_id")
            .ToTable(Tables.Employees).InSchema(Tables.Schemas.DuneLight).PrimaryColumn("id");

        Create.Index("ux_employee_leave_settings_employee_id")
            .OnTable(Tables.EmployeeLeaveSettings).InSchema(Tables.Schemas.DuneLight)
            .OnColumn("employee_id").Ascending()
            .WithOptions().Unique();
    }
}

[DeveloperMigration(2026, 08, 18, Developer.SilvioHabazin, 9)]
public class CreateLeaveFundsTable : DuneLightMigration
{
    public override void Up()
    {
        Create.Table(Tables.LeaveFunds)
            .InSchema(Tables.Schemas.DuneLight)
            .WithColumn("id").AsGuid().NotNullable().PrimaryKey("pk_leave_funds")
            .WithColumn("organization_id").AsGuid().NotNullable()
            .WithColumn("employee_id").AsGuid().NotNullable()
            .WithColumn("fund_year").AsInt32().NotNullable()
            .WithColumn("opened_at").AsDateTimeOffset().NotNullable()
            .WithColumn("expires_at").AsDateTimeOffset().NotNullable()
            .WithColumn("allocated_days").AsInt32().NotNullable()
            .WithColumn("used_days").AsInt32().NotNullable().WithDefaultValue(0)
            .WithColumn("created_at").AsDateTimeOffset().NotNullable()
            .WithColumn("created_by").AsGuid().Nullable()
            .WithColumn("updated_at").AsDateTimeOffset().Nullable()
            .WithColumn("updated_by").AsGuid().Nullable();

        Create.ForeignKey("fk_leave_funds_organization_id")
            .FromTable(Tables.LeaveFunds).InSchema(Tables.Schemas.DuneLight).ForeignColumn("organization_id")
            .ToTable(Tables.Organizations).InSchema(Tables.Schemas.DuneLight).PrimaryColumn("id");

        Create.ForeignKey("fk_leave_funds_employee_id")
            .FromTable(Tables.LeaveFunds).InSchema(Tables.Schemas.DuneLight).ForeignColumn("employee_id")
            .ToTable(Tables.Employees).InSchema(Tables.Schemas.DuneLight).PrimaryColumn("id");

        Create.Index("ux_leave_funds_employee_year")
            .OnTable(Tables.LeaveFunds).InSchema(Tables.Schemas.DuneLight)
            .OnColumn("organization_id").Ascending()
            .OnColumn("employee_id").Ascending()
            .OnColumn("fund_year").Ascending()
            .WithOptions().Unique();
    }
}

[DeveloperMigration(2026, 08, 18, Developer.SilvioHabazin, 10)]
public class CreateLeaveFundUsagesTable : DuneLightMigration
{
    public override void Up()
    {
        Create.Table(Tables.LeaveFundUsages)
            .InSchema(Tables.Schemas.DuneLight)
            .WithColumn("id").AsGuid().NotNullable().PrimaryKey("pk_leave_fund_usages")
            .WithColumn("organization_id").AsGuid().NotNullable()
            .WithColumn("roster_entry_id").AsGuid().NotNullable()
            .WithColumn("leave_fund_id").AsGuid().NotNullable()
            .WithColumn("days").AsInt32().NotNullable()
            .WithColumn("created_at").AsDateTimeOffset().NotNullable();

        // CASCADE — samo sigurnosna mreža za fizičko brisanje RosterEntry-ja; stvarni povrat dana u fond
        // MORA se dogoditi u kodu PRIJE brisanja (RosterEntryService.ReturnLeaveFundUsages), cascade to ne radi.
        Create.ForeignKey("fk_leave_fund_usages_roster_entry_id")
            .FromTable(Tables.LeaveFundUsages).InSchema(Tables.Schemas.DuneLight).ForeignColumn("roster_entry_id")
            .ToTable(Tables.RosterEntries).InSchema(Tables.Schemas.DuneLight).PrimaryColumn("id")
            .OnDelete(System.Data.Rule.Cascade);

        Create.ForeignKey("fk_leave_fund_usages_leave_fund_id")
            .FromTable(Tables.LeaveFundUsages).InSchema(Tables.Schemas.DuneLight).ForeignColumn("leave_fund_id")
            .ToTable(Tables.LeaveFunds).InSchema(Tables.Schemas.DuneLight).PrimaryColumn("id");

        Create.Index("ix_leave_fund_usages_roster_entry_id")
            .OnTable(Tables.LeaveFundUsages).InSchema(Tables.Schemas.DuneLight)
            .OnColumn("roster_entry_id").Ascending();

        Create.Index("ix_leave_fund_usages_leave_fund_id")
            .OnTable(Tables.LeaveFundUsages).InSchema(Tables.Schemas.DuneLight)
            .OnColumn("leave_fund_id").Ascending();
    }
}

/// <summary>Isti obrazac kao ExpandAdminGrantsWithWorkingHoursTemplates / ExpandDefaultReceptionGrants — dopunjuje
/// postojeće organizacije retroaktivno (nove organizacije dobivaju ove grantove već preko DefaultGrantGroups).</summary>
[DeveloperMigration(2026, 08, 18, Developer.SilvioHabazin, 11)]
public class ExpandGrantsWithLeaveFund : DuneLightMigration
{
    public override void Up()
    {
        Execute.Sql(@"
            INSERT INTO dunelight.grant_group_grants (id, grant_group_id, grant_key)
            SELECT (md5(random()::text || clock_timestamp()::text || gg.id::text || grant_key))::uuid, gg.id, grant_key
            FROM dunelight.grant_groups gg
            CROSS JOIN (VALUES
                ('roster.leave-fund.settings.view'), ('roster.leave-fund.settings.manage'),
                ('roster.leave-fund.view.own'), ('roster.leave-fund.view.all'), ('roster.leave-fund.manage')
            ) AS missing(grant_key)
            WHERE gg.name = 'Admin'
              AND NOT EXISTS (
                  SELECT 1 FROM dunelight.grant_group_grants existing
                  WHERE existing.grant_group_id = gg.id AND existing.grant_key = missing.grant_key
              );
        ");

        Execute.Sql(@"
            INSERT INTO dunelight.grant_group_grants (id, grant_group_id, grant_key)
            SELECT (md5(random()::text || clock_timestamp()::text || gg.id::text || 'roster.leave-fund.view.own'))::uuid,
                   gg.id, 'roster.leave-fund.view.own'
            FROM dunelight.grant_groups gg
            WHERE gg.name = 'Trener'
              AND NOT EXISTS (
                  SELECT 1 FROM dunelight.grant_group_grants existing
                  WHERE existing.grant_group_id = gg.id AND existing.grant_key = 'roster.leave-fund.view.own'
              );
        ");
    }
}
