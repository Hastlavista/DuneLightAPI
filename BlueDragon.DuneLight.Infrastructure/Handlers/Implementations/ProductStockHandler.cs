using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using BlueDragon.DuneLight.Infrastructure.Domain.Contexts;
using BlueDragon.DuneLight.Infrastructure.Domain.Models.Products;
using BlueDragon.DuneLight.Infrastructure.Domain.Settings;
using BlueDragon.DuneLight.Infrastructure.Handlers.Interfaces;
using BlueDragon.DuneLight.Infrastructure.UnitOfWork;
using Microsoft.EntityFrameworkCore;

namespace BlueDragon.DuneLight.Infrastructure.Handlers.Implementations;

public class ProductStockHandler : IProductStockHandler
{
    private readonly DatabaseSettings _databaseSettings;

    public ProductStockHandler(DatabaseSettings databaseSettings)
    {
        _databaseSettings = databaseSettings;
    }

    public async Task<ProductStock> GetOrCreateForUpdate(IUnitOfWork uow, Guid organizationId, Guid productId, Guid companyId, DateTimeOffset now)
    {
        // ON CONFLICT DO NOTHING cilja ux_product_stock_product_company (vidi migraciju) — atomski siguran čak
        // i kad dvije transakcije istovremeno prvi put stvaraju redak za isti par, bez retry petlje (vidi
        // ProductStock.cs klasnu napomenu).
        await uow.Context.Database.ExecuteSqlInterpolatedAsync($@"
            INSERT INTO dunelight.product_stock (id, organization_id, product_id, company_id, quantity, updated_at)
            VALUES ({Guid.NewGuid()}, {organizationId}, {productId}, {companyId}, 0, {now})
            ON CONFLICT (product_id, company_id) DO NOTHING");

        return await uow.Context.ProductStock
            .FromSqlInterpolated($@"SELECT * FROM dunelight.product_stock
                WHERE organization_id = {organizationId} AND product_id = {productId} AND company_id = {companyId}
                FOR UPDATE")
            .SingleAsync();
    }

    public async Task Update(IUnitOfWork uow, ProductStock stock)
    {
        uow.Context.ProductStock.Update(stock);
        await uow.Context.SaveChangesAsync();
    }

    public async Task<ProductStock> Get(Guid organizationId, Guid productId, Guid companyId)
    {
        await using DatabaseContext context = DatabaseContext.GenerateContext(_databaseSettings.ConnectionString);
        return await context.ProductStock
            .SingleOrDefaultAsync(s => s.OrganizationId == organizationId && s.ProductId == productId && s.CompanyId == companyId);
    }

    public async Task<List<ProductStock>> GetByCompany(Guid organizationId, Guid companyId)
    {
        await using DatabaseContext context = DatabaseContext.GenerateContext(_databaseSettings.ConnectionString);
        return await context.ProductStock
            .Include(s => s.Product)
            .Include(s => s.Company)
            .Where(s => s.OrganizationId == organizationId && s.CompanyId == companyId)
            .OrderBy(s => s.Product.Name)
            .ToListAsync();
    }

    public async Task<List<ProductStock>> GetByProduct(Guid organizationId, Guid productId)
    {
        await using DatabaseContext context = DatabaseContext.GenerateContext(_databaseSettings.ConnectionString);
        return await context.ProductStock
            .Include(s => s.Product)
            .Include(s => s.Company)
            .Where(s => s.OrganizationId == organizationId && s.ProductId == productId)
            .OrderBy(s => s.Company.Name)
            .ToListAsync();
    }
}
