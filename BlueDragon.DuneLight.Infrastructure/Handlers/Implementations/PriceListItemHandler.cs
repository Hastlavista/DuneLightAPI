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
        Guid organizationId, PagedRequest request, Guid? companyId, PricingSubjectType? subjectType)
    {
        await using DatabaseContext context = DatabaseContext.GenerateContext(_databaseSettings.ConnectionString);

        IQueryable<PriceListItem> query = context.PriceListItems
            .Include(p => p.Service)
            .Include(p => p.Package)
            .Include(p => p.Company)
            .Where(p => p.OrganizationId == organizationId);

        if (companyId.HasValue)
            query = query.Where(p => p.CompanyId == companyId.Value);

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
            .Include(p => p.Company)
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

    public async Task<List<PriceListItem>> GetActiveForExactCompany(
        Guid organizationId, PricingSubjectType subjectType, Guid subjectId, Guid? companyId, Guid? excludeId)
    {
        await using DatabaseContext context = DatabaseContext.GenerateContext(_databaseSettings.ConnectionString);

        IQueryable<PriceListItem> query = context.PriceListItems.Where(p =>
            p.OrganizationId == organizationId &&
            p.IsActive &&
            p.CompanyId == companyId &&
            (excludeId == null || p.Id != excludeId));

        query = subjectType == PricingSubjectType.Service
            ? query.Where(p => p.ServiceId == subjectId)
            : query.Where(p => p.PackageId == subjectId);

        return await query.ToListAsync();
    }

    public async Task<List<PriceListItem>> GetActiveCandidates(
        Guid organizationId, PricingSubjectType subjectType, Guid subjectId, Guid? companyId)
    {
        await using DatabaseContext context = DatabaseContext.GenerateContext(_databaseSettings.ConnectionString);

        IQueryable<PriceListItem> query = context.PriceListItems.Where(p =>
            p.OrganizationId == organizationId &&
            p.IsActive &&
            (p.CompanyId == companyId || p.CompanyId == null));

        query = subjectType == PricingSubjectType.Service
            ? query.Where(p => p.ServiceId == subjectId)
            : query.Where(p => p.PackageId == subjectId);

        return await query.ToListAsync();
    }

    public async Task<List<PriceListItem>> GetActiveForCompany(Guid organizationId, Guid? companyId, DateTimeOffset date)
    {
        await using DatabaseContext context = DatabaseContext.GenerateContext(_databaseSettings.ConnectionString);

        return await context.PriceListItems
            .Where(p =>
                p.OrganizationId == organizationId &&
                p.IsActive &&
                (p.CompanyId == companyId || p.CompanyId == null) &&
                p.ValidFrom <= date &&
                (p.ValidTo == null || p.ValidTo >= date))
            .ToListAsync();
    }

}
