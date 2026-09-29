using System;
using System.Collections.Generic;
using System.Linq;
using BlueDragon.DuneLight.Core.Enums;
using BlueDragon.DuneLight.Core.Interfaces.Capabilities;
using BlueDragon.DuneLight.DatabaseMigration.Migrations.Baseline;
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

    public static Guid DeterministicId(string key) => CapabilityReferenceData.CapabilityId(key);

    public static HashSet<string> Materialize(TemplateSelectionInput selection) =>
        Materializer.Materialize(selection.ScopeModel, selection.SelectedScope, selection.Grants);

    public static HashSet<string> MaterializeAll(IEnumerable<TemplateSelectionInput> selections)
    {
        HashSet<string> result = new();
        foreach (TemplateSelectionInput s in selections)
            result.UnionWith(Materialize(s));
        return result;
    }
}
