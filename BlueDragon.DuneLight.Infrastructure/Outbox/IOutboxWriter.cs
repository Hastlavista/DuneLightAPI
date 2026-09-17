using System;
using System.Threading.Tasks;
using BlueDragon.DuneLight.Infrastructure.UnitOfWork;

namespace BlueDragon.DuneLight.Infrastructure.Outbox;

/// <summary>
/// Fokusirana abstrakcija koju poslovni servisi (BookingService/WaitlistService/AppointmentService/GroupService)
/// pozivaju da dodaju OutboxMessage u TRENUTNU transakciju (vidi spec section 9/10) — NIKAD ne otvara vlastiti
/// DbContext/transakciju, ne commita, ne dispatch-a odmah, ne zove vanjski servis. Poziva se UVIJEK s istim
/// IUnitOfWork kojim pozivatelj već mutira domensko stanje, tako da rollback poslovne transakcije obriše i ovaj
/// redak.
/// </summary>
public interface IOutboxWriter
{
    /// <summary>Serijalizira <paramref name="event"/> preko OutboxJsonOptions i dodaje Pending OutboxMessage.
    /// Kad je <paramref name="idempotencyKey"/> zadan, insert je "ON CONFLICT DO NOTHING" prema
    /// (OrganizationId, Type, IdempotencyKey) — ponovljen poziv s istim ključem je tih no-op, ne baca iznimku
    /// (vidi spec section 11).</summary>
    Task Add<TEvent>(
        IUnitOfWork uow, Guid organizationId, string type, TEvent @event, DateTimeOffset occurredAt, string idempotencyKey = null);
}
