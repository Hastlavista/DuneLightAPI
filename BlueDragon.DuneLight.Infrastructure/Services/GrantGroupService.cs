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

public class GrantGroupService : IGrantGroupService
{
    private static readonly HashSet<string> ValidGrantKeys = Grants.Catalog.Select(g => g.Key).ToHashSet();

    private readonly IGrantGroupHandler _grantGroupHandler;

    public GrantGroupService(IGrantGroupHandler grantGroupHandler)
    {
        _grantGroupHandler = grantGroupHandler;
    }

    public async Task<List<GrantGroupDto>> GetAll(Guid organizationId)
    {
        List<GrantGroup> groups = await _grantGroupHandler.GetAll(organizationId);
        return groups.Select(ToDto).ToList();
    }

    public async Task<GrantGroupDto> GetById(Guid organizationId, Guid id)
    {
        GrantGroup group = await _grantGroupHandler.GetById(organizationId, id);
        if (group == null)
            throw new NotFoundAppException("GrantGroup", id);

        return ToDto(group);
    }

    public async Task<GrantGroupDto> Create(Guid organizationId, Guid userId, GrantGroupCreateRequest request)
    {
        ValidateGrantKeys(request.Grants);

        bool nameExists = await _grantGroupHandler.NameExists(organizationId, request.Name, excludeId: null);
        if (nameExists)
            throw new BusinessRuleException(ErrorCodes.DuplicateName, "Grant-grupa s ovim nazivom već postoji.");

        GrantGroup group = new GrantGroup
        {
            Id = Guid.NewGuid(),
            OrganizationId = organizationId,
            Name = request.Name,
            CreatedAt = DateTimeOffset.UtcNow,
            CreatedBy = userId,
            Grants = request.Grants.Distinct().Select(key => new GrantGroupGrant { GrantKey = key }).ToList()
        };

        await _grantGroupHandler.Add(group);
        return await GetById(organizationId, group.Id.GetValueOrDefault());
    }

    public async Task<GrantGroupDto> Update(Guid organizationId, Guid userId, Guid id, GrantGroupUpdateRequest request)
    {
        ValidateGrantKeys(request.Grants);

        GrantGroup existing = await _grantGroupHandler.GetById(organizationId, id);
        if (existing == null)
            throw new NotFoundAppException("GrantGroup", id);

        bool nameExists = await _grantGroupHandler.NameExists(organizationId, request.Name, excludeId: id);
        if (nameExists)
            throw new BusinessRuleException(ErrorCodes.DuplicateName, "Grant-grupa s ovim nazivom već postoji.");

        existing.Name = request.Name;
        existing.UpdatedAt = DateTimeOffset.UtcNow;
        existing.UpdatedBy = userId;

        await _grantGroupHandler.Update(existing, request.Grants.Distinct().ToList());
        return await GetById(organizationId, id);
    }

    public async Task Delete(Guid organizationId, Guid id)
    {
        GrantGroup existing = await _grantGroupHandler.GetById(organizationId, id);
        if (existing == null)
            throw new NotFoundAppException("GrantGroup", id);

        bool hasAssignedUsers = await _grantGroupHandler.HasAssignedUsers(organizationId, id);
        if (hasAssignedUsers)
            throw new BusinessRuleException(ErrorCodes.ReferencedCannotDelete, "Grant-grupa je dodijeljena korisnicima i ne može se obrisati — prvo im dodijelite drugu grupu.");

        await _grantGroupHandler.Delete(organizationId, id);
    }

    public async Task<List<Guid>> GetAssignedGrantGroupIds(Guid organizationId, Guid userId)
    {
        return await _grantGroupHandler.GetAssignedGrantGroupIds(organizationId, userId);
    }

    public async Task SetUserGrantGroups(Guid organizationId, Guid userId, AssignUserGrantGroupsRequest request)
    {
        List<Guid> distinctIds = request.GrantGroupIds.Distinct().ToList();
        List<GrantGroup> organizationGroups = await _grantGroupHandler.GetAll(organizationId);
        HashSet<Guid> validIds = organizationGroups.Select(g => g.Id.GetValueOrDefault()).ToHashSet();

        foreach (Guid grantGroupId in distinctIds)
            if (!validIds.Contains(grantGroupId))
                throw new NotFoundAppException("GrantGroup", grantGroupId);

        await _grantGroupHandler.SetUserGrantGroups(organizationId, userId, distinctIds);
    }

    private static void ValidateGrantKeys(List<string> grantKeys)
    {
        List<string> unknown = grantKeys.Where(key => !ValidGrantKeys.Contains(key)).ToList();
        if (unknown.Count > 0)
            throw new ValidationAppException($"Nepoznati grant-ključevi: {string.Join(", ", unknown)}.");
    }

    private static GrantGroupDto ToDto(GrantGroup group)
    {
        return new GrantGroupDto
        {
            Id = group.Id.GetValueOrDefault(),
            Name = group.Name,
            Grants = group.Grants.Select(g => g.GrantKey).OrderBy(k => k).ToList(),
            AssignedUserCount = group.UserGrantGroups.Count,
            CreatedAt = group.CreatedAt,
            UpdatedAt = group.UpdatedAt
        };
    }
}