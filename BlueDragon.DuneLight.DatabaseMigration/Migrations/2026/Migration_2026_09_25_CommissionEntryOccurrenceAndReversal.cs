using BlueDragon.DuneLight.DatabaseMigration.Extensions;
using BlueDragon.DuneLight.DatabaseMigration.Models;

namespace BlueDragon.DuneLight.DatabaseMigration.Migrations._2026;

/// <summary>
/// Uvodi occurrence-identitet i reverziju za CommissionEntry (vidi CommissionEntry.cs domensku napomenu) —
/// potrebno za P1 korekcijski tok "Individual Booking: Completed -&gt; Confirmed"
/// (BookingService.ApplyIndividualCompletionCorrection). Isti obrazac kao Migration_2026_09_23 (Booking.StatusVersion
/// + Notification.SourceVersion, vidi AddSourceVersionToNotifications) — dodaje source_version koji veže
/// CommissionEntry na TOČNO ONU pojavu completiona koja ga je zaradila (Booking.StatusVersion u trenutku zarade),
/// jer stari unique indeks (samo booking_id) dopušta najviše JEDAN CommissionEntry po Bookingu ZAUVIJEK — bez
/// ovoga bi korekcija (Earned -&gt; Reversed na postojećem retku) trajno blokirala umetanje NOVOG Earned retka kod
/// ponovnog completiona istog Bookinga (INSERT bi pao na povredi starog indeksa, vidi spec section 16/17).
///
/// reversed_at/reversed_by prate isti obrazac kao checkouts.cancelled_at/cancelled_by
/// (Migration_2026_09_24_AuditCleanup) — CommissionEntry nema generički UpdatedAt/UpdatedBy (namjerno
/// nepromjenjiv ledger, vidi klasnu napomenu), a Status-only reverzija bez ikakve actor/timestamp evidencije bi
/// bila jedina mutacija u cijeloj bazi bez traga tko/kada. Backfill: source_version=0 za sve postojeće retke
/// (WithDefaultValue — dosljedno s Migration_2026_09_23 konvencijom, povijesni retci prije ove migracije nemaju
/// više od jedne pojave po Bookingu pa je 0 ispravan/jedini mogući identitet za njih).
/// </summary>
[DeveloperMigration(2026, 09, 25, Developer.SilvioHabazin, 0)]
public class AddOccurrenceAndReversalToCommissionEntries : DuneLightMigration
{
    public override void Up()
    {
        Alter.Table(Tables.CommissionEntries)
            .InSchema(Tables.Schemas.DuneLight)
            .AddColumn("source_version").AsInt32().NotNullable().WithDefaultValue(0)
            .AddColumn("reversed_at").AsDateTimeOffset().Nullable()
            .AddColumn("reversed_by").AsGuid().Nullable();

        // Zamjenjuje ux_commission_entries_booking_id (Migration_2026_09_21) — stari indeks je dopuštao najviše
        // jedan CommissionEntry po Bookingu ZAUVIJEK, novi dopušta jedan po (booking_id, source_version), tj. jedan
        // po completion-occurrenceu (vidi klasnu napomenu). Djelomičan (WHERE booking_id IS NOT NULL) i dalje
        // isključivo relevantan za SourceType=IndividualService (isti obrazac kao prije, vidi
        // ck_commission_entries_source) — GroupService/ProductSale/PackageSale indeksi ostaju nepromijenjeni.
        Execute.Sql("DROP INDEX dunelight.ux_commission_entries_booking_id;");
        Execute.Sql(@"
            CREATE UNIQUE INDEX ux_commission_entries_booking_id_source_version
            ON dunelight.commission_entries (booking_id, source_version)
            WHERE booking_id IS NOT NULL;");
    }
}
