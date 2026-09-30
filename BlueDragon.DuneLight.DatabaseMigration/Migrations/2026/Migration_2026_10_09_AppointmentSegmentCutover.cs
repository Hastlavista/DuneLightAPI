using BlueDragon.DuneLight.DatabaseMigration.Extensions;
using BlueDragon.DuneLight.DatabaseMigration.Models;

namespace BlueDragon.DuneLight.DatabaseMigration.Migrations._2026;

/// <summary>
/// Phase D3A — AppointmentSegment postaje autoritativan za današnji jednostruki izvršni okvir termina.
///
/// 1. Zaštita: prije D3A nijedan produkcijski tok nije pisao segmente/sudjelovanja, pa postojeći retci u
///    appointment_segments / appointment_segment_employees / appointment_segment_resources / booking_segment_participations
///    znače neočekivano (djelomično) stanje — migracija tada JASNO pada umjesto da ga tiho spaja.
/// 2. Backfill: svaki termin dobiva TOČNO JEDAN segment (deterministički id = md5('appointment-segment:' || id)):
///    service_id, planned_start = starts_at, planned_end = starts_at + duration_minutes, room_id; created_at/updated_at
///    termina. employee_id (ako postoji) postaje jedna dodjela zaposlenika. Resursi i sudjelovanja se ne kreiraju.
///    Svi insertovi su dodatno uvjetovani NOT EXISTS (sigurno i za neočekivano ponovno pokretanje).
/// 3. Provjera: broj termina = broj termina s točno jednim segmentom, inače pad.
/// 4. Stari stupci appointments.service_id/employee_id/room_id/starts_at/duration_minutes se UKLANJAJU (s njima i FK-ovi
///    fk_appointments_service_id/employee_id/room_id, indeksi ix_appointments_org_company_startsat/org_employee_startsat
///    i unique ux_appointments_group_slot_startsat). Nijedan kod ih više ne čita ni ne piše, pa bi zadržani stupci bili
///    samo zastarjela kopija (i FK koji bi blokirao brisanje npr. zaposlenika koji više nije na terminu).
///    Jedinstvenost generiranog grupnog occurrencea (slot, početak) sada provodi GroupHandler.AddAppointments
///    (transakcijski advisory lock po slotu + ponovna provjera).
///
/// Down: vraća stare stupce, FK-ove i indekse te ih puni iz jedinog segmenta svakog termina, pa briše segmente
/// (pada ako postoje sudjelovanja ili termin s više segmenata/zaposlenika — takvo stanje nije izrazivo u starom modelu).
/// </summary>
[DeveloperMigration(2026, 10, 09, Developer.SilvioHabazin, 0)]
public class AppointmentSegmentCutover : DuneLightMigration
{
    public override void Up()
    {
        Execute.Sql(@"
            DO $$
            BEGIN
                IF EXISTS (SELECT 1 FROM dunelight.appointment_segments)
                   OR EXISTS (SELECT 1 FROM dunelight.appointment_segment_employees)
                   OR EXISTS (SELECT 1 FROM dunelight.appointment_segment_resources)
                   OR EXISTS (SELECT 1 FROM dunelight.booking_segment_participations) THEN
                    RAISE EXCEPTION 'D3A segment cutover: appointment_segments/assignments/participations already contain rows — refusing to merge an unexpected partial state. Inspect and clear them before running this migration.';
                END IF;
            END $$;");

        Execute.Sql(@"
            INSERT INTO dunelight.appointment_segments
                (id, organization_id, appointment_id, service_id, planned_start, planned_end, actual_start, actual_end,
                 room_id, created_at, updated_at)
            SELECT md5('appointment-segment:' || a.id::text)::uuid,
                   a.organization_id, a.id, a.service_id,
                   a.starts_at, a.starts_at + make_interval(mins => a.duration_minutes),
                   NULL, NULL, a.room_id, a.created_at, a.updated_at
              FROM dunelight.appointments a
             WHERE NOT EXISTS (SELECT 1 FROM dunelight.appointment_segments s WHERE s.appointment_id = a.id);

            INSERT INTO dunelight.appointment_segment_employees (appointment_segment_id, employee_id)
            SELECT md5('appointment-segment:' || a.id::text)::uuid, a.employee_id
              FROM dunelight.appointments a
             WHERE a.employee_id IS NOT NULL
               AND NOT EXISTS (SELECT 1 FROM dunelight.appointment_segment_employees e
                                WHERE e.appointment_segment_id = md5('appointment-segment:' || a.id::text)::uuid);");

        Execute.Sql(@"
            DO $$
            DECLARE
                mismatched bigint;
            BEGIN
                SELECT count(*) INTO mismatched
                  FROM dunelight.appointments a
                 WHERE (SELECT count(*) FROM dunelight.appointment_segments s WHERE s.appointment_id = a.id) <> 1
                    OR (SELECT count(*) FROM dunelight.appointment_segment_employees e
                          JOIN dunelight.appointment_segments s ON s.id = e.appointment_segment_id
                         WHERE s.appointment_id = a.id) <> (CASE WHEN a.employee_id IS NULL THEN 0 ELSE 1 END);
                IF mismatched > 0 THEN
                    RAISE EXCEPTION 'D3A segment cutover: % appointment(s) do not have exactly one matching segment.', mismatched;
                END IF;
            END $$;");

        Execute.Sql($@"
            ALTER TABLE dunelight.{Tables.Appointments}
                DROP COLUMN service_id,
                DROP COLUMN employee_id,
                DROP COLUMN room_id,
                DROP COLUMN starts_at,
                DROP COLUMN duration_minutes;");
    }

    public override void Down()
    {
        Execute.Sql($@"
            ALTER TABLE dunelight.{Tables.Appointments}
                ADD COLUMN service_id uuid,
                ADD COLUMN employee_id uuid,
                ADD COLUMN room_id uuid,
                ADD COLUMN starts_at timestamptz,
                ADD COLUMN duration_minutes integer;

            DO $$
            BEGIN
                IF EXISTS (SELECT 1 FROM dunelight.booking_segment_participations)
                   OR EXISTS (SELECT 1 FROM dunelight.appointment_segment_resources)
                   OR EXISTS (SELECT 1 FROM dunelight.appointments a
                               WHERE (SELECT count(*) FROM dunelight.appointment_segments s WHERE s.appointment_id = a.id) <> 1)
                   OR EXISTS (SELECT 1 FROM dunelight.appointment_segment_employees GROUP BY appointment_segment_id HAVING count(*) > 1) THEN
                    RAISE EXCEPTION 'D3A rollback: state is not expressible in the legacy single-frame appointment columns.';
                END IF;
            END $$;

            UPDATE dunelight.{Tables.Appointments} a
               SET service_id = s.service_id,
                   room_id = s.room_id,
                   starts_at = s.planned_start,
                   duration_minutes = (EXTRACT(EPOCH FROM (s.planned_end - s.planned_start)) / 60)::integer,
                   employee_id = (SELECT e.employee_id FROM dunelight.appointment_segment_employees e WHERE e.appointment_segment_id = s.id)
              FROM dunelight.appointment_segments s
             WHERE s.appointment_id = a.id;

            ALTER TABLE dunelight.{Tables.Appointments}
                ALTER COLUMN service_id SET NOT NULL,
                ALTER COLUMN starts_at SET NOT NULL,
                ALTER COLUMN duration_minutes SET NOT NULL,
                ADD CONSTRAINT fk_appointments_service_id FOREIGN KEY (service_id) REFERENCES dunelight.services(id),
                ADD CONSTRAINT fk_appointments_employee_id FOREIGN KEY (employee_id) REFERENCES dunelight.employees(id),
                ADD CONSTRAINT fk_appointments_room_id FOREIGN KEY (room_id) REFERENCES dunelight.rooms(id);

            CREATE INDEX ix_appointments_org_company_startsat ON dunelight.appointments (organization_id, company_id, starts_at);
            CREATE INDEX ix_appointments_org_employee_startsat ON dunelight.appointments (organization_id, employee_id, starts_at);
            CREATE UNIQUE INDEX ux_appointments_group_slot_startsat ON dunelight.appointments (group_slot_id, starts_at)
                WHERE group_slot_id IS NOT NULL;

            DELETE FROM dunelight.appointment_segment_employees;
            DELETE FROM dunelight.appointment_segments;");
    }
}
