using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using BlueDragon.DuneLight.Core.Shared;
using BlueDragon.DuneLight.Infrastructure.Domain.Contexts;
using BlueDragon.DuneLight.Infrastructure.Domain.Models.Roster;
using BlueDragon.DuneLight.Infrastructure.Domain.Settings;
using BlueDragon.DuneLight.Infrastructure.Handlers.Interfaces;
using Microsoft.EntityFrameworkCore;

namespace BlueDragon.DuneLight.Infrastructure.Handlers.Implementations;

public class RosterEntryHandler : IRosterEntryHandler
{
    private readonly DatabaseSettings _databaseSettings;

    public RosterEntryHandler(DatabaseSettings databaseSettings)
    {
        _databaseSettings = databaseSettings;
    }

    public async Task<(List<RosterEntry> Items, int TotalCount)> GetPaged(
        Guid organizationId, PagedRequest request, Guid? employeeId, Guid? rosterTypeId, DateTimeOffset? from, DateTimeOffset? to)
    {
        await using DatabaseContext context = DatabaseContext.GenerateContext(_databaseSettings.ConnectionString);

        IQueryable<RosterEntry> query = context.RosterEntries
            .Include(e => e.Employee)
            .Include(e => e.RosterType)
            .Where(e => e.OrganizationId == organizationId);

        if (employeeId.HasValue)
            query = query.Where(e => e.EmployeeId == employeeId.Value);

        if (rosterTypeId.HasValue)
            query = query.Where(e => e.RosterTypeId == rosterTypeId.Value);

        if (from.HasValue)
            query = query.Where(e => e.DateTo == null ? e.DateFrom >= from.Value : e.DateTo >= from.Value);

        if (to.HasValue)
            query = query.Where(e => e.DateFrom <= to.Value);

        int totalCount = await query.CountAsync();

        List<RosterEntry> items = await query
            .OrderByDescending(e => e.DateFrom)
            .Skip((request.Page - 1) * request.PageSize)
            .Take(request.PageSize)
            .ToListAsync();

        return (items, totalCount);
    }

    public async Task<RosterEntry> GetById(Guid organizationId, Guid id)
    {
        await using DatabaseContext context = DatabaseContext.GenerateContext(_databaseSettings.ConnectionString);
        return await context.RosterEntries
            .Include(e => e.Employee)
            .Include(e => e.RosterType)
            .SingleOrDefaultAsync(e => e.OrganizationId == organizationId && e.Id == id);
    }

    public async Task<RosterEntry> GetByIdLight(Guid organizationId, Guid id)
    {
        await using DatabaseContext context = DatabaseContext.GenerateContext(_databaseSettings.ConnectionString);
        return await context.RosterEntries.SingleOrDefaultAsync(e => e.OrganizationId == organizationId && e.Id == id);
    }

    public async Task<List<RosterEntry>> GetOverlapCandidates(
        Guid organizationId, Guid employeeId, DateTimeOffset windowFrom, DateTimeOffset windowTo, Guid? excludeId)
    {
        await using DatabaseContext context = DatabaseContext.GenerateContext(_databaseSettings.ConnectionString);
        return await context.RosterEntries
            .Include(e => e.RosterType)
            .Where(e =>
                e.OrganizationId == organizationId &&
                e.EmployeeId == employeeId &&
                e.DateFrom <= windowTo &&
                (e.DateTo == null || e.DateTo >= windowFrom) &&
                (excludeId == null || e.Id != excludeId))
            .ToListAsync();
    }

    public async Task<List<RosterEntry>> GetForPeriod(
        Guid organizationId, List<Guid> employeeIds, DateTimeOffset periodFrom, DateTimeOffset periodTo)
    {
        await using DatabaseContext context = DatabaseContext.GenerateContext(_databaseSettings.ConnectionString);
        return await context.RosterEntries
            .Include(e => e.RosterType)
            .Include(e => e.Employee)
            .Where(e =>
                e.OrganizationId == organizationId &&
                employeeIds.Contains(e.EmployeeId) &&
                e.DateFrom <= periodTo &&
                (e.DateTo == null || e.DateTo >= periodFrom))
            .OrderBy(e => e.DateFrom)
            .ToListAsync();
    }

    public async Task Add(RosterEntry entry)
    {
        await using DatabaseContext context = DatabaseContext.GenerateContext(_databaseSettings.ConnectionString);
        context.RosterEntries.Add(entry);
        await context.SaveChangesAsync();
    }

    public async Task Update(RosterEntry entry)
    {
        await using DatabaseContext context = DatabaseContext.GenerateContext(_databaseSettings.ConnectionString);
        context.RosterEntries.Update(entry);
        await context.SaveChangesAsync();
    }

    public async Task Delete(RosterEntry entry)
    {
        await using DatabaseContext context = DatabaseContext.GenerateContext(_databaseSettings.ConnectionString);
        context.RosterEntries.Remove(entry);
        await context.SaveChangesAsync();
    }
}
