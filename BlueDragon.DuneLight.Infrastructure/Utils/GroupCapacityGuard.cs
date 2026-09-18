using System;
using System.Threading.Tasks;
using BlueDragon.DuneLight.Core.Enums;
using BlueDragon.DuneLight.Core.Shared;
using BlueDragon.DuneLight.Core.Shared.Exceptions;
using BlueDragon.DuneLight.Infrastructure.Domain.Models.Appointments;
using BlueDragon.DuneLight.Infrastructure.Handlers.Interfaces;
using BlueDragon.DuneLight.Infrastructure.UnitOfWork;

namespace BlueDragon.DuneLight.Infrastructure.Utils;

/// <summary>Jedini izvor istine za "smije li se stvoriti JOŠ JEDAN Confirmed Booking na grupnom terminu" —
/// koriste ga BookingService (nova Confirmed rezervacija/administrativna korekcija natrag na Confirmed) i
/// GroupService.AddMember (sinkronizacija članstva na već generirane buduće termine), tako da ne postoje dvije
/// implementacije "confirmedCount &lt; capacity" s različitim locking semantikama. Zaključava Appointment redak
/// (FOR UPDATE) i broji Confirmed Bookinge POD tim lockom — isti lock i izvor istine
/// (IAppointmentHandler.CountConfirmedBookings) kao WaitlistService.PromoteEligibleWaiters, tako da dva
/// konkurentna zahtjeva za posljednje slobodno mjesto (bilo AddBooking, korekcija, ili AddMember sinkronizacija)
/// ne mogu oba proći — drugi poziv čeka na lock pa svježe broji nakon commita/rollbacka prvog. No-op za termine
/// koji nisu Form=Group (ili grupa nedostaje). Poziva se SAMO za buduće occurrence — pozivatelj je odgovoran
/// za future-only odluku (vidi BookingService.SetStatus), jer nakon početka termina nominalni kapacitet više ne
/// ograničava korekciju povijesne prisutnosti.</summary>
public static class GroupCapacityGuard
{
    public static async Task EnsureAvailable(
        IAppointmentHandler appointmentHandler, IUnitOfWork uow, Guid organizationId, Guid appointmentId)
    {
        Appointment locked = await appointmentHandler.GetForUpdateWithGroup(uow, organizationId, appointmentId);
        if (locked == null || locked.Form != AppointmentForm.Group || locked.Group == null)
            return;

        int confirmedCount = await appointmentHandler.CountConfirmedBookings(uow, organizationId, appointmentId);
        if (confirmedCount >= locked.Group.Capacity)
            throw new BusinessRuleException(
                ErrorCodes.GroupCapacityReached, "Grupa je popunjena — kapacitet je dosegnut.",
                new { appointmentId, capacity = locked.Group.Capacity, confirmedCount });
    }
}
