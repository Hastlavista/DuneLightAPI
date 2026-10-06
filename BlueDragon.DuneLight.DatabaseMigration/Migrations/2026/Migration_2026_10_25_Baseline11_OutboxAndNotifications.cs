using BlueDragon.DuneLight.DatabaseMigration.Extensions;
using BlueDragon.DuneLight.DatabaseMigration.Models;

namespace BlueDragon.DuneLight.DatabaseMigration.Migrations._2026;

/// <summary>
/// Početna migracija (ADR-0022), korak 11/11: transakcijski outbox i notifikacije.
/// Gradi isključivo trenutnu ciljnu shemu; ne seeda podatke.
/// </summary>
[DeveloperMigration(2026, 10, 25, Developer.SilvioHabazin, 11)]
public class Baseline11OutboxAndNotifications : DuneLightMigration
{
    public override void Up()
    {
        // outbox_messages
        Execute.Sql(@"
            CREATE TABLE dunelight.outbox_messages (
                id uuid NOT NULL,
                organization_id uuid,
                type character varying(100) NOT NULL,
                payload jsonb NOT NULL,
                idempotency_key character varying(200),
                status character varying(20) NOT NULL,
                occurred_at timestamp with time zone NOT NULL,
                available_at timestamp with time zone NOT NULL,
                attempt_count integer DEFAULT 0 NOT NULL,
                last_attempt_at timestamp with time zone,
                processed_at timestamp with time zone,
                locked_at timestamp with time zone,
                locked_until timestamp with time zone,
                locked_by uuid,
                last_error character varying(2000),
                created_at timestamp with time zone NOT NULL,
                CONSTRAINT ck_outbox_messages_attempt_count CHECK ((attempt_count >= 0)),
                CONSTRAINT ck_outbox_messages_status CHECK ((status IN ('Pending', 'Processing', 'Processed', 'Failed'))),
                CONSTRAINT ck_outbox_messages_type_not_empty CHECK ((length(TRIM(BOTH FROM type)) > 0))
            );

            ALTER TABLE dunelight.outbox_messages ADD CONSTRAINT pk_outbox_messages PRIMARY KEY (id);

            CREATE INDEX ix_outbox_messages_status_available ON dunelight.outbox_messages USING btree (status, available_at);

            CREATE UNIQUE INDEX ux_outbox_messages_idempotency ON dunelight.outbox_messages USING btree (organization_id, type, idempotency_key) WHERE (idempotency_key IS NOT NULL);

            ALTER TABLE dunelight.outbox_messages ADD CONSTRAINT fk_outbox_messages_organization_id FOREIGN KEY (organization_id) REFERENCES dunelight.organizations(id);");

        // notifications
        Execute.Sql(@"
            CREATE TABLE dunelight.notifications (
                id uuid NOT NULL,
                organization_id uuid NOT NULL,
                client_id uuid,
                type character varying(50) NOT NULL,
                source_type character varying(50) NOT NULL,
                source_id uuid NOT NULL,
                status character varying(20) NOT NULL,
                data jsonb NOT NULL,
                occurred_at timestamp with time zone NOT NULL,
                created_at timestamp with time zone NOT NULL,
                source_version integer DEFAULT 0 NOT NULL,
                CONSTRAINT ck_notifications_status CHECK ((status IN ('Pending', 'Cancelled')))
            );

            ALTER TABLE dunelight.notifications ADD CONSTRAINT pk_notifications PRIMARY KEY (id);

            CREATE INDEX ix_notifications_org_client_created ON dunelight.notifications USING btree (organization_id, client_id, created_at);

            CREATE UNIQUE INDEX ux_notifications_org_type_source_version ON dunelight.notifications USING btree (organization_id, type, source_type, source_id, source_version);

            ALTER TABLE dunelight.notifications ADD CONSTRAINT fk_notifications_client_id FOREIGN KEY (client_id) REFERENCES dunelight.clients(id);

            ALTER TABLE dunelight.notifications ADD CONSTRAINT fk_notifications_organization_id FOREIGN KEY (organization_id) REFERENCES dunelight.organizations(id);");
    }
}
