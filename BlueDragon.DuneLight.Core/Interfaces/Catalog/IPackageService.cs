using System;
using System.Threading.Tasks;
using BlueDragon.DuneLight.Core.DTOs.Catalog;
using BlueDragon.DuneLight.Core.Shared;

namespace BlueDragon.DuneLight.Core.Interfaces.Catalog;

public interface IPackageService
{
    Task<PagedResult<PackageDto>> GetPaged(Guid organizationId, PagedRequest request);
    Task<PackageDto> GetById(Guid organizationId, Guid id);
    Task<PackageDto> Create(Guid organizationId, Guid userId, PackageCreateRequest request);
    Task<PackageDto> Update(Guid organizationId, Guid userId, Guid id, PackageUpdateRequest request);
    Task<PackageDto> SetActive(Guid organizationId, Guid userId, Guid id, bool isActive);
    Task Delete(Guid organizationId, Guid id);
}
