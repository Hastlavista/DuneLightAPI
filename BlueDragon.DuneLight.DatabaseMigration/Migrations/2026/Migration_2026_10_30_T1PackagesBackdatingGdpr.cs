using BlueDragon.DuneLight.DatabaseMigration.Extensions;
using BlueDragon.DuneLight.DatabaseMigration.Models;

namespace BlueDragon.DuneLight.DatabaseMigration.Migrations._2026;

/// <summary>
/// T1-9 — paketi unatrag i GDPR suglasnost:
/// (1) povijest klijenta client_audit_logs (promjene GDPR suglasnosti, ručni upis paketa unatrag): tko, kada, staro/novo,
///     razlog. Trajno brisanje klijenta briše i njegovu povijest (ON DELETE CASCADE), da postojeće trajno brisanje ostane moguće;
/// (2) novi grant clients.packages.write.past dodaje se SAMO Admin grupama (grant_groups.system_key = 'admin', ADR-0023).
///     Idempotentno (ON CONFLICT DO NOTHING). Ništa se ne seeda (ADR-0022).
/// </summary>
[DeveloperMigration(2026, 10, 30, Developer.SilvioHabazin, 2)]
public class T1PackagesBackdatingGdpr : DuneLightMigration
{
    public override void Up()
    {
        Execute.Sql(@"
            CREATE TABLE dunelight.client_audit_logs (
                id uuid NOT NULL,
                organization_id uuid NOT NULL,
                client_id uuid NOT NULL,
                change_type character varying(60) NOT NULL,
                old_value text,
                new_value text,
                reason character varying(500),
                changed_at timestamp with time zone NOT NULL,
                changed_by uuid
            );

            ALTER TABLE dunelight.client_audit_logs ADD CONSTRAINT pk_client_audit_logs PRIMARY KEY (id);

            CREATE INDEX ix_client_audit_logs_client ON dunelight.client_audit_logs USING btree (client_id);

            ALTER TABLE dunelight.client_audit_logs ADD CONSTRAINT fk_client_audit_logs_client_id FOREIGN KEY (client_id) REFERENCES dunelight.clients(id) ON DELETE CASCADE;

            ALTER TABLE dunelight.client_audit_logs ADD CONSTRAINT fk_client_audit_logs_organization_id FOREIGN KEY (organization_id) REFERENCES dunelight.organizations(id) ON DELETE RESTRICT;");

        Execute.Sql(@"
            INSERT INTO dunelight.grant_group_grants (id, grant_group_id, grant_key)
            SELECT gen_random_uuid(), g.id, 'clients.packages.write.past'
            FROM dunelight.grant_groups g
            WHERE g.system_key = 'admin'
            ON CONFLICT (grant_group_id, grant_key) DO NOTHING;");
    }
}
