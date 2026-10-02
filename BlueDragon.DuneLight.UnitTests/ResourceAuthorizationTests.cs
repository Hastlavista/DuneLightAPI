#nullable disable
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using BlueDragon.DuneLight.API.Authorization;
using BlueDragon.DuneLight.API.Controllers.Catalog;
using BlueDragon.DuneLight.Core.Enums;
using BlueDragon.DuneLight.Core.Interfaces.Capabilities;
using BlueDragon.DuneLight.Core.Services;
using BlueDragon.DuneLight.Core.Shared;
using BlueDragon.DuneLight.DatabaseMigration.Migrations._2026;
using BlueDragon.DuneLight.Infrastructure.Domain.Contexts;
using BlueDragon.DuneLight.Infrastructure.Domain.Models;
using BlueDragon.DuneLight.Infrastructure.Domain.Settings;
using BlueDragon.DuneLight.Infrastructure.Handlers.Implementations;
using BlueDragon.DuneLight.Infrastructure.Services;
using BlueDragon.DuneLight.Infrastructure.UnitOfWork;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace BlueDragon.DuneLight.UnitTests;

/// <summary>
/// Resource catalog authorization (Phase C): grant-only endpoints (catalog.resources.view / catalog.resources.manage,
/// mirroring catalog.rooms.*), one new capability (catalog.resources.manage) and a new Admin template version (v4 =
/// v3 + that capability at Manage), all through the existing capability/template mechanism.
/// </summary>
public class ResourceAuthorizationTests
{
    private const string LocalConnectionString = "Host=localhost;Database=postgres;Password=root1234;Username=postgres";

    #region Endpoints

    public static IEnumerable<object[]> CatalogControllers() => new[]
    {
        new object[] { typeof(ResourcesController), Grants.CatalogResourcesView, Grants.CatalogResourcesManage },
        new object[] { typeof(RoomsController), Grants.CatalogRoomsView, Grants.CatalogRoomsManage },
    };

    [Theory]
    [MemberData(nameof(CatalogControllers))]
    public void EveryAction_RequiresExactlyTheViewOrManageGrant(Type controller, string viewGrant, string manageGrant)
    {
        List<MethodInfo> actions = controller.GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.DeclaredOnly).ToList();
        Assert.Equal(7, actions.Count); // list, get, create, update, activate, deactivate, delete

