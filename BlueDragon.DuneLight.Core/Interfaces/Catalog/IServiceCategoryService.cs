using System;
using System.Threading.Tasks;
using BlueDragon.DuneLight.Core.DTOs.Catalog;
using BlueDragon.DuneLight.Core.Shared;

namespace BlueDragon.DuneLight.Core.Interfaces.Catalog;

public interface IServiceCategoryService
{
    Task<PagedResult<ServiceCategoryDto>> GetPaged(Guid organizationId, PagedRequest request);
    Task<ServiceCategoryDto> GetById(Guid organizationId, Guid id);
    Task<ServiceCategoryDto> Create(Guid organizationId, Guid userId, ServiceCategoryCreateRequest request);
    Task<ServiceCategoryDto> Update(Guid organizationId, Guid userId, Guid id, ServiceCategoryUpdateRequest request);
    Task<ServiceCategoryDto> SetActive(Guid organizationId, Guid userId, Guid id, bool isActive);
    Task Delete(Guid organizationId, Guid id);
}
