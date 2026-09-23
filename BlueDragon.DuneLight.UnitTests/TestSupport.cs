using System;
using System.Collections.Generic;
using System.Linq;
using BlueDragon.DuneLight.Core.Enums;
using BlueDragon.DuneLight.Core.Interfaces.Capabilities;
using BlueDragon.DuneLight.DatabaseMigration.Migrations._2026;
using BlueDragon.DuneLight.Infrastructure.Services;

namespace BlueDragon.DuneLight.UnitTests;

/// <summary>Zajednički test-helperi za TemplateUpgradePlanner testove (Layer 1 — vidi backend test plan §3).
/// CapabilityMaterializationService je namjerno konkretna klasa (ne mock) — čista/bez-stanja implementacija,
/// isti obrazac kao produkcijski kod (vidi njegovu klasnu napomenu).</summary>
public static class TestSupport
{
    public static readonly ICapabilityMaterializationService Materializer = new CapabilityMaterializationService();

    /// <summary>Sintetički grant-set za jednu test-capability, po ScopeModel-u, s prediktivnim raw grant-imenima
    /// (npr. "{key}.view"/"{key}.own"/"{key}.all"/"{key}.manage"/"{key}.on") + uvijek jedan MandatorySupporting
    /// ("{key}.base") — dovoljno da razlikuje raw grant skupove po odabranom opsegu u testovima klasifikacije.</summary>
    public static IReadOnlyList<CapabilityGrantRoleEntry> SyntheticGrants(string key, CapabilityScopeModel model)
    {
        List<CapabilityGrantRoleEntry> grants = new() { new CapabilityGrantRoleEntry($"{key}.base", CapabilityGrantRole.MandatorySupporting) };

        switch (model)
        {
            case CapabilityScopeModel.None:
                grants.Add(new CapabilityGrantRoleEntry($"{key}.on", CapabilityGrantRole.PrimaryNoScope));
                break;
            case CapabilityScopeModel.ViewManage:
                grants.Add(new CapabilityGrantRoleEntry($"{key}.view", CapabilityGrantRole.PrimaryViewOnly));
                grants.Add(new CapabilityGrantRoleEntry($"{key}.manage", CapabilityGrantRole.PrimaryManage));
                break;
            case CapabilityScopeModel.OwnAll:
                grants.Add(new CapabilityGrantRoleEntry($"{key}.own", CapabilityGrantRole.PrimaryOwn));
                grants.Add(new CapabilityGrantRoleEntry($"{key}.all", CapabilityGrantRole.PrimaryAll));
                break;
            case CapabilityScopeModel.ViewOwnAll:
                grants.Add(new CapabilityGrantRoleEntry($"{key}.view", CapabilityGrantRole.PrimaryViewOnly));
                grants.Add(new CapabilityGrantRoleEntry($"{key}.own", CapabilityGrantRole.PrimaryOwn));
                grants.Add(new CapabilityGrantRoleEntry($"{key}.all", CapabilityGrantRole.PrimaryAll));
                break;
        }

        return grants;
    }

    public static TemplateSelectionInput Selection(string key, CapabilityScopeModel model, CapabilitySelectedScope scope, Guid? definitionId = null) =>
        new(definitionId ?? DeterministicId(key), key, model, scope, SyntheticGrants(key, model));

    public static CapabilitySnapshotInput Snapshot(string key, CapabilityScopeModel model, CapabilitySelectedScope scope, string sourceTemplateKey, int? sourceTemplateVersion, Guid? definitionId = null) =>
        new(definitionId ?? DeterministicId(key), key, model, scope, SyntheticGrants(key, model), sourceTemplateKey, sourceTemplateVersion);

    public static Guid DeterministicId(string key) => CapabilityV1SeedData.CapabilityId(key);

    public static HashSet<string> Materialize(TemplateSelectionInput selection) =>
        Materializer.Materialize(selection.ScopeModel, selection.SelectedScope, selection.Grants);

    public static HashSet<string> MaterializeAll(IEnumerable<TemplateSelectionInput> selections)
    {
        HashSet<string> result = new();
        foreach (TemplateSelectionInput s in selections)
            result.UnionWith(Materialize(s));
        return result;
    }

