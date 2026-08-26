using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using BlueDragon.DuneLight.Infrastructure.Domain.Contexts;
using BlueDragon.DuneLight.Infrastructure.Domain.Models.Roster;
using BlueDragon.DuneLight.Infrastructure.Domain.Settings;
using BlueDragon.DuneLight.Infrastructure.Handlers.Interfaces;
using Microsoft.EntityFrameworkCore;

namespace BlueDragon.DuneLight.Infrastructure.Handlers.Implementations;

public class CompanyHolidayHandler : ICompanyHolidayHandler
{
    private readonly DatabaseSettings _databaseSettings;

    public CompanyHolidayHandler(DatabaseSettings databaseSettings)
    {
        _databaseSettings = databaseSettings;
    }

    public async Task<List<CompanyHoliday>> GetForCompanyByYear(Guid organizationId, Guid companyId, int year)
    {
        DateTimeOffset yearStart = new DateTimeOffset(year, 1, 1, 0, 0, 0, TimeSpan.Zero);
        DateTimeOffset yearEnd = yearStart.AddYears(1).AddTicks(-1);

        await using DatabaseContext context = DatabaseContext.GenerateContext(_databaseSettings.ConnectionString);
        return await context.CompanyHolidays
            .Where(h =>
                h.OrganizationId == organizationId &&
                h.CompanyId == companyId &&
                h.Date >= yearStart && h.Date <= yearEnd)
            .OrderBy(h => h.Date)
            .ToListAsync();
    }

    public async Task<CompanyHoliday> GetByIdLight(Guid organizationId, Guid companyId, Guid id)
    {
        await using DatabaseContext context = DatabaseContext.GenerateContext(_databaseSettings.ConnectionString);
        return await context.CompanyHolidays.SingleOrDefaultAsync(h =>
            h.OrganizationId == organizationId && h.CompanyId == companyId && h.Id == id);
    }

    public async Task<bool> ExistsForDate(Guid organizationId, Guid companyId, DateTimeOffset date)
    {
        DateTimeOffset normalizedDate = NormalizeToUtcMidnight(date);

        await using DatabaseContext context = DatabaseContext.GenerateContext(_databaseSettings.ConnectionString);
        return await context.CompanyHolidays.AnyAsync(h =>
            h.OrganizationId == organizationId && h.CompanyId == companyId && h.Date == normalizedDate);
    }

    public async Task Add(CompanyHoliday holiday)
    {
        await using DatabaseContext context = DatabaseContext.GenerateContext(_databaseSettings.ConnectionString);
        context.CompanyHolidays.Add(holiday);
        await context.SaveChangesAsync();
    }

    public async Task AddRange(List<CompanyHoliday> holidays)
    {
        if (holidays.Count == 0)
            return;

        await using DatabaseContext context = DatabaseContext.GenerateContext(_databaseSettings.ConnectionString);
        context.CompanyHolidays.AddRange(holidays);
        await context.SaveChangesAsync();
    }

    public async Task Delete(CompanyHoliday holiday)
    {
        await using DatabaseContext context = DatabaseContext.GenerateContext(_databaseSettings.ConnectionString);
        context.CompanyHolidays.Remove(holiday);
        await context.SaveChangesAsync();
    }

    public async Task<List<CompanyHoliday>> GetForCompaniesInRange(
        Guid organizationId, List<Guid> companyIds, DateTimeOffset rangeFrom, DateTimeOffset rangeTo)
    {
        if (companyIds.Count == 0)
            return new List<CompanyHoliday>();

        DateTimeOffset normalizedFrom = NormalizeToUtcMidnight(rangeFrom);
        DateTimeOffset normalizedTo = NormalizeToUtcMidnight(rangeTo);

        await using DatabaseContext context = DatabaseContext.GenerateContext(_databaseSettings.ConnectionString);
        return await context.CompanyHolidays
            .Where(h =>
                h.OrganizationId == organizationId &&
                companyIds.Contains(h.CompanyId) &&
                h.Date >= normalizedFrom && h.Date <= normalizedTo)
            .ToListAsync();
    }

    /// <summary>Normalizira na ponoć UTC (offset nula) — CompanyHoliday.Date se UVIJEK sprema u tom obliku
    /// (vidi CompanyHolidayService), pa upiti i usporedbe moraju koristiti isti oblik bez obzira na offset
    /// koji je pozivatelj proslijedio (izbjegava DateTimeOffset.Date implicit-conversion zamku ovisnu o lokalnoj
    /// vremenskoj zoni servera).</summary>
    private static DateTimeOffset NormalizeToUtcMidnight(DateTimeOffset value)
    {
        return new DateTimeOffset(value.Date, TimeSpan.Zero);
    }
}
