using BlueDragon.DuneLight.DatabaseMigration.Extensions;
using BlueDragon.DuneLight.DatabaseMigration.Models;

namespace BlueDragon.DuneLight.DatabaseMigration.Migrations._2026;

/// <summary>
/// Phase M1G — segmenti s više zaposlenika, eksplicitni izvor cijene i provizija po zaposleniku:
/// 1. price_list_items.employee_id — cjenovna razina ZAPOSLENIKA (samo za stavke usluge; paket je uvijek bez zaposlenika).
///    Prvenstvo (Employee način): zaposlenik+poslovnica → zaposlenik → poslovnica → organizacija → zadana cijena usluge.
/// 2. appointment_segments.pricing_mode/pricing_employee_id — izvor cijene segmenta (Standard = bez razina zaposlenika;
///    Employee = razine odabranog zaposlenika). Pripadnost pricing_employee_id skupu zaposlenika segmenta provodi domena:
///    relacijski bi zahtijevala kružni (odgođeni) FK segment → vlastita dodjela zaposlenika — namjerno izbjegnuto.
/// 3. booking_segment_participations.pricing_mode/pricing_employee_id — POVIJESNI snapshot izvora cijene korištenog pri
///    razrješavanju (uz base_amount/base_amount_source); nikad se ne izvodi iz trenutnog stanja segmenta.
/// 4. group_segment_template_employees + group_segment_templates.pricing_mode/pricing_employee_id — osoblje i izvor cijene
///    po predlošku; groups.default_trainer_id se UKLANJA (više nije autoritativno — kompatibilnost je samo u DTO-u).
/// 5. commission_entries: individualna provizija je jedinstvena po (sudjelovanje, zaposlenik, verzija izvora); grupna po
///    (segment occurrencea, zaposlenik) — novi stupac appointment_segment_id (izvor grupne provizije je SEGMENT).
///
/// Razvojna baza (bez složenog backfilla): segment s jednim zaposlenikom → Employee/taj zaposlenik, inače Standard;
/// postojeća razrješavanja cijene nisu koristila razinu zaposlenika → Standard; trener grupe → zaposlenik svakog
/// predloška; postojeća grupna provizija → jedini segment svog occurrencea.
/// </summary>
[DeveloperMigration(2026, 10, 22, Developer.SilvioHabazin, 0)]
public class MultiEmployeeSegments : DuneLightMigration
{
    public override void Up()
    {
        Execute.Sql($@"
            ALTER TABLE dunelight.{Tables.PriceListItems}
                ADD COLUMN employee_id uuid NULL,
                ADD CONSTRAINT fk_price_list_items_employee_id FOREIGN KEY (employee_id)
                    REFERENCES dunelight.{Tables.Employees} (id) ON DELETE RESTRICT,
                ADD CONSTRAINT ck_price_list_items_employee_service CHECK (employee_id IS NULL OR service_id IS NOT NULL);
            CREATE INDEX ix_price_list_items_service_employee ON dunelight.{Tables.PriceListItems} (organization_id, service_id, employee_id);

            ALTER TABLE dunelight.{Tables.AppointmentSegments}
                ADD COLUMN pricing_mode varchar(32) NULL,
                ADD COLUMN pricing_employee_id uuid NULL,
                ADD CONSTRAINT fk_appointment_segments_pricing_employee_id FOREIGN KEY (pricing_employee_id)
                    REFERENCES dunelight.{Tables.Employees} (id) ON DELETE RESTRICT;
            UPDATE dunelight.{Tables.AppointmentSegments} s
               SET pricing_mode = 'Employee',
                   pricing_employee_id = (SELECT e.employee_id FROM dunelight.{Tables.AppointmentSegmentEmployees} e
                                           WHERE e.appointment_segment_id = s.id)
             WHERE (SELECT count(*) FROM dunelight.{Tables.AppointmentSegmentEmployees} e WHERE e.appointment_segment_id = s.id) = 1;
            UPDATE dunelight.{Tables.AppointmentSegments} SET pricing_mode = 'Standard' WHERE pricing_mode IS NULL;
            ALTER TABLE dunelight.{Tables.AppointmentSegments}
                ALTER COLUMN pricing_mode SET NOT NULL,
                ADD CONSTRAINT ck_appointment_segments_pricing_source CHECK (
                    (pricing_mode = 'Standard' AND pricing_employee_id IS NULL) OR
                    (pricing_mode = 'Employee' AND pricing_employee_id IS NOT NULL));

            ALTER TABLE dunelight.{Tables.BookingSegmentParticipations}
                DROP CONSTRAINT ck_booking_segment_participations_base_amount_source,
                ADD CONSTRAINT ck_booking_segment_participations_base_amount_source CHECK (base_amount_source IN
                    ('EmployeeCompanySpecific', 'EmployeeAllCompanies', 'CompanySpecific', 'AllCompanies', 'Default')),
                ADD COLUMN pricing_mode varchar(32) NULL,
                ADD COLUMN pricing_employee_id uuid NULL,
                ADD CONSTRAINT fk_booking_segment_participations_pricing_employee_id FOREIGN KEY (pricing_employee_id)
                    REFERENCES dunelight.{Tables.Employees} (id) ON DELETE RESTRICT;
            UPDATE dunelight.{Tables.BookingSegmentParticipations} SET pricing_mode = 'Standard' WHERE base_amount IS NOT NULL;
            ALTER TABLE dunelight.{Tables.BookingSegmentParticipations}
                ADD CONSTRAINT ck_booking_segment_participations_pricing_source CHECK (
                    (pricing_mode IS NULL AND pricing_employee_id IS NULL AND base_amount IS NULL) OR
                    (pricing_mode = 'Standard' AND pricing_employee_id IS NULL AND base_amount IS NOT NULL) OR
                    (pricing_mode = 'Employee' AND pricing_employee_id IS NOT NULL AND base_amount IS NOT NULL));

            CREATE TABLE dunelight.group_segment_template_employees (
                group_segment_template_id uuid NOT NULL,
                employee_id uuid NOT NULL,
                CONSTRAINT pk_group_segment_template_employees PRIMARY KEY (group_segment_template_id, employee_id),
                CONSTRAINT fk_group_segment_template_employees_template FOREIGN KEY (group_segment_template_id)
                    REFERENCES dunelight.group_segment_templates (id) ON DELETE CASCADE,
                CONSTRAINT fk_group_segment_template_employees_employee FOREIGN KEY (employee_id)
                    REFERENCES dunelight.{Tables.Employees} (id) ON DELETE RESTRICT);
            CREATE INDEX ix_group_segment_template_employees_employee ON dunelight.group_segment_template_employees (employee_id);

            INSERT INTO dunelight.group_segment_template_employees (group_segment_template_id, employee_id)
            SELECT t.id, g.default_trainer_id
              FROM dunelight.group_segment_templates t
              JOIN dunelight.{Tables.Groups} g ON g.id = t.group_id
             WHERE g.default_trainer_id IS NOT NULL;

            ALTER TABLE dunelight.group_segment_templates
                ADD COLUMN pricing_mode varchar(32) NULL,
                ADD COLUMN pricing_employee_id uuid NULL,
                ADD CONSTRAINT fk_group_segment_templates_pricing_employee_id FOREIGN KEY (pricing_employee_id)
                    REFERENCES dunelight.{Tables.Employees} (id) ON DELETE RESTRICT;
            UPDATE dunelight.group_segment_templates t
               SET pricing_mode = CASE WHEN g.default_trainer_id IS NULL THEN 'Standard' ELSE 'Employee' END,
                   pricing_employee_id = g.default_trainer_id
              FROM dunelight.{Tables.Groups} g
             WHERE g.id = t.group_id;
            ALTER TABLE dunelight.group_segment_templates
                ALTER COLUMN pricing_mode SET NOT NULL,
                ADD CONSTRAINT ck_group_segment_templates_pricing_source CHECK (
                    (pricing_mode = 'Standard' AND pricing_employee_id IS NULL) OR
                    (pricing_mode = 'Employee' AND pricing_employee_id IS NOT NULL));

            ALTER TABLE dunelight.{Tables.Groups}
                DROP CONSTRAINT fk_groups_default_trainer_id,
                DROP COLUMN default_trainer_id;

            ALTER TABLE dunelight.{Tables.CommissionEntries}
                ADD COLUMN appointment_segment_id uuid NULL,
                ADD CONSTRAINT fk_commission_entries_appointment_segment_id FOREIGN KEY (appointment_segment_id)
                    REFERENCES dunelight.{Tables.AppointmentSegments} (id);
            UPDATE dunelight.{Tables.CommissionEntries} c
               SET appointment_segment_id = (SELECT min(s.id::text)::uuid FROM dunelight.{Tables.AppointmentSegments} s
                                              WHERE s.appointment_id = c.appointment_id)
             WHERE c.source_type = 'GroupService';

            DROP INDEX dunelight.ux_commission_entries_group_appointment;
            DROP INDEX dunelight.ux_commission_entries_participation_source_version;
            ALTER TABLE dunelight.{Tables.CommissionEntries}
                DROP CONSTRAINT ck_commission_entries_source,
                ADD CONSTRAINT ck_commission_entries_source CHECK (
                    (source_type = 'IndividualService' AND booking_segment_participation_id IS NOT NULL AND booking_id IS NOT NULL
                        AND appointment_id IS NOT NULL AND appointment_segment_id IS NULL AND checkout_item_id IS NULL) OR
                    (source_type = 'GroupService' AND appointment_id IS NOT NULL AND appointment_segment_id IS NOT NULL
                        AND booking_id IS NULL AND booking_segment_participation_id IS NULL AND checkout_item_id IS NULL) OR
                    (source_type = 'ProductSale' AND checkout_item_id IS NOT NULL AND appointment_id IS NULL AND booking_id IS NULL
                        AND booking_segment_participation_id IS NULL AND appointment_segment_id IS NULL) OR
                    (source_type = 'PackageSale' AND checkout_item_id IS NOT NULL AND appointment_id IS NULL AND booking_id IS NULL
                        AND booking_segment_participation_id IS NULL AND appointment_segment_id IS NULL));

            CREATE UNIQUE INDEX ux_commission_entries_participation_employee_source_version
                ON dunelight.{Tables.CommissionEntries} (booking_segment_participation_id, employee_id, source_version)
                WHERE booking_segment_participation_id IS NOT NULL;
            CREATE UNIQUE INDEX ux_commission_entries_group_segment_employee
                ON dunelight.{Tables.CommissionEntries} (appointment_segment_id, employee_id)
                WHERE source_type = 'GroupService';");
    }

    public override void Down()
    {
        Execute.Sql($@"
            DROP INDEX dunelight.ux_commission_entries_group_segment_employee;
            DROP INDEX dunelight.ux_commission_entries_participation_employee_source_version;
            ALTER TABLE dunelight.{Tables.CommissionEntries}
                DROP CONSTRAINT ck_commission_entries_source,
                ADD CONSTRAINT ck_commission_entries_source CHECK (
                    (source_type = 'IndividualService' AND booking_segment_participation_id IS NOT NULL AND booking_id IS NOT NULL
                        AND appointment_id IS NOT NULL AND checkout_item_id IS NULL) OR
                    (source_type = 'GroupService' AND appointment_id IS NOT NULL AND booking_id IS NULL
                        AND booking_segment_participation_id IS NULL AND checkout_item_id IS NULL) OR
                    (source_type = 'ProductSale' AND checkout_item_id IS NOT NULL AND appointment_id IS NULL AND booking_id IS NULL
                        AND booking_segment_participation_id IS NULL) OR
                    (source_type = 'PackageSale' AND checkout_item_id IS NOT NULL AND appointment_id IS NULL AND booking_id IS NULL
                        AND booking_segment_participation_id IS NULL)),
                DROP CONSTRAINT fk_commission_entries_appointment_segment_id,
                DROP COLUMN appointment_segment_id;
            CREATE UNIQUE INDEX ux_commission_entries_participation_source_version
                ON dunelight.{Tables.CommissionEntries} (booking_segment_participation_id, source_version)
                WHERE booking_segment_participation_id IS NOT NULL;
            CREATE UNIQUE INDEX ux_commission_entries_group_appointment
                ON dunelight.{Tables.CommissionEntries} (appointment_id) WHERE source_type = 'GroupService';

            ALTER TABLE dunelight.{Tables.Groups}
                ADD COLUMN default_trainer_id uuid NULL,
                ADD CONSTRAINT fk_groups_default_trainer_id FOREIGN KEY (default_trainer_id) REFERENCES dunelight.{Tables.Employees} (id);
            UPDATE dunelight.{Tables.Groups} g
               SET default_trainer_id = (SELECT min(e.employee_id::text)::uuid
                                           FROM dunelight.group_segment_template_employees e
                                           JOIN dunelight.group_segment_templates t ON t.id = e.group_segment_template_id
                                          WHERE t.group_id = g.id);
            ALTER TABLE dunelight.group_segment_templates
                DROP CONSTRAINT ck_group_segment_templates_pricing_source,
                DROP CONSTRAINT fk_group_segment_templates_pricing_employee_id,
                DROP COLUMN pricing_mode,
                DROP COLUMN pricing_employee_id;
            DROP TABLE dunelight.group_segment_template_employees;

            ALTER TABLE dunelight.{Tables.BookingSegmentParticipations}
                DROP CONSTRAINT ck_booking_segment_participations_pricing_source,
                DROP CONSTRAINT fk_booking_segment_participations_pricing_employee_id,
                DROP COLUMN pricing_mode,
                DROP COLUMN pricing_employee_id,
                DROP CONSTRAINT ck_booking_segment_participations_base_amount_source;
            UPDATE dunelight.{Tables.BookingSegmentParticipations} SET base_amount_source = 'CompanySpecific'
             WHERE base_amount_source = 'EmployeeCompanySpecific';
            UPDATE dunelight.{Tables.BookingSegmentParticipations} SET base_amount_source = 'AllCompanies'
             WHERE base_amount_source = 'EmployeeAllCompanies';
            ALTER TABLE dunelight.{Tables.BookingSegmentParticipations}
                ADD CONSTRAINT ck_booking_segment_participations_base_amount_source
                    CHECK (base_amount_source IN ('CompanySpecific', 'AllCompanies', 'Default'));

            ALTER TABLE dunelight.{Tables.AppointmentSegments}
                DROP CONSTRAINT ck_appointment_segments_pricing_source,
                DROP CONSTRAINT fk_appointment_segments_pricing_employee_id,
                DROP COLUMN pricing_mode,
                DROP COLUMN pricing_employee_id;

            DROP INDEX dunelight.ix_price_list_items_service_employee;
            ALTER TABLE dunelight.{Tables.PriceListItems}
                DROP CONSTRAINT ck_price_list_items_employee_service,
                DROP CONSTRAINT fk_price_list_items_employee_id,
                DROP COLUMN employee_id;");
    }
}
