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

public class PackageHandler : IPackageHandler
{
    private readonly DatabaseSettings _databaseSettings;

    public PackageHandler(DatabaseSettings databaseSettings)
    {
        _databaseSettings = databaseSettings;
    }

    public async Task<(List<Package> Items, int TotalCount)> GetPaged(Guid organizationId, PagedRequest request)
    {
        await using DatabaseContext context = DatabaseContext.GenerateContext(_databaseSettings.ConnectionString);
        IQueryable<Package> query = context.Packages
            .Include(p => p.Services).ThenInclude(ps => ps.Service)
            .Where(p => p.OrganizationId == organizationId);

        if (!string.IsNullOrWhiteSpace(request.Search))
            query = query.Where(p => EF.Functions.ILike(p.Name, $"%{request.Search}%"));

        if (request.IsActive.HasValue)
            query = query.Where(p => p.IsActive == request.IsActive.Value);

        int totalCount = await query.CountAsync();

        List<Package> items = await query
            .OrderBy(p => p.SortOrder)
            .ThenBy(p => p.Name)
            .Skip((request.Page - 1) * request.PageSize)
            .Take(request.PageSize)
            .ToListAsync();

        return (items, totalCount);
    }

    public async Task<Package> GetById(Guid organizationId, Guid id)
    {
        await using DatabaseContext context = DatabaseContext.GenerateContext(_databaseSettings.ConnectionString);
        return await context.Packages
            .Include(p => p.Services).ThenInclude(ps => ps.Service)
            .SingleOrDefaultAsync(p => p.OrganizationId == organizationId && p.Id == id);
    }

    public async Task<List<Package>> GetAllActive(Guid organizationId)
    {
        await using DatabaseContext context = DatabaseContext.GenerateContext(_databaseSettings.ConnectionString);
        return await context.Packages
            .Where(p => p.OrganizationId == organizationId && p.IsActive)
            .ToListAsync();
    }

    public async Task Add(Package package)
    {
        await using DatabaseContext context = DatabaseContext.GenerateContext(_databaseSettings.ConnectionString);
        context.Packages.Add(package);
        await context.SaveChangesAsync();
    }

    public async Task Update(Package package, List<PackageServiceItem> newServiceItems)
    {
        await using DatabaseContext context = DatabaseContext.GenerateContext(_databaseSettings.ConnectionString);

        Package trackedPackage = await context.Packages.SingleAsync(p => p.Id == package.Id && p.OrganizationId == package.OrganizationId);
        trackedPackage.Name = package.Name;
        trackedPackage.Description = package.Description;
        trackedPackage.EntryMode = package.EntryMode;
        trackedPackage.TotalEntryCount = package.TotalEntryCount;
        trackedPackage.ValidityType = package.ValidityType;
        trackedPackage.ValidityDays = package.ValidityDays;
        trackedPackage.ValidityFixedDate = package.ValidityFixedDate;
        trackedPackage.DefaultPrice = package.DefaultPrice;
        trackedPackage.IsActive = package.IsActive;
        trackedPackage.SortOrder = package.SortOrder;
        trackedPackage.UpdatedAt = package.UpdatedAt;
        trackedPackage.UpdatedBy = package.UpdatedBy;

        // Reconcile in place rather than delete-all/insert-all: recreating a row for a service
        // that didn't change would delete and re-insert the same (package_id, service_id) pair
        // in one batch, which can violate ux_package_services_package_service depending on
        // statement ordering within the batch.
        List<PackageServiceItem> existingItems = await context.PackageServiceItems
            .Where(ps => ps.PackageId == package.Id)
            .ToListAsync();
        Dictionary<Guid, PackageServiceItem> existingByServiceId = existingItems.ToDictionary(ps => ps.ServiceId);
        HashSet<Guid> newServiceIds = newServiceItems.Select(s => s.ServiceId).ToHashSet();

        foreach (PackageServiceItem existing in existingItems)
            if (!newServiceIds.Contains(existing.ServiceId))
                context.PackageServiceItems.Remove(existing);

        foreach (PackageServiceItem item in newServiceItems)
        {
            if (existingByServiceId.TryGetValue(item.ServiceId, out PackageServiceItem existing))
                existing.EntryCount = item.EntryCount;
            else
            {
                item.PackageId = package.Id.GetValueOrDefault();
                context.PackageServiceItems.Add(item);
            }
        }

        await context.SaveChangesAsync();
    }

    public async Task Delete(Package package)
    {
        await using DatabaseContext context = DatabaseContext.GenerateContext(_databaseSettings.ConnectionString);
        context.Packages.Remove(package);
        await context.SaveChangesAsync();
    }

    /// <summary>PriceListItems i ClientPackages su jedine "prave" reference koje blokiraju trajno brisanje —
    /// PackageServiceItems su konfiguracijska djeca (kaskadno se brišu) i namjerno se ovdje ne broje, isti
    /// obrazac kao ServiceHandler.IsReferenced.</summary>
    public async Task<bool> IsReferenced(Guid organizationId, Guid id)
    {
        await using DatabaseContext context = DatabaseContext.GenerateContext(_databaseSettings.ConnectionString);

        IQueryable<int> priceListItems = context.PriceListItems
            .Where(p => p.OrganizationId == organizationId && p.PackageId == id)
            .Select(p => 1);

        IQueryable<int> clientPackages = context.ClientPackages
            .Where(cp => cp.OrganizationId == organizationId && cp.PackageId == id)
            .Select(cp => 1);

        IQueryable<int> commissionRules = context.CommissionRules
            .Where(r => r.OrganizationId == organizationId && r.PackageId == id)
            .Select(r => 1);

        return await priceListItems.Union(clientPackages).Union(commissionRules).AnyAsync();
    }

    public async Task<bool> NameExistsAmongActive(Guid organizationId, string name, Guid? excludeId)
    {
        string normalized = Normalize(name);

        await using DatabaseContext context = DatabaseContext.GenerateContext(_databaseSettings.ConnectionString);
        return await context.Packages.AnyAsync(p =>
            p.OrganizationId == organizationId &&
            p.IsActive &&
            p.Name.Trim().ToLower() == normalized &&
            (excludeId == null || p.Id != excludeId));
    }

    private static string Normalize(string name)
    {
        return name?.Trim().ToLowerInvariant() ?? string.Empty;
    }
}
