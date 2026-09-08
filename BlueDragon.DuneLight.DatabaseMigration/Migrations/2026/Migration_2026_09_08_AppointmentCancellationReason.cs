using BlueDragon.DuneLight.DatabaseMigration.Extensions;
using BlueDragon.DuneLight.DatabaseMigration.Models;
using FluentMigrator;

namespace BlueDragon.DuneLight.DatabaseMigration.Migrations._2026;

/// <summary>
/// Razlog otkazivanja/no-showa termina — slobodan tekst koji trener/recepcija upisuje kod prijelaza
/// u Cancelled ili NoShow (vidi AppointmentService.ChangeToTerminalStatus). Dio Povijesti klijenta.
/// </summary>
[DeveloperMigration(2026, 09, 08, Developer.SilvioHabazin, 0)]
public class AddAppointmentCancellationReason : DuneLightMigration
{
    public override void Up()
    {
        Alter.Table(Tables.Appointments)
            .InSchema(Tables.Schemas.DuneLight)
            .AddColumn("cancellation_reason").AsString(500).Nullable();
    }
}
