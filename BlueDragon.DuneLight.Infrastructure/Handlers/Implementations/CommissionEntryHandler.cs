using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using BlueDragon.DuneLight.Core.DTOs.Commissions;
using BlueDragon.DuneLight.Core.Enums;
using BlueDragon.DuneLight.Infrastructure.Domain.Contexts;
using BlueDragon.DuneLight.Infrastructure.Domain.Models.Commissions;
using BlueDragon.DuneLight.Infrastructure.Domain.Models.Employees;
using BlueDragon.DuneLight.Infrastructure.Domain.Settings;
using BlueDragon.DuneLight.Infrastructure.Handlers.Interfaces;
using BlueDragon.DuneLight.Infrastructure.UnitOfWork;
using Microsoft.EntityFrameworkCore;

namespace BlueDragon.DuneLight.Infrastructure.Handlers.Implementations;

public class CommissionEntryHandler : ICommissionEntryHandler
{
    private readonly DatabaseSettings _databaseSettings;

    public CommissionEntryHandler(DatabaseSettings databaseSettings)
    {
        _databaseSettings = databaseSettings;
    }

    public async Task Add(IUnitOfWork uow, CommissionEntry entry)
    {
        uow.Context.CommissionEntries.Add(entry);
        await uow.Context.SaveChangesAsync();
    }

    public async Task<(List<CommissionEntry> Items, int TotalCount)> GetPaged(Guid organizationId, CommissionEntryQuery query)
    {
        await using DatabaseContext context = DatabaseContext.GenerateContext(_databaseSettings.ConnectionString);

        IQueryable<CommissionEntry> filtered = Filter(context, organizationId, query.EmployeeId, query.CompanyId, query.From, query.To);

        int totalCount = await filtered.CountAsync();

        List<CommissionEntry> items = await filtered
            .Include(e => e.Employee)
            .Include(e => e.Company)
            .OrderByDescending(e => e.EarnedAt)
            .Skip((query.Page - 1) * query.PageSize)
            .Take(query.PageSize)
            .ToListAsync();

        return (items, totalCount);
    }

    /// <summary>Agregira U MEMORIJI nakon filtriranog dohvata (ne preko SQL GROUP BY) — namjerno, jer je
    /// provizija niskog volumena (vidi spec section 58) i ovo izbjegava krhkost EF Core prijevoda ugniježđenih
    /// uvjetnih Sum() izraza preko GroupBy s navigation-property ključem.</summary>
    public async Task<List<EmployeeCommissionSummaryDto>> GetSummaryByEmployee(Guid organizationId, CommissionSummaryQuery query)
    {
        await using DatabaseContext context = DatabaseContext.GenerateContext(_databaseSettings.ConnectionString);

        List<CommissionEntry> entries = await Filter(context, organizationId, query.EmployeeId, query.CompanyId, query.From, query.To)
            .Include(e => e.Employee)
            .ToListAsync();

        List<EmployeeCommissionSummaryDto> summaries = entries
            .GroupBy(e => e.EmployeeId)
            .Select(g =>
            {
                decimal earned = g.Where(e => e.Status == CommissionEntryStatus.Earned).Sum(e => e.CommissionAmount);
                decimal reversed = g.Where(e => e.Status == CommissionEntryStatus.Reversed).Sum(e => e.CommissionAmount);
                Employee employee = g.First().Employee;
                return new EmployeeCommissionSummaryDto
                {
                    EmployeeId = g.Key,
                    EmployeeName = employee != null ? $"{employee.FirstName} {employee.LastName}" : null,
                    EarnedAmount = earned,
                    ReversedAmount = reversed,
                    NetAmount = earned - reversed,
                    EntryCount = g.Count()
                };
            })
            .OrderBy(s => s.EmployeeName)
            .ToList();

        return summaries;
    }

    private static IQueryable<CommissionEntry> Filter(
        DatabaseContext context, Guid organizationId, Guid? employeeId, Guid? companyId, DateTimeOffset from, DateTimeOffset to)
    {
        IQueryable<CommissionEntry> query = context.CommissionEntries
            .Where(e => e.OrganizationId == organizationId && e.EarnedAt >= from && e.EarnedAt < to);

        if (employeeId.HasValue)
            query = query.Where(e => e.EmployeeId == employeeId.Value);
        if (companyId.HasValue)
            query = query.Where(e => e.CompanyId == companyId.Value);

        return query;
    }
}
