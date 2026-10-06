using BlueDragon.DuneLight.DatabaseMigration.Extensions;
using BlueDragon.DuneLight.DatabaseMigration.Models;

namespace BlueDragon.DuneLight.DatabaseMigration.Migrations._2026;

/// <summary>
/// Početna migracija (ADR-0022), korak 5/11: cjenik po poslovnici/zaposleniku i povijest cijena.
/// Gradi isključivo trenutnu ciljnu shemu; ne seeda podatke.
/// </summary>
[DeveloperMigration(2026, 10, 25, Developer.SilvioHabazin, 5)]
public class Baseline05PriceList : DuneLightMigration
{
    public override void Up()
    {
        // price_list_items
        Execute.Sql(@"
            CREATE TABLE dunelight.price_list_items (
                id uuid NOT NULL,
                organization_id uuid NOT NULL,
                service_id uuid,
                package_id uuid,
                company_id uuid,
                price numeric(10,2) NOT NULL,
                valid_from timestamp with time zone NOT NULL,
                valid_to timestamp with time zone,
                is_active boolean DEFAULT true NOT NULL,
                created_at timestamp with time zone NOT NULL,
                created_by uuid,
                updated_at timestamp with time zone,
                updated_by uuid,
                employee_id uuid,
                CONSTRAINT ck_price_list_items_employee_service CHECK (((employee_id IS NULL) OR (service_id IS NOT NULL))),
                CONSTRAINT ck_price_list_items_subject CHECK ((((service_id IS NOT NULL) AND (package_id IS NULL)) OR ((service_id IS NULL) AND (package_id IS NOT NULL))))
            );

            ALTER TABLE dunelight.price_list_items ADD CONSTRAINT pk_price_list_items PRIMARY KEY (id);

            CREATE INDEX ix_price_list_items_service_employee ON dunelight.price_list_items USING btree (organization_id, service_id, employee_id);

            CREATE INDEX ix_price_list_items_subject_company ON dunelight.price_list_items USING btree (organization_id, service_id, package_id, company_id);

            ALTER TABLE dunelight.price_list_items ADD CONSTRAINT fk_price_list_items_company_id FOREIGN KEY (company_id) REFERENCES dunelight.companies(id);

            ALTER TABLE dunelight.price_list_items ADD CONSTRAINT fk_price_list_items_employee_id FOREIGN KEY (employee_id) REFERENCES dunelight.employees(id) ON DELETE RESTRICT;

            ALTER TABLE dunelight.price_list_items ADD CONSTRAINT fk_price_list_items_organization_id FOREIGN KEY (organization_id) REFERENCES dunelight.organizations(id);

            ALTER TABLE dunelight.price_list_items ADD CONSTRAINT fk_price_list_items_package_id FOREIGN KEY (package_id) REFERENCES dunelight.packages(id);

            ALTER TABLE dunelight.price_list_items ADD CONSTRAINT fk_price_list_items_service_id FOREIGN KEY (service_id) REFERENCES dunelight.services(id);");

        // price_list_item_history
        Execute.Sql(@"
            CREATE TABLE dunelight.price_list_item_history (
                id uuid NOT NULL,
                price_list_item_id uuid NOT NULL,
                old_price numeric(10,2) NOT NULL,
                new_price numeric(10,2) NOT NULL,
                changed_at timestamp with time zone NOT NULL,
                changed_by uuid,
                old_valid_from timestamp with time zone NOT NULL,
                new_valid_from timestamp with time zone NOT NULL,
                old_valid_to timestamp with time zone,
                new_valid_to timestamp with time zone
            );

            ALTER TABLE dunelight.price_list_item_history ADD CONSTRAINT pk_price_list_item_history PRIMARY KEY (id);

            CREATE INDEX ix_price_list_item_history_price_list_item_id ON dunelight.price_list_item_history USING btree (price_list_item_id);

            ALTER TABLE dunelight.price_list_item_history ADD CONSTRAINT fk_price_list_item_history_price_list_item_id FOREIGN KEY (price_list_item_id) REFERENCES dunelight.price_list_items(id);");
    }
}
