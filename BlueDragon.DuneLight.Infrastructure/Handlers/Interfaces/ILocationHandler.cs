using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using BlueDragon.DuneLight.Core.Shared;
using BlueDragon.DuneLight.Infrastructure.Domain.Models.Catalog;

namespace BlueDragon.DuneLight.Infrastructure.Handlers.Interfaces;

public interface ILocationHandler
{
    Task<(List<Location> Items, int TotalCount)> GetPaged(Guid organizationId, PagedRequest request);
    Task<Location> GetById(Guid organizationId, Guid id);
    Task Add(Location location);
    Task Update(Location location);
    Task Delete(Location location);
    Task<int> CountActive(Guid organizationId);
    Task<bool> IsReferenced(Guid organizationId, Guid id);
}
