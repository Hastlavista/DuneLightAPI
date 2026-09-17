using BlueDragon.DuneLight.DatabaseMigration.Extensions;
using BlueDragon.DuneLight.DatabaseMigration.Models;

namespace BlueDragon.DuneLight.DatabaseMigration.Migrations._2026;

/// <summary>
/// Transakcijski Outbox temelj (vidi OutboxMessage.cs za punu domensku napomenu) — generička infrastrukturna
/// tablica, ne dira nijedno postojeće poslovno ponašanje. IdempotencyKey je opcionalan i, kad je postavljen,
/// jedinstven po (organization_id, type) preko djelomičnog unique indeksa (isti obrazac kao ostali "djelomični
/// unique preko EF-neizraziva uvjeta" slučajevi u ovoj bazi — vidi ux_services_org_name_active i srodni).
/// </summary>
[DeveloperMigration(2026, 09, 22, Developer.SilvioHabazin, 0)]
public class CreateOutboxMessagesTable : DuneLightMigration
{
    public override void Up()
    {
        Create.Table(Tables.OutboxMessages)
            .InSchema(Tables.Schemas.DuneLight)
            .WithColumn("id").AsGuid().NotNullable().PrimaryKey("pk_outbox_messages")
            .WithColumn("organization_id").AsGuid().Nullable()
            .WithColumn("type").AsString(100).NotNullable()
            .WithColumn("payload").AsCustom("jsonb").NotNullable()
            .WithColumn("idempotency_key").AsString(200).Nullable()
            .WithColumn("status").AsString(20).NotNullable()
            .WithColumn("occurred_at").AsDateTimeOffset().NotNullable()
            .WithColumn("available_at").AsDateTimeOffset().NotNullable()
            .WithColumn("attempt_count").AsInt32().NotNullable().WithDefaultValue(0)
            .WithColumn("last_attempt_at").AsDateTimeOffset().Nullable()
            .WithColumn("processed_at").AsDateTimeOffset().Nullable()
            .WithColumn("locked_at").AsDateTimeOffset().Nullable()
            .WithColumn("locked_until").AsDateTimeOffset().Nullable()
            .WithColumn("locked_by").AsGuid().Nullable()
            .WithColumn("last_error").AsString(2000).Nullable()
            .WithColumn("created_at").AsDateTimeOffset().NotNullable();

        Create.ForeignKey("fk_outbox_messages_organization_id")
            .FromTable(Tables.OutboxMessages).InSchema(Tables.Schemas.DuneLight).ForeignColumn("organization_id")
            .ToTable(Tables.Organizations).InSchema(Tables.Schemas.DuneLight).PrimaryColumn("id");

        // Poll upit filtrira po (status, available_at) — OutboxProcessorService.ClaimBatch (vidi spec section 50).
        Create.Index("ix_outbox_messages_status_available")
            .OnTable(Tables.OutboxMessages).InSchema(Tables.Schemas.DuneLight)
            .OnColumn("status").Ascending()
            .OnColumn("available_at").Ascending();

        Execute.Sql(@"
            ALTER TABLE dunelight.outbox_messages
            ADD CONSTRAINT ck_outbox_messages_status CHECK (status IN ('Pending', 'Processing', 'Processed', 'Failed'));");

        Execute.Sql("ALTER TABLE dunelight.outbox_messages ADD CONSTRAINT ck_outbox_messages_attempt_count CHECK (attempt_count >= 0);");
        Execute.Sql("ALTER TABLE dunelight.outbox_messages ADD CONSTRAINT ck_outbox_messages_type_not_empty CHECK (length(trim(type)) > 0);");

        Execute.Sql(@"
            CREATE UNIQUE INDEX ux_outbox_messages_idempotency
            ON dunelight.outbox_messages (organization_id, type, idempotency_key)
            WHERE idempotency_key IS NOT NULL;");
    }
}

/// <summary>
/// Kanal-neovisna Notification namjera (vidi Notification.cs) — idempotencija je DB-garantirana (ne
/// "if not exists") preko standardnog (ne-djelomičnog) unique indeksa na (organization_id, type, source_type,
/// source_id): source_id je uvijek popunjen za sva tri trenutno ožičena eventa, pa nema NULL-per-se-distinct
/// problema kao kod ostalih "vlasnik je točno jedno od X/Y" slučajeva u ovoj bazi.
/// </summary>
[DeveloperMigration(2026, 09, 22, Developer.SilvioHabazin, 1)]
public class CreateNotificationsTable : DuneLightMigration
{
    public override void Up()
    {
        Create.Table(Tables.Notifications)
            .InSchema(Tables.Schemas.DuneLight)
            .WithColumn("id").AsGuid().NotNullable().PrimaryKey("pk_notifications")
            .WithColumn("organization_id").AsGuid().NotNullable()
            .WithColumn("client_id").AsGuid().Nullable()
            .WithColumn("type").AsString(50).NotNullable()
            .WithColumn("source_type").AsString(50).NotNullable()
            .WithColumn("source_id").AsGuid().NotNullable()
            .WithColumn("status").AsString(20).NotNullable()
            .WithColumn("data").AsCustom("jsonb").NotNullable()
            .WithColumn("occurred_at").AsDateTimeOffset().NotNullable()
            .WithColumn("created_at").AsDateTimeOffset().NotNullable();

        Create.ForeignKey("fk_notifications_organization_id")
            .FromTable(Tables.Notifications).InSchema(Tables.Schemas.DuneLight).ForeignColumn("organization_id")
            .ToTable(Tables.Organizations).InSchema(Tables.Schemas.DuneLight).PrimaryColumn("id");

        Create.ForeignKey("fk_notifications_client_id")
            .FromTable(Tables.Notifications).InSchema(Tables.Schemas.DuneLight).ForeignColumn("client_id")
            .ToTable(Tables.Clients).InSchema(Tables.Schemas.DuneLight).PrimaryColumn("id");

        Create.Index("ix_notifications_org_client_created")
            .OnTable(Tables.Notifications).InSchema(Tables.Schemas.DuneLight)
            .OnColumn("organization_id").Ascending()
            .OnColumn("client_id").Ascending()
            .OnColumn("created_at").Ascending();

        Execute.Sql(@"
            CREATE UNIQUE INDEX ux_notifications_org_type_source
            ON dunelight.notifications (organization_id, type, source_type, source_id);");

        Execute.Sql("ALTER TABLE dunelight.notifications ADD CONSTRAINT ck_notifications_status CHECK (status IN ('Pending', 'Cancelled'));");
    }
}
