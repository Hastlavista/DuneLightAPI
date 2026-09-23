using System;
using System.Linq;
using System.Threading.Tasks;
using BlueDragon.DuneLight.Core.Enums;
using BlueDragon.DuneLight.Infrastructure.Domain.Contexts;
using BlueDragon.DuneLight.Infrastructure.Domain.Models;
using BlueDragon.DuneLight.Infrastructure.Domain.Models.Permissions;
using BlueDragon.DuneLight.Infrastructure.Domain.Settings;
using BlueDragon.DuneLight.Infrastructure.Handlers.Implementations;
using Microsoft.EntityFrameworkCore;

namespace BlueDragon.DuneLight.UnitTests;

/// <summary>DB-backed verification for GrantGroupDto.AssignedUserCount semantics (Employee/Workforce UX Cleanup
/// Part D) — this is deliberately NOT a pure-in-memory test: the bug lives in the EF Core query/projection
/// (GrantGroupHandler.GetAll/GetById), so a mocked/in-memory provider would not exercise the actual filtered
/// Include translation. Connects to the same local Postgres instance the rest of local dev uses (see
/// DatabaseConfiguration "Local") and cleans up everything it creates in a dedicated, uniquely-named organization
/// so it never touches real seeded/demo data.</summary>
public class GrantGroupAssignedUserCountTests
{
    private const string LocalConnectionString = "Host=localhost;Database=postgres;Password=root1234;Username=postgres";

    private static DatabaseSettings Settings => new() { ConnectionString = LocalConnectionString };

    private static GrantGroupHandler CreateHandler() => new(Settings, defaultRoleTemplateHandler: null!, capabilityMaterializationService: null!);

