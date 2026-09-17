using System;
using System.Threading;
using System.Threading.Tasks;
using BlueDragon.DuneLight.Infrastructure.UnitOfWork;

namespace BlueDragon.DuneLight.Infrastructure.Outbox;

/// <summary>
/// Poslovni handler za JEDAN Outbox event-tip (vidi spec section 22) — Type mora odgovarati jednoj od
/// OutboxEventTypes konstanti. OutboxProcessorService gradi Type -&gt; handler mapu preko DI-registriranih
/// implementacija (services.AddSingleton&lt;IOutboxMessageHandler, XxxHandler&gt;), bez reflection/assembly-scan
/// magije (vidi spec section 22). Handler odlučuje ŠTO se asinkrono radi za taj event (npr. stvoriti
/// Notification) — Outbox infrastruktura (OutboxProcessorService/IOutboxHandler) ne poznaje domensko značenje
/// payloada, samo ga dispatch-a (vidi spec section 2).
///
/// Handle se poziva unutar IUnitOfWork koji OutboxProcessorService commita ZAJEDNO s markiranjem poruke kao
/// Processed (vidi spec section 45) — handler NE smije sam commitati/otvarati drugu transakciju niti zvati
/// vanjski servis. Mora biti idempotentan (at-least-once isporuka, vidi spec section 24) preko DB uniqueness na
/// entitetu koji stvara, ne oslanjajući se na OutboxMessage.Status.
/// </summary>
public interface IOutboxMessageHandler
{
    string Type { get; }

    /// <summary>cancellationToken je stoppingToken hosta (vidi OutboxProcessorService/spec section 33-35) — samo
    /// za observiranje shutdowna u dugotrajnijim DB pozivima, NIKAD razlog za tretirati poruku kao poslovni
    /// neuspjeh (OperationCanceledException iz ovoga se mora razlikovati od stvarne handler greške, vidi
    /// OutboxProcessorService.ProcessOne).</summary>
    Task Handle(IUnitOfWork uow, Guid? organizationId, string payload, CancellationToken cancellationToken);
}
