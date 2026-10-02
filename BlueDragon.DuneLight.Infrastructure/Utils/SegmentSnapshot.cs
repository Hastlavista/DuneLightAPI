using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using BlueDragon.DuneLight.Core.Shared;
using BlueDragon.DuneLight.Core.Shared.Exceptions;
using BlueDragon.DuneLight.Infrastructure.Domain.Models.Appointments;
using BlueDragon.DuneLight.Infrastructure.Handlers.Interfaces;
using BlueDragon.DuneLight.Infrastructure.UnitOfWork;

namespace BlueDragon.DuneLight.Infrastructure.Utils;

/// <summary>
/// Phase M1E — optimistička provjera stanja segmenta za upise koji validiraju nad pročitanim stanjem. Upis: (1) pročita
/// segment i iz njega izvede što traži (SegmentClaim) i koje subjekte zaključava, (2) zaključa subjekte rasporeda, (3) zaključa
/// Appointment redak (FOR UPDATE — postojeći redoslijed), (4) ovdje usporedi pročitano stanje sa stanjem pod lockom. Ako se
/// segment u međuvremenu promijenio (vrijeme, usluga, prostorija, zaposlenici, resursi, zauzimajući klijenti), provjere su
/// rađene nad zastarjelim stanjem → CONCURRENCY_CONFLICT (ponoviti), nikad tiho kršenje invarijante. Svi upisi koji mijenjaju
/// segment ili mu dodaju sudjelovanje drže Appointment lock do commita, pa je stanje provjereno pod lockom i konačno.
/// </summary>
public static class SegmentSnapshot
{
    public sealed record State(
        Guid SegmentId,
        DateTimeOffset PlannedStart,
        DateTimeOffset PlannedEnd,
        Guid ServiceId,
        Guid? RoomId,
        IReadOnlyList<Guid> EmployeeIds,
        IReadOnlyList<(Guid ResourceId, int Quantity)> Resources,
        IReadOnlyList<Guid> OccupyingClientIds,
        bool ReservesSlot);

    public static State Capture(Appointment appointment, AppointmentSegment segment, IEnumerable<ResourceClaim> resources) => new(
        segment.Id.GetValueOrDefault(),
        segment.PlannedStart,
        segment.PlannedEnd,
        segment.ServiceId,
        segment.RoomId,
        segment.Employees.Select(e => e.EmployeeId).OrderBy(id => id).ToList(),
        resources.Select(r => (r.ResourceId, r.Quantity)).OrderBy(r => r.ResourceId).ToList(),
        appointment.Bookings
            .Where(b => b.Participations.Any(p => p.AppointmentSegmentId == segment.Id && ParticipationOccupancy.Occupies(p.Status)))
            .Select(b => b.ClientId).OrderBy(id => id).ToList(),
        SegmentOccupancy.Reserves(appointment, segment));

    /// <summary>Stanje pod lockom iz praćenog agregata (segmenti s resursima, Bookinzi sa sudjelovanjima).</summary>
    public static State CaptureTracked(Appointment locked, AppointmentSegment segment) =>
        Capture(locked, segment, segment.Resources.Select(r => new ResourceClaim(r.ResourceId, r.QuantityRequired)));

    /// <summary>Zaključava Appointment (FOR UPDATE, nepraćeno svježe stanje) i provjerava da se pročitani segmenti nisu
    /// promijenili. <paramref name="includeParticipants"/> = false za upise koji SAMI dodaju/aktiviraju sudjelovanje
    /// (validirali su samo okvir segmenta: vrijeme, uslugu, prostoriju, zaposlenike, resurse) — suprotni smjer (prepisivač
    /// segmenta koji ne vidi novog sudionika) hvata prepisivačeva vlastita puna provjera, jer ovaj upis drži Appointment
    /// lock do commita.</summary>
    public static async Task VerifyUnderLock(
        IAppointmentHandler appointmentHandler, IUnitOfWork uow, Guid organizationId, Guid appointmentId,
        IEnumerable<State> validated, bool includeParticipants = true)
    {
        Appointment locked = await appointmentHandler.GetLockedSegmentState(uow, organizationId, appointmentId)
            ?? throw new NotFoundAppException("Appointment", appointmentId);
        foreach (State state in validated)
        {
            AppointmentSegment segment = locked.Segments.SingleOrDefault(s => s.Id == state.SegmentId);
            EnsureUnchanged(state, segment == null ? null : CaptureTracked(locked, segment), includeParticipants);
        }
    }

    public static void EnsureUnchanged(State validated, State locked, bool includeParticipants = true)
    {
        ArgumentNullException.ThrowIfNull(validated);
        bool same = locked != null
                    && validated.SegmentId == locked.SegmentId
                    && validated.PlannedStart == locked.PlannedStart
                    && validated.PlannedEnd == locked.PlannedEnd
                    && validated.ServiceId == locked.ServiceId
                    && validated.RoomId == locked.RoomId
                    && validated.EmployeeIds.SequenceEqual(locked.EmployeeIds)
                    && validated.Resources.SequenceEqual(locked.Resources)
                    && (!includeParticipants
                        || (validated.ReservesSlot == locked.ReservesSlot && validated.OccupyingClientIds.SequenceEqual(locked.OccupyingClientIds)));
        if (!same)
            throw new BusinessRuleException(
                ErrorCodes.ConcurrencyConflict, "Segment je upravo promijenjen od strane drugog zahtjeva — pokušajte ponovno.");
    }
}
