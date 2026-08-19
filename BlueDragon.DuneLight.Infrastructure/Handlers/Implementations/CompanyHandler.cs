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

public class CompanyHandler : ICompanyHandler
{
    private readonly DatabaseSettings _databaseSettings;

    public CompanyHandler(DatabaseSettings databaseSettings)
    {
        _databaseSettings = databaseSettings;
    }

    public async Task<(List<Company> Items, int TotalCount)> GetPaged(Guid organizationId, PagedRequest request)
    {
        await using DatabaseContext context = DatabaseContext.GenerateContext(_databaseSettings.ConnectionString);
        IQueryable<Company> query = context.Companies.Where(l => l.OrganizationId == organizationId);
        if (!string.IsNullOrWhiteSpace(request.Search))
            query = query.Where(l => EF.Functions.ILike(l.Name, $"%{request.Search}%"));

        if (request.IsActive.HasValue)
            query = query.Where(l => l.IsActive == request.IsActive.Value);

        int totalCount = await query.CountAsync();

        List<Company> items = await query
            .OrderBy(l => l.SortOrder)
            .ThenBy(l => l.Name)
            .Skip((request.Page - 1) * request.PageSize)
            .Take(request.PageSize)
            .ToListAsync();

        return (items, totalCount);
    }

    public async Task<Company> GetById(Guid organizationId, Guid id)
    {
        await using DatabaseContext context = DatabaseContext.GenerateContext(_databaseSettings.ConnectionString);
        return await context.Companies.SingleOrDefaultAsync(l => l.OrganizationId == organizationId && l.Id == id);
    }

    public async Task<List<Company>> GetByIds(Guid organizationId, List<Guid> ids)
    {
        await using DatabaseContext context = DatabaseContext.GenerateContext(_databaseSettings.ConnectionString);
        return await context.Companies
            .Where(l => l.OrganizationId == organizationId && l.Id.HasValue && ids.Contains(l.Id.Value))
            .ToListAsync();
    }

    public async Task Add(Company company)
    {
        await using DatabaseContext context = DatabaseContext.GenerateContext(_databaseSettings.ConnectionString);
        context.Companies.Add(company);
        await context.SaveChangesAsync();
    }

    public async Task Update(Company company)
    {
        await using DatabaseContext context = DatabaseContext.GenerateContext(_databaseSettings.ConnectionString);
        context.Companies.Update(company);
        await context.SaveChangesAsync();
    }

    public async Task Delete(Company company)
    {
        await using DatabaseContext context = DatabaseContext.GenerateContext(_databaseSettings.ConnectionString);
        context.Companies.Remove(company);
        await context.SaveChangesAsync();
    }

    public async Task<int> CountActive(Guid organizationId)
    {
        await using DatabaseContext context = DatabaseContext.GenerateContext(_databaseSettings.ConnectionString);
        return await context.Companies.CountAsync(l => l.OrganizationId == organizationId && l.IsActive);
    }

    public async Task<bool> IsReferenced(Guid organizationId, Guid id)
    {
        await using DatabaseContext context = DatabaseContext.GenerateContext(_databaseSettings.ConnectionString);
        return await context.PriceListItems.AnyAsync(p => p.OrganizationId == organizationId && p.CompanyId == id);
    }
}
