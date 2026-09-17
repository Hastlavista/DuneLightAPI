using BlueDragon.DuneLight.DatabaseMigration.Extensions;
using BlueDragon.DuneLight.DatabaseMigration.Models;

namespace BlueDragon.DuneLight.DatabaseMigration.Migrations._2026;

/// <summary>
/// Uvodi stabilan identitet JEDNE konkretne pojave Booking status-prijelaza (vidi Booking.cs StatusVersion
/// domensku napomenu) — bez ovoga Outbox idempotency-key i Notification uniqueness po samom BookingId trajno
/// "zaključaju" prvu Cancelled/NoShow pojavu i tiho progutaju svaku narednu legitimnu pojavu istog statusa na
/// istom grupnom Bookingu (npr. Confirmed-&gt;NoShow-&gt;Confirmed-&gt;NoShow). Backfill je 0 za sve postojeće retke
/// (WithDefaultValue) — povijesne pojave prije uvođenja ovog polja se ne rekonstruiraju, buduće STVARNE promjene
/// statusa broje ispravno od sada (vidi BookingStatusVersioning.TrySetStatus, jedina dozvoljena putanja koja
/// mijenja Booking.Status).
/// </summary>
[DeveloperMigration(2026, 09, 23, Developer.SilvioHabazin, 0)]
public class AddStatusVersionToBookings : DuneLightMigration
{
    public override void Up()
    {
        Alter.Table(Tables.Bookings)
            .InSchema(Tables.Schemas.DuneLight)
            .AddColumn("status_version").AsInt32().NotNullable().WithDefaultValue(0);
    }
}

/// <summary>
/// Proširuje Notification idempotency uniqueness sa SourceVersion (vidi Notification.cs domensku napomenu) —
/// stari indeks (organization_id, type, source_type, source_id) je zaključavao NAJVIŠE JEDAN Notification
/// zauvijek po Bookingu+Type, što je netočno za grupne Bookinge čiji status legitimno ciklira. Backfill je 0 za
/// sve postojeće retke (svi trenutno ožičeni handleri prije ove migracije stvarali su Notification samo za
/// PRVU pojavu, pa je 0 dosljedan nastavak brojanja unaprijed).
/// </summary>
[DeveloperMigration(2026, 09, 23, Developer.SilvioHabazin, 1)]
public class AddSourceVersionToNotifications : DuneLightMigration
{
    public override void Up()
    {
        Alter.Table(Tables.Notifications)
            .InSchema(Tables.Schemas.DuneLight)
            .AddColumn("source_version").AsInt32().NotNullable().WithDefaultValue(0);

        Execute.Sql("DROP INDEX dunelight.ux_notifications_org_type_source;");

        Execute.Sql(@"
            CREATE UNIQUE INDEX ux_notifications_org_type_source_version
            ON dunelight.notifications (organization_id, type, source_type, source_id, source_version);");
    }
}
