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

public class ServiceCategoryHandler : IServiceCategoryHandler
{
    private readonly DatabaseSettings _databaseSettings;

    public ServiceCategoryHandler(DatabaseSettings databaseSettings)
    {
        _databaseSettings = databaseSettings;
    }

    public async Task<(List<ServiceCategory> Items, int TotalCount)> GetPaged(Guid organizationId, PagedRequest request)
    {
        await using DatabaseContext context = DatabaseContext.GenerateContext(_databaseSettings.ConnectionString);

        IQueryable<ServiceCategory> query = context.ServiceCategories.Where(c => c.OrganizationId == organizationId);

        if (!string.IsNullOrWhiteSpace(request.Search))
            query = query.Where(c => EF.Functions.ILike(c.Name, $"%{request.Search}%"));

        if (request.IsActive.HasValue)
            query = query.Where(c => c.IsActive == request.IsActive.Value);

        int totalCount = await query.CountAsync();

        List<ServiceCategory> items = await query
            .OrderBy(c => c.SortOrder)
            .ThenBy(c => c.Name)
            .Skip((request.Page - 1) * request.PageSize)
            .Take(request.PageSize)
            .ToListAsync();

        return (items, totalCount);
    }

    public async Task<ServiceCategory> GetById(Guid organizationId, Guid id)
    {
        await using DatabaseContext context = DatabaseContext.GenerateContext(_databaseSettings.ConnectionString);
        return await context.ServiceCategories.SingleOrDefaultAsync(c => c.OrganizationId == organizationId && c.Id == id);
    }

    public async Task<bool> NameExistsAmongActive(Guid organizationId, string name, Guid? excludeId)
    {
        await using DatabaseContext context = DatabaseContext.GenerateContext(_databaseSettings.ConnectionString);
        return await context.ServiceCategories.AnyAsync(c =>
            c.OrganizationId == organizationId &&
            c.IsActive &&
            c.Name == name &&
            (excludeId == null || c.Id != excludeId));
    }

    public async Task Add(ServiceCategory category)
    {
        await using DatabaseContext context = DatabaseContext.GenerateContext(_databaseSettings.ConnectionString);
        context.ServiceCategories.Add(category);
        await context.SaveChangesAsync();
    }

    public async Task Update(ServiceCategory category)
    {
        await using DatabaseContext context = DatabaseContext.GenerateContext(_databaseSettings.ConnectionString);
        context.ServiceCategories.Update(category);
        await context.SaveChangesAsync();
    }

    public async Task Delete(ServiceCategory category)
    {
        await using DatabaseContext context = DatabaseContext.GenerateContext(_databaseSettings.ConnectionString);
        context.ServiceCategories.Remove(category);
        await context.SaveChangesAsync();
    }
}
