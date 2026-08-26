using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using BlueDragon.DuneLight.Infrastructure.Domain.Models.Roster;

namespace BlueDragon.DuneLight.Infrastructure.Handlers.Interfaces;

public interface ICompanyHolidayHandler
{
    /// <summary>Svi praznici poslovnice unutar zadane godine, poredani po datumu.</summary>
    Task<List<CompanyHoliday>> GetForCompanyByYear(Guid organizationId, Guid companyId, int year);

    Task<CompanyHoliday> GetByIdLight(Guid organizationId, Guid companyId, Guid id);

    Task<bool> ExistsForDate(Guid organizationId, Guid companyId, DateTimeOffset date);

    Task Add(CompanyHoliday holiday);

    /// <summary>Batch insert za Generate — jedan SaveChangesAsync za cijelu godinu.</summary>
    Task AddRange(List<CompanyHoliday> holidays);

    Task Delete(CompanyHoliday holiday);

    /// <summary>Svi praznici bilo koje od zadanih poslovnica čiji Date pada u [rangeFrom,rangeTo] (uključivo) —
    /// jedan upit za više poslovnica odjednom, isti obrazac kao ScheduleBreakHandler.GetForEmployeesInRange.</summary>
    Task<List<CompanyHoliday>> GetForCompaniesInRange(Guid organizationId, List<Guid> companyIds, DateTimeOffset rangeFrom, DateTimeOffset rangeTo);
}
