using BlueDragon.DuneLight.DatabaseMigration.Extensions;
using BlueDragon.DuneLight.DatabaseMigration.Models;

namespace BlueDragon.DuneLight.DatabaseMigration.Migrations._2026;

/// <summary>
/// Phase M1A — životni ciklus termina je Scheduled / Cancelled / Closed (agregat izveden iz sudjelovanja; nema
/// "Completed" termina — izvršenje je po sudjelovanju). appointments.status dobiva CHECK s točno te tri vrijednosti.
///
/// Razvojna baza (sadržaj se ne čuva): zatečeni 'Completed' retci postaju 'Closed' samo da bi CHECK vrijedio (bez
/// ponovnog izvođenja iz sudjelovanja). Down: obrnuto.
/// </summary>
[DeveloperMigration(2026, 10, 17, Developer.SilvioHabazin, 0)]
public class AppointmentLifecycleClosed : DuneLightMigration
{
    public override void Up()
    {
        Execute.Sql($@"
            UPDATE dunelight.{Tables.Appointments} SET status = 'Closed' WHERE status = 'Completed';
            ALTER TABLE dunelight.{Tables.Appointments}
                ADD CONSTRAINT ck_appointments_status CHECK (status IN ('Scheduled', 'Cancelled', 'Closed'));");
    }

    public override void Down()
    {
        Execute.Sql($@"
            ALTER TABLE dunelight.{Tables.Appointments} DROP CONSTRAINT ck_appointments_status;
            UPDATE dunelight.{Tables.Appointments} SET status = 'Completed' WHERE status = 'Closed';");
    }
}
