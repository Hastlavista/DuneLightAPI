using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using BlueDragon.DuneLight.Core.Enums;
using BlueDragon.DuneLight.Core.Shared;
using BlueDragon.DuneLight.Infrastructure.Domain.Contexts;
using BlueDragon.DuneLight.Infrastructure.Domain.Models;
using BlueDragon.DuneLight.Infrastructure.Domain.Models.Permissions;
using BlueDragon.DuneLight.Infrastructure.Domain.Settings;
using BlueDragon.DuneLight.Infrastructure.Handlers.Implementations;
using Microsoft.EntityFrameworkCore;

namespace BlueDragon.DuneLight.UnitTests;

/// <summary>DB-backed verification for the Grant-only Tenant Authorization Refactor's last-permission-admin
/// lockout invariant (GrantGroupHandler.HasActiveUserWithGrant, consumed by
/// IPermissionAdministrationSafetyService). Deliberately NOT a pure in-memory test — the invariant spans three
/// joined tables (Users/UserGrantGroups/GrantGroupGrants), so a mock would not exercise the actual EF query.
/// Same isolated-organization pattern as GrantGroupAssignedUserCountTests.</summary>
public class PermissionAdministrationSafetyTests
{
    private const string LocalConnectionString = "Host=localhost;Database=postgres;Password=root1234;Username=postgres";

    private static GrantGroupHandler CreateHandler() => new(new DatabaseSettings { ConnectionString = LocalConnectionString }, defaultRoleTemplateHandler: null!, capabilityMaterializationService: null!);

    private static async Task<(Guid OrganizationId, Func<Task> Cleanup)> CreateIsolatedOrganization(string testName)
    {
        Guid organizationId = Guid.NewGuid();
        await using DatabaseContext context = DatabaseContext.GenerateContext(LocalConnectionString);

        context.Organizations.Add(new Organization
        {
            Id = organizationId,
            Name = $"PermissionAdminSafetyTest-{testName}",
            Slug = $"permission-admin-safety-test-{organizationId:N}",
            CreatedAt = DateTimeOffset.UtcNow
        });
        await context.SaveChangesAsync();

        Func<Task> cleanup = async () =>
        {
            await using DatabaseContext cleanupContext = DatabaseContext.GenerateContext(LocalConnectionString);

            List<Guid> groupIds = await cleanupContext.GrantGroups
                .Where(g => g.OrganizationId == organizationId)
                .Select(g => g.Id.GetValueOrDefault())
                .ToListAsync();

            cleanupContext.UserGrantGroups.RemoveRange(cleanupContext.UserGrantGroups.Where(ugg => groupIds.Contains(ugg.GrantGroupId)));
            cleanupContext.GrantGroupGrants.RemoveRange(cleanupContext.GrantGroupGrants.Where(g => groupIds.Contains(g.GrantGroupId)));
            await cleanupContext.SaveChangesAsync();

            cleanupContext.GrantGroups.RemoveRange(cleanupContext.GrantGroups.Where(g => g.OrganizationId == organizationId));
            cleanupContext.Users.RemoveRange(cleanupContext.Users.Where(u => u.OrganizationId == organizationId));
            await cleanupContext.SaveChangesAsync();

            cleanupContext.Organizations.RemoveRange(cleanupContext.Organizations.Where(o => o.Id == organizationId));
            await cleanupContext.SaveChangesAsync();
        };

        return (organizationId, cleanup);
    }

    private static async Task<Guid> AddUser(Guid organizationId, bool isActive, string emailPrefix, UserRole role = UserRole.Member)
    {
        await using DatabaseContext context = DatabaseContext.GenerateContext(LocalConnectionString);
        Guid userId = Guid.NewGuid();
        context.Users.Add(new User
        {
            Id = userId,
            OrganizationId = organizationId,
            Email = $"{emailPrefix}-{userId:N}@permission-admin-safety-test.local",
            PasswordHash = "test-hash",
            ApiKey = $"test-api-key-{userId:N}",
            Role = role,
            IsActive = isActive,
            CreatedAt = DateTimeOffset.UtcNow
        });
        await context.SaveChangesAsync();
        return userId;
    }

    private static async Task<Guid> AddGrantGroup(Guid organizationId, string name, params string[] grantKeys)
    {
        await using DatabaseContext context = DatabaseContext.GenerateContext(LocalConnectionString);
        Guid groupId = Guid.NewGuid();
        context.GrantGroups.Add(new GrantGroup
        {
            Id = groupId,
            OrganizationId = organizationId,
            Name = name,
            CreatedAt = DateTimeOffset.UtcNow,
            Grants = grantKeys.Select(k => new GrantGroupGrant { GrantKey = k }).ToList()
        });
        await context.SaveChangesAsync();
        return groupId;
    }

    private static async Task Assign(Guid userId, Guid grantGroupId)
    {
        await using DatabaseContext context = DatabaseContext.GenerateContext(LocalConnectionString);
        context.UserGrantGroups.Add(new UserGrantGroup { UserId = userId, GrantGroupId = grantGroupId });
        await context.SaveChangesAsync();
    }

