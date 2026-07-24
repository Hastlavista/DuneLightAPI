using System;
using System.Threading.Tasks;
using BlueDragon.DuneLight.Core.DTOs.Employees;
using BlueDragon.DuneLight.Core.Shared;

namespace BlueDragon.DuneLight.Core.Interfaces.Employees;

public interface IEngagementTypeService
{
    Task<PagedResult<EngagementTypeDto>> GetPaged(Guid organizationId, PagedRequest request);
    Task<EngagementTypeDto> GetById(Guid organizationId, Guid id);
    Task<EngagementTypeDto> Create(Guid organizationId, Guid userId, EngagementTypeCreateRequest request);
    Task<EngagementTypeDto> Update(Guid organizationId, Guid userId, Guid id, EngagementTypeUpdateRequest request);
    Task<EngagementTypeDto> SetActive(Guid organizationId, Guid userId, Guid id, bool isActive);
    Task Delete(Guid organizationId, Guid id);
}
