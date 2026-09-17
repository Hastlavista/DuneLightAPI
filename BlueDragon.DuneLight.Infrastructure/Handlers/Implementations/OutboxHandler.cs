using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using BlueDragon.DuneLight.Infrastructure.Domain.Contexts;
using BlueDragon.DuneLight.Infrastructure.Domain.Models.Outbox;
using BlueDragon.DuneLight.Infrastructure.Domain.Settings;
using BlueDragon.DuneLight.Infrastructure.Handlers.Interfaces;
using BlueDragon.DuneLight.Infrastructure.Outbox;
using BlueDragon.DuneLight.Infrastructure.UnitOfWork;
using Microsoft.EntityFrameworkCore;

namespace BlueDragon.DuneLight.Infrastructure.Handlers.Implementations;

public class OutboxHandler : IOutboxHandler
{
    private readonly DatabaseSettings _databaseSettings;

    public OutboxHandler(DatabaseSettings databaseSettings)
    {
        _databaseSettings = databaseSettings;
    }

    /// <summary>FOR UPDATE SKIP LOCKED preko FromSqlInterpolated (isti obrazac kao AppointmentHandler.GetForUpdate*
    /// / ProductStockHandler) — dva konkurentna workera koja pollaju istovremeno svaki zaključavaju/preskaču
    /// TUĐE claimane redke umjesto čekanja, pa se ne blokiraju međusobno (vidi spec section 16/65). Pending ILI
    /// Processing-s-isteklim-leaseom su podobni (crash recovery, vidi spec section 17). Svježi ClaimToken (Guid)
    /// se generira PO POZIVU i perzistira kao locked_by za CIJELU dodijeljenu seriju — fencing token, ne
    /// proces-trajni worker identitet (vidi IOutboxHandler.ClaimBatch/spec section 19-20): isti proces koji
    /// kasnije ponovno preuzme ISTI redak (nakon isteka leasea dok mu je raniji pokušaj još "u letu") dobiva
    /// RAZLIČIT token, tako da terminalne mutacije starog pokušaja ne mogu prepisati novo vlasništvo.</summary>
    public async Task<List<ClaimedOutboxMessage>> ClaimBatch(IUnitOfWork uow, int batchSize, TimeSpan leaseDuration, CancellationToken cancellationToken)
    {
        DateTimeOffset now = DateTimeOffset.UtcNow;

        List<OutboxMessage> claimed = await uow.Context.OutboxMessages
            .FromSqlInterpolated($@"
                SELECT * FROM dunelight.outbox_messages
                WHERE (status = 'Pending' OR (status = 'Processing' AND locked_until < {now}))
                  AND available_at <= {now}
                ORDER BY available_at
                LIMIT {batchSize}
                FOR UPDATE SKIP LOCKED")
            .ToListAsync(cancellationToken);

        if (claimed.Count == 0)
            return new List<ClaimedOutboxMessage>();

        Guid claimToken = Guid.NewGuid();
        List<Guid> ids = claimed.Select(m => m.Id.GetValueOrDefault()).ToList();
        DateTimeOffset lockedUntil = now + leaseDuration;

        await uow.Context.Database.ExecuteSqlInterpolatedAsync($@"
            UPDATE dunelight.outbox_messages
            SET status = 'Processing', locked_at = {now}, locked_until = {lockedUntil}, locked_by = {claimToken},
                attempt_count = attempt_count + 1, last_attempt_at = {now}
            WHERE id = ANY({ids})", cancellationToken);

        return claimed
            .Select(m => new ClaimedOutboxMessage
            {
                Id = m.Id.GetValueOrDefault(),
                OrganizationId = m.OrganizationId,
                Type = m.Type,
                Payload = m.Payload,
                AttemptCount = m.AttemptCount + 1,
                ClaimToken = claimToken
            })
            .ToList();
    }

    public async Task<int> MarkProcessed(IUnitOfWork uow, Guid id, Guid claimToken, CancellationToken cancellationToken)
    {
        return await uow.Context.Database.ExecuteSqlInterpolatedAsync($@"
            UPDATE dunelight.outbox_messages
            SET status = 'Processed', processed_at = {DateTimeOffset.UtcNow}, locked_at = NULL, locked_until = NULL, locked_by = NULL
            WHERE id = {id} AND status = 'Processing' AND locked_by = {claimToken}", cancellationToken);
    }

    public async Task<int> MarkForRetry(Guid id, Guid claimToken, DateTimeOffset availableAt, string error, CancellationToken cancellationToken)
    {
        await using DatabaseContext context = DatabaseContext.GenerateContext(_databaseSettings.ConnectionString);
        return await context.Database.ExecuteSqlInterpolatedAsync($@"
            UPDATE dunelight.outbox_messages
            SET status = 'Pending', available_at = {availableAt}, last_error = {Truncate(error)}, locked_at = NULL, locked_until = NULL, locked_by = NULL
            WHERE id = {id} AND status = 'Processing' AND locked_by = {claimToken}", cancellationToken);
    }

    public async Task<int> MarkFailed(Guid id, Guid claimToken, string error, CancellationToken cancellationToken)
    {
        await using DatabaseContext context = DatabaseContext.GenerateContext(_databaseSettings.ConnectionString);
        return await context.Database.ExecuteSqlInterpolatedAsync($@"
            UPDATE dunelight.outbox_messages
            SET status = 'Failed', last_error = {Truncate(error)}, locked_at = NULL, locked_until = NULL, locked_by = NULL
            WHERE id = {id} AND status = 'Processing' AND locked_by = {claimToken}", cancellationToken);
    }

    private static string Truncate(string error)
    {
        if (string.IsNullOrEmpty(error))
            return error;

        const int maxLength = 2000;
        return error.Length <= maxLength ? error : error.Substring(0, maxLength);
    }
}
