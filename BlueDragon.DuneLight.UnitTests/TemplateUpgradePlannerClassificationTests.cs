using System;
using System.Collections.Generic;
using System.Linq;
using BlueDragon.DuneLight.Core.Enums;
using BlueDragon.DuneLight.Core.Interfaces.Capabilities;
using BlueDragon.DuneLight.Core.Services;
using Xunit;

namespace BlueDragon.DuneLight.UnitTests;

/// <summary>Layer 1 — sintetički (ne-seed) scenariji koji izoliraju svaki od četiri Part G klasifikacijska
/// slučaja + raw grant tagging + missing-resolution guard, jedan po jedan, bez šuma iz stvarnih Admin/Trener/
/// Recepcija predložaka (ti su pokriveni u SeedTemplateUpgradeTests).</summary>
public class TemplateUpgradePlannerClassificationTests
{
    private readonly TemplateUpgradePlanner _planner = new(TestSupport.Materializer);

    private static TemplateUpgradePlanningInput BuildInput(
        IReadOnlyList<CapabilitySnapshotInput> currentSnapshots,
        IReadOnlyList<TemplateSelectionInput> targetSelections,
        Dictionary<Guid, TemplateSelectionInput> baseByDefinitionId,
        HashSet<string> existingRawGrantKeys,
        HashSet<string> currentCompatKeys = null,
        HashSet<string> targetCompatKeys = null,
        IReadOnlyDictionary<string, ConflictResolution> resolutions = null)
    {
        return new TemplateUpgradePlanningInput(
            TemplateKey: "test",
            CurrentTemplateVersion: 1,
            TargetTemplateVersion: 2,
            CurrentSnapshots: currentSnapshots,
            TargetSelections: targetSelections,
            CurrentTemplateCompatibilityGrantKeys: currentCompatKeys ?? new HashSet<string>(),
            TargetTemplateCompatibilityGrantKeys: targetCompatKeys ?? new HashSet<string>(),
            ExistingRawGrantKeys: existingRawGrantKeys,
            BaseResolver: id => baseByDefinitionId.TryGetValue(id, out TemplateSelectionInput sel) ? sel : null,
            Resolutions: resolutions ?? new Dictionary<string, ConflictResolution>());
    }

    [Fact]
    public void UnchangedByTenant_FollowsTarget_AndIsClassifiedAsChanged()
    {
        // Base = View, Current = View (netaknuto), Target = Manage -> slijedi target, "changed" diff unos.
        TemplateSelectionInput baseSel = TestSupport.Selection("x.cap", CapabilityScopeModel.ViewManage, CapabilitySelectedScope.View);
        CapabilitySnapshotInput current = TestSupport.Snapshot("x.cap", CapabilityScopeModel.ViewManage, CapabilitySelectedScope.View, "test", 1);
        TemplateSelectionInput target = TestSupport.Selection("x.cap", CapabilityScopeModel.ViewManage, CapabilitySelectedScope.Manage);

        HashSet<string> existing = TestSupport.Materialize(baseSel);

        TemplateUpgradePlanningInput input = BuildInput(
            new[] { current }, new[] { target },
            new Dictionary<Guid, TemplateSelectionInput> { [baseSel.CapabilityDefinitionId] = baseSel },
            existing);

        TemplateUpgradePlanningResult result = _planner.Plan(input);

        CapabilityDiffEntry changed = Assert.Single(result.ChangedCapabilities);
        Assert.Equal("x.cap", changed.CapabilityKey);
        Assert.Equal(CapabilitySelectedScope.View, changed.CurrentScope);
        Assert.Equal(CapabilitySelectedScope.Manage, changed.TargetScope);
        Assert.Empty(result.Conflicts);

        TemplateSelectionInput resulting = Assert.Single(result.ResultingCapabilitySelections);
        Assert.Equal(CapabilitySelectedScope.Manage, resulting.SelectedScope);
    }

