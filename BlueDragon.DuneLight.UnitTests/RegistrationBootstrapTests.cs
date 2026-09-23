using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using BlueDragon.DuneLight.Core.Shared;
using BlueDragon.DuneLight.Infrastructure.Domain.Contexts;
using BlueDragon.DuneLight.Infrastructure.Domain.Models;
using BlueDragon.DuneLight.Infrastructure.Domain.Settings;
using BlueDragon.DuneLight.Infrastructure.Handlers.Implementations;
using BlueDragon.DuneLight.Infrastructure.Services;
using BlueDragon.DuneLight.Infrastructure.UnitOfWork;
using Microsoft.EntityFrameworkCore;

namespace BlueDragon.DuneLight.UnitTests;

/// <summary>DB-backed verification for Grant-only Tenant Authorization Refactor's registration bootstrap
/// (GrantGroupHandler.EnsureDefaultGrantGroups, the piece AuthService.Register calls) - a new organization gets
/// EXACTLY ONE automatically-created GrantGroup ("Admin", materialized from the latest active "admin"
/// DefaultRoleTemplate), never Trener/Recepcija, and that group already carries permissions.manage. Residual
/// IsOwner Removal - this whole mechanism is now the ONLY way a new organization's creator gets any access at
/// all; there is no User.IsOwner bypass left to fall back on. Same isolated-organization pattern as the other
/// DB-backed tests in this project.</summary>
public class RegistrationBootstrapTests
{
    private const string LocalConnectionString = "Host=localhost;Database=postgres;Password=root1234;Username=postgres";

    private static GrantGroupHandler CreateHandler() => new(
        new DatabaseSettings { ConnectionString = LocalConnectionString },
        new DefaultRoleTemplateHandler(new DatabaseSettings { ConnectionString = LocalConnectionString }),
        new CapabilityMaterializationService());

    [Fact]
    public async Task EnsureDefaultGrantGroups_CreatesExactlyOneAdminGroup_WithPermissionsManage()
    {
        Guid organizationId = Guid.NewGuid();
        await using DatabaseContext context = DatabaseContext.GenerateContext(LocalConnectionString);

        context.Organizations.Add(new Organization
        {
            Id = organizationId,
            Name = "RegistrationBootstrapTest",
            Slug = $"registration-bootstrap-test-{organizationId:N}",
            CreatedAt = DateTimeOffset.UtcNow
        });
        await context.SaveChangesAsync();

        try
        {
            GrantGroupHandler handler = CreateHandler();

            await using IUnitOfWork uow = await new UnitOfWorkFactory(new DatabaseSettings { ConnectionString = LocalConnectionString }).Begin();
            Guid? adminGroupId = await handler.EnsureDefaultGrantGroups(uow, organizationId);
            await uow.CommitAsync();

            Assert.NotNull(adminGroupId);

            await using DatabaseContext verifyContext = DatabaseContext.GenerateContext(LocalConnectionString);

            // Exactly one GrantGroup - never Trener/Recepcija for a new organization.
            var groups = await verifyContext.GrantGroups
                .Where(g => g.OrganizationId == organizationId)
                .Select(g => g.Name)
                .ToListAsync();
            Assert.Equal(new[] { "Admin" }, groups);

            HashSet<string> grants = await handler.ResolveEffective(organizationId, Guid.NewGuid());
            // A brand-new, unassigned user has nothing - sanity check the group itself, not an assignment.
            Assert.Empty(grants);

            List<string> adminGrants = await verifyContext.GrantGroupGrants
                .Where(g => g.GrantGroupId == adminGroupId.Value)
                .Select(g => g.GrantKey)
                .ToListAsync();
            Assert.Contains(Grants.PermissionsManage, adminGrants);
            Assert.Contains(Grants.PermissionsView, adminGrants);
            Assert.Contains(Grants.PermissionsAssignmentsManage, adminGrants);
        }
        finally
        {
            await using DatabaseContext cleanupContext = DatabaseContext.GenerateContext(LocalConnectionString);

            var groupIds = await cleanupContext.GrantGroups
                .Where(g => g.OrganizationId == organizationId)
                .Select(g => g.Id.GetValueOrDefault())
                .ToListAsync();

            cleanupContext.GrantGroupGrants.RemoveRange(cleanupContext.GrantGroupGrants.Where(g => groupIds.Contains(g.GrantGroupId)));
            cleanupContext.GrantGroupCapabilitySnapshots.RemoveRange(cleanupContext.GrantGroupCapabilitySnapshots.Where(s => groupIds.Contains(s.GrantGroupId)));
            cleanupContext.GrantGroupTemplateGrants.RemoveRange(cleanupContext.GrantGroupTemplateGrants.Where(g => groupIds.Contains(g.GrantGroupId)));
            await cleanupContext.SaveChangesAsync();

            cleanupContext.GrantGroups.RemoveRange(cleanupContext.GrantGroups.Where(g => g.OrganizationId == organizationId));
            await cleanupContext.SaveChangesAsync();

            cleanupContext.Organizations.RemoveRange(cleanupContext.Organizations.Where(o => o.Id == organizationId));
            await cleanupContext.SaveChangesAsync();
        }
    }

