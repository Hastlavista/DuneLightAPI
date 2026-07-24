using System;
using System.Threading.Tasks;
using BlueDragon.DuneLight.Core.DTOs.Clients;
using BlueDragon.DuneLight.Core.Shared;

namespace BlueDragon.DuneLight.Core.Interfaces.Clients;

public interface IClientTagService
{
    Task<PagedResult<ClientTagDto>> GetPaged(Guid organizationId, PagedRequest request);
    Task<ClientTagDto> GetById(Guid organizationId, Guid id);
    Task<ClientTagDto> Create(Guid organizationId, Guid userId, ClientTagCreateRequest request);
    Task<ClientTagDto> Update(Guid organizationId, Guid userId, Guid id, ClientTagUpdateRequest request);
    Task<ClientTagDto> SetActive(Guid organizationId, Guid userId, Guid id, bool isActive);
    Task Delete(Guid organizationId, Guid id);
}
