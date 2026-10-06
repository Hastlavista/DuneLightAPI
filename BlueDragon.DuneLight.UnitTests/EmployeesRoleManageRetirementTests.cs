#nullable disable
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using BlueDragon.DuneLight.API.Authorization;
using BlueDragon.DuneLight.Core.Interfaces.Capabilities;
using BlueDragon.DuneLight.Core.Services;
using BlueDragon.DuneLight.Core.Shared;
using BlueDragon.DuneLight.DatabaseMigration.Migrations._2026;
using BlueDragon.DuneLight.Infrastructure.Domain.Contexts;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace BlueDragon.DuneLight.UnitTests;

/// <summary>
/// ADR-0019 (foundation cleanup) — the employees.role.manage grant/capability is retired: it left Grants.Catalog,
/// no endpoint requires it, Admin v6 (= v5 without it) is published through the normal template-version mechanism,
/// the capability version is deprecated (never hard-deleted) and no tenant GrantGroup keeps it.
/// </summary>
public class EmployeesRoleManageRetirementTests
{
    private const string LocalConnectionString = "Host=localhost;Database=postgres;Password=root1234;Username=postgres";
    private const string RetiredKey = EmployeesRoleManageRetirementSeedData.RetiredGrantKey;

    [Fact]
    public void Catalog_AndDefaultGroups_NoLongerContainTheGrant()
    {
        Assert.DoesNotContain(RetiredKey, Grants.Catalog.Select(g => g.Key));
        Assert.DoesNotContain(DefaultGrantGroups.All, d => d.Grants.Contains(RetiredKey));
    }

    [Fact]
    public void NoEndpoint_RequiresTheGrant()
    {
        IEnumerable<Type> controllers = typeof(RequireGrantAttribute).Assembly.GetTypes()
            .Where(t => typeof(ControllerBase).IsAssignableFrom(t) && !t.IsAbstract);

        List<string> offenders = controllers
            .SelectMany(c => c.GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.DeclaredOnly)
                .SelectMany(m => m.GetCustomAttributes<RequireGrantAttribute>())
                .Concat(c.GetCustomAttributes<RequireGrantAttribute>())
                .Where(a => a.Grants.Contains(RetiredKey))
                .Select(_ => c.Name))
            .ToList();

