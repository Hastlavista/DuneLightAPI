using BlueDragon.DuneLight.DatabaseMigration.Extensions;
using BlueDragon.DuneLight.DatabaseMigration.Models;

namespace BlueDragon.DuneLight.DatabaseMigration.Migrations._2026;

/// <summary>
/// K2 (ADR-0032) — ovlasti:
/// (1) zatvorenost termina: ručno zatvaranje (closed_at/by) i zadnje ponovno otvaranje (reopened_at/by/reason); automatsko
///     zatvaranje se izvodi (Utils/AppointmentClosure), ne sprema se;
/// (2) gasi se appointments.policy.override — briše se iz SVIH grupa (ne-Admin grupe gube tu ovlast i ne dobivaju nove; nema
///     produkcijskih podataka, ADR-0003);
/// (3) novi grantovi (otpis naknade / jedinice, korekcije po izvornom statusu, rad izvan radnog vremena, roster u prošlosti)
///     dodaju se SAMO Admin grupama (grant_groups.system_key = 'admin', ADR-0023). Idempotentno za grantove.
/// </summary>
[DeveloperMigration(2026, 10, 29, Developer.SilvioHabazin, 0)]
public class K2Permissions : DuneLightMigration
{
    public override void Up()
    {
        Execute.Sql(@"
            ALTER TABLE dunelight.appointments
                ADD COLUMN closed_at timestamp with time zone,
                ADD COLUMN closed_by uuid,
                ADD COLUMN reopened_at timestamp with time zone,
                ADD COLUMN reopened_by uuid,
                ADD COLUMN reopen_reason character varying(500),
                ADD CONSTRAINT ck_appointments_closed CHECK ((closed_at IS NULL) = (closed_by IS NULL)),
                ADD CONSTRAINT ck_appointments_reopened CHECK (
                    (reopened_at IS NULL AND reopened_by IS NULL AND reopen_reason IS NULL)
                    OR (reopened_at IS NOT NULL AND reopened_by IS NOT NULL AND reopen_reason IS NOT NULL));");

        Execute.Sql(@"
            DELETE FROM dunelight.grant_group_grants WHERE grant_key = 'appointments.policy.override';");

        Execute.Sql(@"
            INSERT INTO dunelight.grant_group_grants (id, grant_group_id, grant_key)
            SELECT gen_random_uuid(), g.id, k.grant_key
            FROM dunelight.grant_groups g
            CROSS JOIN (VALUES
                ('appointments.policy.fee.waive'),
                ('appointments.policy.unit.waive'),
                ('appointments.corrections.completed'),
                ('appointments.corrections.no-show'),
                ('appointments.corrections.cancelled'),
                ('appointments.availability.override'),
                ('roster.entries.write.past')) AS k(grant_key)
            WHERE g.system_key = 'admin'
            ON CONFLICT (grant_group_id, grant_key) DO NOTHING;");
    }
}
