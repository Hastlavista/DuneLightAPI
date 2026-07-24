using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using BlueDragon.DuneLight.Core.Shared;
using BlueDragon.DuneLight.Infrastructure.Domain.Models.Catalog;

namespace BlueDragon.DuneLight.Infrastructure.Handlers.Interfaces;

public interface IServiceCategoryHandler
{
    Task<(List<ServiceCategory> Items, int TotalCount)> GetPaged(Guid organizationId, PagedRequest request);
    Task<ServiceCategory> GetById(Guid organizationId, Guid id);
    Task<bool> NameExistsAmongActive(Guid organizationId, string name, Guid? excludeId);
    Task Add(ServiceCategory category);
    Task Update(ServiceCategory category);
    Task Delete(ServiceCategory category);
}
