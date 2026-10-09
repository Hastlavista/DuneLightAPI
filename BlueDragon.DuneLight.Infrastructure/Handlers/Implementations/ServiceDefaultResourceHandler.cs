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

public class ServiceDefaultResourceHandler : IServiceDefaultResourceHandler
{
    private readonly DatabaseSettings _databaseSettings;

    public ServiceDefaultResourceHandler(DatabaseSettings databaseSettings)
    {
        _databaseSettings = databaseSettings;
    }

    public async Task<List<ServiceDefaultResource>> GetForService(Guid organizationId, Guid serviceId)
    {
        await using DatabaseContext context = DatabaseContext.GenerateContext(_databaseSettings.ConnectionString);
        return await context.ServiceDefaultResources
            .AsNoTracking()
            .Include(x => x.Resource)
            .Where(x => x.ServiceId == serviceId && x.Service.OrganizationId == organizationId)
            .OrderBy(x => x.Resource.SortOrder).ThenBy(x => x.Resource.Name)
            .ToListAsync();
    }

    public async Task ReplaceForService(Guid serviceId, IReadOnlyList<(Guid ResourceId, int QuantityRequired)> resources)
    {
        await using DatabaseContext context = DatabaseContext.GenerateContext(_databaseSettings.ConnectionString);
        List<ServiceDefaultResource> existing = await context.ServiceDefaultResources
            .Where(x => x.ServiceId == serviceId)
            .ToListAsync();

        Dictionary<Guid, int> target = resources.ToDictionary(r => r.ResourceId, r => r.QuantityRequired);
        context.ServiceDefaultResources.RemoveRange(existing.Where(x => !target.ContainsKey(x.ResourceId)));
        foreach (ServiceDefaultResource kept in existing.Where(x => target.ContainsKey(x.ResourceId)))
            kept.QuantityRequired = target[kept.ResourceId];
        HashSet<Guid> existingIds = existing.Select(x => x.ResourceId).ToHashSet();
        context.ServiceDefaultResources.AddRange(target
            .Where(t => !existingIds.Contains(t.Key))
            .Select(t => new ServiceDefaultResource { Id = Guid.NewGuid(), ServiceId = serviceId, ResourceId = t.Key, QuantityRequired = t.Value }));

        // Jedan SaveChanges = jedna transakcija (brisanje, izmjena i dodavanje zajedno ili ništa).
        await context.SaveChangesAsync();
    }
}