        foreach (MethodInfo action in actions)
        {
            RequireGrantAttribute attribute = action.GetCustomAttribute<RequireGrantAttribute>();
            Assert.NotNull(attribute);
            string expected = action.GetCustomAttribute<HttpGetAttribute>() != null ? viewGrant : manageGrant;
            Assert.Equal(new[] { expected }, attribute.Grants);

            // Grant-only: no other authorization attribute (role/owner/assigned-company) on the action or controller.
            Assert.DoesNotContain(action.GetCustomAttributes().Concat(controller.GetCustomAttributes()),
                a => a is not RequireGrantAttribute && a.GetType().Name.Contains("Require", StringComparison.Ordinal));
            Assert.DoesNotContain(action.GetCustomAttributes().Concat(controller.GetCustomAttributes()),
                a => a.GetType().Name.StartsWith("Authorize", StringComparison.Ordinal));
        }
    }

    #endregion

    #region Grant catalog and default groups

    [Fact]
    public void ResourceGrants_AreInTheCatalog_AndOnlyTheAdminDefaultGroupHasThem()
    {
        List<string> catalog = Grants.Catalog.Select(g => g.Key).ToList();
        Assert.Contains("catalog.resources.view", catalog);
        Assert.Contains("catalog.resources.manage", catalog);
        Assert.Equal(catalog.Count, catalog.Distinct().Count());

        Assert.Contains(Grants.CatalogResourcesView, DefaultGrantGroups.AdminGrants);
        Assert.Contains(Grants.CatalogResourcesManage, DefaultGrantGroups.AdminGrants);
        // Same as catalog.rooms.*: not part of the original Trener/Recepcija scope.
        Assert.DoesNotContain(DefaultGrantGroups.TrenerGrants, g => g.StartsWith("catalog.resources.", StringComparison.Ordinal));
        Assert.DoesNotContain(DefaultGrantGroups.RecepcijaGrants, g => g.StartsWith("catalog.resources.", StringComparison.Ordinal));
    }

    #endregion

    #region Capability and Admin template v4 (seed data)

    private static readonly CapabilityGrantRoleEntry[] ResourceCapabilityGrants = ResourcesCapabilitySeedData.Grants
        .Select(g => new CapabilityGrantRoleEntry(g.GrantKey, Enum.Parse<CapabilityGrantRole>(g.Role)))
        .ToArray();

    private static TemplateSelectionInput GroupCapacityOverrideSelection() => new(
        GroupCapacityOverrideCapabilitySeedData.CapabilityId(), GroupCapacityOverrideCapabilitySeedData.CapabilityKey, CapabilityScopeModel.None,
        CapabilitySelectedScope.On,
        GroupCapacityOverrideCapabilitySeedData.Grants
            .Select(g => new CapabilityGrantRoleEntry(g.GrantKey, Enum.Parse<CapabilityGrantRole>(g.Role))).ToArray());

    private static TemplateSelectionInput ResourceSelection(CapabilitySelectedScope scope) => new(
        ResourcesCapabilitySeedData.CapabilityId(), ResourcesCapabilitySeedData.CapabilityKey, CapabilityScopeModel.ViewManage, scope, ResourceCapabilityGrants);

    /// <summary>Admin v3 selections exactly as SeedAdminTemplateV3 writes them: Admin v2 + organization.permissions.manage = Manage.</summary>
    private static List<TemplateSelectionInput> AdminV3Selections()
    {
        Dictionary<string, CapabilityV1SeedData.CapabilitySeed> capsByKey = CapabilityV1SeedData.Capabilities.ToDictionary(c => c.Key);
        List<TemplateSelectionInput> selections = CapabilityV2SeedData.Templates.Single(t => t.Key == "admin").Selections
            .Select(s =>
            {
                CapabilityV1SeedData.CapabilitySeed cap = capsByKey[s.CapabilityKey];
                return new TemplateSelectionInput(CapabilityV1SeedData.CapabilityId(s.CapabilityKey), s.CapabilityKey,
                    Enum.Parse<CapabilityScopeModel>(cap.ScopeModel), Enum.Parse<CapabilitySelectedScope>(s.SelectedScope),
                    cap.Grants.Select(g => new CapabilityGrantRoleEntry(g.GrantKey, Enum.Parse<CapabilityGrantRole>(g.Role))).ToList());
            })
            .ToList();

        selections.Add(new TemplateSelectionInput(PermissionAdministrationCapabilitySeedData.CapabilityId(),
            PermissionAdministrationCapabilitySeedData.CapabilityKey, CapabilityScopeModel.ViewManage, CapabilitySelectedScope.Manage,
            PermissionAdministrationCapabilitySeedData.Grants.Select(g => new CapabilityGrantRoleEntry(g.GrantKey, Enum.Parse<CapabilityGrantRole>(g.Role))).ToList()));
        return selections;
    }

    [Fact]
    public void ResourceCapability_ViewGrantsViewOnly_ManageGrantsBoth()
    {
        Assert.Equal(new HashSet<string> { Grants.CatalogResourcesView }, TestSupport.Materialize(ResourceSelection(CapabilitySelectedScope.View)));
        Assert.Equal(new HashSet<string> { Grants.CatalogResourcesView, Grants.CatalogResourcesManage },
            TestSupport.Materialize(ResourceSelection(CapabilitySelectedScope.Manage)));
        Assert.All(ResourcesCapabilitySeedData.Grants, g => Assert.Contains(g.GrantKey, Grants.Catalog.Select(c => c.Key)));
    }

    [Fact]
    public void AdminLatest_MaterializesTheWholeGrantCatalog()
    {
        // CHANGED in M1F: the latest Admin template is v5 = v4 + groups.capacity.override (On).
        List<TemplateSelectionInput> v5 = AdminV3Selections();
        v5.Add(ResourceSelection(CapabilitySelectedScope.Manage));
        v5.Add(GroupCapacityOverrideSelection());

        HashSet<string> grants = TestSupport.MaterializeAll(v5);
        grants.UnionWith(CapabilityV2SeedData.Templates.Single(t => t.Key == "admin").CompatibilityExtraGrants);

        Assert.Equal(Grants.Catalog.Select(g => g.Key).ToHashSet(), grants);
    }

    [Fact]
    public void Admin_V3ToV4_AddsExactlyTheResourcesCapability()
    {
        List<TemplateSelectionInput> v3 = AdminV3Selections();
        List<TemplateSelectionInput> v4 = AdminV3Selections();
        v4.Add(ResourceSelection(CapabilitySelectedScope.Manage));
        HashSet<string> compat = CapabilityV2SeedData.Templates.Single(t => t.Key == "admin").CompatibilityExtraGrants.ToHashSet();
        Dictionary<Guid, TemplateSelectionInput> baseById = v3.ToDictionary(s => s.CapabilityDefinitionId);

        List<CapabilitySnapshotInput> snapshots = v3
            .Select(s => new CapabilitySnapshotInput(s.CapabilityDefinitionId, s.CapabilityKey, s.ScopeModel, s.SelectedScope, s.Grants, "admin", 3))
            .ToList();
        HashSet<string> existing = TestSupport.MaterializeAll(v3);
        existing.UnionWith(compat);

        TemplateUpgradePlanningResult result = new TemplateUpgradePlanner(TestSupport.Materializer).Plan(new TemplateUpgradePlanningInput(
            TemplateKey: "admin", CurrentTemplateVersion: 3, TargetTemplateVersion: ResourcesCapabilitySeedData.AdminTemplateVersion,
            CurrentSnapshots: snapshots, TargetSelections: v4,
            CurrentTemplateCompatibilityGrantKeys: compat, TargetTemplateCompatibilityGrantKeys: compat,
            ExistingRawGrantKeys: existing,
            BaseResolver: id => baseById.TryGetValue(id, out TemplateSelectionInput sel) ? sel : null,
            Resolutions: new Dictionary<string, ConflictResolution>()));

        CapabilityDiffEntry added = Assert.Single(result.AddedCapabilities);
        Assert.Equal("catalog.resources.manage", added.CapabilityKey);
        Assert.Equal(CapabilitySelectedScope.Manage, added.TargetScope);
        Assert.Empty(result.RemovedCapabilities);
        Assert.Empty(result.ChangedCapabilities);
        Assert.Empty(result.Conflicts);
        Assert.True(result.IsFullyResolved);
        Assert.Equal(new HashSet<string> { Grants.CatalogResourcesView, Grants.CatalogResourcesManage }, result.RawGrantsAdded.Select(g => g.GrantKey).ToHashSet());
        Assert.Empty(result.RawGrantsRemoved);
    }

    #endregion

    #region Reference data in the database

    [Fact]
    public async Task Database_HasTheCapability_AndAdminV5IsTheLatestAdminTemplate()
    {
        await using DatabaseContext db = DatabaseContext.GenerateContext(LocalConnectionString);

        var capability = await db.CapabilityDefinitions
            .Where(c => c.Key == "catalog.resources.manage")
            .Select(c => new { c.Id, c.Version, c.IsActive })
            .SingleAsync();
        Assert.Equal(ResourcesCapabilitySeedData.CapabilityId(), capability.Id);
        Assert.True(capability.IsActive);

        var grants = await db.CapabilityDefinitionGrants
            .Where(g => g.CapabilityDefinitionId == capability.Id)
            .Select(g => new { g.GrantKey, Role = g.Role.ToString() })
            .ToListAsync();
        Assert.Equal(ResourcesCapabilitySeedData.Grants.Select(g => (g.GrantKey, g.Role)).OrderBy(g => g.GrantKey),
            grants.Select(g => (g.GrantKey, g.Role)).OrderBy(g => g.GrantKey));

        var latestAdmin = await db.DefaultRoleTemplates
            .Where(t => t.Key == "admin" && t.IsActive)
            .OrderByDescending(t => t.Version)
            .Select(t => new { t.Id, t.Version })
            .FirstAsync();
        // CHANGED in M1F: v5 (v4 + groups.capacity.override) is the latest Admin template; it still carries the resource capability.
        Assert.Equal(5, latestAdmin.Version);
        Assert.Equal(GroupCapacityOverrideCapabilitySeedData.AdminTemplateId(), latestAdmin.Id);

        List<Guid?> v4Caps = await db.DefaultRoleTemplateCapabilities
            .Where(c => c.DefaultRoleTemplateId == latestAdmin.Id)
            .Select(c => (Guid?)c.CapabilityDefinitionId)
            .ToListAsync();
        Assert.Equal(AdminV3Selections().Count + 2, v4Caps.Count);
        Assert.Contains(capability.Id, v4Caps);
        Assert.Contains(GroupCapacityOverrideCapabilitySeedData.CapabilityId(), v4Caps);

        // Trener/Recepcija templates were not re-published.
        Assert.Equal(2, await db.DefaultRoleTemplates.Where(t => t.Key == "trener").MaxAsync(t => t.Version));
        Assert.Equal(2, await db.DefaultRoleTemplates.Where(t => t.Key == "recepcija").MaxAsync(t => t.Version));
    }

    [Fact]
    public async Task NewOrganization_AdminGroup_GetsTheResourceGrants()
    {
        Guid organizationId = Guid.NewGuid();
        await using (DatabaseContext context = DatabaseContext.GenerateContext(LocalConnectionString))
        {
            context.Organizations.Add(new Organization
            {
                Id = organizationId, Name = "ResourceGrantBootstrapTest", Slug = $"resource-grant-bootstrap-{organizationId:N}", CreatedAt = DateTimeOffset.UtcNow
            });
            await context.SaveChangesAsync();
        }

        try
        {
            GrantGroupHandler handler = new(
                new DatabaseSettings { ConnectionString = LocalConnectionString },
                new DefaultRoleTemplateHandler(new DatabaseSettings { ConnectionString = LocalConnectionString }),
                new CapabilityMaterializationService());

            await using IUnitOfWork uow = await new UnitOfWorkFactory(new DatabaseSettings { ConnectionString = LocalConnectionString }).Begin();
            Guid? adminGroupId = await handler.EnsureDefaultGrantGroups(uow, organizationId);
            await uow.CommitAsync();

            await using DatabaseContext verify = DatabaseContext.GenerateContext(LocalConnectionString);
            HashSet<string> adminGrants = (await verify.GrantGroupGrants
                .Where(g => g.GrantGroupId == adminGroupId.Value)
                .Select(g => g.GrantKey)
                .ToListAsync()).ToHashSet();

            Assert.Contains(Grants.CatalogResourcesView, adminGrants);
            Assert.Contains(Grants.CatalogResourcesManage, adminGrants);
            Assert.Contains(Grants.CatalogRoomsManage, adminGrants);
            Assert.Contains(Grants.PermissionsManage, adminGrants);
            Assert.Equal(Grants.Catalog.Select(g => g.Key).ToHashSet(), adminGrants);
        }
        finally
        {
            await using DatabaseContext cleanup = DatabaseContext.GenerateContext(LocalConnectionString);
            List<Guid> groupIds = await cleanup.GrantGroups.Where(g => g.OrganizationId == organizationId).Select(g => g.Id.GetValueOrDefault()).ToListAsync();
            cleanup.GrantGroupGrants.RemoveRange(cleanup.GrantGroupGrants.Where(g => groupIds.Contains(g.GrantGroupId)));
            cleanup.GrantGroupCapabilitySnapshots.RemoveRange(cleanup.GrantGroupCapabilitySnapshots.Where(s => groupIds.Contains(s.GrantGroupId)));
            cleanup.GrantGroupTemplateGrants.RemoveRange(cleanup.GrantGroupTemplateGrants.Where(g => groupIds.Contains(g.GrantGroupId)));
            await cleanup.SaveChangesAsync();
            cleanup.GrantGroups.RemoveRange(cleanup.GrantGroups.Where(g => g.OrganizationId == organizationId));
            await cleanup.SaveChangesAsync();
            cleanup.Organizations.RemoveRange(cleanup.Organizations.Where(o => o.Id == organizationId));
            await cleanup.SaveChangesAsync();
        }
    }

    #endregion
}