    [Fact]
    public async Task OnlyActiveAdmin_CannotBeExcluded()
    {
        (Guid organizationId, Func<Task> cleanup) = await CreateIsolatedOrganization(nameof(OnlyActiveAdmin_CannotBeExcluded));
        try
        {
            Guid adminGroup = await AddGrantGroup(organizationId, "Master", Grants.PermissionsManage);
            Guid userId = await AddUser(organizationId, isActive: true, "sole-admin");
            await Assign(userId, adminGroup);

            GrantGroupHandler handler = CreateHandler();

            bool retainedBefore = await handler.HasActiveUserWithGrant(organizationId, Grants.PermissionsManage);
            Assert.True(retainedBefore);

            // Simulate deactivating this exact user (excludeUserId + empty assignment list).
            bool retainedIfExcluded = await handler.HasActiveUserWithGrant(
                organizationId, Grants.PermissionsManage, overrideUserId: userId, overrideUserGrantGroupIds: new List<Guid>());
            Assert.False(retainedIfExcluded);
        }
        finally
        {
            await cleanup();
        }
    }

    [Fact]
    public async Task OneOfMultipleAdmins_MayBeExcluded()
    {
        (Guid organizationId, Func<Task> cleanup) = await CreateIsolatedOrganization(nameof(OneOfMultipleAdmins_MayBeExcluded));
        try
        {
            Guid adminGroup = await AddGrantGroup(organizationId, "Direktor", Grants.PermissionsManage);
            Guid userA = await AddUser(organizationId, isActive: true, "admin-a");
            Guid userB = await AddUser(organizationId, isActive: true, "admin-b");
            await Assign(userA, adminGroup);
            await Assign(userB, adminGroup);

            GrantGroupHandler handler = CreateHandler();

            bool retainedIfAExcluded = await handler.HasActiveUserWithGrant(
                organizationId, Grants.PermissionsManage, overrideUserId: userA, overrideUserGrantGroupIds: new List<Guid>());
            Assert.True(retainedIfAExcluded);
        }
        finally
        {
            await cleanup();
        }
    }

    [Fact]
    public async Task LegacyUserRoleAdmin_HasNoEffectOnInvariant()
    {
        (Guid organizationId, Func<Task> cleanup) = await CreateIsolatedOrganization(nameof(LegacyUserRoleAdmin_HasNoEffectOnInvariant));
        try
        {
            // Legacy UserRole.Admin, but zero GrantGroup assignments at all - must NOT count as a permission admin.
            await AddUser(organizationId, isActive: true, "legacy-admin", role: UserRole.Admin);

            GrantGroupHandler handler = CreateHandler();
            bool retained = await handler.HasActiveUserWithGrant(organizationId, Grants.PermissionsManage);

            Assert.False(retained);
        }
        finally
        {
            await cleanup();
        }
    }

    [Fact]
    public async Task CustomGrantGroupName_StillCountsIfItHoldsTheGrant()
    {
        (Guid organizationId, Func<Task> cleanup) = await CreateIsolatedOrganization(nameof(CustomGrantGroupName_StillCountsIfItHoldsTheGrant));
        try
        {
            // Deliberately NOT named "Admin" - name must have zero bearing on the invariant.
            Guid group = await AddGrantGroup(organizationId, "Šef smjene", Grants.PermissionsManage);
            Guid userId = await AddUser(organizationId, isActive: true, "custom-named-group-user");
            await Assign(userId, group);

            GrantGroupHandler handler = CreateHandler();
            bool retained = await handler.HasActiveUserWithGrant(organizationId, Grants.PermissionsManage);

            Assert.True(retained);
        }
        finally
        {
            await cleanup();
        }
    }

    [Fact]
    public async Task InactiveUser_DoesNotSatisfyInvariantEvenWithGrant()
    {
        (Guid organizationId, Func<Task> cleanup) = await CreateIsolatedOrganization(nameof(InactiveUser_DoesNotSatisfyInvariantEvenWithGrant));
        try
        {
            Guid group = await AddGrantGroup(organizationId, "Admin", Grants.PermissionsManage);
            Guid userId = await AddUser(organizationId, isActive: false, "inactive-admin");
            await Assign(userId, group);

            GrantGroupHandler handler = CreateHandler();
            bool retained = await handler.HasActiveUserWithGrant(organizationId, Grants.PermissionsManage);

            Assert.False(retained);
        }
        finally
        {
            await cleanup();
        }
    }

    [Fact]
    public async Task OverridingGrantGroupGrants_SimulatesRemovingPermissionsManage()
    {
        (Guid organizationId, Func<Task> cleanup) = await CreateIsolatedOrganization(nameof(OverridingGrantGroupGrants_SimulatesRemovingPermissionsManage));
        try
        {
            Guid group = await AddGrantGroup(organizationId, "Admin", Grants.PermissionsManage, "employees.manage");
            Guid userId = await AddUser(organizationId, isActive: true, "group-edit-user");
            await Assign(userId, group);

            GrantGroupHandler handler = CreateHandler();

            // Simulate editing the group to keep employees.manage but drop permissions.manage.
            bool retainedAfterEdit = await handler.HasActiveUserWithGrant(
                organizationId, Grants.PermissionsManage,
                overrideGrantGroupId: group, overrideGrantGroupGrants: new HashSet<string> { "employees.manage" });

            Assert.False(retainedAfterEdit);

            // Simulate deleting the group entirely (empty grant set) - same effect on this invariant.
            bool retainedAfterDelete = await handler.HasActiveUserWithGrant(
                organizationId, Grants.PermissionsManage,
                overrideGrantGroupId: group, overrideGrantGroupGrants: new HashSet<string>());

            Assert.False(retainedAfterDelete);
        }
        finally
        {
            await cleanup();
        }
    }
}
