using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using BlueDragon.DuneLight.Core.DTOs.Permissions;

namespace BlueDragon.DuneLight.Core.Interfaces.Permissions;

public interface IGrantGroupService
{
    Task<List<GrantGroupDto>> GetAll(Guid organizationId);
    Task<GrantGroupDto> GetById(Guid organizationId, Guid id);
    Task<GrantGroupDto> Create(Guid organizationId, Guid userId, GrantGroupCreateRequest request);
    Task<GrantGroupDto> Update(Guid organizationId, Guid userId, Guid id, GrantGroupUpdateRequest request);
    Task Delete(Guid organizationId, Guid id);

    Task<List<Guid>> GetAssignedGrantGroupIds(Guid organizationId, Guid userId);
    Task SetUserGrantGroups(Guid organizationId, Guid userId, AssignUserGrantGroupsRequest request);
}