    [Fact]
    public void CustomizedByTenant_TargetUnchanged_PreservesCurrent_NoDiffNoConflict()
    {
        // Base = View, Current = Manage (tenant prilagodio), Target = View (target NIJE dirao) -> zadrži Manage, tiho.
        TemplateSelectionInput baseSel = TestSupport.Selection("y.cap", CapabilityScopeModel.ViewManage, CapabilitySelectedScope.View);
        CapabilitySnapshotInput current = TestSupport.Snapshot("y.cap", CapabilityScopeModel.ViewManage, CapabilitySelectedScope.Manage, "test", 1);
        TemplateSelectionInput target = TestSupport.Selection("y.cap", CapabilityScopeModel.ViewManage, CapabilitySelectedScope.View);

        HashSet<string> existing = TestSupport.Materialize(TestSupport.Selection("y.cap", CapabilityScopeModel.ViewManage, CapabilitySelectedScope.Manage));

        TemplateUpgradePlanningInput input = BuildInput(
            new[] { current }, new[] { target },
            new Dictionary<Guid, TemplateSelectionInput> { [baseSel.CapabilityDefinitionId] = baseSel },
            existing);

        TemplateUpgradePlanningResult result = _planner.Plan(input);

        Assert.Empty(result.AddedCapabilities);
        Assert.Empty(result.RemovedCapabilities);
        Assert.Empty(result.ChangedCapabilities);
        Assert.Empty(result.Conflicts);
        Assert.True(result.IsFullyResolved);

        TemplateSelectionInput resulting = Assert.Single(result.ResultingCapabilitySelections);
        Assert.Equal(CapabilitySelectedScope.Manage, resulting.SelectedScope);
    }

    [Fact]
    public void NewInTarget_NoBaseNoCurrent_IsProposedAsAddition()
    {
        TemplateSelectionInput target = TestSupport.Selection("z.cap", CapabilityScopeModel.OwnAll, CapabilitySelectedScope.Own);

        TemplateUpgradePlanningInput input = BuildInput(
            Array.Empty<CapabilitySnapshotInput>(), new[] { target },
            new Dictionary<Guid, TemplateSelectionInput>(),
            new HashSet<string>());

        TemplateUpgradePlanningResult result = _planner.Plan(input);

        CapabilityDiffEntry added = Assert.Single(result.AddedCapabilities);
        Assert.Equal("z.cap", added.CapabilityKey);
        Assert.Null(added.CurrentScope);
        Assert.Equal(CapabilitySelectedScope.Own, added.TargetScope);
        Assert.Empty(result.Conflicts);
    }

    [Fact]
    public void RemovedInTarget_Untouched_IsProposedAsRemoval()
    {
        // Base = Manage, Current = Manage (netaknuto), Target = odsutan (uklonjen) -> Removed, izbačen iz rezultata.
        TemplateSelectionInput baseSel = TestSupport.Selection("w.cap", CapabilityScopeModel.ViewManage, CapabilitySelectedScope.Manage);
        CapabilitySnapshotInput current = TestSupport.Snapshot("w.cap", CapabilityScopeModel.ViewManage, CapabilitySelectedScope.Manage, "test", 1);

        HashSet<string> existing = TestSupport.Materialize(baseSel);

        TemplateUpgradePlanningInput input = BuildInput(
            new[] { current }, Array.Empty<TemplateSelectionInput>(),
            new Dictionary<Guid, TemplateSelectionInput> { [baseSel.CapabilityDefinitionId] = baseSel },
            existing);

        TemplateUpgradePlanningResult result = _planner.Plan(input);

        CapabilityDiffEntry removed = Assert.Single(result.RemovedCapabilities);
        Assert.Equal("w.cap", removed.CapabilityKey);
        Assert.Equal(CapabilitySelectedScope.Manage, removed.CurrentScope);
        Assert.Null(removed.TargetScope);
        Assert.Empty(result.Conflicts);
        Assert.Empty(result.ResultingCapabilitySelections);
    }

