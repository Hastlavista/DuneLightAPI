using BlueDragon.DuneLight.DatabaseMigration.Extensions;
using BlueDragon.DuneLight.DatabaseMigration.Models;
using FluentMigrator;

namespace BlueDragon.DuneLight.DatabaseMigration.Migrations._2026;

/// <summary>
/// FAZA 2 (predložak radnog vremena + dostupnost). Vlasnik predloška je točno jedno od employee_id/company_id —
/// isti obrazac kao price_list_items.service_id/package_id (djelomični unique indeksi + CHECK constraint).
/// </summary>
[DeveloperMigration(2026, 08, 18, Developer.SilvioHabazin, 2)]
public class CreateWorkingHoursTemplatesTable : DuneLightMigration
{
    public override void Up()
    {
        Create.Table(Tables.WorkingHoursTemplates)
            .InSchema(Tables.Schemas.DuneLight)
            .WithColumn("id").AsGuid().NotNullable().PrimaryKey("pk_working_hours_templates")
            .WithColumn("organization_id").AsGuid().NotNullable()
            .WithColumn("employee_id").AsGuid().Nullable()
            .WithColumn("company_id").AsGuid().Nullable()
            .WithColumn("cycle_type").AsString(20).NotNullable()
            .WithColumn("anchor_date").AsDateTimeOffset().NotNullable()
            .WithColumn("created_at").AsDateTimeOffset().NotNullable()
            .WithColumn("created_by").AsGuid().Nullable()
            .WithColumn("updated_at").AsDateTimeOffset().Nullable()
            .WithColumn("updated_by").AsGuid().Nullable();

        Create.ForeignKey("fk_working_hours_templates_organization_id")
            .FromTable(Tables.WorkingHoursTemplates).InSchema(Tables.Schemas.DuneLight).ForeignColumn("organization_id")
            .ToTable(Tables.Organizations).InSchema(Tables.Schemas.DuneLight).PrimaryColumn("id");

        Create.ForeignKey("fk_working_hours_templates_employee_id")
            .FromTable(Tables.WorkingHoursTemplates).InSchema(Tables.Schemas.DuneLight).ForeignColumn("employee_id")
            .ToTable(Tables.Employees).InSchema(Tables.Schemas.DuneLight).PrimaryColumn("id");

        Create.ForeignKey("fk_working_hours_templates_company_id")
            .FromTable(Tables.WorkingHoursTemplates).InSchema(Tables.Schemas.DuneLight).ForeignColumn("company_id")
            .ToTable(Tables.Companies).InSchema(Tables.Schemas.DuneLight).PrimaryColumn("id");

        Execute.Sql(
            "CREATE UNIQUE INDEX ux_working_hours_templates_employee " +
            "ON dunelight.working_hours_templates (employee_id) WHERE employee_id IS NOT NULL;");
        Execute.Sql(
            "CREATE UNIQUE INDEX ux_working_hours_templates_company " +
            "ON dunelight.working_hours_templates (company_id) WHERE company_id IS NOT NULL;");

        Execute.Sql(
            "ALTER TABLE dunelight.working_hours_templates ADD CONSTRAINT ck_working_hours_templates_owner " +
            "CHECK ((employee_id IS NOT NULL AND company_id IS NULL) OR (employee_id IS NULL AND company_id IS NOT NULL));");
    }
}

[DeveloperMigration(2026, 08, 18, Developer.SilvioHabazin, 3)]
public class CreateWorkingHoursIntervalsTable : DuneLightMigration
{
    public override void Up()
    {
        Create.Table(Tables.WorkingHoursIntervals)
            .InSchema(Tables.Schemas.DuneLight)
            .WithColumn("id").AsGuid().NotNullable().PrimaryKey("pk_working_hours_intervals")
            .WithColumn("working_hours_template_id").AsGuid().NotNullable()
            .WithColumn("cycle_week_index").AsInt32().NotNullable()
            .WithColumn("day_of_week").AsString(10).NotNullable()
            .WithColumn("start_time").AsTime().NotNullable()
            .WithColumn("end_time").AsTime().NotNullable();

        Create.ForeignKey("fk_working_hours_intervals_template_id")
            .FromTable(Tables.WorkingHoursIntervals).InSchema(Tables.Schemas.DuneLight).ForeignColumn("working_hours_template_id")
            .ToTable(Tables.WorkingHoursTemplates).InSchema(Tables.Schemas.DuneLight).PrimaryColumn("id")
            .OnDelete(System.Data.Rule.Cascade);

        Create.Index("ix_working_hours_intervals_template_id")
            .OnTable(Tables.WorkingHoursIntervals).InSchema(Tables.Schemas.DuneLight)
            .OnColumn("working_hours_template_id").Ascending();
    }
}

[DeveloperMigration(2026, 08, 18, Developer.SilvioHabazin, 4)]
public class AddIsOverrideToRosterEntries : DuneLightMigration
{
    public override void Up()
    {
        Alter.Table(Tables.RosterEntries).InSchema(Tables.Schemas.DuneLight)
            .AddColumn("is_override").AsBoolean().NotNullable().WithDefaultValue(false);
    }
}

