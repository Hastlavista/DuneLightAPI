using BlueDragon.DuneLight.DatabaseMigration.Extensions;
using BlueDragon.DuneLight.DatabaseMigration.Models;

namespace BlueDragon.DuneLight.DatabaseMigration.Migrations._2026;

/// <summary>
/// Phase D3B2 — BookingSegmentParticipation postaje AUTORITATIVAN izvor CIJENE Bookinga (Amount, SuggestedAmount,
/// IsAmountManuallyOverridden). Paket, namirenje i naplata ostaju na Bookingu.
///
/// 1. Zaštita (jasan pad, bez popravljanja): svaki Booking mora imati TOČNO JEDNO sudjelovanje, iste organizacije, na
///    segmentu SVOG termina; nijedno sudjelovanje ne smije već imati cijenu (neočekivano djelomično stanje); cijena
///    Bookinga mora biti izraziva na sudjelovanju (nenegativna — CHECK ck_booking_segment_participations_amounts_non_negative;
///    isti tip numeric(10,2) pa je kopija egzaktna).
/// 2. Backfill: amount, suggested_amount i is_amount_manually_overridden se kopiraju EGZAKTNO. Povijesni snapshot
///    razrješavanja (base_amount, base_amount_source) i adjustment_amount ostaju NULL — Booking ih nikad nije čuvao, a
///    ponovno razrješavanje po DANAŠNJEM cjeniku bi prepisalo povijest (BaseAmount = SuggestedAmount se NE pretpostavlja,
///    prilagodba se NE izvodi iz SuggestedAmount - Amount jer ručna promjena nije komercijalna prilagodba).
/// 3. Provjera + NOT NULL za tri autoritativna stupca (bez zadanih vrijednosti — nepotpun upis mora pasti).
/// 4. bookings.amount/suggested_amount/is_amount_manually_overridden se UKLANJAJU (jedan izvor cijene, bez kopije).
///
/// Down: zaštita (točno jedno sudjelovanje po Bookingu) → vraća tri stupca Bookinga (isti tip/NOT NULL/default kao
/// Migration_2026_09_15) egzaktno iz sudjelovanja, pa vraća sudjelovanja u D3B1 oblik (cjenovni stupci NULLABLE i NULL).
/// Snapshot razrješavanja (base_amount/base_amount_source) se pritom NAMJERNO odbacuje — stari model ga ne može izraziti,
/// a sve što stari model izražava (tri stupca) vraća se bez gubitka.
/// </summary>
[DeveloperMigration(2026, 10, 11, Developer.SilvioHabazin, 0)]
public class BookingParticipationPricingCutover : DuneLightMigration
{
    private const string SingleParticipationGuard = @"
                IF EXISTS (SELECT 1 FROM dunelight.bookings b
                            WHERE (SELECT count(*) FROM dunelight.booking_segment_participations p WHERE p.booking_id = b.id) <> 1
                               OR NOT EXISTS (SELECT 1 FROM dunelight.booking_segment_participations p
                                                JOIN dunelight.appointment_segments s ON s.id = p.appointment_segment_id
                                               WHERE p.booking_id = b.id AND s.appointment_id = b.appointment_id
                                                 AND p.organization_id = b.organization_id)) THEN
                    RAISE EXCEPTION 'D3B2 pricing cutover: a booking does not have exactly one participation on its own appointment''s segment.';
                END IF;";

    public override void Up()
    {
        Execute.Sql($@"
            DO $$
            BEGIN
                {SingleParticipationGuard}
                IF EXISTS (SELECT 1 FROM dunelight.booking_segment_participations
                            WHERE amount IS NOT NULL OR suggested_amount IS NOT NULL OR is_amount_manually_overridden IS NOT NULL
                               OR base_amount IS NOT NULL OR base_amount_source IS NOT NULL OR adjustment_amount IS NOT NULL) THEN
                    RAISE EXCEPTION 'D3B2 pricing cutover: a participation already carries pricing — refusing to merge an unexpected partial state.';
                END IF;
                IF EXISTS (SELECT 1 FROM dunelight.bookings WHERE amount < 0 OR suggested_amount < 0) THEN
                    RAISE EXCEPTION 'D3B2 pricing cutover: a booking price is negative and cannot be represented on its participation.';
                END IF;
            END $$;");

        Execute.Sql(@"
            UPDATE dunelight.booking_segment_participations p
               SET amount = b.amount,
                   suggested_amount = b.suggested_amount,
                   is_amount_manually_overridden = b.is_amount_manually_overridden
              FROM dunelight.bookings b
             WHERE p.booking_id = b.id;");

        Execute.Sql(@"
            DO $$
            DECLARE
                mismatched bigint;
            BEGIN
                SELECT count(*) INTO mismatched
                  FROM dunelight.bookings b
                  JOIN dunelight.booking_segment_participations p ON p.booking_id = b.id
                 WHERE p.amount IS DISTINCT FROM b.amount
                    OR p.suggested_amount IS DISTINCT FROM b.suggested_amount
                    OR p.is_amount_manually_overridden IS DISTINCT FROM b.is_amount_manually_overridden;
                IF mismatched > 0 OR EXISTS (SELECT 1 FROM dunelight.booking_segment_participations
                                              WHERE amount IS NULL OR suggested_amount IS NULL OR is_amount_manually_overridden IS NULL) THEN
                    RAISE EXCEPTION 'D3B2 pricing cutover: % participation(s) do not carry their booking''s exact price.', mismatched;
                END IF;
            END $$;");

        Execute.Sql($@"
            ALTER TABLE dunelight.{Tables.BookingSegmentParticipations}
                ALTER COLUMN amount SET NOT NULL,
                ALTER COLUMN suggested_amount SET NOT NULL,
                ALTER COLUMN is_amount_manually_overridden SET NOT NULL;

            ALTER TABLE dunelight.{Tables.Bookings}
                DROP COLUMN amount,
                DROP COLUMN suggested_amount,
                DROP COLUMN is_amount_manually_overridden;");
    }

    public override void Down()
    {
        Execute.Sql($@"
            DO $$
            BEGIN
                {SingleParticipationGuard.Replace("D3B2 pricing cutover", "D3B2 pricing rollback")}
            END $$;

            ALTER TABLE dunelight.{Tables.Bookings}
                ADD COLUMN amount numeric(10,2) NOT NULL DEFAULT 0,
                ADD COLUMN suggested_amount numeric(10,2) NOT NULL DEFAULT 0,
                ADD COLUMN is_amount_manually_overridden boolean NOT NULL DEFAULT false;

            UPDATE dunelight.{Tables.Bookings} b
               SET amount = p.amount,
                   suggested_amount = p.suggested_amount,
                   is_amount_manually_overridden = p.is_amount_manually_overridden
              FROM dunelight.booking_segment_participations p
             WHERE p.booking_id = b.id;

            ALTER TABLE dunelight.{Tables.BookingSegmentParticipations}
                ALTER COLUMN amount DROP NOT NULL,
                ALTER COLUMN suggested_amount DROP NOT NULL,
                ALTER COLUMN is_amount_manually_overridden DROP NOT NULL;

            UPDATE dunelight.{Tables.BookingSegmentParticipations}
               SET amount = NULL, suggested_amount = NULL, is_amount_manually_overridden = NULL,
                   base_amount = NULL, base_amount_source = NULL, adjustment_amount = NULL;");
    }
}