    [Fact]
    public async Task EnsureDefaultGrantGroups_IsIdempotent_ReturnsSameGroupOnSecondCall()
    {
        Guid organizationId = Guid.NewGuid();
        await using DatabaseContext context = DatabaseContext.GenerateContext(LocalConnectionString);

        context.Organizations.Add(new Organization
        {
            Id = organizationId,
            Name = "RegistrationBootstrapIdempotentTest",
            Slug = $"registration-bootstrap-idempotent-test-{organizationId:N}",
            CreatedAt = DateTimeOffset.UtcNow
        });
        await context.SaveChangesAsync();

        try
        {
            GrantGroupHandler handler = CreateHandler();

            await using IUnitOfWork uow1 = await new UnitOfWorkFactory(new DatabaseSettings { ConnectionString = LocalConnectionString }).Begin();
            Guid? firstCallGroupId = await handler.EnsureDefaultGrantGroups(uow1, organizationId);
            await uow1.CommitAsync();

            await using IUnitOfWork uow2 = await new UnitOfWorkFactory(new DatabaseSettings { ConnectionString = LocalConnectionString }).Begin();
            Guid? secondCallGroupId = await handler.EnsureDefaultGrantGroups(uow2, organizationId);
            await uow2.CommitAsync();

            Assert.Equal(firstCallGroupId, secondCallGroupId);

            await using DatabaseContext verifyContext = DatabaseContext.GenerateContext(LocalConnectionString);
            int groupCount = await verifyContext.GrantGroups.CountAsync(g => g.OrganizationId == organizationId);
            Assert.Equal(1, groupCount);
        }
        finally
        {
            await using DatabaseContext cleanupContext = DatabaseContext.GenerateContext(LocalConnectionString);

            var groupIds = await cleanupContext.GrantGroups
                .Where(g => g.OrganizationId == organizationId)
                .Select(g => g.Id.GetValueOrDefault())
                .ToListAsync();

            cleanupContext.GrantGroupGrants.RemoveRange(cleanupContext.GrantGroupGrants.Where(g => groupIds.Contains(g.GrantGroupId)));
            cleanupContext.GrantGroupCapabilitySnapshots.RemoveRange(cleanupContext.GrantGroupCapabilitySnapshots.Where(s => groupIds.Contains(s.GrantGroupId)));
            cleanupContext.GrantGroupTemplateGrants.RemoveRange(cleanupContext.GrantGroupTemplateGrants.Where(g => groupIds.Contains(g.GrantGroupId)));
            await cleanupContext.SaveChangesAsync();

            cleanupContext.GrantGroups.RemoveRange(cleanupContext.GrantGroups.Where(g => g.OrganizationId == organizationId));
            await cleanupContext.SaveChangesAsync();

            cleanupContext.Organizations.RemoveRange(cleanupContext.Organizations.Where(o => o.Id == organizationId));
            await cleanupContext.SaveChangesAsync();
        }
    }
}
