using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using BlueDragon.DuneLight.Infrastructure.Domain.Models.Catalog;

namespace BlueDragon.DuneLight.Infrastructure.Handlers.Interfaces;

/// <summary>K1-6 — zadani resursi usluge (konfiguracijski zapis, vidi ServiceDefaultResource).</summary>
public interface IServiceDefaultResourceHandler
{
    /// <summary>Svi zadani resursi usluge, tenant-provjereno preko Service.OrganizationId. Resource navigacija je učitana.</summary>
    Task<List<ServiceDefaultResource>> GetForService(Guid organizationId, Guid serviceId);

    /// <summary>Zamjenjuje cijeli popis u jednoj transakciji (jedan SaveChanges).</summary>
    Task ReplaceForService(Guid serviceId, IReadOnlyList<(Guid ResourceId, int QuantityRequired)> resources);
}
