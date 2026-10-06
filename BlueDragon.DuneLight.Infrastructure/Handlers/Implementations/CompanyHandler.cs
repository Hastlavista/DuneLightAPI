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

    public async Task<Dictionary<Guid, string>> GetTimeZoneOverrides(Guid organizationId, List<Guid> ids)
    {
        await using DatabaseContext context = DatabaseContext.GenerateContext(_databaseSettings.ConnectionString);
        return await context.Companies
            .Where(l => l.OrganizationId == organizationId && l.Id.HasValue && ids.Contains(l.Id.Value))
            .ToDictionaryAsync(l => l.Id.Value, l => l.TimeZone);
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

    public async Task<bool> NameExistsAmongActive(Guid organizationId, string name, Guid? excludeId)
    {
        string normalized = Normalize(name);

        await using DatabaseContext context = DatabaseContext.GenerateContext(_databaseSettings.ConnectionString);
        return await context.Companies.AnyAsync(c =>
            c.OrganizationId == organizationId &&
            c.IsActive &&
            c.Name.Trim().ToLower() == normalized &&
            (excludeId == null || c.Id != excludeId));
    }

    public async Task<CompanyDeactivationOutcome> Deactivate(Guid organizationId, Guid id, Guid userId)
    {
        await using DatabaseContext context = DatabaseContext.GenerateContext(_databaseSettings.ConnectionString);

        // Organizacija smije imati nula aktivnih poslovnica (ADR-0021) — nema prebrojavanja ni lockiranja
        // ostalih aktivnih tvrtki, deaktivacija dira samo ciljnu tvrtku.
        Company target = await context.Companies.SingleOrDefaultAsync(c => c.OrganizationId == organizationId && c.Id == id);
        if (target == null)
            return CompanyDeactivationOutcome.NotFound;
        if (!target.IsActive)
            return CompanyDeactivationOutcome.AlreadyInactive;

        target.IsActive = false;
        target.UpdatedAt = DateTimeOffset.UtcNow;
        target.UpdatedBy = userId;

        await context.SaveChangesAsync();
        return CompanyDeactivationOutcome.Deactivated;
    }

    /// <summary>
    /// Sve poznate FK reference na tvrtku, provjerene u JEDNOM upitu (UNION podupita, umjesto 9 zasebnih
    /// round-tripova). Svaki krak je filtriran na organization_id gdje kolona postoji; EmployeeCompanies
    /// je jedina iznimka — ta tablica nema svoj organization_id (samo employee_id/company_id), ali id je
    /// već tenant-provjeren kod pozivatelja (CompanyService.Delete radi GetById(organizationId, id) prije
    /// ovog poziva), pa je company_id sam po sebi dovoljan i tenant-siguran.
    /// </summary>
    public async Task<bool> IsReferenced(Guid organizationId, Guid id)
    {
        await using DatabaseContext context = DatabaseContext.GenerateContext(_databaseSettings.ConnectionString);

        IQueryable<int> priceListItems = context.PriceListItems
            .Where(p => p.OrganizationId == organizationId && p.CompanyId == id)
            .Select(p => 1);

        IQueryable<int> rooms = context.Rooms
            .Where(r => r.OrganizationId == organizationId && r.CompanyId == id)
            .Select(r => 1);

        // resources.company_id je Restrict FK — bez ovog kraka brisanje poslovnice s resursom bi palo sirovom FK greškom.
        IQueryable<int> resources = context.Resources
            .Where(r => r.OrganizationId == organizationId && r.CompanyId == id)
            .Select(r => 1);

        IQueryable<int> employeeCompanies = context.EmployeeCompanies
            .Where(ec => ec.CompanyId == id)
            .Select(ec => 1);

        IQueryable<int> clientsWithHomeCompany = context.Clients
            .Where(c => c.OrganizationId == organizationId && c.HomeCompanyId == id)
            .Select(c => 1);

        IQueryable<int> appointments = context.Appointments
            .Where(a => a.OrganizationId == organizationId && a.CompanyId == id)
            .Select(a => 1);

        IQueryable<int> scheduleBreaks = context.ScheduleBreaks
            .Where(b => b.OrganizationId == organizationId && b.CompanyId == id)
            .Select(b => 1);

        IQueryable<int> groups = context.Groups
            .Where(g => g.OrganizationId == organizationId && g.CompanyId == id)
            .Select(g => 1);

        IQueryable<int> workingHoursTemplates = context.WorkingHoursTemplates
            .Where(t => t.OrganizationId == organizationId && t.CompanyId == id)
            .Select(t => 1);

        IQueryable<int> companyHolidays = context.CompanyHolidays
            .Where(h => h.OrganizationId == organizationId && h.CompanyId == id)
            .Select(h => 1);

        IQueryable<int> commissionEntries = context.CommissionEntries
            .Where(e => e.OrganizationId == organizationId && e.CompanyId == id)
            .Select(e => 1);

        // Data/Lifecycle Consistency Cleanup — Checkout.CompanyId je Restrict FK (vidi DatabaseContext), ali do
        // sada nije bio ovdje uključen, pa bi hard-delete pokušaj na Company referenciranoj SAMO preko Checkouta
        // (bez ijedne od gornjih tablica) propao sirovom Postgres FK greškom umjesto čistom domenskom porukom.
        // CommissionRule nema CompanyId (samo Employee+Subject) — nije relevantan ovdje, CommissionEntry već jest.
        IQueryable<int> checkouts = context.Checkouts
            .Where(c => c.OrganizationId == organizationId && c.CompanyId == id)
            .Select(c => 1);

        IQueryable<int> anyReference = priceListItems
            .Union(rooms)
            .Union(resources)
            .Union(employeeCompanies)
            .Union(clientsWithHomeCompany)
            .Union(appointments)
            .Union(scheduleBreaks)
            .Union(groups)
            .Union(workingHoursTemplates)
            .Union(companyHolidays)
            .Union(commissionEntries)
            .Union(checkouts);

        return await anyReference.AnyAsync();
    }

    private static string Normalize(string name)
    {
        return name?.Trim().ToLowerInvariant() ?? string.Empty;
    }
}