        Assert.Empty(offenders);
    }

    [Fact]
    public void Admin_V5ToV6_RemovesExactlyTheRetiredCapability_ForAGroupStillOnV5()
    {
        TemplateUpgradePlanningResult result = PlanV5ToV6(snapshotsFromV5: ResourceAuthorizationTests.AdminV5Selections());

        CapabilityDiffEntry removed = Assert.Single(result.RemovedCapabilities);
        Assert.Equal(EmployeesRoleManageRetirementSeedData.RetiredCapabilityKey, removed.CapabilityKey);
        Assert.Null(removed.TargetScope);
        Assert.Empty(result.AddedCapabilities);
        Assert.Empty(result.ChangedCapabilities);
        Assert.Empty(result.Conflicts);
        Assert.True(result.IsFullyResolved);
        Assert.Equal(new HashSet<string> { RetiredKey }, result.RawGrantsRemoved.Select(g => g.GrantKey).ToHashSet());
        Assert.Empty(result.RawGrantsAdded);
    }

    [Fact]
    public void Admin_V5ToV6_IsConflictFree_ForAGroupAlreadyCleanedByTheRetirementMigration()
    {
        // The migration removes the capability snapshot and the raw grant from tenant groups that stay on v5;
        // a later v5 -> v6 upgrade must then see Current == Target (no conflict, nothing left to remove).
        TemplateUpgradePlanningResult result = PlanV5ToV6(snapshotsFromV5: ResourceAuthorizationTests.AdminV6Selections());

        Assert.Empty(result.Conflicts);
        Assert.True(result.IsFullyResolved);
        Assert.Empty(result.RawGrantsRemoved);
        Assert.Empty(result.RawGrantsAdded);
        Assert.DoesNotContain(result.ResultingCapabilitySelections, s => s.CapabilityKey == RetiredKey);
    }

    [Fact]
    public async Task Database_CapabilityIsDeprecated_AndNoActiveTemplateOrTenantGroupReferencesIt()
    {
        await using DatabaseContext db = DatabaseContext.GenerateContext(LocalConnectionString);
        Guid capabilityId = EmployeesRoleManageRetirementSeedData.RetiredCapabilityId();

        var capability = await db.CapabilityDefinitions
            .Where(c => c.Id == capabilityId)
            .Select(c => new { c.IsActive, c.DeprecatedAt })
            .SingleAsync();
        Assert.False(capability.IsActive);
        Assert.NotNull(capability.DeprecatedAt);

        Assert.False(await db.DefaultRoleTemplateCapabilities
            .AnyAsync(c => c.CapabilityDefinitionId == capabilityId && c.DefaultRoleTemplate.IsActive));
        Assert.False(await db.DefaultRoleTemplateGrants.AnyAsync(g => g.GrantKey == RetiredKey));
        Assert.False(await db.GrantGroupGrants.AnyAsync(g => g.GrantKey == RetiredKey));
        Assert.False(await db.GrantGroupCapabilitySnapshots.AnyAsync(s => s.CapabilityDefinitionId == capabilityId));
        Assert.False(await db.GrantGroupTemplateGrants.AnyAsync(g => g.GrantKey == RetiredKey));

        // Superseded Admin versions stay as immutable history, only deactivated; v6 is the only active Admin version.
        List<int> activeAdminVersions = await db.DefaultRoleTemplates
            .Where(t => t.Key == "admin" && t.IsActive)
            .Select(t => t.Version)
            .ToListAsync();
        Assert.Equal(new List<int> { EmployeesRoleManageRetirementSeedData.AdminTemplateVersion }, activeAdminVersions);
        Assert.Equal(EmployeesRoleManageRetirementSeedData.AdminTemplateVersion, await db.DefaultRoleTemplates.Where(t => t.Key == "admin").CountAsync());
    }

    private static TemplateUpgradePlanningResult PlanV5ToV6(List<TemplateSelectionInput> snapshotsFromV5)
    {
        List<TemplateSelectionInput> v5 = ResourceAuthorizationTests.AdminV5Selections();
        List<TemplateSelectionInput> v6 = ResourceAuthorizationTests.AdminV6Selections();
        HashSet<string> compat = CapabilityV2SeedData.Templates.Single(t => t.Key == "admin").CompatibilityExtraGrants.ToHashSet();
        Dictionary<Guid, TemplateSelectionInput> baseById = v5.ToDictionary(s => s.CapabilityDefinitionId);

        List<CapabilitySnapshotInput> snapshots = snapshotsFromV5
            .Select(s => new CapabilitySnapshotInput(s.CapabilityDefinitionId, s.CapabilityKey, s.ScopeModel, s.SelectedScope, s.Grants, "admin", 5))
            .ToList();
        HashSet<string> existing = TestSupport.MaterializeAll(snapshotsFromV5);
        existing.UnionWith(compat);

        return new TemplateUpgradePlanner(TestSupport.Materializer).Plan(new TemplateUpgradePlanningInput(
            TemplateKey: "admin", CurrentTemplateVersion: 5, TargetTemplateVersion: EmployeesRoleManageRetirementSeedData.AdminTemplateVersion,
            CurrentSnapshots: snapshots, TargetSelections: v6,
            CurrentTemplateCompatibilityGrantKeys: compat, TargetTemplateCompatibilityGrantKeys: compat,
            ExistingRawGrantKeys: existing,
            BaseResolver: id => baseById.TryGetValue(id, out TemplateSelectionInput sel) ? sel : null,
            Resolutions: new Dictionary<string, ConflictResolution>()));
    }
}
