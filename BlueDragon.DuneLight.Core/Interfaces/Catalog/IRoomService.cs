using System;
using System.Threading.Tasks;
using BlueDragon.DuneLight.Core.DTOs.Catalog;
using BlueDragon.DuneLight.Core.Shared;

namespace BlueDragon.DuneLight.Core.Interfaces.Catalog;

public interface IRoomService
{
    Task<PagedResult<RoomDto>> GetPaged(Guid organizationId, Guid? companyId, PagedRequest request);
    Task<RoomDto> GetById(Guid organizationId, Guid id);
    Task<RoomDto> Create(Guid organizationId, Guid userId, RoomCreateRequest request);
    Task<RoomDto> Update(Guid organizationId, Guid userId, Guid id, RoomUpdateRequest request);
    Task<RoomDto> SetActive(Guid organizationId, Guid userId, Guid id, bool isActive);
    Task Delete(Guid organizationId, Guid id);
}
