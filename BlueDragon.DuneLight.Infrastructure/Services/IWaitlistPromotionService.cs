using System;
using System.Threading.Tasks;
using BlueDragon.DuneLight.Infrastructure.UnitOfWork;

namespace BlueDragon.DuneLight.Infrastructure.Services;

/// <summary>
/// Infrastructure-only strana liste čekanja — metode s <see cref="IUnitOfWork"/> parametrom su namijenjene
/// pozivu IZ TUĐE transakcije (BookingService/GroupService/AppointmentService), kao dio iste atomične
/// cjeline kao operacija koja ih pokreće. Odvojeno od Core IWaitlistService jer Core ne smije referencirati
/// Infrastructure.UnitOfWork (isti razlog zašto Handler sučelja žive u Infrastructure, ne Core) — jedan
/// WaitlistService implementira oba sučelja.
/// </summary>
public interface IWaitlistPromotionService
{
    /// <summary>Kad se jedno ili više mjesta oslobodi na budućem Scheduled grupnom terminu (Booking otkazan),
    /// promovira onoliko najstarijih eligible Waiting redaka koliko ima slobodnog kapaciteta — revalidira
    /// eligibility za svakog kandidata, permanentno neeligible kandidate prebacuje u Expired i nastavlja dalje
    /// (ne blokira red). No-op ako termin nije Form=Group, nije Scheduled, ili je StartsAt već prošao.</summary>
    Task PromoteEligibleWaiters(IUnitOfWork uow, Guid organizationId, Guid appointmentId, Guid userId);

    /// <summary>Prebacuje sve preostale Waiting retke termina u Expired (bez promocije) — koristi se kod
    /// otkazivanja/zatvaranja cijelog Appointmenta (vidi WaitlistExpiredReasons).</summary>
    Task ExpireWaitingForAppointment(IUnitOfWork uow, Guid organizationId, Guid appointmentId, Guid? userId, string reason);
}
