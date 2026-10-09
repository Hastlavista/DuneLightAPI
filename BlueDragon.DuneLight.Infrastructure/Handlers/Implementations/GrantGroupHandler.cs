using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using BlueDragon.DuneLight.Core.Shared;
using BlueDragon.DuneLight.Infrastructure.Domain.Contexts;
using BlueDragon.DuneLight.Infrastructure.Domain.Models;
using BlueDragon.DuneLight.Infrastructure.Domain.Models.Permissions;
using BlueDragon.DuneLight.Infrastructure.Domain.Settings;
using BlueDragon.DuneLight.Infrastructure.Handlers.Interfaces;
using BlueDragon.DuneLight.Infrastructure.UnitOfWork;
using Microsoft.EntityFrameworkCore;

namespace BlueDragon.DuneLight.Infrastructure.Handlers.Implementations;

public class GrantGroupHandler : IGrantGroupHandler
{
    private readonly DatabaseSettings _databaseSettings;
    private readonly TimeProvider _timeProvider;

    public GrantGroupHandler(DatabaseSettings databaseSettings, TimeProvider timeProvider)
    {
        _databaseSettings = databaseSettings;
        _timeProvider = timeProvider;
    }

    public async Task<List<GrantGroup>> GetAll(Guid organizationId)
    {
        await using DatabaseContext context = DatabaseContext.GenerateContext(_databaseSettings.ConnectionString);
        return await context.GrantGroups
            .Include(g => g.Grants)
            // AssignedUserCount mora odražavati SAMO trenutno aktivne korisnike (vidi GrantGroupDto.AssignedUserCount
            // napomenu) — filtrirani Include ovdje je namjerno umjesto učitavanja svih UserGrantGroup redaka, jer
            // deaktivacija zaposlenika NE briše UserGrantGroup redak (povijest dodjele se čuva), samo gasi User.IsActive.
            .Include(g => g.UserGrantGroups.Where(ugg => ugg.User.IsActive))
            .Where(g => g.OrganizationId == organizationId)
            .OrderBy(g => g.Name)
            .ToListAsync();
    }

    public async Task<GrantGroup> GetById(Guid organizationId, Guid id)
    {
        await using DatabaseContext context = DatabaseContext.GenerateContext(_databaseSettings.ConnectionString);
        return await context.GrantGroups
            .Include(g => g.Grants)
            // vidi napomenu u GetAll o filtriranom Include-u za AssignedUserCount
            .Include(g => g.UserGrantGroups.Where(ugg => ugg.User.IsActive))
            .SingleOrDefaultAsync(g => g.OrganizationId == organizationId && g.Id == id);
    }

    public async Task<Guid> CreateSystemAdminGroup(IUnitOfWork uow, Guid organizationId)
    {
        GrantGroup group = new GrantGroup
        {
            Id = Guid.NewGuid(),
            OrganizationId = organizationId,
            Name = SystemGrantGroups.AdminDisplayName,
            SystemKey = SystemGrantGroups.Admin,
            CreatedAt = _timeProvider.GetUtcNow(),
            Grants = Grants.Catalog.Select(g => new GrantGroupGrant { GrantKey = g.Key }).ToList()
        };

        uow.Context.GrantGroups.Add(group);
        await uow.Context.SaveChangesAsync();
        return group.Id.GetValueOrDefault();
    }

    public async Task<bool> NameExists(Guid organizationId, string name, Guid? excludeId)
    {
        await using DatabaseContext context = DatabaseContext.GenerateContext(_databaseSettings.ConnectionString);
        return await context.GrantGroups.AnyAsync(g =>
            g.OrganizationId == organizationId && g.Name == name && (excludeId == null || g.Id != excludeId));
    }

    public async Task Add(GrantGroup grantGroup)
    {
        await using DatabaseContext context = DatabaseContext.GenerateContext(_databaseSettings.ConnectionString);
        context.GrantGroups.Add(grantGroup);
        await context.SaveChangesAsync();
    }

