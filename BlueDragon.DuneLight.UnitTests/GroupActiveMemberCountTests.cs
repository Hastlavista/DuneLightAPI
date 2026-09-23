using System;
using System.Linq;
using System.Threading.Tasks;
using BlueDragon.DuneLight.Core.Enums;
using BlueDragon.DuneLight.Infrastructure.Domain.Contexts;
using BlueDragon.DuneLight.Infrastructure.Domain.Models;
using BlueDragon.DuneLight.Infrastructure.Domain.Models.Catalog;
using BlueDragon.DuneLight.Infrastructure.Domain.Models.Clients;
using BlueDragon.DuneLight.Infrastructure.Domain.Models.Groups;
using BlueDragon.DuneLight.Infrastructure.Domain.Settings;
using BlueDragon.DuneLight.Infrastructure.Handlers.Implementations;
using Microsoft.EntityFrameworkCore;

namespace BlueDragon.DuneLight.UnitTests;

/// <summary>DB-backed verification for GroupHandler.CountActiveMembers (Data/Lifecycle Consistency Cleanup) —
/// same bug class the Authorization Cleanup already fixed for GrantGroup.AssignedUserCount: a GroupMember row
/// staying IsActive=true after its Client is deactivated/anonymized must not keep occupying group capacity.
/// Deliberately DB-backed (EF query, not mockable), same isolated-organization pattern as
/// GrantGroupAssignedUserCountTests/PermissionAdministrationSafetyTests.</summary>
public class GroupActiveMemberCountTests
{
    private const string LocalConnectionString = "Host=localhost;Database=postgres;Password=root1234;Username=postgres";

    private static GroupHandler CreateHandler() => new(new DatabaseSettings { ConnectionString = LocalConnectionString });

    private static async Task<(Guid OrganizationId, Guid CompanyId, Guid ServiceId, Guid GroupId, Func<Task> Cleanup)> CreateIsolatedFixture(string testName)
    {
        Guid organizationId = Guid.NewGuid();
        Guid companyId = Guid.NewGuid();
        Guid serviceId = Guid.NewGuid();
        Guid groupId = Guid.NewGuid();

        await using DatabaseContext context = DatabaseContext.GenerateContext(LocalConnectionString);

        context.Organizations.Add(new Organization
        {
            Id = organizationId,
            Name = $"GroupActiveMemberCountTest-{testName}",
            Slug = $"group-active-member-count-test-{organizationId:N}",
            CreatedAt = DateTimeOffset.UtcNow
        });
        await context.SaveChangesAsync();

        context.Companies.Add(new Company
        {
            Id = companyId,
            OrganizationId = organizationId,
            Name = "Test Company",
            Country = "HR",
            IsActive = true
        });
        context.Services.Add(new Service
        {
            Id = serviceId,
            OrganizationId = organizationId,
            Name = "Test Group Service",
            ExecutionMode = ServiceExecutionMode.Group,
            DefaultDurationMinutes = 60,
            DefaultPrice = 0,
            IsActive = true,
            SortOrder = 0,
            CreatedAt = DateTimeOffset.UtcNow
        });
        await context.SaveChangesAsync();

        context.Groups.Add(new Group
        {
            Id = groupId,
            OrganizationId = organizationId,
            Name = "Test Group",
            ServiceId = serviceId,
            CompanyId = companyId,
            Capacity = 10,
            IsActive = true
        });
        await context.SaveChangesAsync();

        Func<Task> cleanup = async () =>
        {
            await using DatabaseContext cleanupContext = DatabaseContext.GenerateContext(LocalConnectionString);

            cleanupContext.GroupMembers.RemoveRange(cleanupContext.GroupMembers.Where(m => m.GroupId == groupId));
            await cleanupContext.SaveChangesAsync();

            cleanupContext.Groups.RemoveRange(cleanupContext.Groups.Where(g => g.Id == groupId));
            await cleanupContext.SaveChangesAsync();

            cleanupContext.Clients.RemoveRange(cleanupContext.Clients.Where(c => c.OrganizationId == organizationId));
            cleanupContext.Services.RemoveRange(cleanupContext.Services.Where(s => s.Id == serviceId));
            cleanupContext.Companies.RemoveRange(cleanupContext.Companies.Where(c => c.Id == companyId));
            await cleanupContext.SaveChangesAsync();

            cleanupContext.Organizations.RemoveRange(cleanupContext.Organizations.Where(o => o.Id == organizationId));
            await cleanupContext.SaveChangesAsync();
        };

        return (organizationId, companyId, serviceId, groupId, cleanup);
    }