    /// <summary>Creates an isolated organization for one test, with a cleanup callback that removes every row the
    /// test created (grant groups, grant-group grants, user-grant-groups, users, organization) — never anything
    /// pre-existing. Callers add users/grant-groups via the returned context helpers.</summary>
    private static async Task<(Guid OrganizationId, Func<Task> Cleanup)> CreateIsolatedOrganization(string testName)
    {
        Guid organizationId = Guid.NewGuid();
        await using DatabaseContext context = DatabaseContext.GenerateContext(LocalConnectionString);

        context.Organizations.Add(new Organization
        {
            Id = organizationId,
            Name = $"AssignedUserCountTest-{testName}",
            Slug = $"assigned-user-count-test-{organizationId:N}",
            CreatedAt = DateTimeOffset.UtcNow
        });
        await context.SaveChangesAsync();

        Func<Task> cleanup = async () =>
        {
            await using DatabaseContext cleanupContext = DatabaseContext.GenerateContext(LocalConnectionString);

            var groupIds = await cleanupContext.GrantGroups
                .Where(g => g.OrganizationId == organizationId)
                .Select(g => g.Id)
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

    private static async Task<Guid> AddUser(Guid organizationId, bool isActive, string emailPrefix)
    {
        await using DatabaseContext context = DatabaseContext.GenerateContext(LocalConnectionString);
        Guid userId = Guid.NewGuid();
        context.Users.Add(new User
        {
            Id = userId,
            OrganizationId = organizationId,
            Email = $"{emailPrefix}-{userId:N}@assigned-user-count-test.local",
            PasswordHash = "test-hash",
            ApiKey = $"test-api-key-{userId:N}",
            Role = UserRole.Member,
            IsActive = isActive,
            CreatedAt = DateTimeOffset.UtcNow
        });
        await context.SaveChangesAsync();
        return userId;
    }

    private static async Task<Guid> AddGrantGroup(Guid organizationId, string name)
    {
        await using DatabaseContext context = DatabaseContext.GenerateContext(LocalConnectionString);
        Guid groupId = Guid.NewGuid();
        context.GrantGroups.Add(new GrantGroup
        {
            Id = groupId,
            OrganizationId = organizationId,
            Name = name,
            CreatedAt = DateTimeOffset.UtcNow
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

    private static async Task RemoveAssignment(Guid userId, Guid grantGroupId)
    {
        await using DatabaseContext context = DatabaseContext.GenerateContext(LocalConnectionString);
        UserGrantGroup row = await context.UserGrantGroups.SingleAsync(u => u.UserId == userId && u.GrantGroupId == grantGroupId);
        context.UserGrantGroups.Remove(row);
        await context.SaveChangesAsync();
    }

    private static async Task SetUserActive(Guid userId, bool isActive)
    {
        await using DatabaseContext context = DatabaseContext.GenerateContext(LocalConnectionString);
        User user = await context.Users.SingleAsync(u => u.Id == userId);
        user.IsActive = isActive;
        await context.SaveChangesAsync();
    }

    private static async Task<int> AssignedUserCountOf(Guid organizationId, Guid grantGroupId)
    {
        GrantGroupHandler handler = CreateHandler();
        GrantGroup group = await handler.GetById(organizationId, grantGroupId);
        return group.UserGrantGroups.Count;
    }

    [Fact]
    public async Task ActiveAssignedUser_IsCounted()
    {
        (Guid organizationId, Func<Task> cleanup) = await CreateIsolatedOrganization(nameof(ActiveAssignedUser_IsCounted));
        try
        {
            Guid groupId = await AddGrantGroup(organizationId, "Trener");
            Guid userId = await AddUser(organizationId, isActive: true, "active");
            await Assign(userId, groupId);

            int count = await AssignedUserCountOf(organizationId, groupId);

            Assert.Equal(1, count);
        }
        finally
        {
            await cleanup();
        }
    }

    [Fact]
    public async Task InactiveAssignedUser_IsNotCounted()
    {
        (Guid organizationId, Func<Task> cleanup) = await CreateIsolatedOrganization(nameof(InactiveAssignedUser_IsNotCounted));
        try
        {
            Guid groupId = await AddGrantGroup(organizationId, "Trener");
            Guid userId = await AddUser(organizationId, isActive: false, "inactive");
            await Assign(userId, groupId);

            int count = await AssignedUserCountOf(organizationId, groupId);

            Assert.Equal(0, count);
        }
        finally
        {
            await cleanup();
        }
    }

    [Fact]
    public async Task ActiveAndInactiveAssignedUsers_OnlyActiveCounted()
    {
        (Guid organizationId, Func<Task> cleanup) = await CreateIsolatedOrganization(nameof(ActiveAndInactiveAssignedUsers_OnlyActiveCounted));
        try
        {
            Guid groupId = await AddGrantGroup(organizationId, "Recepcija");
            Guid activeUserId = await AddUser(organizationId, isActive: true, "active");
            Guid inactiveUserId = await AddUser(organizationId, isActive: false, "inactive");
            await Assign(activeUserId, groupId);
            await Assign(inactiveUserId, groupId);

            int count = await AssignedUserCountOf(organizationId, groupId);

            Assert.Equal(1, count);
        }
        finally
        {
            await cleanup();
        }
    }

    [Fact]
    public async Task SameActiveUser_AssignedToMultipleGroups_EachGroupCountedOnce()
    {
        (Guid organizationId, Func<Task> cleanup) = await CreateIsolatedOrganization(nameof(SameActiveUser_AssignedToMultipleGroups_EachGroupCountedOnce));
        try
        {
            Guid groupA = await AddGrantGroup(organizationId, "Admin");
            Guid groupB = await AddGrantGroup(organizationId, "Recepcija");
            Guid userId = await AddUser(organizationId, isActive: true, "multi");
            await Assign(userId, groupA);
            await Assign(userId, groupB);

            Assert.Equal(1, await AssignedUserCountOf(organizationId, groupA));
            Assert.Equal(1, await AssignedUserCountOf(organizationId, groupB));
        }
        finally
        {
            await cleanup();
        }
    }

    [Fact]
    public async Task Deactivation_DecrementsCount_WithoutDeletingAssignmentRow()
    {
        (Guid organizationId, Func<Task> cleanup) = await CreateIsolatedOrganization(nameof(Deactivation_DecrementsCount_WithoutDeletingAssignmentRow));
        try
        {
            Guid groupId = await AddGrantGroup(organizationId, "Trener");
            Guid userId = await AddUser(organizationId, isActive: true, "todeactivate");
            await Assign(userId, groupId);

            Assert.Equal(1, await AssignedUserCountOf(organizationId, groupId));

            await SetUserActive(userId, isActive: false);

            Assert.Equal(0, await AssignedUserCountOf(organizationId, groupId));

            await using DatabaseContext verifyContext = DatabaseContext.GenerateContext(LocalConnectionString);
            bool assignmentRowStillExists = await verifyContext.UserGrantGroups.AnyAsync(u => u.UserId == userId && u.GrantGroupId == groupId);
            Assert.True(assignmentRowStillExists, "Deactivation must not delete the UserGrantGroup assignment history row.");
        }
        finally
        {
            await cleanup();
        }
    }

    [Fact]
    public async Task Reactivation_RestoresCount()
    {
        (Guid organizationId, Func<Task> cleanup) = await CreateIsolatedOrganization(nameof(Reactivation_RestoresCount));
        try
        {
            Guid groupId = await AddGrantGroup(organizationId, "Trener");
            Guid userId = await AddUser(organizationId, isActive: true, "toreactivate");
            await Assign(userId, groupId);

            await SetUserActive(userId, isActive: false);
            Assert.Equal(0, await AssignedUserCountOf(organizationId, groupId));

            await SetUserActive(userId, isActive: true);
            Assert.Equal(1, await AssignedUserCountOf(organizationId, groupId));
        }
        finally
        {
            await cleanup();
        }
    }

    [Fact]
    public async Task RemovingAssignment_UpdatesCount()
    {
        (Guid organizationId, Func<Task> cleanup) = await CreateIsolatedOrganization(nameof(RemovingAssignment_UpdatesCount));
        try
        {
            Guid groupId = await AddGrantGroup(organizationId, "Trener");
            Guid userId = await AddUser(organizationId, isActive: true, "toremove");
            await Assign(userId, groupId);

            Assert.Equal(1, await AssignedUserCountOf(organizationId, groupId));

            await RemoveAssignment(userId, groupId);

            Assert.Equal(0, await AssignedUserCountOf(organizationId, groupId));
        }
        finally
        {
            await cleanup();
        }
    }

    [Fact]
    public async Task UnrelatedInactiveUsers_DoNotAffectOtherGroupsCount()
    {
        (Guid organizationId, Func<Task> cleanup) = await CreateIsolatedOrganization(nameof(UnrelatedInactiveUsers_DoNotAffectOtherGroupsCount));
        try
        {
            Guid groupWithActiveUser = await AddGrantGroup(organizationId, "Admin");
            Guid unrelatedGroup = await AddGrantGroup(organizationId, "Recepcija");

            Guid activeUserId = await AddUser(organizationId, isActive: true, "active");
            Guid unrelatedInactiveUserId = await AddUser(organizationId, isActive: false, "unrelated-inactive");

            await Assign(activeUserId, groupWithActiveUser);
            await Assign(unrelatedInactiveUserId, unrelatedGroup);

            Assert.Equal(1, await AssignedUserCountOf(organizationId, groupWithActiveUser));
            Assert.Equal(0, await AssignedUserCountOf(organizationId, unrelatedGroup));
        }
        finally
        {
            await cleanup();
        }
    }
}
