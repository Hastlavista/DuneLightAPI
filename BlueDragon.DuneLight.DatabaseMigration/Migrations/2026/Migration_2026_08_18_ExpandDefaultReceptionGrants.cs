using BlueDragon.DuneLight.DatabaseMigration.Extensions;
using BlueDragon.DuneLight.DatabaseMigration.Models;
using FluentMigrator;

namespace BlueDragon.DuneLight.DatabaseMigration.Migrations._2026;

/// <summary>
/// Proširuje default "Recepcija" GrantGroup (stvorenu u SeedDefaultGrantGroupsAndBackfillOwner) s appointments.view,
/// appointments.write.all i roster.entries.view — recepcija sad radi raspored za sve trenere i vidi tko je
/// odsutan/slobodan, ali i dalje ne upisuje svoj roster (vidi DefaultGrantGroups.ReceptionGrants u Core, koji je
/// izvor istine za AuthService.Register kod novih organizacija). Dopunjuje SAMO grupe koje se zovu točno "Recepcija"
/// i kojima ovi grantovi već ne nedostaju — sigurno za ponovno pokretanje i ne dira grupe koje je korisnik preimenovao.
/// </summary>
[DeveloperMigration(2026, 08, 18, Developer.SilvioHabazin, 1)]
public class ExpandDefaultReceptionGrants : DuneLightMigration
{
    public override void Up()
    {
        Execute.Sql(@"
            INSERT INTO dunelight.grant_group_grants (id, grant_group_id, grant_key)
            SELECT (md5(random()::text || clock_timestamp()::text || gg.id::text || grant_key))::uuid, gg.id, grant_key
            FROM dunelight.grant_groups gg
            CROSS JOIN (VALUES ('appointments.view'), ('appointments.write.all'), ('roster.entries.view')) AS missing(grant_key)
            WHERE gg.name = 'Recepcija'
              AND NOT EXISTS (
                  SELECT 1 FROM dunelight.grant_group_grants existing
                  WHERE existing.grant_group_id = gg.id AND existing.grant_key = missing.grant_key
              );
        ");
    }
}