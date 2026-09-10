using BlueDragon.DuneLight.DatabaseMigration.Extensions;
using BlueDragon.DuneLight.DatabaseMigration.Models;
using FluentMigrator;

namespace BlueDragon.DuneLight.DatabaseMigration.Migrations._2026;

/// <summary>
/// Dodaje surface_color (pozadina kartica/panela) uz postojeće primary/secondary boje brandinga.
/// Nullable — dok organizacija ne postavi, aplicira se platformski default.
/// </summary>
[DeveloperMigration(2026, 09, 10, Developer.SilvioHabazin, 0)]
public class AddSurfaceColorToOrganizations : DuneLightMigration
{
    public override void Up()
    {
        Alter.Table(Tables.Organizations)
            .InSchema(Tables.Schemas.DuneLight)
            .AddColumn("surface_color").AsString(7).Nullable();
    }
}
