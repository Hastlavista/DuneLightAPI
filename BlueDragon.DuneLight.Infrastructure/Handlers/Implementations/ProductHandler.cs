using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using BlueDragon.DuneLight.Core.Shared;
using BlueDragon.DuneLight.Infrastructure.Domain.Contexts;
using BlueDragon.DuneLight.Infrastructure.Domain.Models.Products;
using BlueDragon.DuneLight.Infrastructure.Domain.Settings;
using BlueDragon.DuneLight.Infrastructure.Handlers.Interfaces;
using Microsoft.EntityFrameworkCore;

namespace BlueDragon.DuneLight.Infrastructure.Handlers.Implementations;

public class ProductHandler : IProductHandler
{
    private readonly DatabaseSettings _databaseSettings;

    public ProductHandler(DatabaseSettings databaseSettings)
    {
        _databaseSettings = databaseSettings;
    }

    public async Task<(List<Product> Items, int TotalCount)> GetPaged(Guid organizationId, PagedRequest request)
    {
        await using DatabaseContext context = DatabaseContext.GenerateContext(_databaseSettings.ConnectionString);

        IQueryable<Product> query = context.Products
            .Where(p => p.OrganizationId == organizationId);

        if (!string.IsNullOrWhiteSpace(request.Search))
            query = query.Where(p =>
                EF.Functions.ILike(p.Name, $"%{request.Search}%") ||
                (p.Sku != null && EF.Functions.ILike(p.Sku, $"%{request.Search}%")));

        if (request.IsActive.HasValue)
            query = query.Where(p => p.IsActive == request.IsActive.Value);

        int totalCount = await query.CountAsync();

        List<Product> items = await query
            .OrderBy(p => p.Name)
            .Skip((request.Page - 1) * request.PageSize)
            .Take(request.PageSize)
            .ToListAsync();

        return (items, totalCount);
    }

    public async Task<Product> GetById(Guid organizationId, Guid id)
    {
        await using DatabaseContext context = DatabaseContext.GenerateContext(_databaseSettings.ConnectionString);
        return await context.Products
            .SingleOrDefaultAsync(p => p.OrganizationId == organizationId && p.Id == id);
    }

    public async Task<List<Product>> GetActive(Guid organizationId)
    {
        await using DatabaseContext context = DatabaseContext.GenerateContext(_databaseSettings.ConnectionString);
        return await context.Products
            .Where(p => p.OrganizationId == organizationId && p.IsActive)
            .OrderBy(p => p.Name)
            .ToListAsync();
    }

    public async Task<bool> NameExistsAmongActive(Guid organizationId, string name, Guid? excludeId)
    {
        string normalized = Normalize(name);

        await using DatabaseContext context = DatabaseContext.GenerateContext(_databaseSettings.ConnectionString);
        return await context.Products.AnyAsync(p =>
            p.OrganizationId == organizationId &&
            p.IsActive &&
            p.Name.Trim().ToLower() == normalized &&
            (excludeId == null || p.Id != excludeId));
    }

    public async Task<bool> SkuExists(Guid organizationId, string sku, Guid? excludeId)
    {
        if (string.IsNullOrWhiteSpace(sku))
            return false;

        string normalized = Normalize(sku);

        await using DatabaseContext context = DatabaseContext.GenerateContext(_databaseSettings.ConnectionString);
        return await context.Products.AnyAsync(p =>
            p.OrganizationId == organizationId &&
            p.Sku != null &&
            p.Sku.Trim().ToLower() == normalized &&
            (excludeId == null || p.Id != excludeId));
    }

    public async Task Add(Product product)
    {
        await using DatabaseContext context = DatabaseContext.GenerateContext(_databaseSettings.ConnectionString);
        context.Products.Add(product);
        await context.SaveChangesAsync();
    }

    public async Task Update(Product product)
    {
        await using DatabaseContext context = DatabaseContext.GenerateContext(_databaseSettings.ConnectionString);
        context.Products.Update(product);
        await context.SaveChangesAsync();
    }

    public async Task Delete(Product product)
    {
        await using DatabaseContext context = DatabaseContext.GenerateContext(_databaseSettings.ConnectionString);
        context.Products.Remove(product);
        await context.SaveChangesAsync();
    }

    public async Task<bool> IsReferenced(Guid organizationId, Guid id)
    {
        await using DatabaseContext context = DatabaseContext.GenerateContext(_databaseSettings.ConnectionString);

        IQueryable<int> checkoutItems = context.CheckoutItems
            .Where(ci => ci.OrganizationId == organizationId && ci.ProductId == id)
            .Select(ci => 1);

        IQueryable<int> stockMovements = context.StockMovements
            .Where(m => m.OrganizationId == organizationId && m.ProductId == id)
            .Select(m => 1);

        IQueryable<int> productStock = context.ProductStock
            .Where(s => s.OrganizationId == organizationId && s.ProductId == id)
            .Select(s => 1);

        IQueryable<int> commissionRules = context.CommissionRules
            .Where(r => r.OrganizationId == organizationId && r.ProductId == id)
            .Select(r => 1);

        IQueryable<int> anyReference = checkoutItems.Union(stockMovements).Union(productStock).Union(commissionRules);

        return await anyReference.AnyAsync();
    }

    private static string Normalize(string value)
    {
        return value?.Trim().ToLowerInvariant() ?? string.Empty;
    }
}
