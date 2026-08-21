using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using BlueDragon.DuneLight.Core.Enums;
using BlueDragon.DuneLight.Core.Shared;
using BlueDragon.DuneLight.Infrastructure.Domain.Contexts;
using BlueDragon.DuneLight.Infrastructure.Domain.Models.Catalog;
using BlueDragon.DuneLight.Infrastructure.Domain.Settings;
using BlueDragon.DuneLight.Infrastructure.Handlers.Interfaces;
using Microsoft.EntityFrameworkCore;

namespace BlueDragon.DuneLight.Infrastructure.Handlers.Implementations;

public class ServiceHandler : IServiceHandler
{
    private readonly DatabaseSettings _databaseSettings;

    public ServiceHandler(DatabaseSettings databaseSettings)
    {
        _databaseSettings = databaseSettings;
    }

    public async Task<(List<Service> Items, int TotalCount)> GetPaged(
        Guid organizationId, PagedRequest request, ServiceExecutionMode? executionMode)
    {
        await using DatabaseContext context = DatabaseContext.GenerateContext(_databaseSettings.ConnectionString);

        IQueryable<Service> query = context.Services
            .Where(s => s.OrganizationId == organizationId);

        if (!string.IsNullOrWhiteSpace(request.Search))
            query = query.Where(s => EF.Functions.ILike(s.Name, $"%{request.Search}%"));

        if (request.IsActive.HasValue)
            query = query.Where(s => s.IsActive == request.IsActive.Value);

        if (executionMode.HasValue)
            query = query.Where(s => s.ExecutionMode == executionMode.Value);

        int totalCount = await query.CountAsync();

        List<Service> items = await query
            .OrderBy(s => s.SortOrder)
            .ThenBy(s => s.Name)
            .Skip((request.Page - 1) * request.PageSize)
            .Take(request.PageSize)
            .ToListAsync();

        return (items, totalCount);
    }

    public async Task<Service> GetById(Guid organizationId, Guid id)
    {
        await using DatabaseContext context = DatabaseContext.GenerateContext(_databaseSettings.ConnectionString);
        return await context.Services
            .SingleOrDefaultAsync(s => s.OrganizationId == organizationId && s.Id == id);
    }

    public async Task<List<Service>> GetByIds(Guid organizationId, List<Guid> ids)
    {
        await using DatabaseContext context = DatabaseContext.GenerateContext(_databaseSettings.ConnectionString);
        return await context.Services
            .Where(s => s.OrganizationId == organizationId && s.Id.HasValue && ids.Contains(s.Id.Value))
            .ToListAsync();
    }

    public async Task<List<Service>> GetAllActive(Guid organizationId)
    {
        await using DatabaseContext context = DatabaseContext.GenerateContext(_databaseSettings.ConnectionString);
        return await context.Services
            .Where(s => s.OrganizationId == organizationId && s.IsActive)
            .ToListAsync();
    }

    public async Task<bool> NameExistsAmongActive(Guid organizationId, string name, Guid? excludeId)
    {
        await using DatabaseContext context = DatabaseContext.GenerateContext(_databaseSettings.ConnectionString);
        return await context.Services.AnyAsync(s =>
            s.OrganizationId == organizationId &&
            s.IsActive &&
            s.Name == name &&
            (excludeId == null || s.Id != excludeId));
    }

    public async Task Add(Service service)
    {
        await using DatabaseContext context = DatabaseContext.GenerateContext(_databaseSettings.ConnectionString);
        context.Services.Add(service);
        await context.SaveChangesAsync();
    }

    public async Task Update(Service service)
    {
        await using DatabaseContext context = DatabaseContext.GenerateContext(_databaseSettings.ConnectionString);
        context.Services.Update(service);
        await context.SaveChangesAsync();
    }

    public async Task Delete(Service service)
    {
        await using DatabaseContext context = DatabaseContext.GenerateContext(_databaseSettings.ConnectionString);
        context.Services.Remove(service);
        await context.SaveChangesAsync();
    }

    public async Task<bool> IsReferenced(Guid organizationId, Guid id)
    {
        await using DatabaseContext context = DatabaseContext.GenerateContext(_databaseSettings.ConnectionString);
        bool inPriceList = await context.PriceListItems.AnyAsync(p => p.OrganizationId == organizationId && p.ServiceId == id);
        if (inPriceList)
            return true;

        return await context.PackageServiceItems.AnyAsync(ps => ps.ServiceId == id && ps.Package.OrganizationId == organizationId);
    }
}
