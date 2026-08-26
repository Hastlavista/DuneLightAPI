using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using BlueDragon.DuneLight.Core.DTOs.Roster;

namespace BlueDragon.DuneLight.Core.Interfaces.Roster;

public interface ICompanyHolidayService
{
    Task<List<CompanyHolidayDto>> GetForCompany(Guid organizationId, Guid companyId, int year);

    /// <summary>Ručni unos — uvijek IsRecurringFixed=false, uključivo pomične praznike (Uskrs i sl.).</summary>
    Task<CompanyHolidayDto> Create(Guid organizationId, Guid userId, Guid companyId, CompanyHolidayCreateRequest request);

    Task Delete(Guid organizationId, Guid companyId, Guid id);

    /// <summary>Idempotentno generira fiksne praznike (vidi DefaultCompanyHolidays) za zadanu godinu i poslovnicu —
    /// datumi koji već postoje se preskaču (skip, ne error).</summary>
    Task<GenerateCompanyHolidaysResult> Generate(Guid organizationId, Guid userId, Guid companyId, int year);
}
