using BlueDragon.DuneLight.DatabaseMigration.Extensions;
using BlueDragon.DuneLight.DatabaseMigration.Models;

namespace BlueDragon.DuneLight.DatabaseMigration.Migrations._2026;

/// <summary>
/// Phase D3B3A.2 — packages.validity_fixed_date (timestamptz) postaje date: zadnji valjani dan iz kataloga je kalendarski
/// datum bez zone i doba dana. Razvojna baza (sadržaj se ne čuva): postojeći retci dobivaju jednostavnu UTC-datumsku
/// pretvorbu samo radi promjene tipa — to nije poslovno pravilo. Down: natrag na timestamptz (ponoć UTC tog datuma).
/// </summary>
[DeveloperMigration(2026, 10, 14, Developer.SilvioHabazin, 0)]
public class PackageFixedDateIsDate : DuneLightMigration
{
    public override void Up()
    {
        Execute.Sql($@"
            ALTER TABLE dunelight.{Tables.Packages}
                ALTER COLUMN validity_fixed_date TYPE date USING (validity_fixed_date AT TIME ZONE 'UTC')::date;");
    }

    public override void Down()
    {
        Execute.Sql($@"
            ALTER TABLE dunelight.{Tables.Packages}
                ALTER COLUMN validity_fixed_date TYPE timestamptz USING (validity_fixed_date::timestamp AT TIME ZONE 'UTC');");
    }
}
