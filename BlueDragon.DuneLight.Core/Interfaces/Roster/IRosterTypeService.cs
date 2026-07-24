using System;
using System.Threading.Tasks;
using BlueDragon.DuneLight.Core.DTOs.Roster;
using BlueDragon.DuneLight.Core.Shared;

namespace BlueDragon.DuneLight.Core.Interfaces.Roster;

public interface IRosterTypeService
{
    Task<PagedResult<RosterTypeDto>> GetPaged(Guid organizationId, PagedRequest request);
    Task<RosterTypeDto> GetById(Guid organizationId, Guid id);
    Task<RosterTypeDto> Create(Guid organizationId, Guid userId, RosterTypeCreateRequest request);
    Task<RosterTypeDto> Update(Guid organizationId, Guid userId, Guid id, RosterTypeUpdateRequest request);
    Task<RosterTypeDto> SetActive(Guid organizationId, Guid userId, Guid id, bool isActive);
    Task Delete(Guid organizationId, Guid id);
}
