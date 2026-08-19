using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using BlueDragon.DuneLight.Core.DTOs.Permissions;
using BlueDragon.DuneLight.Core.Interfaces.Permissions;
using BlueDragon.DuneLight.Core.Shared;
using BlueDragon.DuneLight.Core.Shared.Exceptions;
using BlueDragon.DuneLight.Infrastructure.Domain.Models.Permissions;
using BlueDragon.DuneLight.Infrastructure.Handlers.Interfaces;

namespace BlueDragon.DuneLight.Infrastructure.Services;

public class RoleService : IRoleService
{
    private readonly IRoleHandler _roleHandler;

    public RoleService(IRoleHandler roleHandler)
    {
        _roleHandler = roleHandler;
    }

    public async Task<List<RoleDto>> GetAll(Guid organizationId)
    {
        List<Role> roles = await _roleHandler.GetAll(organizationId);
        return roles.Select(ToDto).ToList();
    }

    public async Task<RoleDto> GetById(Guid organizationId, Guid id)
    {
        Role role = await _roleHandler.GetById(organizationId, id);
        if (role == null)
            throw new NotFoundAppException("Role", id);

        return ToDto(role);
    }

    public async Task<RoleDto> Create(Guid organizationId, Guid userId, RoleCreateRequest request)
    {
        bool nameExists = await _roleHandler.NameExists(organizationId, request.Name, excludeId: null);
        if (nameExists)
            throw new BusinessRuleException(ErrorCodes.DuplicateName, "Uloga s ovim nazivom već postoji.");

        Role role = new Role
        {
            Id = Guid.NewGuid(),
            OrganizationId = organizationId,
            Name = request.Name,
            CreatedAt = DateTimeOffset.UtcNow,
            CreatedBy = userId
        };

        await _roleHandler.Add(role);
        return ToDto(role);
    }

    public async Task<RoleDto> Update(Guid organizationId, Guid userId, Guid id, RoleUpdateRequest request)
    {
        Role existing = await _roleHandler.GetById(organizationId, id);
        if (existing == null)
            throw new NotFoundAppException("Role", id);

        bool nameExists = await _roleHandler.NameExists(organizationId, request.Name, excludeId: id);
        if (nameExists)
            throw new BusinessRuleException(ErrorCodes.DuplicateName, "Uloga s ovim nazivom već postoji.");

        existing.Name = request.Name;
        await _roleHandler.Update(existing);
        return await GetById(organizationId, id);
    }

    public async Task Delete(Guid organizationId, Guid id)
    {
        Role existing = await _roleHandler.GetById(organizationId, id);
        if (existing == null)
            throw new NotFoundAppException("Role", id);

        await _roleHandler.Delete(organizationId, id);
    }

    public async Task<List<Guid>> GetAssignedRoleIds(Guid organizationId, Guid userId)
    {
        return await _roleHandler.GetAssignedRoleIds(organizationId, userId);
    }

    public async Task SetUserRoles(Guid organizationId, Guid userId, AssignUserRolesRequest request)
    {
        List<Guid> distinctIds = request.RoleIds.Distinct().ToList();
        List<Role> organizationRoles = await _roleHandler.GetAll(organizationId);
        HashSet<Guid> validIds = organizationRoles.Select(r => r.Id.GetValueOrDefault()).ToHashSet();

        foreach (Guid roleId in distinctIds)
            if (!validIds.Contains(roleId))
                throw new NotFoundAppException("Role", roleId);

        await _roleHandler.SetUserRoles(organizationId, userId, distinctIds);
    }

    private static RoleDto ToDto(Role role)
    {
        return new RoleDto
        {
            Id = role.Id.GetValueOrDefault(),
            Name = role.Name,
            CreatedAt = role.CreatedAt
        };
    }
}