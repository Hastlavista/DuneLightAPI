using System;
using BlueDragon.DuneLight.DatabaseMigration.Extensions;
using BlueDragon.DuneLight.DatabaseMigration.Models;
using FluentMigrator;

namespace BlueDragon.DuneLight.DatabaseMigration.Migrations.Baseline;

/// <summary>
/// Baseline 009 — Messaging. Kreira KONAČNU strukturu odjednom (nema create->rename/alter povijesti, nema backfilla).
/// Tablice: outbox_messages, notifications.
/// </summary>
[DeveloperMigration(2026, 09, 29, Developer.SilvioHabazin, 9)]
public class Baseline009Messaging : DuneLightMigration
{
    public override void Up()
    {
        Execute.Sql(Ddl);
    }

    private const string Ddl = """
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
    CONSTRAINT ck_outbox_messages_status CHECK (((status)::text = ANY ((ARRAY['Pending'::character varying, 'Processing'::character varying, 'Processed'::character varying, 'Failed'::character varying])::text[]))),
    CONSTRAINT ck_outbox_messages_type_not_empty CHECK ((length(TRIM(BOTH FROM type)) > 0))
);

ALTER TABLE ONLY dunelight.outbox_messages
    ADD CONSTRAINT pk_outbox_messages PRIMARY KEY (id);

CREATE INDEX ix_outbox_messages_status_available ON dunelight.outbox_messages USING btree (status, available_at);

CREATE UNIQUE INDEX ux_outbox_messages_idempotency ON dunelight.outbox_messages USING btree (organization_id, type, idempotency_key) WHERE (idempotency_key IS NOT NULL);

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
    CONSTRAINT ck_notifications_status CHECK (((status)::text = ANY ((ARRAY['Pending'::character varying, 'Cancelled'::character varying])::text[])))
);

ALTER TABLE ONLY dunelight.notifications
    ADD CONSTRAINT pk_notifications PRIMARY KEY (id);

CREATE INDEX ix_notifications_org_client_created ON dunelight.notifications USING btree (organization_id, client_id, created_at);

CREATE UNIQUE INDEX ux_notifications_org_type_source_version ON dunelight.notifications USING btree (organization_id, type, source_type, source_id, source_version);

ALTER TABLE ONLY dunelight.outbox_messages
    ADD CONSTRAINT fk_outbox_messages_organization_id FOREIGN KEY (organization_id) REFERENCES dunelight.organizations(id);

ALTER TABLE ONLY dunelight.notifications
    ADD CONSTRAINT fk_notifications_client_id FOREIGN KEY (client_id) REFERENCES dunelight.clients(id);

ALTER TABLE ONLY dunelight.notifications
    ADD CONSTRAINT fk_notifications_organization_id FOREIGN KEY (organization_id) REFERENCES dunelight.organizations(id);
""";
}
