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
/// Phase M1C/M1D — JEDINA provjera tvrdih invarijanti rasporeda (zaposlenik, klijent, kapacitet prostorije, kapacitet
/// resursa) za upis. Pozivatelj je unutar vlastite
/// transakcije (<see cref="IUnitOfWork"/>), PRIJE ikakvog drugog locka (vidi <see cref="SchedulingLockOrder"/>):
/// <list type="number">
/// <item>ciljno stanje u memoriji (sudari IZMEĐU predloženih segmenata — novi sestrinski segment još nije u bazi);</item>
/// <item>zaključavanje subjekata (zaposlenici, klijenti, te prostorije/resursi čija zauzetost raste — globalnim
/// redoslijedom);</item>
/// <item>postojeće stanje u bazi pod lockom: po segmentu prvo zaposlenici, zatim klijenti; potom (Phase M1D) kapacitet
/// prostorija (osobe) i resursa (količina), vremenski raslojeno. Isključuju se SAMO segmenti koji se upravo prepisuju
/// (<see cref="SegmentClaim.SegmentId"/>), nikad termin.</item>
/// </list>
/// Gubitnik konkurentne utrke dobiva istu poslovnu grešku kao i nekonkurentni sudar (APPOINTMENT_OVERLAP) — nikad grešku
/// baze.
/// </summary>
public static class SchedulingConflictGuard
{
    public static async Task Claim(
        ISchedulingOccupancyHandler occupancy, IUnitOfWork uow, Guid organizationId, IReadOnlyList<SegmentClaim> claims,
        Func<Guid, string> clientLabel = null, SchedulingSubjects alsoLock = null)
    {
        ArgumentNullException.ThrowIfNull(claims);
        SchedulingConflicts.EnsureTargetStateConsistent(claims, clientLabel);

        // Phase M1E: promjena stari -> novi (zaposlenik, prostorija, resursi) zaključava UNIJU starih i novih subjekata,
        // jednim pozivom (jedan globalni redoslijed) — oslobađanje starog subjekta je serijalizirano s njegovim zauzimanjem.
        alsoLock ??= SchedulingSubjects.None;
        await occupancy.LockSchedulingSubjects(
            uow,
            claims.SelectMany(c => c.EmployeeIds).Concat(alsoLock.EmployeeIds),
            claims.SelectMany(c => c.ClientIds).Concat(alsoLock.ClientIds),
            RoomsOf(claims).Concat(alsoLock.RoomIds),
            ResourcesOf(claims).Concat(alsoLock.ResourceIds));

        List<Guid> rewritten = Rewritten(claims);
        foreach (SegmentClaim claim in claims)
        {
            if (claim.EmployeeIds.Count > 0)
            {
                List<OccupancySlot> employeeConflicts = await occupancy.GetOverlappingForEmployees(
                    uow, organizationId, claim.EmployeeIds, claim.PlannedStart, claim.PlannedEnd, rewritten);
                if (employeeConflicts.Count > 0)
                    throw new BusinessRuleException(ErrorCodes.AppointmentOverlap, SchedulingConflicts.EmployeeConflictMessage);
            }

            if (claim.ClientIds.Count > 0)
            {
                List<OccupancySlot> clientConflicts = await occupancy.GetOverlappingForClients(
                    uow, organizationId, claim.ClientIds, claim.PlannedStart, claim.PlannedEnd, rewritten);
                foreach (Guid clientId in claim.ClientIds)
                    if (clientConflicts.Any(slot => slot.ActiveClientIds.Contains(clientId)))
                        throw new BusinessRuleException(ErrorCodes.AppointmentOverlap, SchedulingConflicts.ClientMessage(clientLabel?.Invoke(clientId)));
            }
        }

        CapacityViolation violation = (await FindCapacityViolations(occupancy, uow, organizationId, claims)).FirstOrDefault();
        if (violation != null)
            throw violation.ToException();
    }

    /// <summary>Novo/ponovno aktivno sudjelovanje klijenta na POSTOJEĆEM segmentu (AddBooking, gost, AddMember, reaktivacija
    /// prijelazom statusa) — vidi <see cref="SegmentClaim.ForParticipationActivation"/>.</summary>
    public static Task ClaimParticipationActivation(
        ISchedulingOccupancyHandler occupancy, IUnitOfWork uow, Guid organizationId, SegmentClaim activation,
        Func<Guid, string> clientLabel = null) =>
        Claim(occupancy, uow, organizationId, new[] { activation }, clientLabel);

