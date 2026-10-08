using System;
using System.Threading.Tasks;
using BlueDragon.DuneLight.Core.DTOs.Appointments;
using BlueDragon.DuneLight.Infrastructure.Domain.Models.Appointments;
using BlueDragon.DuneLight.Infrastructure.UnitOfWork;

namespace BlueDragon.DuneLight.Infrastructure.Services;

/// <summary>
/// Phase M1H — JEDINA jezgra prijelaza sudjelovanja (status, StatusVersion, cijena, paket/novac, provizija po zaposleniku,
/// audit, Outbox, lista čekanja, izvođenje životnog ciklusa termina) izložena za poziv IZ TUĐE transakcije — isti obrazac kao
/// ICommissionLedgerService/IPaymentLedgerService. Koristi je naredba "upiši odrađeno" (AppointmentService.CompleteNow) da
/// odradi sudjelovanja upravo stvorenog termina ISTOM transakcijom, kroz ista pravila kao participation-native naredba.
/// Pozivatelj drži potrebne lockove i persistirane (praćene) retke termina/Bookinga/sudjelovanja.
/// </summary>
public interface IParticipationLifecycleService
{
    Task ApplyTransitionInTransaction(
        IUnitOfWork uow, Guid organizationId, Guid userId, Appointment appointment, Booking booking,
        BookingSegmentParticipation participation, BookingSetStatusRequest request);

    /// <summary>P1 — appointment-wide kaskada (otkazivanje/izostanak cijelog termina): isti prijelaz s ZAJEDNIČKIM serverskim
    /// timestampom događaja; pozivatelj nakon svih sudjelovanja sam izvodi status termina i istječe listu čekanja.</summary>
    Task ApplyCascadeTransitionInTransaction(
        IUnitOfWork uow, Guid organizationId, Guid userId, Appointment appointment, Booking booking,
        BookingSegmentParticipation participation, BookingSetStatusRequest request, DateTimeOffset eventAt);
}
