using BlueDragon.DuneLight.DatabaseMigration.Extensions;
using BlueDragon.DuneLight.DatabaseMigration.Models;

namespace BlueDragon.DuneLight.DatabaseMigration.Migrations._2026;

/// <summary>
/// Anonimizacija trenutno prepisuje UpdatedBy/UpdatedAt (isto polje kao svaka druga izmjena) — nakon
/// anonimizacije daljnje izmjene su blokirane (ClientService.EnsureNotAnonymized) pa u praksi ne prepisuje
/// vrijednost, ali polje samo po sebi ne izražava eksplicitno "tko je anonimizirao" kao odvojenu, namjensku
/// činjenicu (vidi audit-cleanup spec section 32). Dodaje dedicirano anonymized_by uz postojeće
/// is_anonymized/anonymized_at (vidi ClientHandler.Anonymize) — SAMO ID aktera, NE staru PII vrijednost.
/// </summary>
[DeveloperMigration(2026, 09, 24, Developer.SilvioHabazin, 0)]
public class AddAnonymizedByToClients : DuneLightMigration
{
    public override void Up()
    {
        Alter.Table(Tables.Clients)
            .InSchema(Tables.Schemas.DuneLight)
            .AddColumn("anonymized_by").AsGuid().Nullable();
    }
}

/// <summary>
/// ClientPackage.Cancel trenutno prepisuje samo generički UpdatedAt/UpdatedBy — za razliku od Checkout
/// (CancelledAt/By odvojeno od generičkih polja) ovo polje NIJE zaštićeno od naknadnog prepisivanja: ako se
/// nakon ručnog otkazivanja paketa naknadno odradi ReturnPackageEntryInTransaction za neku NEPOVEZANU
/// prethodnu rezervaciju istog paketa (AppointmentService/BookingService, izvan ClientPackageService.Cancel),
/// UpdatedAt/UpdatedBy se tiho prepišu na taj kasniji, nepovezani akter/trenutak — trajno gubeći tko je i
/// kada stvarno otkazao paket (vidi audit-cleanup spec section 17). Dodaje dedicirana cancelled_at/cancelled_by
/// polja, po istom obrascu kao checkouts.cancelled_at/cancelled_by.
/// </summary>
[DeveloperMigration(2026, 09, 24, Developer.SilvioHabazin, 1)]
public class AddCancelledAtByToClientPackages : DuneLightMigration
{
    public override void Up()
    {
        Alter.Table(Tables.ClientPackages)
            .InSchema(Tables.Schemas.DuneLight)
            .AddColumn("cancelled_at").AsDateTimeOffset().Nullable()
            .AddColumn("cancelled_by").AsGuid().Nullable();
    }
}

/// <summary>
/// AppointmentAuditLog retci s ChangeType="BookingStatus" već su jedina, potpuna, iste-transakcije povijest
/// Booking status-prijelaza (tko/kada/staro/novo) — dovoljno da se cikličan Confirmed-&gt;NoShow-&gt;Confirmed-&gt;NoShow
/// ispravno rekonstruira po redoslijedu ChangedAt (vidi audit-cleanup spec section 51/60). Dodaje NEOBAVEZNO
/// status_version polje koje veže svaki takav redak na ISTU pojavu koju već referenciraju Outbox idempotency key
/// i Notification.SourceVersion (Booking.StatusVersion, vidi Migration_2026_09_23), umjesto oslanjanja samo na
/// ChangedAt redoslijed za korelaciju s Outbox/Notification zapisima te pojave. Nullable/bez backfilla jer
/// postojeći retci (WaitlistJoined/Cancelled/Promoted/Expired, Status na razini termina, PaymentCreated/Voided,
/// BookingPackageCoverageApplied/Returned) nemaju i ne trebaju odgovarajući StatusVersion.
/// </summary>
[DeveloperMigration(2026, 09, 24, Developer.SilvioHabazin, 2)]
public class AddStatusVersionToAppointmentAuditLog : DuneLightMigration
{
    public override void Up()
    {
        Alter.Table(Tables.AppointmentAuditLog)
            .InSchema(Tables.Schemas.DuneLight)
            .AddColumn("status_version").AsInt32().Nullable();
    }
}
