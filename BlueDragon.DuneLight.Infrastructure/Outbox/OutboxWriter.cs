using System;
using System.Text.Json;
using System.Threading.Tasks;
using BlueDragon.DuneLight.Infrastructure.UnitOfWork;
using Microsoft.EntityFrameworkCore;

namespace BlueDragon.DuneLight.Infrastructure.Outbox;

public class OutboxWriter : IOutboxWriter
{
    public async Task Add<TEvent>(
        IUnitOfWork uow, Guid organizationId, string type, TEvent @event, DateTimeOffset occurredAt, string idempotencyKey = null)
    {
        string payload = JsonSerializer.Serialize(@event, OutboxJsonOptions.Instance);
        Guid id = Guid.NewGuid();
        DateTimeOffset now = DateTimeOffset.UtcNow;

        // ON CONFLICT DO NOTHING cilja ux_outbox_messages_idempotency (djelomični unique indeks, vidi migraciju)
        // — isti obrazac kao ProductStockHandler.GetOrCreateForUpdate. Bez IdempotencyKey nema konflikt-cilja pa
        // insert ide bezuvjetno.
        if (idempotencyKey != null)
        {
            await uow.Context.Database.ExecuteSqlInterpolatedAsync($@"
                INSERT INTO dunelight.outbox_messages
                    (id, organization_id, type, payload, idempotency_key, status, occurred_at, available_at, attempt_count, created_at)
                VALUES
                    ({id}, {organizationId}, {type}, CAST({payload} AS jsonb), {idempotencyKey}, 'Pending', {occurredAt}, {now}, 0, {now})
                ON CONFLICT (organization_id, type, idempotency_key) WHERE idempotency_key IS NOT NULL DO NOTHING");
        }
        else
        {
            await uow.Context.Database.ExecuteSqlInterpolatedAsync($@"
                INSERT INTO dunelight.outbox_messages
                    (id, organization_id, type, payload, idempotency_key, status, occurred_at, available_at, attempt_count, created_at)
                VALUES
                    ({id}, {organizationId}, {type}, CAST({payload} AS jsonb), NULL, 'Pending', {occurredAt}, {now}, 0, {now})");
        }
    }
}
