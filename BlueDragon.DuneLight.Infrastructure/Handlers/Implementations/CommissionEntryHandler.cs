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

    public Task<List<CommissionEntry>> GetActiveForParticipation(IUnitOfWork uow, Guid organizationId, Guid participationId)
    {
        return uow.Context.CommissionEntries
            .Where(e => e.OrganizationId == organizationId && e.BookingSegmentParticipationId == participationId &&
                        e.SourceType == CommissionSourceType.IndividualService && e.Status == CommissionEntryStatus.Earned)
            .OrderBy(e => e.EmployeeId)
            .ToListAsync();
    }

    public Task<List<CommissionEntry>> GetForSource(IUnitOfWork uow, Guid organizationId, CommissionSourceType sourceType, Guid sourceId)
    {
        IQueryable<CommissionEntry> query = uow.Context.CommissionEntries
            .Where(e => e.OrganizationId == organizationId && e.SourceType == sourceType);
        query = sourceType switch
        {
            CommissionSourceType.PolicyFee => query.Where(e => e.BookingSegmentParticipationId == sourceId),
            CommissionSourceType.MembershipSale => query.Where(e => e.ClientMembershipId == sourceId),
            CommissionSourceType.ProductSale or CommissionSourceType.PackageSale => query.Where(e => e.CheckoutItemId == sourceId),
            _ => throw new ArgumentOutOfRangeException(nameof(sourceType))
        };
        return query.OrderBy(e => e.SourceVersion).ThenBy(e => e.EmployeeId).ToListAsync();
    }

    public Task<CommissionEntry> GetForUpdate(IUnitOfWork uow, Guid organizationId, Guid id)
    {
        return uow.Context.CommissionEntries
            .FromSqlInterpolated($"SELECT * FROM dunelight.commission_entries WHERE id = {id} AND organization_id = {organizationId} FOR UPDATE")
            .SingleOrDefaultAsync();
    }

    public async Task Update(IUnitOfWork uow, CommissionEntry entry)
    {
        uow.Context.CommissionEntries.Update(entry);
        await uow.Context.SaveChangesAsync();
    }

    public async Task<CommissionEntry> GetById(Guid organizationId, Guid id)
    {
        await using DatabaseContext context = DatabaseContext.GenerateContext(_databaseSettings.ConnectionString);
        return await context.CommissionEntries
            .Include(e => e.Employee)
            .Include(e => e.Company)
            .AsNoTracking()
            .SingleOrDefaultAsync(e => e.OrganizationId == organizationId && e.Id == id);
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
            .ThenBy(e => e.Id)
            .Skip((query.Page - 1) * query.PageSize)
            .Take(query.PageSize)
            .ToListAsync();

        return (items, totalCount);
    }

    /// <summary>Agregira U MEMORIJI nakon filtriranog dohvata (ne preko SQL GROUP BY) — namjerno, jer je
    /// provizija niskog volumena (vidi spec section 58) i ovo izbjegava krhkost EF Core prijevoda ugniježđenih
    /// uvjetnih Sum() izraza preko GroupBy s navigation-property ključem. P2 (2F): po događajima — zarada se broji u razdoblju
    /// nastanka (bez obzira na kasniji storno), storno u razdoblju storna (bez obzira kad je provizija nastala).</summary>
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
                decimal earned = g.Where(e => e.EarnedAt >= query.From && e.EarnedAt < query.To).Sum(e => e.CommissionAmount);
                decimal reversed = g.Where(e => e.ReversedAt >= query.From && e.ReversedAt < query.To).Sum(e => e.CommissionAmount);
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
            .Where(e => e.OrganizationId == organizationId &&
                        ((e.EarnedAt >= from && e.EarnedAt < to) || (e.ReversedAt >= from && e.ReversedAt < to)));

        if (employeeId.HasValue)
            query = query.Where(e => e.EmployeeId == employeeId.Value);
        if (companyId.HasValue)
            query = query.Where(e => e.CompanyId == companyId.Value);

        return query;
    }
}