    [Fact]
    public void RemovedInTarget_ButCustomized_IsPreservedAndFlaggedAsConflict()
    {
        // Base = Manage, Current = Own (tenant prilagodio), Target = odsutan (uklonjen) -> KONFLIKT (Current != Base
        // I Target(None) != Base I Target(None) != Current), default PreserveCurrent.
        // Napomena: ViewOwnAll model nema "Manage" — koristimo View kao BASE da ostane legalno.
        TemplateSelectionInput baseSel = TestSupport.Selection("v.cap", CapabilityScopeModel.ViewOwnAll, CapabilitySelectedScope.View);
        CapabilitySnapshotInput current = TestSupport.Snapshot("v.cap", CapabilityScopeModel.ViewOwnAll, CapabilitySelectedScope.Own, "test", 1);

        HashSet<string> existing = TestSupport.Materialize(TestSupport.Selection("v.cap", CapabilityScopeModel.ViewOwnAll, CapabilitySelectedScope.Own));

        TemplateUpgradePlanningInput input = BuildInput(
            new[] { current }, Array.Empty<TemplateSelectionInput>(),
            new Dictionary<Guid, TemplateSelectionInput> { [baseSel.CapabilityDefinitionId] = baseSel },
            existing);

        TemplateUpgradePlanningResult resultUnresolved = _planner.Plan(input);

        UpgradeConflict conflict = Assert.Single(resultUnresolved.Conflicts);
        Assert.Equal("v.cap", conflict.CapabilityKey);
        Assert.Equal(CapabilitySelectedScope.View, conflict.BaseScope);
        Assert.Equal(CapabilitySelectedScope.Own, conflict.CurrentScope);
        Assert.Equal(CapabilitySelectedScope.None, conflict.TargetScope);
        Assert.Equal(ConflictResolution.PreserveCurrent, conflict.DefaultResolution);

        Assert.False(resultUnresolved.IsFullyResolved);
        Assert.Equal(new[] { "v.cap" }, resultUnresolved.UnresolvedConflictCapabilityKeys);

        // Preserve current -> capability i njeni raw grantovi prežive (nije izbačena iz rezultata).
        Dictionary<string, ConflictResolution> resolutions = new() { ["v.cap"] = ConflictResolution.PreserveCurrent };
        TemplateUpgradePlanningInput resolvedInput = input with { Resolutions = resolutions };
        TemplateUpgradePlanningResult resultResolved = _planner.Plan(resolvedInput);

        Assert.True(resultResolved.IsFullyResolved);
        TemplateSelectionInput resulting = Assert.Single(resultResolved.ResultingCapabilitySelections);
        Assert.Equal(CapabilitySelectedScope.Own, resulting.SelectedScope);
    }

    [Fact]
    public void Conflict_BothDivergeFromBase_ResolutionBothWays_ProduceExpectedResultingScope()
    {
        // Base = View, Current = Manage (tenant), Target = Own -- pričekaj, ViewManage nema Own. Koristi ViewOwnAll:
        // Base=View, Current=Own (tenant), Target=All (predložak) -> sve tri različite -> pravi konflikt.
        TemplateSelectionInput baseSel = TestSupport.Selection("u.cap", CapabilityScopeModel.ViewOwnAll, CapabilitySelectedScope.View);
        CapabilitySnapshotInput current = TestSupport.Snapshot("u.cap", CapabilityScopeModel.ViewOwnAll, CapabilitySelectedScope.Own, "test", 1);
        TemplateSelectionInput target = TestSupport.Selection("u.cap", CapabilityScopeModel.ViewOwnAll, CapabilitySelectedScope.All);

        HashSet<string> existing = TestSupport.Materialize(TestSupport.Selection("u.cap", CapabilityScopeModel.ViewOwnAll, CapabilitySelectedScope.Own));

        TemplateUpgradePlanningInput baseInput = BuildInput(
            new[] { current }, new[] { target },
            new Dictionary<Guid, TemplateSelectionInput> { [baseSel.CapabilityDefinitionId] = baseSel },
            existing);

        TemplateUpgradePlanningResult unresolved = _planner.Plan(baseInput);
        Assert.Single(unresolved.Conflicts);
        Assert.False(unresolved.IsFullyResolved);

        TemplateUpgradePlanningResult preserveCurrent = _planner.Plan(baseInput with
        {
            Resolutions = new Dictionary<string, ConflictResolution> { ["u.cap"] = ConflictResolution.PreserveCurrent }
        });
        Assert.Equal(CapabilitySelectedScope.Own, Assert.Single(preserveCurrent.ResultingCapabilitySelections).SelectedScope);

        TemplateUpgradePlanningResult useTemplate = _planner.Plan(baseInput with
        {
            Resolutions = new Dictionary<string, ConflictResolution> { ["u.cap"] = ConflictResolution.UseTemplate }
        });
        Assert.Equal(CapabilitySelectedScope.All, Assert.Single(useTemplate.ResultingCapabilitySelections).SelectedScope);
    }

