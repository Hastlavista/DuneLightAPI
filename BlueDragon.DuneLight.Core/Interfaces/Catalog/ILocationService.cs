using System;
using System.Threading.Tasks;
using BlueDragon.DuneLight.Core.DTOs.Catalog;
using BlueDragon.DuneLight.Core.Shared;

namespace BlueDragon.DuneLight.Core.Interfaces.Catalog;

public interface ILocationService
{
    Task<PagedResult<LocationDto>> GetPaged(Guid organizationId, PagedRequest request);
    Task<LocationDto> GetById(Guid organizationId, Guid id);
    Task<LocationDto> Create(Guid organizationId, Guid userId, LocationCreateRequest request);
    Task<LocationDto> Update(Guid organizationId, Guid userId, Guid id, LocationUpdateRequest request);
    Task<LocationDto> SetActive(Guid organizationId, Guid userId, Guid id, bool isActive);
    Task Delete(Guid organizationId, Guid id);
}
