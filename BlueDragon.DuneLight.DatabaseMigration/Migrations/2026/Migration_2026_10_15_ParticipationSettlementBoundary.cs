using BlueDragon.DuneLight.DatabaseMigration.Extensions;
using BlueDragon.DuneLight.DatabaseMigration.Models;

namespace BlueDragon.DuneLight.DatabaseMigration.Migrations._2026;

/// <summary>
/// Phase D3B3B — granica novčanog namirenja postaje SUDJELOVANJE: stavka usluge (checkout_items.type = 'Booking')
/// referencira booking_segment_participations umjesto bookings.
///
/// 1. booking_segment_participations: jedinstveni (id, organization_id) kao meta složenog FK-a (tenant integritet).
/// 2. checkout_items.booking_segment_participation_id + složeni FK (booking_segment_participation_id, organization_id)
///    -&gt; booking_segment_participations (id, organization_id), RESTRICT (povijest namirenja se nikad ne briše kaskadom,
///    nema povezivanja preko organizacija), indeks za upite po sudjelovanju.
/// 3. locks_booking/ux_checkout_items_locks_booking -&gt; locks_participation/ux_checkout_items_locks_participation (isto
///    sudjelovanje najviše jednom u Open checkoutima).
/// 4. CHECK ck_checkout_items_subject: Booking stavka nosi sudjelovanje (i ništa drugo).
/// 5. checkout_items.booking_id (i njegov FK) se UKLANJA — bez dvostrukog izvora.
///
/// Razvojna baza (sadržaj se ne čuva): postojeće stavke usluge dobivaju sudjelovanje svog Bookinga jednostavnim
/// spajanjem (jednostruki model) samo da bi CHECK vrijedio. Down: obrnuto.
/// </summary>
[DeveloperMigration(2026, 10, 15, Developer.SilvioHabazin, 0)]
public class ParticipationSettlementBoundary : DuneLightMigration
{
    public override void Up()
    {
        Execute.Sql($@"
            CREATE UNIQUE INDEX ux_booking_segment_participations_id_organization
                ON dunelight.{Tables.BookingSegmentParticipations} (id, organization_id);

            ALTER TABLE dunelight.{Tables.CheckoutItems}
                ADD COLUMN booking_segment_participation_id uuid NULL,
                ADD COLUMN locks_participation boolean NOT NULL DEFAULT false;

            UPDATE dunelight.{Tables.CheckoutItems} i
               SET booking_segment_participation_id = p.id, locks_participation = i.locks_booking
              FROM dunelight.{Tables.BookingSegmentParticipations} p
             WHERE p.booking_id = i.booking_id;

            DROP INDEX dunelight.ux_checkout_items_locks_booking;
            ALTER TABLE dunelight.{Tables.CheckoutItems}
                DROP CONSTRAINT ck_checkout_items_subject,
                DROP CONSTRAINT fk_checkout_items_booking_id,
                DROP COLUMN booking_id,
                DROP COLUMN locks_booking,
                ADD CONSTRAINT fk_checkout_items_participation_organization
                    FOREIGN KEY (booking_segment_participation_id, organization_id)
                    REFERENCES dunelight.{Tables.BookingSegmentParticipations} (id, organization_id) ON DELETE RESTRICT,
                ADD CONSTRAINT ck_checkout_items_subject CHECK (
                    (type = 'Booking' AND booking_segment_participation_id IS NOT NULL AND package_id IS NULL AND product_id IS NULL) OR
                    (type = 'Package' AND package_id IS NOT NULL AND booking_segment_participation_id IS NULL AND product_id IS NULL) OR
                    (type = 'Product' AND product_id IS NOT NULL AND booking_segment_participation_id IS NULL AND package_id IS NULL));

            CREATE INDEX ix_checkout_items_participation_id
                ON dunelight.{Tables.CheckoutItems} (booking_segment_participation_id);
            CREATE UNIQUE INDEX ux_checkout_items_locks_participation
                ON dunelight.{Tables.CheckoutItems} (booking_segment_participation_id) WHERE locks_participation = true;");
    }

    public override void Down()
    {
        Execute.Sql($@"
            ALTER TABLE dunelight.{Tables.CheckoutItems}
                ADD COLUMN booking_id uuid NULL,
                ADD COLUMN locks_booking boolean NOT NULL DEFAULT false;

            UPDATE dunelight.{Tables.CheckoutItems} i
               SET booking_id = p.booking_id, locks_booking = i.locks_participation
              FROM dunelight.{Tables.BookingSegmentParticipations} p
             WHERE p.id = i.booking_segment_participation_id;

            DROP INDEX dunelight.ux_checkout_items_locks_participation;
            DROP INDEX dunelight.ix_checkout_items_participation_id;
            ALTER TABLE dunelight.{Tables.CheckoutItems}
                DROP CONSTRAINT ck_checkout_items_subject,
                DROP CONSTRAINT fk_checkout_items_participation_organization,
                DROP COLUMN booking_segment_participation_id,
                DROP COLUMN locks_participation,
                ADD CONSTRAINT fk_checkout_items_booking_id FOREIGN KEY (booking_id) REFERENCES dunelight.{Tables.Bookings} (id),
                ADD CONSTRAINT ck_checkout_items_subject CHECK (
                    (type = 'Booking' AND booking_id IS NOT NULL AND package_id IS NULL AND product_id IS NULL) OR
                    (type = 'Package' AND package_id IS NOT NULL AND booking_id IS NULL AND product_id IS NULL) OR
                    (type = 'Product' AND product_id IS NOT NULL AND booking_id IS NULL AND package_id IS NULL));

            CREATE UNIQUE INDEX ux_checkout_items_locks_booking
                ON dunelight.{Tables.CheckoutItems} (booking_id) WHERE locks_booking = true;

            DROP INDEX dunelight.ux_booking_segment_participations_id_organization;");
    }
}
