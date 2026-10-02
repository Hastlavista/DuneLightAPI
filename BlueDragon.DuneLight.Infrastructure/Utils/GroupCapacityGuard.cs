using System;
using System.Linq;
using System.Threading.Tasks;
using BlueDragon.DuneLight.Core.Enums;
using BlueDragon.DuneLight.Core.Interfaces;
using BlueDragon.DuneLight.Core.Shared;
using BlueDragon.DuneLight.Core.Shared.Exceptions;
using BlueDragon.DuneLight.Infrastructure.Domain.Models.Appointments;
using BlueDragon.DuneLight.Infrastructure.Handlers.Interfaces;
using BlueDragon.DuneLight.Infrastructure.UnitOfWork;
using Microsoft.EntityFrameworkCore;

namespace BlueDragon.DuneLight.Infrastructure.Utils;

/// <summary>
/// Phase M1F — JEDINI izvor istine za MEKI (poslovni) kapacitet segmenta grupnog occurrencea: "smije li se na OVOM
/// segmentu stvoriti još jedno Confirmed sudjelovanje". Kapacitet je kapacitet PREDLOŠKA koji je generirao segment (ne
/// grupe) — različiti segmenti istog occurrencea imaju različite kapacitete. Pravilo brojanja (nepromijenjeno): mjesto
/// zauzima sudjelovanje u statusu Confirmed na tom segmentu (Completed/NoShow/Cancelled ne zauzimaju poslovno mjesto).
///
/// Zaključava Appointment redak (FOR UPDATE) i broji POD tim lockom — isti lock kao promocija s liste čekanja, pa dva
/// konkurentna zahtjeva za posljednje mjesto ne mogu oba proći. Kapacitet je MEK: eksplicitni override (provjeren grant
/// groups.capacity.override, vidi <see cref="GroupCapacityOverride"/>) ga smije prekoračiti; fizički kapacitet prostorije/
/// resursa i preklapanja ostaju tvrdi i provjeravaju se odvojeno. Zatečeno stanje iznad kapaciteta (smanjen kapacitet,
/// raniji override) je dopušteno — samo novi dodatak iznad granice traži override. No-op za negrupne termine.
/// Poziva se SAMO za buduće segmente (pozivatelj odlučuje future-only).
/// </summary>
public static class GroupCapacityGuard
{
    public static async Task EnsureAvailable(
        IAppointmentHandler appointmentHandler, IUnitOfWork uow, Guid organizationId, Guid appointmentId, Guid segmentId,
        bool overrideCapacity)
    {
        Appointment locked = await appointmentHandler.GetForUpdateWithGroup(uow, organizationId, appointmentId);
        if (locked == null || locked.Form != AppointmentForm.Group || locked.Group == null)
            return;

        (int capacity, int confirmedCount) = await SeatsOf(appointmentHandler, uow, organizationId, locked, segmentId);
        if (confirmedCount >= capacity && !overrideCapacity)
            throw new BusinessRuleException(
                ErrorCodes.GroupCapacityReached, "Segment grupe je popunjen — kapacitet je dosegnut.",
                new { appointmentId, segmentId, capacity, confirmedCount });
    }

    /// <summary>Kapacitet predloška segmenta (svježe pročitan u transakciji) i broj zauzetih (Confirmed) mjesta.</summary>
    public static async Task<(int Capacity, int ConfirmedCount)> SeatsOf(
        IAppointmentHandler appointmentHandler, IUnitOfWork uow, Guid organizationId, Appointment appointment, Guid segmentId)
    {
        AppointmentSegment segment = appointment.Segments.SingleOrDefault(s => s.Id == segmentId)
            ?? throw new NotFoundAppException("Segment", segmentId);
        Guid templateId = segment.GroupSegmentTemplateId
            ?? throw new InvalidAppointmentSegmentStateException($"Segment {segmentId} grupnog occurrencea nema predložak.");
        int capacity = await uow.Context.GroupSegmentTemplates.Where(t => t.Id == templateId).Select(t => t.Capacity).SingleAsync();
        int confirmedCount = await appointmentHandler.CountConfirmedOnSegment(uow, organizationId, segmentId);
        return (capacity, confirmedCount);
    }
}

/// <summary>
/// Phase M1F — eksplicitni zahtjev za prekoračenje mekog kapaciteta grupe vrijedi SAMO uz efektivni raw grant
/// groups.capacity.override (grant-only; bez uloga/naziva GrantGroupa/Owner bypassa). Sam grant ne ignorira kapacitet:
/// override se primjenjuje tek kad ga zahtjev eksplicitno traži. Bez granta: 403 (ForbiddenAppException).
/// </summary>
public static class GroupCapacityOverride
{
    public static async Task EnsureAllowed(IGrantResolver grantResolver, Guid organizationId, Guid userId, bool requested)
    {
        if (!requested)
            return;
        GrantContext grants = await grantResolver.Resolve(organizationId, userId);
        if (!grants.Has(Grants.GroupsCapacityOverride))
            throw new ForbiddenAppException("Prekoračenje kapaciteta grupe zahtijeva ovlast groups.capacity.override.");
    }
}
