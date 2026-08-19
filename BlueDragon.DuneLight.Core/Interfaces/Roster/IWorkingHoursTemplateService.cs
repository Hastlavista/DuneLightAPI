using System;
using System.Threading.Tasks;
using BlueDragon.DuneLight.Core.DTOs.Roster;

namespace BlueDragon.DuneLight.Core.Interfaces.Roster;

public interface IWorkingHoursTemplateService
{
    Task<WorkingHoursTemplateDto> GetForEmployee(Guid organizationId, Guid employeeId);

    Task<WorkingHoursTemplateDto> UpsertForEmployee(Guid organizationId, Guid userId, Guid employeeId, WorkingHoursTemplateUpsertRequest request);

    Task<WorkingHoursTemplateDto> GetForCompany(Guid organizationId, Guid companyId);

    Task<WorkingHoursTemplateDto> UpsertForCompany(Guid organizationId, Guid userId, Guid companyId, WorkingHoursTemplateUpsertRequest request);

    /// <summary>Nacrt iz FAZE 1 — EffectiveIntervals je presjek employee ∩ company intervala za taj dan.</summary>
    Task<AvailabilityDto> GetAvailability(Guid organizationId, Guid employeeId, Guid companyId, DateTimeOffset date);
}