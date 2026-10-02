using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using BlueDragon.DuneLight.Core.Shared;
using BlueDragon.DuneLight.Core.Shared.Exceptions;
using BlueDragon.DuneLight.Infrastructure.Handlers.Interfaces;
using BlueDragon.DuneLight.Infrastructure.UnitOfWork;

namespace BlueDragon.DuneLight.Infrastructure.Utils;

/// <summary>
/// Phase M1D.1 — izmjena Room.Capacity / Resource.Capacity čuva TVRDU invarijantu kapaciteta. Pozivatelj je u transakciji
/// zaključao ISTI subjekt rasporeda (prostoriju/resurs, SchedulingLockOrder) kao i upisi zakazivanja i tek ZATIM pročitao
/// trenutni kapacitet — izmjena i upis zakazivanja za isti subjekt su time serijalizirani (nikad "zauzetost 5 / kapacitet 4").
///
/// Povećanje je uvijek dopušteno. Smanjenje je dopušteno samo ako novi kapacitet pokriva vršnu zauzetost svih RELEVANTNIH
/// segmenata: rezerviraju slot (SegmentOccupancy) i nisu završili (PlannedEnd &gt; sada, UTC instant) — uključivo segment koji
/// je u tijeku; dio prije "sada" se ne gleda. Prošlost (završeni segmenti) nikad ne blokira smanjenje i ostaje nepromijenjena.
/// Isti brojač osoba (RoomPeopleCount) i isti vremenski raslojeni izračun (IntervalCapacity) kao zakazivanje.
/// </summary>
public static class CapacityChangeGuard
{
    /// <summary>Gornja granica "budućnosti" za upit (dovoljno daleko, unutar raspona timestamptz).</summary>
    private static readonly DateTimeOffset Horizon = new(9999, 1, 1, 0, 0, 0, TimeSpan.Zero);

    public static async Task EnsureRoomCapacityChange(
        ISchedulingOccupancyHandler occupancy, IUnitOfWork uow, Guid organizationId, Guid roomId, string roomName,
        int currentCapacity, int proposedCapacity, DateTimeOffset now)
    {
        if (proposedCapacity >= currentCapacity)
            return;

        (int peak, DateTimeOffset? peakAt) = RelevantPeak(
            await occupancy.GetRoomUsage(uow, organizationId, roomId, now, Horizon, excludedSegmentIds: null), now);
        if (peak > proposedCapacity)
            throw new BusinessRuleException(ErrorCodes.RoomCapacityBelowScheduledUsage,
                $"Kapacitet prostorije '{roomName}' ne može se smanjiti na {proposedCapacity} — zakazano je do {peak} osoba istovremeno.",
                new { roomId, roomName, currentCapacity, proposedCapacity, peakUsage = peak, peakAt });
    }

    public static async Task EnsureResourceCapacityChange(
        ISchedulingOccupancyHandler occupancy, IUnitOfWork uow, Guid organizationId, Guid resourceId, string resourceName,
        int currentCapacity, int proposedCapacity, DateTimeOffset now)
    {
        if (proposedCapacity >= currentCapacity)
            return;

        (int peak, DateTimeOffset? peakAt) = RelevantPeak(
            await occupancy.GetResourceUsage(uow, organizationId, resourceId, now, Horizon, excludedSegmentIds: null), now);
        if (peak > proposedCapacity)
            throw new BusinessRuleException(ErrorCodes.ResourceCapacityBelowScheduledUsage,
                $"Kapacitet resursa '{resourceName}' ne može se smanjiti na {proposedCapacity} — zakazano je do {peak} jedinica istovremeno.",
                new { resourceId, resourceName, currentCapacity, proposedCapacity, peakUsage = peak, peakAt });
    }

    /// <summary>Vrh zauzetosti od "sada" nadalje: segment u tijeku se reže na [sada, kraj).</summary>
    private static (int PeakUsage, DateTimeOffset? PeakAt) RelevantPeak(IReadOnlyList<CapacityClaim> claims, DateTimeOffset now) =>
        IntervalCapacity.Peak(claims
            .Where(c => c.End > now)
            .Select(c => c.Start < now ? c with { Start = now } : c)
            .ToList());
}
