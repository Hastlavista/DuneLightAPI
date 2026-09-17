using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using BlueDragon.DuneLight.Infrastructure.Outbox;
using BlueDragon.DuneLight.Infrastructure.UnitOfWork;

namespace BlueDragon.DuneLight.Infrastructure.Handlers.Interfaces;

/// <summary>Podatkovni pristup za OutboxProcessorService — čisto infrastrukturno (claim/lease/status), bez
/// ikakvog domenskog znanja o Type/Payload sadržaju (vidi IOutboxMessageHandler za poslovnu stranu).</summary>
public interface IOutboxHandler
{
    /// <summary>Zaključava (FOR UPDATE SKIP LOCKED) i markira Processing ograđenu seriju Pending/isteklo-leasanih
    /// redaka unutar <paramref name="uow"/> (kratkotrajna claim transakcija, vidi spec section 16-17). Svaki
    /// poziv dodjeljuje SVJEŽ fencing token (ClaimedOutboxMessage.ClaimToken, perzistiran kao locked_by) cijeloj
    /// dodijeljenoj seriji — NIKAD proces-trajni worker identitet, jer isti proces može kasnije ponovno preuzeti
    /// (nakon isteka leasea) redak koji jedan njegov RANIJI (još "u letu") pokušaj još obrađuje (vidi spec
    /// section 19-20).</summary>
    Task<List<ClaimedOutboxMessage>> ClaimBatch(IUnitOfWork uow, int batchSize, TimeSpan leaseDuration, CancellationToken cancellationToken);

    /// <summary>Uvjetovano markira Processed (WHERE status='Processing' AND locked_by=claimToken) unutar ISTOG uow
    /// kao handlerova DB mutacija (vidi spec section 45), TEK prije uow.CommitAsync(). Vraća broj pogođenih
    /// redaka — 0 znači da je lease u međuvremenu istekao i redak preuzeo NOVIJI claim; pozivatelj MORA odustati
    /// od uow.CommitAsync() u tom slučaju (rollback briše i handlerovu mutaciju, vidi spec section 27-29).</summary>
    Task<int> MarkProcessed(IUnitOfWork uow, Guid id, Guid claimToken, CancellationToken cancellationToken);

    /// <summary>Uvjetovano (isti WHERE obrazac kao MarkProcessed) vraća redak u Pending s pomaknutim AvailableAt
    /// (backoff) — vlastita kratka transakcija, poziva se nakon što je handlerov uow već rollbackan (vidi spec
    /// section 20). Vraća broj pogođenih redaka; 0 znači stalu ownership — pozivatelj samo logira, ne poduzima
    /// dodatnu mutaciju (vidi spec section 30-31).</summary>
    Task<int> MarkForRetry(Guid id, Guid claimToken, DateTimeOffset availableAt, string error, CancellationToken cancellationToken);

    /// <summary>Terminalno Failed nakon iscrpljenih pokušaja — vlastita kratka transakcija, isti uvjetovani
    /// obrazac (vidi spec section 21/30-31).</summary>
    Task<int> MarkFailed(Guid id, Guid claimToken, string error, CancellationToken cancellationToken);
}
