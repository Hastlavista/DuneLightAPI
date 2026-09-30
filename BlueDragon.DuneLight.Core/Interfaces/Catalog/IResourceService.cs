using System;
using System.Threading.Tasks;
using BlueDragon.DuneLight.Core.DTOs.Catalog;
using BlueDragon.DuneLight.Core.Shared;

namespace BlueDragon.DuneLight.Core.Interfaces.Catalog;

public interface IResourceService
{
    Task<PagedResult<ResourceDto>> GetPaged(Guid organizationId, Guid? companyId, PagedRequest request);
    Task<ResourceDto> GetById(Guid organizationId, Guid id);
    Task<ResourceDto> Create(Guid organizationId, Guid userId, ResourceCreateRequest request);
    Task<ResourceDto> Update(Guid organizationId, Guid userId, Guid id, ResourceUpdateRequest request);
    Task<ResourceDto> SetActive(Guid organizationId, Guid userId, Guid id, bool isActive);
    Task Delete(Guid organizationId, Guid id);
}
