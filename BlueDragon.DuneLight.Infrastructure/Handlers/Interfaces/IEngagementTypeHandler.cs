using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using BlueDragon.DuneLight.Core.Shared;
using BlueDragon.DuneLight.Infrastructure.Domain.Models.Employees;

namespace BlueDragon.DuneLight.Infrastructure.Handlers.Interfaces;

public interface IEngagementTypeHandler
{
    Task<(List<EngagementType> Items, int TotalCount)> GetPaged(Guid organizationId, PagedRequest request);
    Task<EngagementType> GetById(Guid organizationId, Guid id);
    Task<bool> NameExistsAmongActive(Guid organizationId, string name, Guid? excludeId);
    Task Add(EngagementType engagementType);
    Task Update(EngagementType engagementType);
    Task Delete(EngagementType engagementType);
    Task<bool> IsReferenced(Guid organizationId, Guid id);
}
