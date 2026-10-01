using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using BlueDragon.DuneLight.Core.Shared;
using BlueDragon.DuneLight.Core.Shared.Exceptions;
using BlueDragon.DuneLight.Infrastructure.Domain.Models.Appointments;
using BlueDragon.DuneLight.Infrastructure.Domain.Models.Employees;
using BlueDragon.DuneLight.Infrastructure.Handlers.Interfaces;

namespace BlueDragon.DuneLight.Infrastructure.Utils;

/// <summary>
/// JEDINO mjesto koje odlučuje "own" opseg za termine (appointments.write.own i povezane .own grantove — pozivatelj
/// prosljeđuje hasFullScope iz kontrolera). Pozivatelj se UVIJEK razrješava na backendu (User → Employee preko
/// IEmployeeHandler.GetByUserId); EmployeeId iz zahtjeva klijenta nikad ne dokazuje vlasništvo sam po sebi.
///
/// Phase M1B — vlasništvo je SEGMENTNO: own-opseg smije mijenjati segmente kojima je pozivatelj dodijeljen i njihova
/// sudjelovanja (operacija nad sudjelovanjem slijedi segment sudjelovanja). Operacija koja dira VIŠE segmenata (ili cijeli
/// termin) u own-opsegu zahtijeva da je pozivatelj dodijeljen SVAKOM od njih — čim zahvaća tuđi segment, potreban je
/// all-opseg. Za današnje jednosegmentne termine to je isto pravilo kao prije ("dodijeljen terminu"); konačna semantika
/// own-opsega nad cijelim višesegmentnim terminom je otvorena i namjerno se ne izmišlja (vidi izvještaj M1B).
/// </summary>
public static class AppointmentOwnership
{
    /// <summary>Segment bez zaposlenika (grupni occurrence bez trenera) nije dodijeljen nikome.</summary>
    public static bool IsAssignedToSegment(AppointmentSegment segment, Guid employeeId)
    {
        ArgumentNullException.ThrowIfNull(segment);
        return AppointmentSegments.HasEmployee(segment, employeeId);
    }

    /// <summary>Own-scope za POSTOJEĆE segmente: pozivatelj mora biti dodijeljen SVAKOM zadanom segmentu (prazan skup
    /// segmenata u own-opsegu se odbija). Provjerava se samo trenutna dodjela (novi trener kod Update/Move se ovdje ne
    /// provjerava — pinned F-04).</summary>
    public static async Task EnsureCallerOwnsSegments(
        IEmployeeHandler employeeHandler, Guid organizationId, Guid userId, bool hasFullScope,
        IEnumerable<AppointmentSegment> segments, string notOwnerMessage)
    {
        ArgumentNullException.ThrowIfNull(segments);
        if (hasFullScope)
            return;

        List<AppointmentSegment> list = segments.ToList();
        Employee employee = await employeeHandler.GetByUserId(organizationId, userId);
        if (employee == null || !employee.Id.HasValue || list.Count == 0 || !list.All(s => IsAssignedToSegment(s, employee.Id.Value)))
            throw new BusinessRuleException(ErrorCodes.NotOwner, notOwnerMessage);
    }

    /// <summary>Operacija nad cijelim terminom (otkazivanje, close-out, completion, Update/Move kompatibilnost): u own-opsegu
    /// pozivatelj mora biti dodijeljen SVIM segmentima termina.</summary>
    public static Task EnsureCallerOwnsWholeAppointment(
        IEmployeeHandler employeeHandler, Guid organizationId, Guid userId, bool hasFullScope, Appointment appointment, string notOwnerMessage)
    {
        ArgumentNullException.ThrowIfNull(appointment);
        return EnsureCallerOwnsSegments(employeeHandler, organizationId, userId, hasFullScope, appointment.Segments, notOwnerMessage);
    }

    /// <summary>Own-scope za NOVI termin: pozivatelj smije zakazati samo za sebe — svaki zatraženi zaposlenik (svih
    /// segmenata) mora biti zaposlenik pozivatelja.</summary>
    public static async Task EnsureCallerIsEmployee(
        IEmployeeHandler employeeHandler, Guid organizationId, Guid userId, bool hasFullScope,
        IEnumerable<Guid> requestedEmployeeIds, string notOwnerMessage)
    {
        if (hasFullScope)
            return;

        List<Guid> requested = requestedEmployeeIds.ToList();
        Employee employee = await employeeHandler.GetByUserId(organizationId, userId);
        if (employee == null || requested.Count == 0 || requested.Any(id => id != employee.Id))
            throw new BusinessRuleException(ErrorCodes.NotOwner, notOwnerMessage);
    }
}
