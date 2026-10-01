using BlueDragon.DuneLight.DatabaseMigration.Extensions;
using BlueDragon.DuneLight.DatabaseMigration.Models;

namespace BlueDragon.DuneLight.DatabaseMigration.Migrations._2026;

/// <summary>
/// Phase M0 — sudjelovanje je adresa izvršnih i komercijalnih pojava (Booking nema životni ciklus ni verziju):
///
/// 1. commission_entries.booking_segment_participation_id — IZVOR IndividualService provizije je sudjelovanje;
///    idempotencija po (booking_segment_participation_id, source_version) (source_version = StatusVersion sudjelovanja)
///    umjesto (booking_id, source_version), koja bi se sudarila za dva sudjelovanja istog Bookinga s istom verzijom.
///    Složeni FK (participation, booking, organization) -&gt; booking_segment_participations jamči da sudjelovanje pripada
///    navedenom Bookingu/organizaciji (booking_id ostaje kontekst za izvještaje, ne drugi izvor). CASCADE kao postojeći
///    FK na booking. CHECK izvora: IndividualService nosi sudjelovanje, ostali ne.
/// 2. appointment_audit_log.booking_segment_participation_id — audit prijelaza/paketa/plaćanja po sudjelovanju
///    (status_version je verzija tog sudjelovanja). CASCADE kao booking_id.
///
/// Razvojna baza (sadržaj se ne čuva): postojeći IndividualService zapisi dobivaju sudjelovanje svog Bookinga
/// jednostavnim spajanjem (jednostruki model) samo da bi CHECK vrijedio. Down: obrnuto.
/// </summary>
[DeveloperMigration(2026, 10, 16, Developer.SilvioHabazin, 0)]
public class ParticipationNativeAddressing : DuneLightMigration
{
    public override void Up()
    {
        Execute.Sql($@"
            CREATE UNIQUE INDEX ux_booking_segment_participations_id_booking_organization
                ON dunelight.{Tables.BookingSegmentParticipations} (id, booking_id, organization_id);

            ALTER TABLE dunelight.{Tables.CommissionEntries}
                ADD COLUMN booking_segment_participation_id uuid NULL;

            UPDATE dunelight.{Tables.CommissionEntries} e
               SET booking_segment_participation_id = p.id
              FROM dunelight.{Tables.BookingSegmentParticipations} p
             WHERE p.booking_id = e.booking_id AND e.source_type = 'IndividualService';

            DROP INDEX dunelight.ux_commission_entries_booking_id_source_version;
            ALTER TABLE dunelight.{Tables.CommissionEntries}
                DROP CONSTRAINT ck_commission_entries_source,
                ADD CONSTRAINT fk_commission_entries_participation
                    FOREIGN KEY (booking_segment_participation_id, booking_id, organization_id)
                    REFERENCES dunelight.{Tables.BookingSegmentParticipations} (id, booking_id, organization_id) ON DELETE CASCADE,
                ADD CONSTRAINT ck_commission_entries_source CHECK (
                    (source_type = 'IndividualService' AND booking_segment_participation_id IS NOT NULL AND booking_id IS NOT NULL
                        AND appointment_id IS NOT NULL AND checkout_item_id IS NULL) OR
                    (source_type = 'GroupService' AND appointment_id IS NOT NULL AND booking_id IS NULL
                        AND booking_segment_participation_id IS NULL AND checkout_item_id IS NULL) OR
                    (source_type = 'ProductSale' AND checkout_item_id IS NOT NULL AND appointment_id IS NULL AND booking_id IS NULL
                        AND booking_segment_participation_id IS NULL) OR
                    (source_type = 'PackageSale' AND checkout_item_id IS NOT NULL AND appointment_id IS NULL AND booking_id IS NULL
                        AND booking_segment_participation_id IS NULL));

            CREATE UNIQUE INDEX ux_commission_entries_participation_source_version
                ON dunelight.{Tables.CommissionEntries} (booking_segment_participation_id, source_version)
                WHERE booking_segment_participation_id IS NOT NULL;

            ALTER TABLE dunelight.{Tables.AppointmentAuditLog}
                ADD COLUMN booking_segment_participation_id uuid NULL,
                ADD CONSTRAINT fk_appointment_audit_log_participation
                    FOREIGN KEY (booking_segment_participation_id)
                    REFERENCES dunelight.{Tables.BookingSegmentParticipations} (id) ON DELETE CASCADE;

            CREATE INDEX ix_appointment_audit_log_participation_id
                ON dunelight.{Tables.AppointmentAuditLog} (booking_segment_participation_id);");
    }

    public override void Down()
    {
        Execute.Sql($@"
            DROP INDEX dunelight.ix_appointment_audit_log_participation_id;
            ALTER TABLE dunelight.{Tables.AppointmentAuditLog}
                DROP CONSTRAINT fk_appointment_audit_log_participation,
                DROP COLUMN booking_segment_participation_id;

            DROP INDEX dunelight.ux_commission_entries_participation_source_version;
            ALTER TABLE dunelight.{Tables.CommissionEntries}
                DROP CONSTRAINT ck_commission_entries_source,
                DROP CONSTRAINT fk_commission_entries_participation,
                DROP COLUMN booking_segment_participation_id,
                ADD CONSTRAINT ck_commission_entries_source CHECK (
                    (source_type = 'IndividualService' AND booking_id IS NOT NULL AND appointment_id IS NOT NULL AND checkout_item_id IS NULL) OR
                    (source_type = 'GroupService' AND appointment_id IS NOT NULL AND booking_id IS NULL AND checkout_item_id IS NULL) OR
                    (source_type = 'ProductSale' AND checkout_item_id IS NOT NULL AND appointment_id IS NULL AND booking_id IS NULL) OR
                    (source_type = 'PackageSale' AND checkout_item_id IS NOT NULL AND appointment_id IS NULL AND booking_id IS NULL));

            CREATE UNIQUE INDEX ux_commission_entries_booking_id_source_version
                ON dunelight.{Tables.CommissionEntries} (booking_id, source_version)
                WHERE booking_id IS NOT NULL;

            DROP INDEX dunelight.ux_booking_segment_participations_id_booking_organization;");
    }
}
