using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using BlueDragon.DuneLight.Infrastructure.Domain.Contexts;
using BlueDragon.DuneLight.Infrastructure.Domain.Models.Permissions;
using BlueDragon.DuneLight.Infrastructure.Domain.Settings;
using BlueDragon.DuneLight.Infrastructure.Handlers.Interfaces;
using Microsoft.EntityFrameworkCore;

namespace BlueDragon.DuneLight.Infrastructure.Handlers.Implementations;

public class RoleHandler : IRoleHandler
{
    private readonly DatabaseSettings _databaseSettings;

    public RoleHandler(DatabaseSettings databaseSettings)
    {
        _databaseSettings = databaseSettings;
    }

    public async Task<List<Role>> GetAll(Guid organizationId)
    {
        await using DatabaseContext context = DatabaseContext.GenerateContext(_databaseSettings.ConnectionString);
        return await context.Roles
            .Where(r => r.OrganizationId == organizationId)
            .OrderBy(r => r.Name)
            .ToListAsync();
    }

    public async Task<Role> GetById(Guid organizationId, Guid id)
    {
        await using DatabaseContext context = DatabaseContext.GenerateContext(_databaseSettings.ConnectionString);
        return await context.Roles.SingleOrDefaultAsync(r => r.OrganizationId == organizationId && r.Id == id);
    }

    public async Task<bool> NameExists(Guid organizationId, string name, Guid? excludeId)
    {
        await using DatabaseContext context = DatabaseContext.GenerateContext(_databaseSettings.ConnectionString);
        return await context.Roles.AnyAsync(r =>
            r.OrganizationId == organizationId && r.Name == name && (excludeId == null || r.Id != excludeId));
    }

    public async Task Add(Role role)
    {
        await using DatabaseContext context = DatabaseContext.GenerateContext(_databaseSettings.ConnectionString);
        context.Roles.Add(role);
        await context.SaveChangesAsync();
    }

    public async Task Update(Role role)
    {
        await using DatabaseContext context = DatabaseContext.GenerateContext(_databaseSettings.ConnectionString);
        Role tracked = await context.Roles.SingleAsync(r => r.Id == role.Id && r.OrganizationId == role.OrganizationId);
        tracked.Name = role.Name;
        await context.SaveChangesAsync();
    }

    public async Task Delete(Guid organizationId, Guid id)
    {
        await using DatabaseContext context = DatabaseContext.GenerateContext(_databaseSettings.ConnectionString);
        Role role = await context.Roles.SingleOrDefaultAsync(r => r.Id == id && r.OrganizationId == organizationId);
        if (role == null)
            return;

        context.Roles.Remove(role);
        await context.SaveChangesAsync();
    }

    public async Task<List<Guid>> GetAssignedRoleIds(Guid organizationId, Guid userId)
    {
        await using DatabaseContext context = DatabaseContext.GenerateContext(_databaseSettings.ConnectionString);
        return await context.UserRoleAssignments
            .Where(u => u.UserId == userId && u.Role.OrganizationId == organizationId)
            .Select(u => u.RoleId)
            .ToListAsync();
    }

    public async Task SetUserRoles(Guid organizationId, Guid userId, List<Guid> roleIds)
    {
        await using DatabaseContext context = DatabaseContext.GenerateContext(_databaseSettings.ConnectionString);

        List<UserRoleAssignment> existing = await context.UserRoleAssignments
            .Where(u => u.UserId == userId && u.Role.OrganizationId == organizationId)
            .ToListAsync();
        HashSet<Guid> existingIds = existing.Select(u => u.RoleId).ToHashSet();
        HashSet<Guid> newIds = roleIds.ToHashSet();

        foreach (UserRoleAssignment assignment in existing)
            if (!newIds.Contains(assignment.RoleId))
                context.UserRoleAssignments.Remove(assignment);

        foreach (Guid roleId in newIds)
            if (!existingIds.Contains(roleId))
                context.UserRoleAssignments.Add(new UserRoleAssignment { UserId = userId, RoleId = roleId });

        await context.SaveChangesAsync();
    }

    public async Task<Dictionary<Guid, List<string>>> GetRoleNamesByUserIds(Guid organizationId, List<Guid> userIds)
    {
        await using DatabaseContext context = DatabaseContext.GenerateContext(_databaseSettings.ConnectionString);

        var rows = await context.UserRoleAssignments
            .Where(u => userIds.Contains(u.UserId) && u.Role.OrganizationId == organizationId)
            .Select(u => new { u.UserId, u.Role.Name })
            .ToListAsync();

        return rows
            .GroupBy(r => r.UserId)
            .ToDictionary(g => g.Key, g => g.Select(r => r.Name).ToList());
    }
}