    public async Task Update(GrantGroup grantGroup, List<string> newGrantKeys)
    {
        await using DatabaseContext context = DatabaseContext.GenerateContext(_databaseSettings.ConnectionString);

        GrantGroup tracked = await context.GrantGroups.SingleAsync(g => g.Id == grantGroup.Id && g.OrganizationId == grantGroup.OrganizationId);
        tracked.Name = grantGroup.Name;
        tracked.UpdatedAt = grantGroup.UpdatedAt;
        tracked.UpdatedBy = grantGroup.UpdatedBy;

        List<GrantGroupGrant> existing = await context.GrantGroupGrants
            .Where(g => g.GrantGroupId == grantGroup.Id)
            .ToListAsync();
        HashSet<string> existingKeys = existing.Select(g => g.GrantKey).ToHashSet();
        HashSet<string> newKeys = newGrantKeys.ToHashSet();

        foreach (GrantGroupGrant grant in existing)
            if (!newKeys.Contains(grant.GrantKey))
                context.GrantGroupGrants.Remove(grant);

        foreach (string key in newKeys)
            if (!existingKeys.Contains(key))
                context.GrantGroupGrants.Add(new GrantGroupGrant { GrantGroupId = grantGroup.Id.GetValueOrDefault(), GrantKey = key });

        await context.SaveChangesAsync();
    }

    public async Task<bool> HasAssignedUsers(Guid organizationId, Guid id)
    {
        await using DatabaseContext context = DatabaseContext.GenerateContext(_databaseSettings.ConnectionString);
        return await context.UserGrantGroups.AnyAsync(u => u.GrantGroupId == id && u.GrantGroup.OrganizationId == organizationId);
    }

    public async Task Delete(Guid organizationId, Guid id)
    {
        await using DatabaseContext context = DatabaseContext.GenerateContext(_databaseSettings.ConnectionString);
        GrantGroup grantGroup = await context.GrantGroups.SingleOrDefaultAsync(g => g.Id == id && g.OrganizationId == organizationId);
        if (grantGroup == null)
            return;

        context.GrantGroups.Remove(grantGroup);
        await context.SaveChangesAsync();
    }

    public async Task<List<Guid>> GetAssignedGrantGroupIds(Guid organizationId, Guid userId)
    {
        await using DatabaseContext context = DatabaseContext.GenerateContext(_databaseSettings.ConnectionString);
        return await context.UserGrantGroups
            .Where(u => u.UserId == userId && u.GrantGroup.OrganizationId == organizationId)
            .Select(u => u.GrantGroupId)
            .ToListAsync();
    }

    public async Task SetUserGrantGroups(Guid organizationId, Guid userId, List<Guid> grantGroupIds)
    {
        await using DatabaseContext context = DatabaseContext.GenerateContext(_databaseSettings.ConnectionString);

        List<UserGrantGroup> existing = await context.UserGrantGroups
            .Where(u => u.UserId == userId && u.GrantGroup.OrganizationId == organizationId)
            .ToListAsync();
        HashSet<Guid> existingIds = existing.Select(u => u.GrantGroupId).ToHashSet();
        HashSet<Guid> newIds = grantGroupIds.ToHashSet();

        foreach (UserGrantGroup userGrantGroup in existing)
            if (!newIds.Contains(userGrantGroup.GrantGroupId))
                context.UserGrantGroups.Remove(userGrantGroup);

        foreach (Guid grantGroupId in newIds)
            if (!existingIds.Contains(grantGroupId))
                context.UserGrantGroups.Add(new UserGrantGroup { UserId = userId, GrantGroupId = grantGroupId });

        await context.SaveChangesAsync();
    }

    public async Task<HashSet<string>> ResolveEffective(Guid organizationId, Guid userId)
    {
        await using DatabaseContext context = DatabaseContext.GenerateContext(_databaseSettings.ConnectionString);

        // Grant-only Tenant Authorization Refactor — nema Owner bypass-a. Organizacijski osnivač dobiva pun
        // pristup isključivo kroz dodjelu Admin starter GrantGroup-e pri registraciji (vidi AuthService.Register),
        // pa se ovdje uvijek računa isključivo iz UserGrantGroup -> GrantGroup -> GrantGroupGrant, bez posebnog
        // slučaja za bilo kojeg korisnika.
        List<string> keys = await context.UserGrantGroups
            .Where(ugg => ugg.UserId == userId && ugg.GrantGroup.OrganizationId == organizationId)
            .SelectMany(ugg => ugg.GrantGroup.Grants.Select(g => g.GrantKey))
            .Distinct()
            .ToListAsync();

        return keys.ToHashSet();
    }