    [Fact]
    public void ManualAdvancedGrants_SurviveUpgradeUnchanged_RegardlessOfCapabilityChanges()
    {
        TemplateSelectionInput baseSel = TestSupport.Selection("m.cap", CapabilityScopeModel.ViewManage, CapabilitySelectedScope.View);
        CapabilitySnapshotInput current = TestSupport.Snapshot("m.cap", CapabilityScopeModel.ViewManage, CapabilitySelectedScope.View, "test", 1);
        TemplateSelectionInput target = TestSupport.Selection("m.cap", CapabilityScopeModel.ViewManage, CapabilitySelectedScope.Manage);

        HashSet<string> existing = TestSupport.Materialize(baseSel);
        existing.Add("manual.advanced.grant"); // Owner ga je ranije ručno dodao preko Advanced editora.

        TemplateUpgradePlanningInput input = BuildInput(
            new[] { current }, new[] { target },
            new Dictionary<Guid, TemplateSelectionInput> { [baseSel.CapabilityDefinitionId] = baseSel },
            existing);

        TemplateUpgradePlanningResult result = _planner.Plan(input);

        Assert.Contains("manual.advanced.grant", result.PreservedManualGrantKeys);
        Assert.Contains(result.RawGrantsUnchanged, c => c.GrantKey == "manual.advanced.grant" && c.Source == RawGrantSource.ManualAdvanced);
        Assert.DoesNotContain(result.RawGrantsRemoved, c => c.GrantKey == "manual.advanced.grant");
    }

    [Fact]
    public void TemplateCompatibilityGrants_OldSetFullyReplacedByTargetSet()
    {
        HashSet<string> currentCompat = new() { "legacy.compat.grant" };
        HashSet<string> targetCompat = new() { "new.compat.grant" };
        HashSet<string> existing = new(currentCompat);

        TemplateUpgradePlanningInput input = BuildInput(
            Array.Empty<CapabilitySnapshotInput>(), Array.Empty<TemplateSelectionInput>(),
            new Dictionary<Guid, TemplateSelectionInput>(),
            existing,
            currentCompatKeys: currentCompat,
            targetCompatKeys: targetCompat);

        TemplateUpgradePlanningResult result = _planner.Plan(input);

        Assert.Equal(targetCompat, result.ResultingTemplateCompatibilityGrantKeys);
        Assert.Contains(result.RawGrantsRemoved, c => c.GrantKey == "legacy.compat.grant" && c.Source == RawGrantSource.TemplateCompatibility);
        Assert.Contains(result.RawGrantsAdded, c => c.GrantKey == "new.compat.grant" && c.Source == RawGrantSource.TemplateCompatibility);
    }

    [Fact]
    public void UnresolvedConflict_ReportsIsFullyResolvedFalse_WithCorrectUnresolvedKeys()
    {
        TemplateSelectionInput baseSel1 = TestSupport.Selection("c1.cap", CapabilityScopeModel.ViewOwnAll, CapabilitySelectedScope.View);
        CapabilitySnapshotInput current1 = TestSupport.Snapshot("c1.cap", CapabilityScopeModel.ViewOwnAll, CapabilitySelectedScope.Own, "test", 1);
        TemplateSelectionInput target1 = TestSupport.Selection("c1.cap", CapabilityScopeModel.ViewOwnAll, CapabilitySelectedScope.All);

        TemplateSelectionInput baseSel2 = TestSupport.Selection("c2.cap", CapabilityScopeModel.ViewOwnAll, CapabilitySelectedScope.View);
        CapabilitySnapshotInput current2 = TestSupport.Snapshot("c2.cap", CapabilityScopeModel.ViewOwnAll, CapabilitySelectedScope.Own, "test", 1);
        TemplateSelectionInput target2 = TestSupport.Selection("c2.cap", CapabilityScopeModel.ViewOwnAll, CapabilitySelectedScope.All);

        HashSet<string> existing = new();
        existing.UnionWith(TestSupport.Materialize(TestSupport.Selection("c1.cap", CapabilityScopeModel.ViewOwnAll, CapabilitySelectedScope.Own)));
        existing.UnionWith(TestSupport.Materialize(TestSupport.Selection("c2.cap", CapabilityScopeModel.ViewOwnAll, CapabilitySelectedScope.Own)));

        TemplateUpgradePlanningInput input = BuildInput(
            new[] { current1, current2 }, new[] { target1, target2 },
            new Dictionary<Guid, TemplateSelectionInput> { [baseSel1.CapabilityDefinitionId] = baseSel1, [baseSel2.CapabilityDefinitionId] = baseSel2 },
            existing,
            resolutions: new Dictionary<string, ConflictResolution> { ["c1.cap"] = ConflictResolution.PreserveCurrent }); // c2.cap namjerno nerazriješen

        TemplateUpgradePlanningResult result = _planner.Plan(input);

        Assert.Equal(2, result.Conflicts.Count);
        Assert.False(result.IsFullyResolved);
        Assert.Equal(new[] { "c2.cap" }, result.UnresolvedConflictCapabilityKeys);
    }
}
