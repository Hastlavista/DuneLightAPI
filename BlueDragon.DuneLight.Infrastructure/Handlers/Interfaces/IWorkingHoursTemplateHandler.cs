using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using BlueDragon.DuneLight.Core.Enums;
using BlueDragon.DuneLight.Infrastructure.Domain.Models.Roster;

namespace BlueDragon.DuneLight.Infrastructure.Handlers.Interfaces;

public interface IWorkingHoursTemplateHandler
{
    /// <summary>Puni redak s Intervals + Employee uključenim, ili null ako predložak još nije konfiguriran (vidi FAZA 1 fail-closed).</summary>
    Task<WorkingHoursTemplate> GetForEmployee(Guid organizationId, Guid employeeId);

    /// <summary>Batch dohvat po zaposlenicima u jednom upitu (Intervals uključen) — za team-monthly pregled, izbjegava N+1 (vidi FAZA 2).</summary>
    Task<List<WorkingHoursTemplate>> GetForEmployees(Guid organizationId, List<Guid> employeeIds);

    /// <summary>Puni redak s Intervals + Company uključenim, ili null ako predložak još nije konfiguriran.</summary>
    Task<WorkingHoursTemplate> GetForCompany(Guid organizationId, Guid companyId);

    /// <summary>Upsert-u-mjestu (singleton po vlasniku) — pun zamjenski popis intervala (delete-all/insert-all, nema unique indeksa na intervalima).</summary>
    Task<WorkingHoursTemplate> UpsertForEmployee(
        Guid organizationId, Guid employeeId, WorkingHoursCycleType cycleType, DateTimeOffset anchorDate,
        List<WorkingHoursInterval> intervals, Guid userId);

    Task<WorkingHoursTemplate> UpsertForCompany(
        Guid organizationId, Guid companyId, WorkingHoursCycleType cycleType, DateTimeOffset anchorDate,
        List<WorkingHoursInterval> intervals, Guid userId);
}