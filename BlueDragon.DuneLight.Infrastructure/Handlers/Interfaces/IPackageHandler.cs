using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using BlueDragon.DuneLight.Core.Shared;
using BlueDragon.DuneLight.Infrastructure.Domain.Models.Catalog;

namespace BlueDragon.DuneLight.Infrastructure.Handlers.Interfaces;

public interface IPackageHandler
{
    Task<(List<Package> Items, int TotalCount)> GetPaged(Guid organizationId, PagedRequest request);
    Task<Package> GetById(Guid organizationId, Guid id);
    Task<List<Package>> GetAllActive(Guid organizationId);
    Task Add(Package package);
    Task Update(Package package, List<PackageServiceItem> newServiceItems);
    Task Delete(Package package);
    Task<bool> IsReferenced(Guid organizationId, Guid id);
}
