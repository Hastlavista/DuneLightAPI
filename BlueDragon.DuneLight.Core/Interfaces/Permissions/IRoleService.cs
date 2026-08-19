using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using BlueDragon.DuneLight.Core.DTOs.Permissions;

namespace BlueDragon.DuneLight.Core.Interfaces.Permissions;

public interface IRoleService
{
    Task<List<RoleDto>> GetAll(Guid organizationId);
    Task<RoleDto> GetById(Guid organizationId, Guid id);
    Task<RoleDto> Create(Guid organizationId, Guid userId, RoleCreateRequest request);
    Task<RoleDto> Update(Guid organizationId, Guid userId, Guid id, RoleUpdateRequest request);
    Task Delete(Guid organizationId, Guid id);

    Task<List<Guid>> GetAssignedRoleIds(Guid organizationId, Guid userId);
    Task SetUserRoles(Guid organizationId, Guid userId, AssignUserRolesRequest request);
}