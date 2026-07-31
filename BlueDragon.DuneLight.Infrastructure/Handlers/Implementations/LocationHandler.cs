using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using BlueDragon.DuneLight.Core.Shared;
using BlueDragon.DuneLight.Infrastructure.Domain.Contexts;
using BlueDragon.DuneLight.Infrastructure.Domain.Models.Catalog;
using BlueDragon.DuneLight.Infrastructure.Domain.Settings;
using BlueDragon.DuneLight.Infrastructure.Handlers.Interfaces;
using Microsoft.EntityFrameworkCore;

namespace BlueDragon.DuneLight.Infrastructure.Handlers.Implementations;

public class LocationHandler : ILocationHandler
{
    private readonly DatabaseSettings _databaseSettings;

    public LocationHandler(DatabaseSettings databaseSettings)
    {
        _databaseSettings = databaseSettings;
    }

    public async Task<(List<Location> Items, int TotalCount)> GetPaged(Guid organizationId, PagedRequest request)
    {
        await using DatabaseContext context = DatabaseContext.GenerateContext(_databaseSettings.ConnectionString);
        IQueryable<Location> query = context.Locations.Where(l => l.OrganizationId == organizationId);
        if (!string.IsNullOrWhiteSpace(request.Search))
            query = query.Where(l => EF.Functions.ILike(l.Name, $"%{request.Search}%"));

        if (request.IsActive.HasValue)
            query = query.Where(l => l.IsActive == request.IsActive.Value);

        int totalCount = await query.CountAsync();

        List<Location> items = await query
            .OrderBy(l => l.SortOrder)
            .ThenBy(l => l.Name)
            .Skip((request.Page - 1) * request.PageSize)
            .Take(request.PageSize)
            .ToListAsync();

        return (items, totalCount);
    }

    public async Task<Location> GetById(Guid organizationId, Guid id)
    {
        await using DatabaseContext context = DatabaseContext.GenerateContext(_databaseSettings.ConnectionString);
        return await context.Locations.SingleOrDefaultAsync(l => l.OrganizationId == organizationId && l.Id == id);
    }

    public async Task<List<Location>> GetByIds(Guid organizationId, List<Guid> ids)
    {
        await using DatabaseContext context = DatabaseContext.GenerateContext(_databaseSettings.ConnectionString);
        return await context.Locations
            .Where(l => l.OrganizationId == organizationId && l.Id.HasValue && ids.Contains(l.Id.Value))
            .ToListAsync();
    }

    public async Task Add(Location location)
    {
        await using DatabaseContext context = DatabaseContext.GenerateContext(_databaseSettings.ConnectionString);
        context.Locations.Add(location);
        await context.SaveChangesAsync();
    }

    public async Task Update(Location location)
    {
        await using DatabaseContext context = DatabaseContext.GenerateContext(_databaseSettings.ConnectionString);
        context.Locations.Update(location);
        await context.SaveChangesAsync();
    }

    public async Task Delete(Location location)
    {
        await using DatabaseContext context = DatabaseContext.GenerateContext(_databaseSettings.ConnectionString);
        context.Locations.Remove(location);
        await context.SaveChangesAsync();
    }

    public async Task<int> CountActive(Guid organizationId)
    {
        await using DatabaseContext context = DatabaseContext.GenerateContext(_databaseSettings.ConnectionString);
        return await context.Locations.CountAsync(l => l.OrganizationId == organizationId && l.IsActive);
    }

    public async Task<bool> IsReferenced(Guid organizationId, Guid id)
    {
        await using DatabaseContext context = DatabaseContext.GenerateContext(_databaseSettings.ConnectionString);
        return await context.PriceListItems.AnyAsync(p => p.OrganizationId == organizationId && p.LocationId == id);
    }
}
