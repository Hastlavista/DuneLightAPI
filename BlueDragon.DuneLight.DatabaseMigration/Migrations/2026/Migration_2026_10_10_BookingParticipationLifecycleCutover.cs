using BlueDragon.DuneLight.DatabaseMigration.Extensions;
using BlueDragon.DuneLight.DatabaseMigration.Models;

namespace BlueDragon.DuneLight.DatabaseMigration.Migrations._2026;

/// <summary>
/// Phase D3B1 — BookingSegmentParticipation postaje AUTORITATIVAN izvor izvršnog životnog ciklusa Bookinga (status,
/// StatusVersion, razlog otkazivanja, klasifikacija kasnog otkazivanja). Cijena, paket i naplata ostaju na Bookingu.
///
/// 1. Zaštita (jasan pad, bez popravljanja): postoje li već sudjelovanja; ima li ijedan termin s Bookingom broj segmenata
///    različit od 1 (jednostruki kompatibilni model iz D3A).
/// 2. Cjenovni snapshot sudjelovanja (base_amount, base_amount_source, suggested_amount, amount,
///    is_amount_manually_overridden) postaje NULLABLE i ostaje NULL: u D3B1 NIJE autoritativan (cijena je i dalje na
///    Bookingu, koji se re-cijeni npr. kod Update) — kopija bi odmah mogla zastarjeti. Postaje autoritativan tek u D3B2.
/// 3. Backfill: svaki Booking dobiva TOČNO JEDNO sudjelovanje (deterministički id md5('booking-participation:' || id)) na
///    jedinom segmentu svog termina: status, status_version, cancellation_reason, is_late_cancellation se kopiraju;
///    created_at/updated_at Bookinga. Dolazak (arrived_at/by) ostaje NULL — ne postoji postojeći autoritativan izvor.
/// 4. Provjera: svaki Booking ima točno jedno sudjelovanje na izvršnom segmentu svog termina, inače pad.
/// 5. bookings.status/status_version/cancellation_reason/is_late_cancellation se UKLANJAJU (jedan autoritativan izvor
///    životnog ciklusa, bez zastarjele kopije).
///
/// Down: vraća četiri stupca iz jedinog sudjelovanja svakog Bookinga, briše sudjelovanja i vraća NOT NULL cjenovnih
/// stupaca sudjelovanja (pada ako neki Booking nema točno jedno sudjelovanje — stanje neizrazivo u starom modelu).
/// </summary>
[DeveloperMigration(2026, 10, 10, Developer.SilvioHabazin, 0)]
public class BookingParticipationLifecycleCutover : DuneLightMigration
{
    public override void Up()
    {
        Execute.Sql(@"
            DO $$
            BEGIN
                IF EXISTS (SELECT 1 FROM dunelight.booking_segment_participations) THEN
                    RAISE EXCEPTION 'D3B1 cutover: booking_segment_participations already contains rows — refusing to merge an unexpected partial state.';
                END IF;
                IF EXISTS (SELECT 1 FROM dunelight.bookings b
                            WHERE (SELECT count(*) FROM dunelight.appointment_segments s WHERE s.appointment_id = b.appointment_id) <> 1) THEN
                    RAISE EXCEPTION 'D3B1 cutover: a booking''s appointment does not have exactly one segment — cannot pair bookings with an authoritative segment.';
                END IF;
            END $$;");

        Execute.Sql($@"
            ALTER TABLE dunelight.{Tables.BookingSegmentParticipations}
                ALTER COLUMN base_amount DROP NOT NULL,
                ALTER COLUMN base_amount_source DROP NOT NULL,
                ALTER COLUMN suggested_amount DROP NOT NULL,
                ALTER COLUMN amount DROP NOT NULL,
                ALTER COLUMN is_amount_manually_overridden DROP NOT NULL;");

        Execute.Sql(@"
            INSERT INTO dunelight.booking_segment_participations
                (id, organization_id, booking_id, appointment_segment_id, status, status_version,
                 arrived_at, arrived_by, cancellation_reason, is_late_cancellation, created_at, updated_at)
            SELECT md5('booking-participation:' || b.id::text)::uuid,
                   b.organization_id, b.id, s.id, b.status, b.status_version,
                   NULL, NULL, b.cancellation_reason, b.is_late_cancellation, b.created_at, b.updated_at
              FROM dunelight.bookings b
              JOIN dunelight.appointment_segments s ON s.appointment_id = b.appointment_id
             WHERE NOT EXISTS (SELECT 1 FROM dunelight.booking_segment_participations p WHERE p.booking_id = b.id);");

        Execute.Sql(@"
            DO $$
            DECLARE
                mismatched bigint;
            BEGIN
                SELECT count(*) INTO mismatched
                  FROM dunelight.bookings b
                 WHERE (SELECT count(*) FROM dunelight.booking_segment_participations p
                          JOIN dunelight.appointment_segments s ON s.id = p.appointment_segment_id
                         WHERE p.booking_id = b.id AND s.appointment_id = b.appointment_id AND p.organization_id = b.organization_id) <> 1
                    OR (SELECT count(*) FROM dunelight.booking_segment_participations p WHERE p.booking_id = b.id) <> 1;
                IF mismatched > 0 THEN
                    RAISE EXCEPTION 'D3B1 cutover: % booking(s) are not paired with exactly one participation on their appointment''s segment.', mismatched;
                END IF;
            END $$;");

        Execute.Sql($@"
            ALTER TABLE dunelight.{Tables.Bookings}
                DROP COLUMN status,
                DROP COLUMN status_version,
                DROP COLUMN cancellation_reason,
                DROP COLUMN is_late_cancellation;");
    }

    public override void Down()
    {
        Execute.Sql($@"
            DO $$
            BEGIN
                IF EXISTS (SELECT 1 FROM dunelight.bookings b
                            WHERE (SELECT count(*) FROM dunelight.booking_segment_participations p WHERE p.booking_id = b.id) <> 1)
                   OR EXISTS (SELECT 1 FROM dunelight.booking_segment_participations p
                               WHERE p.arrived_at IS NOT NULL OR p.base_amount IS NOT NULL OR p.amount IS NOT NULL) THEN
                    RAISE EXCEPTION 'D3B1 rollback: participation state is not expressible in the legacy booking lifecycle columns.';
                END IF;
            END $$;

            ALTER TABLE dunelight.{Tables.Bookings}
                ADD COLUMN status varchar(20),
                ADD COLUMN status_version integer NOT NULL DEFAULT 0,
                ADD COLUMN cancellation_reason text,
                ADD COLUMN is_late_cancellation boolean;

            UPDATE dunelight.{Tables.Bookings} b
               SET status = p.status,
                   status_version = p.status_version,
                   cancellation_reason = p.cancellation_reason,
                   is_late_cancellation = p.is_late_cancellation
              FROM dunelight.booking_segment_participations p
             WHERE p.booking_id = b.id;

            ALTER TABLE dunelight.{Tables.Bookings} ALTER COLUMN status SET NOT NULL;

            DELETE FROM dunelight.{Tables.BookingSegmentParticipations};

            ALTER TABLE dunelight.{Tables.BookingSegmentParticipations}
                ALTER COLUMN base_amount SET NOT NULL,
                ALTER COLUMN base_amount_source SET NOT NULL,
                ALTER COLUMN suggested_amount SET NOT NULL,
                ALTER COLUMN amount SET NOT NULL,
                ALTER COLUMN is_amount_manually_overridden SET NOT NULL;");
    }
}
