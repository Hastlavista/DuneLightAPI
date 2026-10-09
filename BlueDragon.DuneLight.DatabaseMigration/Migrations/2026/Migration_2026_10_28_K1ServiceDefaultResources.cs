using BlueDragon.DuneLight.DatabaseMigration.Extensions;
using BlueDragon.DuneLight.DatabaseMigration.Models;

namespace BlueDragon.DuneLight.DatabaseMigration.Migrations._2026;

/// <summary>
/// K1-6 (P-7) — zadani resursi usluge (usluga → resurs poslovnice → količina). Konfiguracijski zapis kao service_companies:
/// kaskadno brisanje sa uslugom i resursom (ne blokira trajno brisanje neiskorištenog resursa/usluge).
/// </summary>
[DeveloperMigration(2026, 10, 28, Developer.SilvioHabazin, 1)]
public class K1ServiceDefaultResources : DuneLightMigration
{
    public override void Up()
    {
        Execute.Sql(@"
            CREATE TABLE dunelight.service_default_resources (
                id uuid NOT NULL,
                service_id uuid NOT NULL,
                resource_id uuid NOT NULL,
                quantity_required integer NOT NULL,
                CONSTRAINT ck_service_default_resources_quantity CHECK (quantity_required > 0)
            );

            ALTER TABLE dunelight.service_default_resources ADD CONSTRAINT pk_service_default_resources PRIMARY KEY (id);

            CREATE UNIQUE INDEX ux_service_default_resources_service_resource ON dunelight.service_default_resources USING btree (service_id, resource_id);

            CREATE INDEX ix_service_default_resources_resource_id ON dunelight.service_default_resources USING btree (resource_id);

            ALTER TABLE dunelight.service_default_resources ADD CONSTRAINT fk_service_default_resources_service_id FOREIGN KEY (service_id) REFERENCES dunelight.services(id) ON DELETE CASCADE;

            ALTER TABLE dunelight.service_default_resources ADD CONSTRAINT fk_service_default_resources_resource_id FOREIGN KEY (resource_id) REFERENCES dunelight.resources(id) ON DELETE CASCADE;");
    }
}
