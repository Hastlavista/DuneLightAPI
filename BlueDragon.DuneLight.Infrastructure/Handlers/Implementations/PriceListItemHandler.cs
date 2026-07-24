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

public class PriceListItemHandler : IPriceListItemHandler
{
    private readonly DatabaseSettings _databaseSettings;

    public PriceListItemHandler(DatabaseSettings databaseSettings)
    {
        _databaseSettings = databaseSettings;
    }

    public async Task<(List<PriceListItem> Items, int TotalCount)> GetPaged(
        Guid organizationId, PagedRequest request, Guid? locationId, PricingSubjectType? subjectType)
    {
        await using DatabaseContext context = DatabaseContext.GenerateContext(_databaseSettings.ConnectionString);

        IQueryable<PriceListItem> query = context.PriceListItems
            .Include(p => p.Service)
            .Include(p => p.Package)
            .Include(p => p.Location)
            .Where(p => p.OrganizationId == organizationId);

        if (locationId.HasValue)
            query = query.Where(p => p.LocationId == locationId.Value);

        if (subjectType == PricingSubjectType.Service)
            query = query.Where(p => p.ServiceId != null);
        else if (subjectType == PricingSubjectType.Package)
            query = query.Where(p => p.PackageId != null);

        if (request.IsActive.HasValue)
            query = query.Where(p => p.IsActive == request.IsActive.Value);

        if (!string.IsNullOrWhiteSpace(request.Search))
            query = query.Where(p =>
                (p.Service != null && EF.Functions.ILike(p.Service.Name, $"%{request.Search}%")) ||
                (p.Package != null && EF.Functions.ILike(p.Package.Name, $"%{request.Search}%")));

        int totalCount = await query.CountAsync();

        List<PriceListItem> items = await query
            .OrderByDescending(p => p.ValidFrom)
            .Skip((request.Page - 1) * request.PageSize)
            .Take(request.PageSize)
            .ToListAsync();

        return (items, totalCount);
    }

    public async Task<PriceListItem> GetById(Guid organizationId, Guid id)
    {
        await using DatabaseContext context = DatabaseContext.GenerateContext(_databaseSettings.ConnectionString);
        return await context.PriceListItems
            .Include(p => p.Service)
            .Include(p => p.Package)
            .Include(p => p.Location)
            .SingleOrDefaultAsync(p => p.OrganizationId == organizationId && p.Id == id);
    }

    public async Task Add(PriceListItem item)
    {
        await using DatabaseContext context = DatabaseContext.GenerateContext(_databaseSettings.ConnectionString);
        context.PriceListItems.Add(item);
        await context.SaveChangesAsync();
    }

    public async Task Update(PriceListItem item)
    {
        await using DatabaseContext context = DatabaseContext.GenerateContext(_databaseSettings.ConnectionString);
        context.PriceListItems.Update(item);
        await context.SaveChangesAsync();
    }

    public async Task Delete(PriceListItem item)
    {
        await using DatabaseContext context = DatabaseContext.GenerateContext(_databaseSettings.ConnectionString);
        context.PriceListItems.Remove(item);
        await context.SaveChangesAsync();
    }

    public async Task AddHistory(PriceListItemHistory history)
    {
        await using DatabaseContext context = DatabaseContext.GenerateContext(_databaseSettings.ConnectionString);
        context.PriceListItemHistory.Add(history);
        await context.SaveChangesAsync();
    }

    public async Task<bool> HasHistory(Guid priceListItemId)
    {
        await using DatabaseContext context = DatabaseContext.GenerateContext(_databaseSettings.ConnectionString);
        return await context.PriceListItemHistory.AnyAsync(h => h.PriceListItemId == priceListItemId);
    }

    public async Task<List<PriceListItem>> GetActiveForExactLocation(
        Guid organizationId, PricingSubjectType subjectType, Guid subjectId, Guid? locationId, Guid? excludeId)
    {
        await using DatabaseContext context = DatabaseContext.GenerateContext(_databaseSettings.ConnectionString);

        IQueryable<PriceListItem> query = context.PriceListItems.Where(p =>
            p.OrganizationId == organizationId &&
            p.IsActive &&
            p.LocationId == locationId &&
            (excludeId == null || p.Id != excludeId));

        query = subjectType == PricingSubjectType.Service
            ? query.Where(p => p.ServiceId == subjectId)
            : query.Where(p => p.PackageId == subjectId);

        return await query.ToListAsync();
    }

    public async Task<List<PriceListItem>> GetActiveCandidates(
        Guid organizationId, PricingSubjectType subjectType, Guid subjectId, Guid? locationId)
    {
        await using DatabaseContext context = DatabaseContext.GenerateContext(_databaseSettings.ConnectionString);

        IQueryable<PriceListItem> query = context.PriceListItems.Where(p =>
            p.OrganizationId == organizationId &&
            p.IsActive &&
            (p.LocationId == locationId || p.LocationId == null));

        query = subjectType == PricingSubjectType.Service
            ? query.Where(p => p.ServiceId == subjectId)
            : query.Where(p => p.PackageId == subjectId);

        return await query.ToListAsync();
    }

    public async Task<List<PriceListItem>> GetActiveForLocation(Guid organizationId, Guid? locationId, DateTimeOffset date)
    {
        await using DatabaseContext context = DatabaseContext.GenerateContext(_databaseSettings.ConnectionString);

        return await context.PriceListItems
            .Where(p =>
                p.OrganizationId == organizationId &&
                p.IsActive &&
                (p.LocationId == locationId || p.LocationId == null) &&
                p.ValidFrom <= date &&
                (p.ValidTo == null || p.ValidTo >= date))
            .ToListAsync();
    }

    public async Task<bool> IsServiceReferenced(Guid organizationId, Guid serviceId)
    {
        await using DatabaseContext context = DatabaseContext.GenerateContext(_databaseSettings.ConnectionString);
        return await context.PriceListItems.AnyAsync(p => p.OrganizationId == organizationId && p.ServiceId == serviceId);
    }

    public async Task<bool> IsPackageReferenced(Guid organizationId, Guid packageId)
    {
        await using DatabaseContext context = DatabaseContext.GenerateContext(_databaseSettings.ConnectionString);
        return await context.PriceListItems.AnyAsync(p => p.OrganizationId == organizationId && p.PackageId == packageId);
    }
}