    public async Task<Dictionary<Guid, List<string>>> GetGrantGroupNamesByUserIds(Guid organizationId, List<Guid> userIds)
    {
        await using DatabaseContext context = DatabaseContext.GenerateContext(_databaseSettings.ConnectionString);

        var rows = await context.UserGrantGroups
            .Where(ugg => userIds.Contains(ugg.UserId) && ugg.GrantGroup.OrganizationId == organizationId)
            .Select(ugg => new { ugg.UserId, ugg.GrantGroup.Name })
            .ToListAsync();

        return rows
            .GroupBy(r => r.UserId)
            .ToDictionary(g => g.Key, g => g.Select(r => r.Name).ToList());
    }

    public async Task<bool> HasActiveUserWithGrant(
        Guid organizationId,
        string grantKey,
        Guid? overrideGrantGroupId = null,
        HashSet<string> overrideGrantGroupGrants = null,
        Guid? overrideUserId = null,
        List<Guid> overrideUserGrantGroupIds = null)
    {
        await using DatabaseContext context = DatabaseContext.GenerateContext(_databaseSettings.ConnectionString);

        List<Guid> activeUserIds = await context.Users
            .Where(u => u.OrganizationId == organizationId && u.IsActive)
            .Select(u => u.Id.GetValueOrDefault())
            .ToListAsync();

        var assignmentRows = await context.UserGrantGroups
            .Where(ugg => ugg.GrantGroup.OrganizationId == organizationId)
            .Select(ugg => new { ugg.UserId, ugg.GrantGroupId })
            .ToListAsync();
        List<(Guid UserId, Guid GrantGroupId)> assignments = assignmentRows.Select(r => (r.UserId, r.GrantGroupId)).ToList();

        if (overrideUserId.HasValue)
        {
            assignments = assignments.Where(a => a.UserId != overrideUserId.Value).ToList();
            assignments.AddRange((overrideUserGrantGroupIds ?? new List<Guid>()).Select(gid => (overrideUserId.Value, gid)));
        }

        HashSet<Guid> activeUserIdSet = activeUserIds.ToHashSet();
        List<(Guid UserId, Guid GrantGroupId)> activeAssignments = assignments.Where(a => activeUserIdSet.Contains(a.UserId)).ToList();
        if (activeAssignments.Count == 0)
            return false;

        HashSet<Guid> referencedGroupIds = activeAssignments.Select(a => a.GrantGroupId).ToHashSet();

        var grantGroupGrantRows = await context.GrantGroupGrants
            .Where(g => referencedGroupIds.Contains(g.GrantGroupId))
            .Select(g => new { g.GrantGroupId, g.GrantKey })
            .ToListAsync();
        List<(Guid GrantGroupId, string GrantKey)> grantRows = grantGroupGrantRows.Select(r => (r.GrantGroupId, r.GrantKey)).ToList();

        if (overrideGrantGroupId.HasValue)
        {
            grantRows = grantRows.Where(g => g.GrantGroupId != overrideGrantGroupId.Value).ToList();
            grantRows.AddRange((overrideGrantGroupGrants ?? new HashSet<string>()).Select(key => (overrideGrantGroupId.Value, key)));
        }

        Dictionary<Guid, HashSet<string>> grantsByGroup = grantRows
            .GroupBy(g => g.GrantGroupId)
            .ToDictionary(g => g.Key, g => g.Select(r => r.GrantKey).ToHashSet());

        return activeAssignments.Any(a =>
            grantsByGroup.TryGetValue(a.GrantGroupId, out HashSet<string> grants) && grants.Contains(grantKey));
    }
}