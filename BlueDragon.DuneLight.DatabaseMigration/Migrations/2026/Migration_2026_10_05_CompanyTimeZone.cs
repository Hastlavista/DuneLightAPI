using BlueDragon.DuneLight.DatabaseMigration.Extensions;
using BlueDragon.DuneLight.DatabaseMigration.Models;

namespace BlueDragon.DuneLight.DatabaseMigration.Migrations._2026;

/// <summary>
/// Timezone foundation — Company override: companies.time_zone je opcionalna IANA zona poslovnice. NULL znači
/// "nasljeđuje Organization.TimeZone" (efektivna zona = Company.TimeZone ?? Organization.TimeZone), pa se postojeći
/// redovi namjerno NE popunjavaju vrijednošću organizacije — promjena zone organizacije i dalje vrijedi za sve
/// poslovnice bez vlastite zone.
/// </summary>
[DeveloperMigration(2026, 10, 05, Developer.SilvioHabazin, 1)]
public class CompanyTimeZone : DuneLightMigration
{
    public override void Up()
    {
        Alter.Table(Tables.Companies)
            .InSchema(Tables.Schemas.DuneLight)
            .AddColumn("time_zone").AsString(64).Nullable();
    }

    public override void Down()
    {
        Delete.Column("time_zone").FromTable(Tables.Companies).InSchema(Tables.Schemas.DuneLight);
    }
}
