using BlueDragon.DuneLight.DatabaseMigration.Extensions;
using BlueDragon.DuneLight.DatabaseMigration.Models;
using FluentMigrator;

namespace BlueDragon.DuneLight.DatabaseMigration.Migrations._2026;

/// <summary>
/// Isti obrazac kao ExpandAdminGrantsWithWorkingHoursTemplates / BackfillAdminBrandingGrant — Prostorije
/// (Migration_2026_09_01_Rooms) su dodane bez odgovarajućeg grant-backfilla, pa postojeće "Admin"
/// GrantGroup-e (kreirane prije tog datuma) nikad nisu dobile catalog.rooms.view/catalog.rooms.manage,
/// pa je tab "Prostorije" na formi poslovnice ostao nedostupan i vlasnicima Admin grupe. Dopunjuje SAMO
/// grupe koje se zovu točno "Admin" i kojima ovi grantovi već ne nedostaju — sigurno za ponovno
/// pokretanje i ne dira grupe koje je korisnik preimenovao.
/// </summary>
[DeveloperMigration(2026, 09, 11, Developer.SilvioHabazin, 0)]
public class ExpandAdminGrantsWithRooms : DuneLightMigration
{
    public override void Up()
    {
        Execute.Sql(@"
            INSERT INTO dunelight.grant_group_grants (id, grant_group_id, grant_key)
            SELECT (md5(random()::text || clock_timestamp()::text || gg.id::text || grant_key))::uuid, gg.id, grant_key
            FROM dunelight.grant_groups gg
            CROSS JOIN (VALUES ('catalog.rooms.view'), ('catalog.rooms.manage')) AS missing(grant_key)
            WHERE gg.name = 'Admin'
              AND NOT EXISTS (
                  SELECT 1 FROM dunelight.grant_group_grants existing
                  WHERE existing.grant_group_id = gg.id AND existing.grant_key = missing.grant_key
              );
        ");
    }
}
