using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using BlueDragon.DuneLight.Infrastructure.Domain.Contexts;
using BlueDragon.DuneLight.Infrastructure.Domain.Models.Catalog;
using BlueDragon.DuneLight.Infrastructure.Domain.Settings;
using BlueDragon.DuneLight.Infrastructure.Handlers.Interfaces;
using Microsoft.EntityFrameworkCore;

namespace BlueDragon.DuneLight.Infrastructure.Handlers.Implementations;

public class ServiceCompanyHandler : IServiceCompanyHandler
{
    private readonly DatabaseSettings _databaseSettings;

    public ServiceCompanyHandler(DatabaseSettings databaseSettings)
    {
        _databaseSettings = databaseSettings;
    }

    public async Task<List<ServiceCompany>> GetForService(Guid organizationId, Guid serviceId)
    {
        await using DatabaseContext context = DatabaseContext.GenerateContext(_databaseSettings.ConnectionString);
        return await context.ServiceCompanies
            .Include(sc => sc.Company)
            .Where(sc => sc.ServiceId == serviceId && sc.Service.OrganizationId == organizationId)
            .ToListAsync();
    }

    public async Task<List<ServiceCompany>> GetForCompany(Guid organizationId, Guid companyId)
    {
        await using DatabaseContext context = DatabaseContext.GenerateContext(_databaseSettings.ConnectionString);
        return await context.ServiceCompanies
            .Include(sc => sc.Service)
            .Where(sc => sc.CompanyId == companyId && sc.Company.OrganizationId == organizationId)
            .ToListAsync();
    }

    public async Task<bool> IsAvailable(Guid organizationId, Guid serviceId, Guid companyId)
    {
        await using DatabaseContext context = DatabaseContext.GenerateContext(_databaseSettings.ConnectionString);
        return await context.ServiceCompanies.AnyAsync(sc =>
            sc.ServiceId == serviceId &&
            sc.CompanyId == companyId &&
            sc.Service.OrganizationId == organizationId &&
            sc.Service.IsActive &&
            sc.Company.OrganizationId == organizationId &&
            sc.Company.IsActive);
    }

    public async Task ReplaceForService(Guid serviceId, List<Guid> companyIds)
    {
        await using DatabaseContext context = DatabaseContext.GenerateContext(_databaseSettings.ConnectionString);

        List<ServiceCompany> existing = await context.ServiceCompanies
            .Where(sc => sc.ServiceId == serviceId)
            .ToListAsync();

        HashSet<Guid> targetIds = companyIds.ToHashSet();
        HashSet<Guid> existingIds = existing.Select(sc => sc.CompanyId).ToHashSet();

        List<ServiceCompany> toRemove = existing.Where(sc => !targetIds.Contains(sc.CompanyId)).ToList();
        if (toRemove.Count > 0)
            context.ServiceCompanies.RemoveRange(toRemove);

        List<ServiceCompany> toAdd = targetIds
            .Where(id => !existingIds.Contains(id))
            .Select(id => new ServiceCompany { Id = Guid.NewGuid(), ServiceId = serviceId, CompanyId = id })
            .ToList();
        if (toAdd.Count > 0)
            context.ServiceCompanies.AddRange(toAdd);

        // Jedan SaveChangesAsync = jedna transakcija (Npgsql) — brisanje izostavljenih i dodavanje novih se
        // primjenjuju zajedno ili nijedno (vidi ServiceAvailabilityService.ReplaceAssignedCompanies).
        await context.SaveChangesAsync();
    }
}
