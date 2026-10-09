using BlueDragon.DuneLight.DatabaseMigration.Extensions;
using BlueDragon.DuneLight.DatabaseMigration.Models;

namespace BlueDragon.DuneLight.DatabaseMigration.Migrations._2026;

/// <summary>
/// K1-2 — označavanje dolaska: (1) dolazak postoji samo uz Confirmed/Completed (metapodatak odgovara statusu, kao P1 metapodaci
/// otkazivanja i izostanka); (2) novi grant appointments.arrival.mark dodaje se SAMO inicijalnim Admin grupama
/// (grant_groups.system_key = 'admin', ADR-0023). Idempotentno za grant.
/// </summary>
[DeveloperMigration(2026, 10, 28, Developer.SilvioHabazin, 0)]
public class K1Arrival : DuneLightMigration
{
    public override void Up()
    {
        Execute.Sql(@"
            ALTER TABLE dunelight.booking_segment_participations
                ADD CONSTRAINT ck_booking_segment_participations_arrival_status
                CHECK (arrived_at IS NULL OR status IN ('Confirmed', 'Completed'));");

        Execute.Sql(@"
            INSERT INTO dunelight.grant_group_grants (id, grant_group_id, grant_key)
            SELECT gen_random_uuid(), g.id, 'appointments.arrival.mark'
            FROM dunelight.grant_groups g
            WHERE g.system_key = 'admin'
            ON CONFLICT (grant_group_id, grant_key) DO NOTHING;");
    }
}
