using BlueDragon.DuneLight.DatabaseMigration.Extensions;
using BlueDragon.DuneLight.DatabaseMigration.Models;

namespace BlueDragon.DuneLight.DatabaseMigration.Migrations._2026;

/// <summary>
/// Phase M1A.1 — dvije eksplicitne činjenice termina koje se NE smiju izvoditi iz sudjelovanja:
/// 1. appointments.cancelled_at/cancelled_by — TRENUTNA eksplicitna otkazanost termina (otkazivanje na razini termina);
///    ulaz u izvođenje statusa (Cancelled samo uz nju; "svi klijenti pojedinačno otkazali" ostaje Scheduled).
/// 2. appointments.closed_out_at/closed_out_by — poslovna činjenica close-outa grupne sesije (idempotencija isteka liste
///    čekanja i provizije po sesiji), neovisna o statusu termina i o postojanju CommissionEntry. Samo Form=Group.
/// CHECK: "by" samo uz "at"; close-out samo za grupne termine.
///
/// Razvojna baza (sadržaj se ne čuva): zatečeni Cancelled retci dobivaju cancelled_at (updated_at/created_at) da bi
/// ostali eksplicitno otkazani. Down: obrnuto (stupci i CHECK-ovi se uklanjaju).
/// </summary>
[DeveloperMigration(2026, 10, 18, Developer.SilvioHabazin, 0)]
public class AppointmentExplicitCancellationAndCloseOut : DuneLightMigration
{
    public override void Up()
    {
        Execute.Sql($@"
            ALTER TABLE dunelight.{Tables.Appointments}
                ADD COLUMN cancelled_at timestamptz NULL,
                ADD COLUMN cancelled_by uuid NULL,
                ADD COLUMN closed_out_at timestamptz NULL,
                ADD COLUMN closed_out_by uuid NULL;

            UPDATE dunelight.{Tables.Appointments}
               SET cancelled_at = COALESCE(updated_at, created_at)
             WHERE status = 'Cancelled';

            ALTER TABLE dunelight.{Tables.Appointments}
                ADD CONSTRAINT ck_appointments_cancelled_by CHECK (cancelled_by IS NULL OR cancelled_at IS NOT NULL),
                ADD CONSTRAINT ck_appointments_closed_out CHECK (
                    (closed_out_by IS NULL OR closed_out_at IS NOT NULL) AND (closed_out_at IS NULL OR form = 'Group'));");
    }

    public override void Down()
    {
        Execute.Sql($@"
            ALTER TABLE dunelight.{Tables.Appointments}
                DROP CONSTRAINT ck_appointments_closed_out,
                DROP CONSTRAINT ck_appointments_cancelled_by,
                DROP COLUMN closed_out_by,
                DROP COLUMN closed_out_at,
                DROP COLUMN cancelled_by,
                DROP COLUMN cancelled_at;");
    }
}
