using System;
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
/// Od Phase D3A "dodijeljen terminu" znači: zaposlenik pozivatelja je dodijeljen JEDINOM segmentu termina (termin s više
/// segmenata/zaposlenika baca iznimku — konačna semantika vlasništva višesegmentnog termina je otvorena odluka).
/// Isti obrazac kao GroupCapacityGuard (statički, handler kao parametar).
/// </summary>
public static class AppointmentOwnership
{
    /// <summary>Termin bez trenera (grupni occurrence bez zadanog trenera) nije dodijeljen nikome.</summary>
    public static bool IsAssignedToEmployee(Appointment appointment, Guid employeeId)
    {
        ArgumentNullException.ThrowIfNull(appointment);
        // Phase D3A: dodijeljen = zaposlenik jedinog segmenta (termin bez zaposlenika nema vlasnika u own-opsegu).
        Guid? assigned = AppointmentSegments.GetSingleEmployeeId(AppointmentSegments.GetSingleExecutionSegment(appointment));
        return assigned.HasValue && assigned.Value == employeeId;
    }

    /// <summary>Own-scope za POSTOJEĆI termin: pozivatelj mora biti zaposlenik dodijeljen tom terminu. Provjerava se
    /// samo trenutna dodjela (novi trener kod Update/Move se ovdje ne provjerava — pinned F-04).</summary>
    public static async Task EnsureCallerIsAssigned(
        IEmployeeHandler employeeHandler, Guid organizationId, Guid userId, bool hasFullScope, Appointment appointment, string notOwnerMessage)
    {
        ArgumentNullException.ThrowIfNull(appointment);
        if (hasFullScope)
            return;

        Employee employee = await employeeHandler.GetByUserId(organizationId, userId);
        if (employee == null || !employee.Id.HasValue || !IsAssignedToEmployee(appointment, employee.Id.Value))
            throw new BusinessRuleException(ErrorCodes.NotOwner, notOwnerMessage);
    }

    /// <summary>Own-scope za NOVI termin: pozivatelj smije zakazati samo za sebe — zatraženi trener mora biti
    /// zaposlenik pozivatelja.</summary>
    public static async Task EnsureCallerIsEmployee(
        IEmployeeHandler employeeHandler, Guid organizationId, Guid userId, bool hasFullScope, Guid requestedEmployeeId, string notOwnerMessage)
    {
        if (hasFullScope)
            return;

        Employee employee = await employeeHandler.GetByUserId(organizationId, userId);
        if (employee == null || employee.Id != requestedEmployeeId)
            throw new BusinessRuleException(ErrorCodes.NotOwner, notOwnerMessage);
    }
}
