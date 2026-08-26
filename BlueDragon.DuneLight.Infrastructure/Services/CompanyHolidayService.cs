using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using BlueDragon.DuneLight.Core.DTOs.Roster;
using BlueDragon.DuneLight.Core.Interfaces.Roster;
using BlueDragon.DuneLight.Core.Shared;
using BlueDragon.DuneLight.Core.Shared.Exceptions;
using BlueDragon.DuneLight.Infrastructure.Domain.Models.Catalog;
using BlueDragon.DuneLight.Infrastructure.Domain.Models.Roster;
using BlueDragon.DuneLight.Infrastructure.Handlers.Interfaces;

namespace BlueDragon.DuneLight.Infrastructure.Services;

public class CompanyHolidayService : ICompanyHolidayService
{
    private readonly ICompanyHolidayHandler _companyHolidayHandler;
    private readonly ICompanyHandler _companyHandler;

    public CompanyHolidayService(ICompanyHolidayHandler companyHolidayHandler, ICompanyHandler companyHandler)
    {
        _companyHolidayHandler = companyHolidayHandler;
        _companyHandler = companyHandler;
    }

    public async Task<List<CompanyHolidayDto>> GetForCompany(Guid organizationId, Guid companyId, int year)
    {
        Company company = await EnsureCompanyExists(organizationId, companyId);
        List<CompanyHoliday> holidays = await _companyHolidayHandler.GetForCompanyByYear(organizationId, companyId, year);
        return holidays.Select(h => ToDto(h, company.Name)).ToList();
    }

    public async Task<CompanyHolidayDto> Create(Guid organizationId, Guid userId, Guid companyId, CompanyHolidayCreateRequest request)
    {
        Company company = await EnsureCompanyExists(organizationId, companyId);

        bool exists = await _companyHolidayHandler.ExistsForDate(organizationId, companyId, request.Date);
        if (exists)
            throw new BusinessRuleException(ErrorCodes.DuplicateHolidayDate, "Poslovnica već ima upisan praznik za taj datum.");

        CompanyHoliday holiday = new CompanyHoliday
        {
            Id = Guid.NewGuid(),
            OrganizationId = organizationId,
            CompanyId = companyId,
            Date = new DateTimeOffset(request.Date.Date, TimeSpan.Zero),
            Name = request.Name,
            IsRecurringFixed = false,
            CreatedAt = DateTimeOffset.UtcNow,
            CreatedBy = userId
        };

        await _companyHolidayHandler.Add(holiday);
        return ToDto(holiday, company.Name);
    }

    public async Task Delete(Guid organizationId, Guid companyId, Guid id)
    {
        CompanyHoliday holiday = await _companyHolidayHandler.GetByIdLight(organizationId, companyId, id);
        if (holiday == null)
            throw new NotFoundAppException("CompanyHoliday", id);

        await _companyHolidayHandler.Delete(holiday);
    }

    public async Task<GenerateCompanyHolidaysResult> Generate(Guid organizationId, Guid userId, Guid companyId, int year)
    {
        Company company = await EnsureCompanyExists(organizationId, companyId);

        IReadOnlyList<DefaultCompanyHolidayDefinition> definitions = DefaultCompanyHolidays.For(company.Country);
        if (definitions.Count == 0)
            throw new BusinessRuleException(
                ErrorCodes.HolidayCatalogNotDefinedForCountry,
                $"Katalog fiksnih praznika za državu '{company.Country}' nije definiran — dodajte praznike ručno.");

        List<CompanyHoliday> existing = await _companyHolidayHandler.GetForCompanyByYear(organizationId, companyId, year);
        HashSet<DateTimeOffset> existingDates = existing.Select(h => h.Date).ToHashSet();

        List<CompanyHoliday> toCreate = new List<CompanyHoliday>();
        int skippedCount = 0;

        foreach (DefaultCompanyHolidayDefinition definition in definitions)
        {
            DateTimeOffset date = new DateTimeOffset(year, definition.Month, definition.Day, 0, 0, 0, TimeSpan.Zero);
            if (existingDates.Contains(date))
            {
                skippedCount++;
                continue;
            }

            toCreate.Add(new CompanyHoliday
            {
                Id = Guid.NewGuid(),
                OrganizationId = organizationId,
                CompanyId = companyId,
                Date = date,
                Name = definition.Name,
                IsRecurringFixed = true,
                CreatedAt = DateTimeOffset.UtcNow,
                CreatedBy = userId
            });
        }

        await _companyHolidayHandler.AddRange(toCreate);

        return new GenerateCompanyHolidaysResult
        {
            CreatedCount = toCreate.Count,
            SkippedCount = skippedCount,
            Created = toCreate.OrderBy(h => h.Date).Select(h => ToDto(h, company.Name)).ToList()
        };
    }

    private async Task<Company> EnsureCompanyExists(Guid organizationId, Guid companyId)
    {
        Company company = await _companyHandler.GetById(organizationId, companyId);
        if (company == null)
            throw new NotFoundAppException("Company", companyId);

        return company;
    }

    private static CompanyHolidayDto ToDto(CompanyHoliday holiday, string companyName)
    {
        return new CompanyHolidayDto
        {
            Id = holiday.Id.GetValueOrDefault(),
            CompanyId = holiday.CompanyId,
            CompanyName = companyName,
            Date = holiday.Date,
            Name = holiday.Name,
            IsRecurringFixed = holiday.IsRecurringFixed,
            CreatedAt = holiday.CreatedAt,
            CreatedBy = holiday.CreatedBy
        };
    }
}
