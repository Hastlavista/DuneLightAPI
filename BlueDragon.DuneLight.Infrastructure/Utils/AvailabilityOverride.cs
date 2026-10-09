using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using BlueDragon.DuneLight.Core.Interfaces;
using BlueDragon.DuneLight.Core.Shared;
using BlueDragon.DuneLight.Core.Shared.Exceptions;
using BlueDragon.DuneLight.Infrastructure.Domain.Models.Appointments;
using BlueDragon.DuneLight.Infrastructure.Handlers.Interfaces;
using BlueDragon.DuneLight.Infrastructure.UnitOfWork;

namespace BlueDragon.DuneLight.Infrastructure.Utils;

/// <summary>
/// K2 (P-2, ADR-0032) — JEDINA provjera rada izvan radnog vremena / dostupnosti (odsutnost, pauza, praznik, izvan radnog
/// vremena): OverrideAvailability vrijedi samo uz raw grant appointments.availability.override, NEOVISNO o opsegu (own + grant
/// = samo vlastiti termin; opseg provjerava pozivatelj). Isti obrazac kao GroupCapacityOverride: zatražen override bez granta
/// → 403. Svaki override koji je stvarno nešto zaobišao bilježi se u audit termina ("AvailabilityOverride": tko, kada, što).
/// </summary>
public static class AvailabilityOverride
{
    public const string AuditChangeType = "AvailabilityOverride";

    private static readonly HashSet<string> AvailabilityWarningCodes = new()
    {
        WarningCodes.EmployeeAbsent, WarningCodes.EmployeeOnBreak, WarningCodes.CompanyClosedHoliday, WarningCodes.OutsideWorkingHours
    };

    /// <summary>Efektivni override: false kad nije zatražen; zatražen bez granta → 403.</summary>
    public static async Task<bool> Resolve(IGrantResolver grantResolver, Guid organizationId, Guid userId, bool requested)
    {
        if (!requested)
            return false;
        GrantContext grants = await grantResolver.Resolve(organizationId, userId);
        if (!grants.Has(Grants.AppointmentsAvailabilityOverride))
            throw ForbiddenAppException.MissingGrant(
                "Rad izvan radnog vremena ili dostupnosti zahtijeva ovlast appointments.availability.override.", Grants.AppointmentsAvailabilityOverride);
        return true;
    }

    /// <summary>Audit zapis termina kad je override zaobišao barem jednu provjeru dostupnosti (upozorenja u odgovoru).</summary>
    public static Task Audit(
        IAppointmentAuditLogHandler auditLogHandler, IUnitOfWork uow, Guid appointmentId, IEnumerable<WarningDto> warnings, Guid userId,
        DateTimeOffset now)
    {
        List<string> codes = (warnings ?? Enumerable.Empty<WarningDto>())
            .Select(w => w.Code)
            .Where(AvailabilityWarningCodes.Contains)
            .Distinct()
            .OrderBy(c => c)
            .ToList();
        if (codes.Count == 0)
            return Task.CompletedTask;
        return auditLogHandler.Add(uow, Entry(appointmentId, codes, userId, now));
    }

    public static AppointmentAuditLog Entry(Guid appointmentId, IReadOnlyCollection<string> codes, Guid userId, DateTimeOffset now) => new()
    {
        Id = Guid.NewGuid(),
        AppointmentId = appointmentId,
        ChangeType = AuditChangeType,
        NewValue = string.Join(",", codes),
        ChangedAt = now,
        ChangedBy = userId
    };
}