    private static async Task<Guid> AddClient(Guid organizationId, bool isActive, bool isAnonymized, string prefix)
    {
        await using DatabaseContext context = DatabaseContext.GenerateContext(LocalConnectionString);
        Guid clientId = Guid.NewGuid();
        context.Clients.Add(new Client
        {
            Id = clientId,
            OrganizationId = organizationId,
            MemberNumber = new Random().Next(1_000_000, int.MaxValue),
            FirstName = prefix,
            LastName = "Test",
            IsActive = isActive,
            IsAnonymized = isAnonymized,
            GdprConsentGiven = true,
            CreatedAt = DateTimeOffset.UtcNow
        });
        await context.SaveChangesAsync();
        return clientId;
    }

    private static async Task AddMember(Guid groupId, Guid clientId, bool memberIsActive)
    {
        await using DatabaseContext context = DatabaseContext.GenerateContext(LocalConnectionString);
        context.GroupMembers.Add(new GroupMember
        {
            GroupId = groupId,
            ClientId = clientId,
            JoinedAt = DateTimeOffset.UtcNow,
            IsActive = memberIsActive,
            CreatedAt = DateTimeOffset.UtcNow
        });
        await context.SaveChangesAsync();
    }

    [Fact]
    public async Task ActiveMember_ActiveClient_IsCounted()
    {
        (Guid organizationId, _, _, Guid groupId, Func<Task> cleanup) = await CreateIsolatedFixture(nameof(ActiveMember_ActiveClient_IsCounted));
        try
        {
            Guid clientId = await AddClient(organizationId, isActive: true, isAnonymized: false, "Active");
            await AddMember(groupId, clientId, memberIsActive: true);

            int count = await CreateHandler().CountActiveMembers(groupId);

            Assert.Equal(1, count);
        }
        finally
        {
            await cleanup();
        }
    }

    [Fact]
    public async Task ActiveMembership_InactiveClient_IsNotCounted()
    {
        (Guid organizationId, _, _, Guid groupId, Func<Task> cleanup) = await CreateIsolatedFixture(nameof(ActiveMembership_InactiveClient_IsNotCounted));
        try
        {
            Guid clientId = await AddClient(organizationId, isActive: false, isAnonymized: false, "Inactive");
            await AddMember(groupId, clientId, memberIsActive: true);

            int count = await CreateHandler().CountActiveMembers(groupId);

            Assert.Equal(0, count);
        }
        finally
        {
            await cleanup();
        }
    }

    [Fact]
    public async Task ActiveMembership_AnonymizedClient_IsNotCounted()
    {
        (Guid organizationId, _, _, Guid groupId, Func<Task> cleanup) = await CreateIsolatedFixture(nameof(ActiveMembership_AnonymizedClient_IsNotCounted));
        try
        {
            // Anonymize implies IsActive=false in production (ClientHandler.Anonymize), but this test sets them
            // independently to prove the count checks IsAnonymized too, not just IsActive.
            Guid clientId = await AddClient(organizationId, isActive: true, isAnonymized: true, "Anonymized");
            await AddMember(groupId, clientId, memberIsActive: true);

            int count = await CreateHandler().CountActiveMembers(groupId);

            Assert.Equal(0, count);
        }
        finally
        {
            await cleanup();
        }
    }

    [Fact]
    public async Task InactiveMembership_ActiveClient_IsNotCounted()
    {
        (Guid organizationId, _, _, Guid groupId, Func<Task> cleanup) = await CreateIsolatedFixture(nameof(InactiveMembership_ActiveClient_IsNotCounted));
        try
        {
            Guid clientId = await AddClient(organizationId, isActive: true, isAnonymized: false, "RemovedMember");
            await AddMember(groupId, clientId, memberIsActive: false);

            int count = await CreateHandler().CountActiveMembers(groupId);

            Assert.Equal(0, count);
        }
        finally
        {
            await cleanup();
        }
    }

    [Fact]
    public async Task MixedMembers_OnlyActiveMembershipWithActiveClientCounted()
    {
        (Guid organizationId, _, _, Guid groupId, Func<Task> cleanup) = await CreateIsolatedFixture(nameof(MixedMembers_OnlyActiveMembershipWithActiveClientCounted));
        try
        {
            Guid activeOk = await AddClient(organizationId, isActive: true, isAnonymized: false, "Ok1");
            Guid activeOk2 = await AddClient(organizationId, isActive: true, isAnonymized: false, "Ok2");
            Guid inactiveClient = await AddClient(organizationId, isActive: false, isAnonymized: false, "Inactive");
            Guid anonymizedClient = await AddClient(organizationId, isActive: false, isAnonymized: true, "Anon");

            await AddMember(groupId, activeOk, memberIsActive: true);
            await AddMember(groupId, activeOk2, memberIsActive: true);
            await AddMember(groupId, inactiveClient, memberIsActive: true);
            await AddMember(groupId, anonymizedClient, memberIsActive: true);

            int count = await CreateHandler().CountActiveMembers(groupId);

            Assert.Equal(2, count);
        }
        finally
        {
            await cleanup();
        }
    }
}
