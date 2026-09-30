using BlueDragon.DuneLight.DatabaseMigration.Extensions;
using BlueDragon.DuneLight.DatabaseMigration.Models;

namespace BlueDragon.DuneLight.DatabaseMigration.Migrations._2026;

/// <summary>
/// Timezone foundation (F-19):
/// 1. organizations.time_zone — IANA id of the Organization's business timezone. Existing organizations get
///    'Europe/Zagreb' (the studios this system serves); scheduling and business-calendar rules evaluate in this zone.
/// 2. Calendar-only business dates become PostgreSQL <c>date</c> instead of an accidental timestamptz midnight:
///    roster_entries.date_from/date_to, company_holidays.date, working_hours_templates.anchor_date.
///
/// Existing values were written as midnight in the HOST timezone (roster, via an implicit DateTime -&gt; DateTimeOffset
/// conversion) or as UTC midnight (holidays), so the calendar date is recovered in the owning Organization's timezone:
/// for an east-of-UTC zone such as Europe/Zagreb both UTC midnight and local midnight map to the intended date.
/// A USING clause cannot contain a subquery, so each date is first computed through a join into a temporary column.
/// Indexes on the converted columns (ix_roster_entries_org_employee_date, ux_company_holidays_org_company_date) are
/// rebuilt by ALTER COLUMN TYPE and keep their definitions.
/// </summary>
[DeveloperMigration(2026, 10, 04, Developer.SilvioHabazin, 1)]
public class OrganizationTimeZoneAndCalendarDates : DuneLightMigration
{
    public override void Up()
    {
        Execute.Sql($@"
            ALTER TABLE dunelight.{Tables.Organizations}
                ADD COLUMN time_zone varchar(64) NOT NULL DEFAULT 'Europe/Zagreb';");

        Execute.Sql($@"
            ALTER TABLE dunelight.{Tables.RosterEntries}
                ADD COLUMN date_from_local date,
                ADD COLUMN date_to_local date;

            UPDATE dunelight.{Tables.RosterEntries} r
               SET date_from_local = (r.date_from AT TIME ZONE o.time_zone)::date,
                   date_to_local = (r.date_to AT TIME ZONE o.time_zone)::date
              FROM dunelight.{Tables.Organizations} o
             WHERE o.id = r.organization_id;

            ALTER TABLE dunelight.{Tables.RosterEntries}
                ALTER COLUMN date_from TYPE date USING date_from_local,
                ALTER COLUMN date_to TYPE date USING date_to_local;

            ALTER TABLE dunelight.{Tables.RosterEntries}
                DROP COLUMN date_from_local,
                DROP COLUMN date_to_local;");

        Execute.Sql($@"
            ALTER TABLE dunelight.{Tables.CompanyHolidays} ADD COLUMN date_local date;

            UPDATE dunelight.{Tables.CompanyHolidays} h
               SET date_local = (h.date AT TIME ZONE o.time_zone)::date
              FROM dunelight.{Tables.Organizations} o
             WHERE o.id = h.organization_id;

            ALTER TABLE dunelight.{Tables.CompanyHolidays} ALTER COLUMN date TYPE date USING date_local;
            ALTER TABLE dunelight.{Tables.CompanyHolidays} DROP COLUMN date_local;");

        Execute.Sql($@"
            ALTER TABLE dunelight.{Tables.WorkingHoursTemplates} ADD COLUMN anchor_date_local date;

            UPDATE dunelight.{Tables.WorkingHoursTemplates} t
               SET anchor_date_local = (t.anchor_date AT TIME ZONE o.time_zone)::date
              FROM dunelight.{Tables.Organizations} o
             WHERE o.id = t.organization_id;

            ALTER TABLE dunelight.{Tables.WorkingHoursTemplates} ALTER COLUMN anchor_date TYPE date USING anchor_date_local;
            ALTER TABLE dunelight.{Tables.WorkingHoursTemplates} DROP COLUMN anchor_date_local;");
    }

    /// <summary>Back to timestamptz at UTC midnight of the calendar date (the pre-migration holiday convention).</summary>
    public override void Down()
    {
        Execute.Sql($@"
            ALTER TABLE dunelight.{Tables.WorkingHoursTemplates}
                ALTER COLUMN anchor_date TYPE timestamptz USING (anchor_date::timestamp AT TIME ZONE 'UTC');
            ALTER TABLE dunelight.{Tables.CompanyHolidays}
                ALTER COLUMN date TYPE timestamptz USING (date::timestamp AT TIME ZONE 'UTC');
            ALTER TABLE dunelight.{Tables.RosterEntries}
                ALTER COLUMN date_from TYPE timestamptz USING (date_from::timestamp AT TIME ZONE 'UTC'),
                ALTER COLUMN date_to TYPE timestamptz USING (date_to::timestamp AT TIME ZONE 'UTC');
            ALTER TABLE dunelight.{Tables.Organizations} DROP COLUMN time_zone;");
    }
}