    /// <summary>Puni "planning input" builder za jedan STVARNI seed predložak (Admin/Trener/Recepcija), simulirajući
    /// grant-grupu koju je EnsureDefaultGrantGroups materijalizirao iz v1 predloška i koju NIKO nije dirao otad
    /// (baseline za Admin/Trener/Recepcija v1->v2 testove) — koristi TOČNO CapabilityV1SeedData/CapabilityV2SeedData,
    /// ne ručno prepisane fixture.</summary>
    public static TemplateUpgradePlanningInput BuildSeedTemplateInput(string templateKey, IReadOnlyDictionary<string, ConflictResolution> resolutions = null)
    {
        Dictionary<string, CapabilityV1SeedData.CapabilitySeed> capsByKey = CapabilityV1SeedData.Capabilities.ToDictionary(c => c.Key);
        CapabilityV1SeedData.TemplateSeed v1Template = CapabilityV1SeedData.Templates.Single(t => t.Key == templateKey);
        CapabilityV1SeedData.TemplateSeed v2Template = CapabilityV2SeedData.Templates.Single(t => t.Key == templateKey);

        List<TemplateSelectionInput> baseSelections = v1Template.Selections
            .Select(s => ToSelection(s, capsByKey))
            .ToList();
        Dictionary<Guid, TemplateSelectionInput> baseByDefinitionId = baseSelections.ToDictionary(s => s.CapabilityDefinitionId);

        List<CapabilitySnapshotInput> currentSnapshots = v1Template.Selections
            .Select(s => ToSnapshot(s, capsByKey, templateKey))
            .ToList();

        List<TemplateSelectionInput> targetSelections = v2Template.Selections
            .Select(s => ToSelection(s, capsByKey))
            .ToList();

        HashSet<string> currentCompatKeys = v1Template.CompatibilityExtraGrants.ToHashSet();
        HashSet<string> targetCompatKeys = v2Template.CompatibilityExtraGrants.ToHashSet();

        HashSet<string> existingRawGrantKeys = MaterializeAll(currentSnapshots.Select(s => new TemplateSelectionInput(s.CapabilityDefinitionId, s.CapabilityKey, s.ScopeModel, s.SelectedScope, s.Grants)));
        existingRawGrantKeys.UnionWith(currentCompatKeys);

        return new TemplateUpgradePlanningInput(
            TemplateKey: templateKey,
            CurrentTemplateVersion: CapabilityV1SeedData.TemplateVersion,
            TargetTemplateVersion: CapabilityV2SeedData.TemplateVersion,
            CurrentSnapshots: currentSnapshots,
            TargetSelections: targetSelections,
            CurrentTemplateCompatibilityGrantKeys: currentCompatKeys,
            TargetTemplateCompatibilityGrantKeys: targetCompatKeys,
            ExistingRawGrantKeys: existingRawGrantKeys,
            BaseResolver: id => baseByDefinitionId.TryGetValue(id, out TemplateSelectionInput sel) ? sel : null,
            Resolutions: resolutions ?? new Dictionary<string, ConflictResolution>());
    }

    private static TemplateSelectionInput ToSelection(CapabilityV1SeedData.TemplateCapabilitySeed seed, Dictionary<string, CapabilityV1SeedData.CapabilitySeed> capsByKey)
    {
        CapabilityV1SeedData.CapabilitySeed cap = capsByKey[seed.CapabilityKey];
        CapabilityScopeModel scopeModel = Enum.Parse<CapabilityScopeModel>(cap.ScopeModel);
        CapabilitySelectedScope selectedScope = Enum.Parse<CapabilitySelectedScope>(seed.SelectedScope);
        List<CapabilityGrantRoleEntry> grants = cap.Grants.Select(g => new CapabilityGrantRoleEntry(g.GrantKey, Enum.Parse<CapabilityGrantRole>(g.Role))).ToList();

        return new TemplateSelectionInput(CapabilityV1SeedData.CapabilityId(seed.CapabilityKey), seed.CapabilityKey, scopeModel, selectedScope, grants);
    }

    private static CapabilitySnapshotInput ToSnapshot(CapabilityV1SeedData.TemplateCapabilitySeed seed, Dictionary<string, CapabilityV1SeedData.CapabilitySeed> capsByKey, string sourceTemplateKey)
    {
        TemplateSelectionInput selection = ToSelection(seed, capsByKey);
        return new CapabilitySnapshotInput(selection.CapabilityDefinitionId, selection.CapabilityKey, selection.ScopeModel, selection.SelectedScope, selection.Grants, sourceTemplateKey, CapabilityV1SeedData.TemplateVersion);
    }
}
