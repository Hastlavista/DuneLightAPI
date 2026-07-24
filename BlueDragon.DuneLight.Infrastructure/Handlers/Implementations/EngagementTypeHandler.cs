using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using BlueDragon.DuneLight.Core.Shared;
using BlueDragon.DuneLight.Infrastructure.Domain.Contexts;
using BlueDragon.DuneLight.Infrastructure.Domain.Models.Employees;
using BlueDragon.DuneLight.Infrastructure.Domain.Settings;
using BlueDragon.DuneLight.Infrastructure.Handlers.Interfaces;
using Microsoft.EntityFrameworkCore;

namespace BlueDragon.DuneLight.Infrastructure.Handlers.Implementations;

public class EngagementTypeHandler : IEngagementTypeHandler
{
    private readonly DatabaseSettings _databaseSettings;

    public EngagementTypeHandler(DatabaseSettings databaseSettings)
    {
        _databaseSettings = databaseSettings;
    }

    public async Task<(List<EngagementType> Items, int TotalCount)> GetPaged(Guid organizationId, PagedRequest request)
    {
        await using DatabaseContext context = DatabaseContext.GenerateContext(_databaseSettings.ConnectionString);

        IQueryable<EngagementType> query = context.EngagementTypes.Where(e => e.OrganizationId == organizationId);

        if (!string.IsNullOrWhiteSpace(request.Search))
            query = query.Where(e => EF.Functions.ILike(e.Name, $"%{request.Search}%"));

        if (request.IsActive.HasValue)
            query = query.Where(e => e.IsActive == request.IsActive.Value);

        int totalCount = await query.CountAsync();

        List<EngagementType> items = await query
            .OrderBy(e => e.SortOrder)
            .ThenBy(e => e.Name)
            .Skip((request.Page - 1) * request.PageSize)
            .Take(request.PageSize)
            .ToListAsync();

        return (items, totalCount);
    }

    public async Task<EngagementType> GetById(Guid organizationId, Guid id)
    {
        await using DatabaseContext context = DatabaseContext.GenerateContext(_databaseSettings.ConnectionString);
        return await context.EngagementTypes.SingleOrDefaultAsync(e => e.OrganizationId == organizationId && e.Id == id);
    }

    public async Task<bool> NameExistsAmongActive(Guid organizationId, string name, Guid? excludeId)
    {
        await using DatabaseContext context = DatabaseContext.GenerateContext(_databaseSettings.ConnectionString);
        return await context.EngagementTypes.AnyAsync(e =>
            e.OrganizationId == organizationId &&
            e.IsActive &&
            e.Name == name &&
            (excludeId == null || e.Id != excludeId));
    }

    public async Task Add(EngagementType engagementType)
    {
        await using DatabaseContext context = DatabaseContext.GenerateContext(_databaseSettings.ConnectionString);
        context.EngagementTypes.Add(engagementType);
        await context.SaveChangesAsync();
    }

    public async Task Update(EngagementType engagementType)
    {
        await using DatabaseContext context = DatabaseContext.GenerateContext(_databaseSettings.ConnectionString);
        context.EngagementTypes.Update(engagementType);
        await context.SaveChangesAsync();
    }

    public async Task Delete(EngagementType engagementType)
    {
        await using DatabaseContext context = DatabaseContext.GenerateContext(_databaseSettings.ConnectionString);
        context.EngagementTypes.Remove(engagementType);
        await context.SaveChangesAsync();
    }

    public async Task<bool> IsReferenced(Guid organizationId, Guid id)
    {
        await using DatabaseContext context = DatabaseContext.GenerateContext(_databaseSettings.ConnectionString);
        return await context.Employees.AnyAsync(e => e.OrganizationId == organizationId && e.EngagementTypeId == id);
    }
}
