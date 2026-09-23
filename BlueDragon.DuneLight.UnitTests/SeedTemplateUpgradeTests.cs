using System.Collections.Generic;
using System.Linq;
using BlueDragon.DuneLight.Core.Interfaces.Capabilities;
using BlueDragon.DuneLight.Core.Services;
using Xunit;

namespace BlueDragon.DuneLight.UnitTests;

/// <summary>Layer 1 — Admin/Trener/Recepcija v1->v2 protiv STVARNIH seed podataka (CapabilityV1SeedData /
/// CapabilityV2SeedData), vidi backend test plan §3.</summary>
public class SeedTemplateUpgradeTests
{
    private readonly TemplateUpgradePlanner _planner = new(TestSupport.Materializer);

    [Fact]
    public void Admin_V1ToV2_IsZeroDiff()
    {
        TemplateUpgradePlanningInput input = TestSupport.BuildSeedTemplateInput("admin");

        TemplateUpgradePlanningResult result = _planner.Plan(input);

        Assert.Empty(result.AddedCapabilities);
        Assert.Empty(result.RemovedCapabilities);
        Assert.Empty(result.ChangedCapabilities);
        Assert.Empty(result.Conflicts);
        Assert.True(result.IsFullyResolved);
        Assert.Empty(result.RawGrantsAdded);
        Assert.Empty(result.RawGrantsRemoved);
        Assert.Equal(input.ExistingRawGrantKeys.Count, result.RawGrantsUnchanged.Count);
    }

    [Fact]
    public void Trener_V1ToV2_AddsExactlyOneCapability_ScheduleBreaksManageOwn()
    {
        TemplateUpgradePlanningInput input = TestSupport.BuildSeedTemplateInput("trener");

        TemplateUpgradePlanningResult result = _planner.Plan(input);

        CapabilityDiffEntry added = Assert.Single(result.AddedCapabilities);
        Assert.Equal("schedule.breaks.manage", added.CapabilityKey);
        Assert.Null(added.CurrentScope);
        Assert.Equal(Core.Enums.CapabilitySelectedScope.Own, added.TargetScope);

        Assert.Empty(result.RemovedCapabilities);
        Assert.Empty(result.ChangedCapabilities);
        Assert.Empty(result.Conflicts);
        Assert.True(result.IsFullyResolved);

        Assert.Contains(result.ResultingCapabilitySelections, s => s.CapabilityKey == "schedule.breaks.manage" && s.SelectedScope == Core.Enums.CapabilitySelectedScope.Own);
    }

    [Fact]
    public void Recepcija_V1ToV2_AddsExactlySixCapabilities()
    {
        TemplateUpgradePlanningInput input = TestSupport.BuildSeedTemplateInput("recepcija");

        TemplateUpgradePlanningResult result = _planner.Plan(input);

        HashSet<string> addedKeys = result.AddedCapabilities.Select(c => c.CapabilityKey).ToHashSet();
        Assert.Equal(6, result.AddedCapabilities.Count);
        Assert.Equal(new HashSet<string>
        {
            "catalog.services.manage",
            "catalog.companies.manage",
            "groups.manage",
            "checkout.manage",
            "products.manage",
            "stock.manage"
        }, addedKeys);

        Assert.Empty(result.RemovedCapabilities);
        Assert.Empty(result.ChangedCapabilities);
        Assert.Empty(result.Conflicts);
        Assert.True(result.IsFullyResolved);
    }
}
