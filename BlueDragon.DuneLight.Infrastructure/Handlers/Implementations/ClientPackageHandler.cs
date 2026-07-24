using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using BlueDragon.DuneLight.Core.Enums;
using BlueDragon.DuneLight.Infrastructure.Domain.Contexts;
using BlueDragon.DuneLight.Infrastructure.Domain.Models.Clients;
using BlueDragon.DuneLight.Infrastructure.Domain.Settings;
using BlueDragon.DuneLight.Infrastructure.Handlers.Interfaces;
using Microsoft.EntityFrameworkCore;

namespace BlueDragon.DuneLight.Infrastructure.Handlers.Implementations;

public class ClientPackageHandler : IClientPackageHandler
{
    private readonly DatabaseSettings _databaseSettings;

    public ClientPackageHandler(DatabaseSettings databaseSettings)
    {
        _databaseSettings = databaseSettings;
    }

    public async Task Add(ClientPackage clientPackage)
    {
        await using DatabaseContext context = DatabaseContext.GenerateContext(_databaseSettings.ConnectionString);
        context.ClientPackages.Add(clientPackage);
        await context.SaveChangesAsync();
    }

    public async Task<ClientPackage> GetById(Guid organizationId, Guid id)
    {
        await using DatabaseContext context = DatabaseContext.GenerateContext(_databaseSettings.ConnectionString);
        return await context.ClientPackages
            .Include(cp => cp.Package)
            .Include(cp => cp.ServiceEntries).ThenInclude(e => e.Service)
            .SingleOrDefaultAsync(cp => cp.OrganizationId == organizationId && cp.Id == id);
    }

    public async Task<List<ClientPackage>> GetByClient(Guid organizationId, Guid clientId)
    {
        await using DatabaseContext context = DatabaseContext.GenerateContext(_databaseSettings.ConnectionString);
        return await context.ClientPackages
            .Include(cp => cp.Package)
            .Include(cp => cp.ServiceEntries).ThenInclude(e => e.Service)
            .Where(cp => cp.OrganizationId == organizationId && cp.ClientId == clientId)
            .OrderByDescending(cp => cp.PurchaseDate)
            .ToListAsync();
    }

    public async Task<List<ClientPackage>> GetEligibleForService(Guid organizationId, Guid clientId, Guid serviceId, DateTimeOffset date)
    {
        await using DatabaseContext context = DatabaseContext.GenerateContext(_databaseSettings.ConnectionString);
        return await context.ClientPackages
            .Include(cp => cp.Package)
            .Include(cp => cp.ServiceEntries).ThenInclude(e => e.Service)
            .Where(cp =>
                cp.OrganizationId == organizationId &&
                cp.ClientId == clientId &&
                cp.Status == ClientPackageStatus.Active &&
                cp.ExpiryDate >= date &&
                cp.ServiceEntries.Any(se => se.ServiceId == serviceId) &&
                (
                    (cp.EntryMode == PackageEntryMode.SharedPool && (cp.RemainingSharedEntries == null || cp.RemainingSharedEntries > 0)) ||
                    (cp.EntryMode == PackageEntryMode.PerService && cp.ServiceEntries.Any(se =>
                        se.ServiceId == serviceId && (se.RemainingEntries == null || se.RemainingEntries > 0)))
                ))
            .OrderBy(cp => cp.ExpiryDate)
            .ToListAsync();
    }

    public async Task Update(ClientPackage clientPackage)
    {
        await using DatabaseContext context = DatabaseContext.GenerateContext(_databaseSettings.ConnectionString);
        context.ClientPackages.Update(clientPackage);
        await context.SaveChangesAsync();
    }

    public async Task<bool> HasAnyForClient(Guid organizationId, Guid clientId)
    {
        await using DatabaseContext context = DatabaseContext.GenerateContext(_databaseSettings.ConnectionString);
        return await context.ClientPackages.AnyAsync(cp => cp.OrganizationId == organizationId && cp.ClientId == clientId);
    }
}
