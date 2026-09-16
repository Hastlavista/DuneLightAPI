using BlueDragon.DuneLight.DatabaseMigration.Extensions;
using BlueDragon.DuneLight.DatabaseMigration.Models;

namespace BlueDragon.DuneLight.DatabaseMigration.Migrations._2026;

/// <summary>
/// Poslovno pravilo: unutar jedne Company (poslovnice), dvije aktivne Room (prostorije) ne smiju imati isti
/// naziv nakon normalizacije (trim + case-insensitive) — "Masaža 1", "masaža 1" i " Masaža 1 " se tretiraju
/// kao isti naziv. Isti Room naziv u DRUGOJ Company je dopušten. Isti obrazac kao
/// ux_companies_org_name_active (AddCompanyActiveNameUniqueIndex), ali proširen s company_id jer Room
/// pripada Company, ne direktno Organization.
///
/// OPREZ prije pokretanja na bazi s postojećim podacima: ako već postoje dvije aktivne Room u istoj Company
/// s istim normaliziranim nazivom, ovaj CREATE UNIQUE INDEX će pasti — takve zapise treba ručno
/// preimenovati/deaktivirati prije primjene migracije.
/// </summary>
[DeveloperMigration(2026, 09, 15, Developer.SilvioHabazin, 1)]
public class AddRoomActiveNameUniqueIndex : DuneLightMigration
{
    public override void Up()
    {
        Execute.Sql(
            "CREATE UNIQUE INDEX ux_rooms_org_company_name_active " +
            "ON dunelight.rooms (organization_id, company_id, lower(trim(name))) WHERE is_active = true;");
    }
}
