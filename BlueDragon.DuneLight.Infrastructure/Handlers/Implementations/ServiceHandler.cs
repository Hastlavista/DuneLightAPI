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
        string normalized = Normalize(name);

        await using DatabaseContext context = DatabaseContext.GenerateContext(_databaseSettings.ConnectionString);
        return await context.Services.AnyAsync(s =>
            s.OrganizationId == organizationId &&
            s.IsActive &&
            s.Name.Trim().ToLower() == normalized &&
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

    /// <summary>Sve poznate FK reference na uslugu, provjerene u jednom upitu (UNION podupita) umjesto pet
    /// zasebnih round-tripova — isti obrazac kao CompanyHandler.IsReferenced. EmployeeServiceAssignments nema
    /// vlastiti organization_id (samo employee_id/service_id), ali id je već tenant-provjeren kod pozivatelja
    /// (ServiceCatalogService.Delete radi GetById(organizationId, id) prije ovog poziva), pa je service_id sam
    /// po sebi dovoljan i tenant-siguran — isti obrazac kao EmployeeCompanies u CompanyHandler.IsReferenced.</summary>
    public async Task<bool> IsReferenced(Guid organizationId, Guid id)
    {
        await using DatabaseContext context = DatabaseContext.GenerateContext(_databaseSettings.ConnectionString);

        IQueryable<int> priceListItems = context.PriceListItems
            .Where(p => p.OrganizationId == organizationId && p.ServiceId == id)
            .Select(p => 1);

        IQueryable<int> packageServiceItems = context.PackageServiceItems
            .Where(ps => ps.ServiceId == id && ps.Package.OrganizationId == organizationId)
            .Select(ps => 1);

        IQueryable<int> employeeServiceAssignments = context.EmployeeServiceAssignments
            .Where(es => es.ServiceId == id)
            .Select(es => 1);

        IQueryable<int> clientPackageServiceEntries = context.ClientPackageServiceEntries
            .Where(e => e.ServiceId == id && e.ClientPackage.OrganizationId == organizationId)
            .Select(e => 1);

        IQueryable<int> appointments = context.Appointments
            .Where(a => a.OrganizationId == organizationId && a.ServiceId == id)
            .Select(a => 1);

        IQueryable<int> groups = context.Groups
            .Where(g => g.OrganizationId == organizationId && g.ServiceId == id)
            .Select(g => 1);

        IQueryable<int> commissionRules = context.CommissionRules
            .Where(r => r.OrganizationId == organizationId && r.ServiceId == id)
            .Select(r => 1);

        IQueryable<int> anyReference = priceListItems
            .Union(packageServiceItems)
            .Union(employeeServiceAssignments)
            .Union(clientPackageServiceEntries)
            .Union(appointments)
            .Union(groups)
            .Union(commissionRules);

        return await anyReference.AnyAsync();
    }

    /// <summary>Usluga je "korištena u zakazivanju" ako je referencirana od Appointment ili Group — namjerno UŽI
    /// skup od IsReferenced (koji uključuje i katalog/konfiguracijske reference poput PriceList/Package/Employee).
    /// Vidi ServiceCatalogService.Update — ExecutionMode je zaključan samo dok postoji ova (uža) referenca.</summary>
    public async Task<bool> IsUsedInScheduling(Guid organizationId, Guid id)
    {
        await using DatabaseContext context = DatabaseContext.GenerateContext(_databaseSettings.ConnectionString);

        IQueryable<int> appointments = context.Appointments
            .Where(a => a.OrganizationId == organizationId && a.ServiceId == id)
            .Select(a => 1);

        IQueryable<int> groups = context.Groups
            .Where(g => g.OrganizationId == organizationId && g.ServiceId == id)
            .Select(g => 1);

        return await appointments.Union(groups).AnyAsync();
    }

    private static string Normalize(string name)
    {
        return name?.Trim().ToLowerInvariant() ?? string.Empty;
    }
}
