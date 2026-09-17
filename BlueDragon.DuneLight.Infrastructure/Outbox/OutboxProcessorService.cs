using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using BlueDragon.DuneLight.Infrastructure.Domain.Settings;
using BlueDragon.DuneLight.Infrastructure.Handlers.Interfaces;
using BlueDragon.DuneLight.Infrastructure.UnitOfWork;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace BlueDragon.DuneLight.Infrastructure.Outbox;

/// <summary>
/// Pozadinski Outbox worker (vidi spec section 13-21/45-49). Svaki tick: (1) kratka claim transakcija koja
/// leasa ograđenu seriju redaka (IOutboxHandler.ClaimBatch), (2) po redak, VLASTITA transakcija koja izvršava
/// handlerovu DB mutaciju i markira Processed ZAJEDNO (jedan commit) jer je sav rad ovog MVP-a lokalan DB-only
/// (vidi spec section 45) — kad se doda vanjski provider, TA obrada mora izaći iz uow-a (arhitektura već drži
/// claim i process odvojenima upravo zbog ovoga, vidi spec section 16).
///
/// Jedan worker loop po procesu je dovoljan za MVP (vidi spec section 49) — DB lease/claim dizajn već je
/// siguran za više instanci procesa (vidi spec section 65).
/// </summary>
public class OutboxProcessorService : BackgroundService
{
    private readonly IUnitOfWorkFactory _unitOfWorkFactory;
    private readonly IOutboxHandler _outboxHandler;
    private readonly IReadOnlyDictionary<string, IOutboxMessageHandler> _handlers;
    private readonly OutboxSettings _settings;
    private readonly ILogger<OutboxProcessorService> _logger;

    public OutboxProcessorService(
        IUnitOfWorkFactory unitOfWorkFactory,
        IOutboxHandler outboxHandler,
        IEnumerable<IOutboxMessageHandler> handlers,
        OutboxSettings settings,
        ILogger<OutboxProcessorService> logger)
    {
        _unitOfWorkFactory = unitOfWorkFactory;
        _outboxHandler = outboxHandler;
        // Eksplicitna DI registracija po handleru (vidi spec section 22) — dupliciran Type je konfiguracijska
        // greška, fail-fast na startu umjesto tihog "prvi registrirani pobjeđuje".
        _handlers = handlers.ToDictionary(h => h.Type);
        _settings = settings;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        TimeSpan pollInterval = TimeSpan.FromSeconds(_settings.PollIntervalSeconds);

        while (!stoppingToken.IsCancellationRequested)
        {
            bool processedAny;
            try
            {
                processedAny = await ProcessBatch(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                // Jedna loša poruka ne smije trajno srušiti worker (vidi spec section 48) — claim-razine
                // iznimka (npr. privremeni DB prekid) samo produžuje sljedeći pokušaj.
                _logger.LogError(ex, "Outbox processor batch failed unexpectedly");
                processedAny = false;
            }

            if (processedAny)
                continue;

            try
            {
                await Task.Delay(pollInterval, stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
        }
    }

    private async Task<bool> ProcessBatch(CancellationToken stoppingToken)
    {
        List<ClaimedOutboxMessage> claimed;
        await using (IUnitOfWork uow = await _unitOfWorkFactory.Begin())
        {
            claimed = await _outboxHandler.ClaimBatch(uow, _settings.BatchSize, TimeSpan.FromSeconds(_settings.LeaseSeconds), stoppingToken);
            await uow.CommitAsync();
        }

        if (claimed.Count == 0)
            return false;

        foreach (ClaimedOutboxMessage message in claimed)
        {
            if (stoppingToken.IsCancellationRequested)
                break;

            await ProcessOne(message, stoppingToken);
        }

        return true;
    }

    private async Task ProcessOne(ClaimedOutboxMessage message, CancellationToken stoppingToken)
    {
        try
        {
            // Nepoznat event-tip (npr. deploy koji je uklonio handler dok stari redci još čekaju) NIKAD se ne
            // markira Processed — tretira se kao obrada koja nije uspjela (vidi spec section 23).
            if (!_handlers.TryGetValue(message.Type, out IOutboxMessageHandler handler))
                throw new InvalidOperationException($"Nepoznat outbox event tip '{message.Type}'.");

            await using IUnitOfWork uow = await _unitOfWorkFactory.Begin();
            await handler.Handle(uow, message.OrganizationId, message.Payload, stoppingToken);

            // Uvjetovano markiranje (WHERE status='Processing' AND locked_by=ClaimToken) KAO POSLJEDNJA mutacija
            // PRIJE commita (vidi spec section 27-29) — 0 pogođenih redaka znači da je lease istekao i redak
            // preuzeo NOVIJI claim dok je OVAJ pokušaj još radio. U tom slučaju se uow NIKAD ne commita: dispose
            // ispod (await using) radi rollback, čime i handlerova Notification mutacija ostaje nepostojana —
            // novi vlasnik je autoritativan, stari pokušaj ne ostavlja trag.
            int affected = await _outboxHandler.MarkProcessed(uow, message.Id, message.ClaimToken, stoppingToken);
            if (affected == 0)
            {
                _logger.LogWarning(
                    "Outbox message {OutboxMessageId} ({Type}) lease ownership lost before MarkProcessed — discarding this attempt's effects, rolling back",
                    message.Id, message.Type);
                return;
            }

            await uow.CommitAsync();
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            // Host se gasi — NIJE poslovni neuspjeh (vidi spec section 33-36). Nikad MarkForRetry/MarkFailed samo
            // zbog shutdowna: lease jednostavno istekne i redak postaje ponovno preuzimljiv. uow gore (ako je
            // otvoren) rollbacka se kroz await using kao i svaka druga neuspjela putanja.
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(
                ex, "Outbox message {OutboxMessageId} ({Type}) attempt {AttemptCount} failed",
                message.Id, message.Type, message.AttemptCount);

            await HandleFailure(message, ex, stoppingToken);
        }
    }

    /// <summary>Poslovno stanje (Booking i dalje Cancelled, itd.) OSTAJE — samo se OVA outbox poruka
    /// retry-a/failuje (vidi spec section 40/63). Uvjetovano na ClaimToken (vidi IOutboxHandler) — ako je
    /// ownership u međuvremenu izgubljen (0 pogođenih redaka), samo se logira, ne poduzima dodatna mutacija
    /// (noviji vlasnik je autoritativan, vidi spec section 30-31).</summary>
    private async Task HandleFailure(ClaimedOutboxMessage message, Exception ex, CancellationToken cancellationToken)
    {
        string error = ex.Message;

        int affected;
        if (message.AttemptCount >= _settings.MaxAttempts)
            affected = await _outboxHandler.MarkFailed(message.Id, message.ClaimToken, error, cancellationToken);
        else
        {
            int[] schedule = _settings.RetryBackoffSeconds;
            int backoffSeconds = schedule.Length == 0
                ? 60
                : schedule[Math.Min(message.AttemptCount - 1, schedule.Length - 1)];

            DateTimeOffset availableAt = DateTimeOffset.UtcNow.AddSeconds(backoffSeconds);
            affected = await _outboxHandler.MarkForRetry(message.Id, message.ClaimToken, availableAt, error, cancellationToken);
        }

        if (affected == 0)
        {
            _logger.LogWarning(
                "Outbox message {OutboxMessageId} ({Type}) lease ownership lost before failure/retry mark — discarding, newer owner is authoritative",
                message.Id, message.Type);
        }
    }
}