    /// <summary>
    /// Phase M1D — kapacitet prostorija (osobe) i resursa (količina) za zadano ciljno stanje: po prostoriji/resursu se
    /// učitaju postojeće zauzetosti iz baze (samo segmenti koji rezerviraju slot, preklapaju raspon predloženih, bez
    /// prepisivanih segmenata) i zajedno s predloženim zauzetostima (uključivo sestrinske predložene segmente) vremenski
    /// raslojeno provjere (<see cref="IntervalCapacity"/>). Pozivatelj je PRIJE zaključao prostorije/resurse. Vraća sve
    /// povrede (batch tokovi ih prijavljuju po occurrenceu).
    /// </summary>
    public static async Task<List<CapacityViolation>> FindCapacityViolations(
        ISchedulingOccupancyHandler occupancy, IUnitOfWork uow, Guid organizationId, IReadOnlyList<SegmentClaim> claims)
    {
        List<Guid> rewritten = Rewritten(claims);
        List<CapacityViolation> violations = new();

        List<Guid> roomIds = RoomsOf(claims).Distinct().ToList();
        Dictionary<Guid, (string Name, int Capacity)> rooms = roomIds.Count == 0
            ? new()
            : await occupancy.GetRoomCapacities(uow, organizationId, roomIds);
        foreach (Guid roomId in roomIds)
        {
            if (!rooms.TryGetValue(roomId, out (string Name, int Capacity) room))
                continue;
            List<SegmentClaim> proposed = claims.Where(c => c.RoomId == roomId && c.RoomPeople > 0).ToList();
            List<CapacityClaim> existing = await occupancy.GetRoomUsage(
                uow, organizationId, roomId, proposed.Min(c => c.PlannedStart), proposed.Max(c => c.PlannedEnd), rewritten);
            CapacityEvaluation evaluation = IntervalCapacity.Evaluate(existing, proposed.Select(c => c.CapacityOf(c.RoomPeople)).ToList(), room.Capacity);
            if (evaluation.Exceeds)
                violations.Add(new CapacityViolation(
                    CapacitySubject.Room, roomId, room.Name, room.Capacity, evaluation,
                    evaluation.ViolatingProposedIndexes.Select(i => proposed[i]).ToList()));
        }

        List<Guid> resourceIds = ResourcesOf(claims).Distinct().ToList();
        Dictionary<Guid, (string Name, int Capacity)> resources = resourceIds.Count == 0
            ? new()
            : await occupancy.GetResourceCapacities(uow, organizationId, resourceIds);
        foreach (Guid resourceId in resourceIds)
        {
            if (!resources.TryGetValue(resourceId, out (string Name, int Capacity) resource))
                continue;
            List<(SegmentClaim Claim, int Quantity)> proposed = claims
                .SelectMany(c => c.Resources.Where(r => r.ResourceId == resourceId).Select(r => (Claim: c, r.Quantity)))
                .ToList();
            List<CapacityClaim> existing = await occupancy.GetResourceUsage(
                uow, organizationId, resourceId, proposed.Min(p => p.Claim.PlannedStart), proposed.Max(p => p.Claim.PlannedEnd), rewritten);
            CapacityEvaluation evaluation = IntervalCapacity.Evaluate(
                existing, proposed.Select(p => p.Claim.CapacityOf(p.Quantity)).ToList(), resource.Capacity);
            if (evaluation.Exceeds)
                violations.Add(new CapacityViolation(
                    CapacitySubject.Resource, resourceId, resource.Name, resource.Capacity, evaluation,
                    evaluation.ViolatingProposedIndexes.Select(i => proposed[i].Claim).ToList()));
        }

        return violations;
    }

    /// <summary>Prostorije čija zauzetost upisom RASTE (zaključavaju se).</summary>
    public static IEnumerable<Guid> RoomsOf(IEnumerable<SegmentClaim> claims) =>
        claims.Where(c => c.RoomId.HasValue && c.RoomPeople > 0).Select(c => c.RoomId.Value);

    /// <summary>Resursi čija zauzetost upisom RASTE (zaključavaju se).</summary>
    public static IEnumerable<Guid> ResourcesOf(IEnumerable<SegmentClaim> claims) =>
        claims.SelectMany(c => c.Resources).Where(r => r.Quantity > 0).Select(r => r.ResourceId);

    private static List<Guid> Rewritten(IEnumerable<SegmentClaim> claims) =>
        claims.Where(c => c.SegmentId.HasValue).Select(c => c.SegmentId.Value).Distinct().ToList();
}

/// <summary>Phase M1E: dodatni subjekti rasporeda koje upis zaključava (npr. STARI zaposlenik/prostorija/resursi segmenta koji
/// se mijenja) uz one koje traži — jedan sortirani skup, isti globalni redoslijed.</summary>
public sealed record SchedulingSubjects(
    IReadOnlyCollection<Guid> EmployeeIds, IReadOnlyCollection<Guid> ClientIds, IReadOnlyCollection<Guid> RoomIds, IReadOnlyCollection<Guid> ResourceIds)
{
    public static readonly SchedulingSubjects None = new(Array.Empty<Guid>(), Array.Empty<Guid>(), Array.Empty<Guid>(), Array.Empty<Guid>());

    /// <summary>Svi subjekti koje segment trenutno zauzima (zaposlenici, prostorija, resursi).</summary>
    public static SchedulingSubjects Of(AppointmentSegment segment) => new(
        segment.Employees.Select(e => e.EmployeeId).ToList(),
        Array.Empty<Guid>(),
        segment.RoomId.HasValue ? new[] { segment.RoomId.Value } : Array.Empty<Guid>(),
        segment.Resources.Select(r => r.ResourceId).ToList());
}

public enum CapacitySubject
{
    Room,
    Resource
}

/// <summary>Phase M1D: povreda tvrdog kapaciteta — subjekt, kapacitet, vršna zauzetost i predloženi segmenti koji je
/// uzrokuju.</summary>
public sealed record CapacityViolation(
    CapacitySubject Subject, Guid SubjectId, string SubjectName, int Capacity, CapacityEvaluation Evaluation, IReadOnlyList<SegmentClaim> Claims)
{
    public BusinessRuleException ToException() => Subject == CapacitySubject.Room
        ? SchedulingConflicts.RoomCapacityExceeded(SubjectId, SubjectName, Capacity, Evaluation)
        : SchedulingConflicts.ResourceCapacityExceeded(SubjectId, SubjectName, Capacity, Evaluation);
}
