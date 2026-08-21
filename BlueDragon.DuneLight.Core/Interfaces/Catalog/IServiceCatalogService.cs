using System;
using System.Threading.Tasks;
using BlueDragon.DuneLight.Core.DTOs.Catalog;
using BlueDragon.DuneLight.Core.Enums;
using BlueDragon.DuneLight.Core.Shared;

namespace BlueDragon.DuneLight.Core.Interfaces.Catalog;

public interface IServiceCatalogService
{
    Task<PagedResult<ServiceDto>> GetPaged(Guid organizationId, PagedRequest request, ServiceExecutionMode? executionMode);
    Task<ServiceDto> GetById(Guid organizationId, Guid id);
    Task<ServiceDto> Create(Guid organizationId, Guid userId, ServiceCreateRequest request);
    Task<ServiceDto> Update(Guid organizationId, Guid userId, Guid id, ServiceUpdateRequest request);
    Task<ServiceDto> SetActive(Guid organizationId, Guid userId, Guid id, bool isActive);
    Task Delete(Guid organizationId, Guid id);
}
