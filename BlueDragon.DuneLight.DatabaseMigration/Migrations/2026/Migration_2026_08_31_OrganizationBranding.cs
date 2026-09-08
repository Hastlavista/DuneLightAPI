using BlueDragon.DuneLight.DatabaseMigration.Extensions;
using BlueDragon.DuneLight.DatabaseMigration.Models;
using FluentMigrator;

namespace BlueDragon.DuneLight.DatabaseMigration.Migrations._2026;

/// <summary>
/// Vizualni identitet organizacije (branding): logo, favicon te primarna i sekundarna boja. Koristi ih frontend
/// na sidebaru, login screenu i općenito za custom CSS varijable. Svi stupci su nullable — dok ih organizacija
/// ne postavi, aplicira se zadani (neutralni) izgled platforme.
/// </summary>
[DeveloperMigration(2026, 08, 31, Developer.SilvioHabazin, 0)]
public class AddBrandingToOrganizations : DuneLightMigration
{
    public override void Up()
    {
        Alter.Table(Tables.Organizations)
            .InSchema(Tables.Schemas.DuneLight)
            .AddColumn("logo").AsString(511).Nullable()
            .AddColumn("favicon").AsString(511).Nullable()
            .AddColumn("primary_color").AsString(7).Nullable()
            .AddColumn("secondary_color").AsString(7).Nullable();
    }
}
