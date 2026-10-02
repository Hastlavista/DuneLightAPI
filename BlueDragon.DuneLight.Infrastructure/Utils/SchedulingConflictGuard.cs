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
/// Phase M1C — JEDINA provjera tvrdih invarijanti rasporeda (zaposlenik, klijent) za upis. Pozivatelj je unutar vlastite
/// transakcije (<see cref="IUnitOfWork"/>), PRIJE ikakvog drugog locka (vidi <see cref="SchedulingLockOrder"/>):
/// <list type="number">
/// <item>ciljno stanje u memoriji (sudari IZMEĐU predloženih segmenata — novi sestrinski segment još nije u bazi);</item>
/// <item>zaključavanje subjekata (svi zaposlenici i klijenti svih segmenata, globalnim redoslijedom);</item>
/// <item>postojeće stanje u bazi pod lockom: po segmentu prvo zaposlenici, (opcionalno prostorija — pozivatelj), zatim
/// klijenti. Isključuju se SAMO segmenti koji se upravo prepisuju (<see cref="SegmentClaim.SegmentId"/>), nikad termin.</item>
/// </list>
/// Gubitnik konkurentne utrke dobiva istu poslovnu grešku kao i nekonkurentni sudar (APPOINTMENT_OVERLAP) — nikad grešku
/// baze.
/// </summary>
public static class SchedulingConflictGuard
{
    public static async Task Claim(
        ISchedulingOccupancyHandler occupancy, IUnitOfWork uow, Guid organizationId, IReadOnlyList<SegmentClaim> claims,
        Func<Guid, string> clientLabel = null, Func<SegmentClaim, Task> afterEmployeeCheck = null)
    {
        ArgumentNullException.ThrowIfNull(claims);
        SchedulingConflicts.EnsureTargetStateConsistent(claims, clientLabel);

        await occupancy.LockSchedulingSubjects(uow, claims.SelectMany(c => c.EmployeeIds), claims.SelectMany(c => c.ClientIds));

        List<Guid> rewritten = claims.Where(c => c.SegmentId.HasValue).Select(c => c.SegmentId.Value).ToList();
        foreach (SegmentClaim claim in claims)
        {
            if (claim.EmployeeIds.Count > 0)
            {
                List<OccupancySlot> employeeConflicts = await occupancy.GetOverlappingForEmployees(
                    uow, organizationId, claim.EmployeeIds, claim.PlannedStart, claim.PlannedEnd, rewritten);
                if (employeeConflicts.Count > 0)
                    throw new BusinessRuleException(ErrorCodes.AppointmentOverlap, SchedulingConflicts.EmployeeConflictMessage);
            }

            if (afterEmployeeCheck != null)
                await afterEmployeeCheck(claim);

            if (claim.ClientIds.Count > 0)
            {
                List<OccupancySlot> clientConflicts = await occupancy.GetOverlappingForClients(
                    uow, organizationId, claim.ClientIds, claim.PlannedStart, claim.PlannedEnd, rewritten);
                foreach (Guid clientId in claim.ClientIds)
                    if (clientConflicts.Any(slot => slot.ActiveClientIds.Contains(clientId)))
                        throw new BusinessRuleException(ErrorCodes.AppointmentOverlap, SchedulingConflicts.ClientMessage(clientLabel?.Invoke(clientId)));
            }
        }
    }

    /// <summary>Novo/ponovno aktivno sudjelovanje klijenta na POSTOJEĆEM segmentu (AddBooking, gost, AddMember, reaktivacija
    /// prijelazom statusa). Segment se ne prepisuje, pa se ne isključuje ništa: klijent koji već zauzima taj segment je
    /// sudar (pozivatelji taj slučaj obrađuju prije kao "već postoji").</summary>
    public static Task ClaimClientOnSegment(
        ISchedulingOccupancyHandler occupancy, IUnitOfWork uow, Guid organizationId, AppointmentSegment segment, Guid clientId,
        Func<Guid, string> clientLabel = null) =>
        Claim(occupancy, uow, organizationId,
            new[] { new SegmentClaim(null, segment.PlannedStart, segment.PlannedEnd, Array.Empty<Guid>(), new[] { clientId }) },
            clientLabel);
}
