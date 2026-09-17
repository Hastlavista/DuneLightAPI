using System;

namespace BlueDragon.DuneLight.Infrastructure.Outbox;

/// <summary>Odvojen (ne-EF-tracked) snapshot claimanog retka — IOutboxHandler.ClaimBatch radi u kratkotrajnom
/// uow koji se commita i disposea odmah nakon claima (vidi spec section 16), pa OutboxProcessorService ne smije
/// dalje koristiti EF entitet iz te (već zatvorene) transakcije za obradu poruke u NOVOM uow.</summary>
public class ClaimedOutboxMessage
{
    public Guid Id { get; init; }
    public Guid? OrganizationId { get; init; }
    public string Type { get; init; }
    public string Payload { get; init; }
    public int AttemptCount { get; init; }

    /// <summary>Fencing token dodijeljen OVOM claimu (perzistiran kao locked_by, vidi OutboxMessage.cs) — svaka
    /// terminalna mutacija (MarkProcessed/MarkForRetry/MarkFailed) mora ga proslijediti i uvjetovati na njega,
    /// tako da stari attempt čiji je lease istekao i redak ponovno preuzet NIKAD ne prepiše novog vlasnika (vidi
    /// spec section 19-31).</summary>
    public Guid ClaimToken { get; init; }
}
