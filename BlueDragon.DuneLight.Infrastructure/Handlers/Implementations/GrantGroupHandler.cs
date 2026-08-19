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

    public GrantGroupHandler(DatabaseSettings databaseSettings)
    {
        _databaseSettings = databaseSettings;
    }

    public async Task<List<GrantGroup>> GetAll(Guid organizationId)
    {
        await using DatabaseContext context = DatabaseContext.GenerateContext(_databaseSettings.ConnectionString);
        return await context.GrantGroups
            .Include(g => g.Grants)
            .Include(g => g.UserGrantGroups)
            .Where(g => g.OrganizationId == organizationId)
            .OrderBy(g => g.Name)
            .ToListAsync();
    }

    public async Task<GrantGroup> GetById(Guid organizationId, Guid id)
    {
        await using DatabaseContext context = DatabaseContext.GenerateContext(_databaseSettings.ConnectionString);
        return await context.GrantGroups
            .Include(g => g.Grants)
            .Include(g => g.UserGrantGroups)
            .SingleOrDefaultAsync(g => g.OrganizationId == organizationId && g.Id == id);
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

    public async Task SeedDefaultGroups(IUnitOfWork uow, Guid organizationId)
    {
        List<GrantGroup> groups = new()
        {
            ToDefaultGroup(organizationId, DefaultGrantGroups.AdminGroupName, DefaultGrantGroups.AdminGrants),
            ToDefaultGroup(organizationId, DefaultGrantGroups.TrainerGroupName, DefaultGrantGroups.TrainerGrants),
            ToDefaultGroup(organizationId, DefaultGrantGroups.ReceptionGroupName, DefaultGrantGroups.ReceptionGrants)
        };

        uow.Context.GrantGroups.AddRange(groups);
        await uow.Context.SaveChangesAsync();
    }

    private static GrantGroup ToDefaultGroup(Guid organizationId, string name, IReadOnlyList<string> grantKeys)
    {
        return new GrantGroup
        {
            Id = Guid.NewGuid(),
            OrganizationId = organizationId,
            Name = name,
            CreatedAt = DateTimeOffset.UtcNow,
            Grants = grantKeys.Select(key => new GrantGroupGrant { GrantKey = key }).ToList()
        };
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

    public async Task<(bool IsOwner, HashSet<string> Grants)> ResolveEffective(Guid organizationId, Guid userId)
    {
        await using DatabaseContext context = DatabaseContext.GenerateContext(_databaseSettings.ConnectionString);

        User user = await context.Users.SingleOrDefaultAsync(u => u.Id == userId && u.OrganizationId == organizationId);
        if (user == null)
            return (false, new HashSet<string>());

        if (user.IsOwner)
            return (true, new HashSet<string>());

        List<string> keys = await context.UserGrantGroups
            .Where(ugg => ugg.UserId == userId)
            .SelectMany(ugg => ugg.GrantGroup.Grants.Select(g => g.GrantKey))
            .Distinct()
            .ToListAsync();

        return (false, keys.ToHashSet());
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
}