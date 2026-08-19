using System;
using System.Threading.Tasks;
using BlueDragon.DuneLight.Core.DTOs.Catalog;
using BlueDragon.DuneLight.Core.Shared;

namespace BlueDragon.DuneLight.Core.Interfaces.Catalog;

public interface ICompanyService
{
    Task<PagedResult<CompanyDto>> GetPaged(Guid organizationId, PagedRequest request);
    Task<CompanyDto> GetById(Guid organizationId, Guid id);
    Task<CompanyDto> Create(Guid organizationId, Guid userId, CompanyCreateRequest request);
    Task<CompanyDto> Update(Guid organizationId, Guid userId, Guid id, CompanyUpdateRequest request);
    Task<CompanyDto> SetActive(Guid organizationId, Guid userId, Guid id, bool isActive);
    Task Delete(Guid organizationId, Guid id);
}
