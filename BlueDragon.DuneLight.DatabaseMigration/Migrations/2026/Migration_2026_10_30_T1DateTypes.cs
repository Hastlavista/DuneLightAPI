using BlueDragon.DuneLight.DatabaseMigration.Extensions;
using BlueDragon.DuneLight.DatabaseMigration.Models;

namespace BlueDragon.DuneLight.DatabaseMigration.Migrations._2026;

/// <summary>
/// T1-7 — tipovi datuma i vremena (odluka "Tipovi datuma i vremena", T1_DECISION_RECORD): kalendarski dan je PostgreSQL
/// <c>date</c>, ne <c>timestamptz</c>. Stupci koji su do sada držali dan kao instant prelaze u <c>date</c>:
/// clients.date_of_birth / gdpr_consent_date, employees.date_of_birth / employment_start_date / employment_end_date,
/// client_packages.purchase_date, price_list_items.valid_from / valid_to, price_list_item_history.old/new_valid_from/to,
/// leave_funds.opened_at / expires_at.
///
/// Pretvorba postojećih redaka (ADR-0003: bez compatibility stupaca, samo pretvorba) u dva koraka po stupcu, jer USING ne smije
/// imati join: (1) UPDATE zapiše instant kao UTC ponoć lokalnog dana u mjerodavnoj zoni (zona dolazi iz join-a), (2) ALTER
/// COLUMN ... TYPE date USING (x AT TIME ZONE 'UTC')::date uzme taj dan. ALTER TYPE zadržava NOT NULL, poziciju stupca i
/// ograničenja (na ovim stupcima nema indeksa ni CHECK ograničenja u baseline migracijama).
///
/// Mjerodavna zona:
/// - klijent, zaposlenik: zona organizacije;
/// - paket klijenta: zona poslovnice prodaje (checkout koji je izdao paket), inače organizacije (ručni upis);
/// - stavka cjenika i njezina povijest: zona poslovnice stavke (company_id), inače organizacije;
/// - fond godišnjeg: UTC — LeaveFundYearCalculator je dan uvijek spremao kao UTC ponoć tog dana, pa je UTC datum točan dan.
/// </summary>
[DeveloperMigration(2026, 10, 30, Developer.SilvioHabazin, 1)]
public class T1DateTypes : DuneLightMigration
{
    public override void Up()
    {
        // clients — zona organizacije
        Execute.Sql(@"
            UPDATE dunelight.clients c
               SET date_of_birth = ((c.date_of_birth AT TIME ZONE o.time_zone)::date)::timestamp AT TIME ZONE 'UTC',
                   gdpr_consent_date = ((c.gdpr_consent_date AT TIME ZONE o.time_zone)::date)::timestamp AT TIME ZONE 'UTC'
              FROM dunelight.organizations o
             WHERE o.id = c.organization_id
               AND (c.date_of_birth IS NOT NULL OR c.gdpr_consent_date IS NOT NULL);

            ALTER TABLE dunelight.clients
                ALTER COLUMN date_of_birth TYPE date USING (date_of_birth AT TIME ZONE 'UTC')::date,
                ALTER COLUMN gdpr_consent_date TYPE date USING (gdpr_consent_date AT TIME ZONE 'UTC')::date;");

        // employees — zona organizacije
        Execute.Sql(@"
            UPDATE dunelight.employees e
               SET date_of_birth = ((e.date_of_birth AT TIME ZONE o.time_zone)::date)::timestamp AT TIME ZONE 'UTC',
                   employment_start_date = ((e.employment_start_date AT TIME ZONE o.time_zone)::date)::timestamp AT TIME ZONE 'UTC',
                   employment_end_date = ((e.employment_end_date AT TIME ZONE o.time_zone)::date)::timestamp AT TIME ZONE 'UTC'
              FROM dunelight.organizations o
             WHERE o.id = e.organization_id;

            ALTER TABLE dunelight.employees
                ALTER COLUMN date_of_birth TYPE date USING (date_of_birth AT TIME ZONE 'UTC')::date,
                ALTER COLUMN employment_start_date TYPE date USING (employment_start_date AT TIME ZONE 'UTC')::date,
                ALTER COLUMN employment_end_date TYPE date USING (employment_end_date AT TIME ZONE 'UTC')::date;");

        // client_packages — zona poslovnice prodaje (checkout koji je izdao paket), inače organizacije
        Execute.Sql(@"
            UPDATE dunelight.client_packages cp
               SET purchase_date = ((cp.purchase_date AT TIME ZONE COALESCE(
                       (SELECT co.time_zone
                          FROM dunelight.checkout_items ci
                          JOIN dunelight.checkouts ch ON ch.id = ci.checkout_id
                          JOIN dunelight.companies co ON co.id = ch.company_id
                         WHERE ci.client_package_id = cp.id
                         LIMIT 1),
                       o.time_zone))::date)::timestamp AT TIME ZONE 'UTC'
              FROM dunelight.organizations o
             WHERE o.id = cp.organization_id;

            ALTER TABLE dunelight.client_packages
                ALTER COLUMN purchase_date TYPE date USING (purchase_date AT TIME ZONE 'UTC')::date;");

        // price_list_items + price_list_item_history — zona poslovnice stavke, inače organizacije
        Execute.Sql(@"
            UPDATE dunelight.price_list_item_history h
               SET old_valid_from = ((h.old_valid_from AT TIME ZONE z.zone)::date)::timestamp AT TIME ZONE 'UTC',
                   new_valid_from = ((h.new_valid_from AT TIME ZONE z.zone)::date)::timestamp AT TIME ZONE 'UTC',
                   old_valid_to = ((h.old_valid_to AT TIME ZONE z.zone)::date)::timestamp AT TIME ZONE 'UTC',
                   new_valid_to = ((h.new_valid_to AT TIME ZONE z.zone)::date)::timestamp AT TIME ZONE 'UTC'
              FROM (SELECT p.id, COALESCE(co.time_zone, o.time_zone) AS zone
                      FROM dunelight.price_list_items p
                      JOIN dunelight.organizations o ON o.id = p.organization_id
                      LEFT JOIN dunelight.companies co ON co.id = p.company_id) z
             WHERE z.id = h.price_list_item_id;

            UPDATE dunelight.price_list_items p
               SET valid_from = ((p.valid_from AT TIME ZONE z.zone)::date)::timestamp AT TIME ZONE 'UTC',
                   valid_to = ((p.valid_to AT TIME ZONE z.zone)::date)::timestamp AT TIME ZONE 'UTC'
              FROM (SELECT p2.id, COALESCE(co.time_zone, o.time_zone) AS zone
                      FROM dunelight.price_list_items p2
                      JOIN dunelight.organizations o ON o.id = p2.organization_id
                      LEFT JOIN dunelight.companies co ON co.id = p2.company_id) z
             WHERE z.id = p.id;

            ALTER TABLE dunelight.price_list_item_history
                ALTER COLUMN old_valid_from TYPE date USING (old_valid_from AT TIME ZONE 'UTC')::date,
                ALTER COLUMN new_valid_from TYPE date USING (new_valid_from AT TIME ZONE 'UTC')::date,
                ALTER COLUMN old_valid_to TYPE date USING (old_valid_to AT TIME ZONE 'UTC')::date,
                ALTER COLUMN new_valid_to TYPE date USING (new_valid_to AT TIME ZONE 'UTC')::date;

            ALTER TABLE dunelight.price_list_items
                ALTER COLUMN valid_from TYPE date USING (valid_from AT TIME ZONE 'UTC')::date,
                ALTER COLUMN valid_to TYPE date USING (valid_to AT TIME ZONE 'UTC')::date;");

        // leave_funds — dan je uvijek spremljen kao UTC ponoć (LeaveFundYearCalculator), pa je UTC datum točan dan
        Execute.Sql(@"
            ALTER TABLE dunelight.leave_funds
                ALTER COLUMN opened_at TYPE date USING (opened_at AT TIME ZONE 'UTC')::date,
                ALTER COLUMN expires_at TYPE date USING (expires_at AT TIME ZONE 'UTC')::date;");
    }
}
