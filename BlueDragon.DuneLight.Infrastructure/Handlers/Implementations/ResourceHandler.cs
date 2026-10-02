using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using BlueDragon.DuneLight.Core.Shared;
using BlueDragon.DuneLight.Infrastructure.Domain.Contexts;
using BlueDragon.DuneLight.Infrastructure.Domain.Models.Catalog;
using BlueDragon.DuneLight.Infrastructure.Domain.Settings;
using BlueDragon.DuneLight.Infrastructure.Handlers.Interfaces;
using BlueDragon.DuneLight.Infrastructure.UnitOfWork;
using Microsoft.EntityFrameworkCore;

namespace BlueDragon.DuneLight.Infrastructure.Handlers.Implementations;

/// <summary>Isti obrazac kao RoomHandler — svaki upit je filtriran po OrganizationId.</summary>
public class ResourceHandler : IResourceHandler
{
    private readonly DatabaseSettings _databaseSettings;

    public ResourceHandler(DatabaseSettings databaseSettings)
    {
        _databaseSettings = databaseSettings;
    }

    private static IQueryable<Resource> IncludeGraph(IQueryable<Resource> query)
    {
        return query.Include(r => r.Company);
    }

    public async Task<(List<Resource> Items, int TotalCount)> GetPaged(Guid organizationId, Guid? companyId, PagedRequest request)
    {
        await using DatabaseContext context = DatabaseContext.GenerateContext(_databaseSettings.ConnectionString);
        IQueryable<Resource> query = IncludeGraph(context.Resources).Where(r => r.OrganizationId == organizationId);

        if (companyId.HasValue)
            query = query.Where(r => r.CompanyId == companyId.Value);

        if (!string.IsNullOrWhiteSpace(request.Search))
            query = query.Where(r => EF.Functions.ILike(r.Name, $"%{request.Search}%"));

        if (request.IsActive.HasValue)
            query = query.Where(r => r.IsActive == request.IsActive.Value);

        int totalCount = await query.CountAsync();

        List<Resource> items = await query
            .OrderBy(r => r.SortOrder)
            .ThenBy(r => r.Name)
            .Skip((request.Page - 1) * request.PageSize)
            .Take(request.PageSize)
            .ToListAsync();

        return (items, totalCount);
    }

    public async Task<Resource> GetById(Guid organizationId, Guid id)
    {
        await using DatabaseContext context = DatabaseContext.GenerateContext(_databaseSettings.ConnectionString);
        return await IncludeGraph(context.Resources).SingleOrDefaultAsync(r => r.OrganizationId == organizationId && r.Id == id);
    }

    public async Task Add(Resource resource)
    {
        await using DatabaseContext context = DatabaseContext.GenerateContext(_databaseSettings.ConnectionString);
        context.Resources.Add(resource);
        await context.SaveChangesAsync();
    }

    public async Task Update(Resource resource)
    {
        await using DatabaseContext context = DatabaseContext.GenerateContext(_databaseSettings.ConnectionString);
        context.Resources.Update(resource);
        context.Entry(resource).Property(x => x.Capacity).IsModified = false;
        await context.SaveChangesAsync();
    }

    public Task<Resource> GetForCapacityChange(IUnitOfWork uow, Guid organizationId, Guid id) =>
        uow.Context.Resources.SingleOrDefaultAsync(x => x.OrganizationId == organizationId && x.Id == id);

    public async Task Delete(Resource resource)
    {
        await using DatabaseContext context = DatabaseContext.GenerateContext(_databaseSettings.ConnectionString);
        context.Resources.Remove(resource);
        await context.SaveChangesAsync();
    }

    public async Task<bool> NameExistsAmongActive(Guid organizationId, Guid companyId, string name, Guid? excludeId)
    {
        string normalized = Normalize(name);

        await using DatabaseContext context = DatabaseContext.GenerateContext(_databaseSettings.ConnectionString);
        return await context.Resources.AnyAsync(r =>
            r.OrganizationId == organizationId &&
            r.CompanyId == companyId &&
            r.IsActive &&
            r.Name.Trim().ToLower() == normalized &&
            (excludeId == null || r.Id != excludeId));
    }

    public async Task<bool> IsReferenced(Guid organizationId, Guid id)
    {
        await using DatabaseContext context = DatabaseContext.GenerateContext(_databaseSettings.ConnectionString);
        return await context.AppointmentSegmentResources
            .AnyAsync(r => r.ResourceId == id && r.Segment.OrganizationId == organizationId);
    }

    private static string Normalize(string name)
    {
        return name?.Trim().ToLowerInvariant() ?? string.Empty;
    }
}