/// <summary>Isti obrazac kao ExpandDefaultReceptionGrants — dopunjuje SAMO grupe koje se zovu točno "Admin" i
/// kojima novi grantovi već ne nedostaju. Za NOVE organizacije Admin grupa ih već dobiva preko
/// DefaultGrantGroups.AdminGrants (izveden iz cijelog Grants.Catalog), ovo je samo retroaktivni backfill.</summary>
[DeveloperMigration(2026, 08, 18, Developer.SilvioHabazin, 5)]
public class ExpandAdminGrantsWithWorkingHoursTemplates : DuneLightMigration
{
    public override void Up()
    {
        Execute.Sql(@"
            INSERT INTO dunelight.grant_group_grants (id, grant_group_id, grant_key)
            SELECT (md5(random()::text || clock_timestamp()::text || gg.id::text || grant_key))::uuid, gg.id, grant_key
            FROM dunelight.grant_groups gg
            CROSS JOIN (VALUES ('roster.templates.view'), ('roster.templates.manage')) AS missing(grant_key)
            WHERE gg.name = 'Admin'
              AND NOT EXISTS (
                  SELECT 1 FROM dunelight.grant_group_grants existing
                  WHERE existing.grant_group_id = gg.id AND existing.grant_key = missing.grant_key
              );
        ");
    }
}

/// <summary>
/// KRITIČNO — mora se izvršiti prije nego AppointmentService.EnsureWithinWorkingHours stvarno počne blokirati
/// zakazivanje (vidi FAZA 1 odluka #1, fail-closed default). Daje SVAKOM postojećem zaposleniku i lokaciji bez
/// predloška širok default (Weekly, svaki dan 08:00-20:00) kako postojeći korisnici ne bi ostali blokirani čim
/// se ovaj deploy pusti u produkciju. AnchorDate je fiksni ponedjeljak (2024-01-01) — za Weekly ciklus ionako ne
/// utječe na izračun (CycleWeekIndex uvijek 0). Company nema polje s postojećim radnim vremenom za preuzeti pa
/// koristi isti default kao zaposlenici. Idempotentno (NOT EXISTS) — sigurno za ponovno pokretanje.
/// </summary>
[DeveloperMigration(2026, 08, 18, Developer.SilvioHabazin, 6)]
public class BackfillDefaultWorkingHoursTemplates : DuneLightMigration
{
    public override void Up()
    {
        Execute.Sql(@"
            DO $$
            DECLARE
                emp RECORD;
                comp RECORD;
                new_template_id uuid;
                days text[] := ARRAY['Monday','Tuesday','Wednesday','Thursday','Friday','Saturday','Sunday'];
                day_name text;
            BEGIN
                FOR emp IN
                    SELECT e.id, e.organization_id FROM dunelight.employees e
                    WHERE NOT EXISTS (SELECT 1 FROM dunelight.working_hours_templates t WHERE t.employee_id = e.id)
                LOOP
                    new_template_id := (md5(random()::text || clock_timestamp()::text || emp.id::text))::uuid;
                    INSERT INTO dunelight.working_hours_templates
                        (id, organization_id, employee_id, company_id, cycle_type, anchor_date, created_at)
                    VALUES
                        (new_template_id, emp.organization_id, emp.id, NULL, 'Weekly', '2024-01-01T00:00:00+00', now());

                    FOREACH day_name IN ARRAY days LOOP
                        INSERT INTO dunelight.working_hours_intervals
                            (id, working_hours_template_id, cycle_week_index, day_of_week, start_time, end_time)
                        VALUES
                            ((md5(random()::text || clock_timestamp()::text || new_template_id::text || day_name))::uuid,
                             new_template_id, 0, day_name, '08:00:00', '20:00:00');
                    END LOOP;
                END LOOP;

                FOR comp IN
                    SELECT c.id, c.organization_id FROM dunelight.companies c
                    WHERE NOT EXISTS (SELECT 1 FROM dunelight.working_hours_templates t WHERE t.company_id = c.id)
                LOOP
                    new_template_id := (md5(random()::text || clock_timestamp()::text || comp.id::text))::uuid;
                    INSERT INTO dunelight.working_hours_templates
                        (id, organization_id, employee_id, company_id, cycle_type, anchor_date, created_at)
                    VALUES
                        (new_template_id, comp.organization_id, NULL, comp.id, 'Weekly', '2024-01-01T00:00:00+00', now());

                    FOREACH day_name IN ARRAY days LOOP
                        INSERT INTO dunelight.working_hours_intervals
                            (id, working_hours_template_id, cycle_week_index, day_of_week, start_time, end_time)
                        VALUES
                            ((md5(random()::text || clock_timestamp()::text || new_template_id::text || day_name))::uuid,
                             new_template_id, 0, day_name, '08:00:00', '20:00:00');
                    END LOOP;
                END LOOP;
            END $$;
        ");
    }
}