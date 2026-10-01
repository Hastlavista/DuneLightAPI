using BlueDragon.DuneLight.DatabaseMigration.Extensions;
using BlueDragon.DuneLight.DatabaseMigration.Models;

namespace BlueDragon.DuneLight.DatabaseMigration.Migrations._2026;

/// <summary>
/// Phase D3B3A.1 — valjanost paketa je KALENDARSKI datum, ne instant.
///
/// 1. client_packages.expiry_date (timestamptz) -&gt; valid_until_date (date, NOT NULL): zadnji dan valjanosti (uključivo).
/// 2. package_consumptions.service_date (date, NOT NULL): lokalni datum izvođenja po kojem je valjanost provjerena.
///
/// Razvojna baza (sadržaj se ne čuva): postojeći retci dobivaju jednostavnu UTC-datumsku pretvorbu samo da bi NOT NULL
/// mogao vrijediti — to NIJE poslovno pravilo (novi paketi računaju datum u kalendaru poslovnice prodaje).
/// Down: vraća expiry_date kao kraj UTC dana (isti jednostavan oblik) i uklanja service_date.
/// </summary>
[DeveloperMigration(2026, 10, 13, Developer.SilvioHabazin, 0)]
public class PackageValidUntilDate : DuneLightMigration
{
    public override void Up()
    {
        Execute.Sql($@"
            ALTER TABLE dunelight.{Tables.ClientPackages} ADD COLUMN valid_until_date date;
            UPDATE dunelight.{Tables.ClientPackages} SET valid_until_date = (expiry_date AT TIME ZONE 'UTC')::date;
            ALTER TABLE dunelight.{Tables.ClientPackages}
                ALTER COLUMN valid_until_date SET NOT NULL,
                DROP COLUMN expiry_date;

            ALTER TABLE dunelight.{Tables.PackageConsumptions} ADD COLUMN service_date date;
            UPDATE dunelight.{Tables.PackageConsumptions} SET service_date = (service_starts_at AT TIME ZONE 'UTC')::date;
            ALTER TABLE dunelight.{Tables.PackageConsumptions} ALTER COLUMN service_date SET NOT NULL;");
    }

    public override void Down()
    {
        Execute.Sql($@"
            ALTER TABLE dunelight.{Tables.PackageConsumptions} DROP COLUMN service_date;

            ALTER TABLE dunelight.{Tables.ClientPackages} ADD COLUMN expiry_date timestamptz;
            UPDATE dunelight.{Tables.ClientPackages}
               SET expiry_date = ((valid_until_date + 1)::timestamp AT TIME ZONE 'UTC') - interval '1 microsecond';
            ALTER TABLE dunelight.{Tables.ClientPackages}
                ALTER COLUMN expiry_date SET NOT NULL,
                DROP COLUMN valid_until_date;");
    }
}
