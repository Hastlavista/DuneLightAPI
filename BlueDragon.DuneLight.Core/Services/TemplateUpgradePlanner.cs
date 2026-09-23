using System;
using System.Collections.Generic;
using System.Linq;
using BlueDragon.DuneLight.Core.Enums;
using BlueDragon.DuneLight.Core.Interfaces.Capabilities;

namespace BlueDragon.DuneLight.Core.Services;

/// <summary>
/// FAZA 3 (v2 template-upgrade) — čisti, bez-stanja three-way-merge algoritam (Parts E/F/G/H iz plana). Uzima
/// isključivo plain-record ulaz (TemplateUpgradePlanningInput) i ICapabilityMaterializationService (i sam čista
/// funkcija) — NIKAD ne pristupa bazi/EF-u, pa je potpuno jedinično testabilan bez DbContext-a. Poziva ga
/// GrantGroupTemplateUpgradeService iz sva tri toka (GetDiff s praznim resolutions, Preview i Apply s Owner-ovim
/// resolutions) — ISTI kod put, nikad duplicirana logika.
///
/// Klasifikacija po capability-ju (Part G, četiri slučaja) — BaseScope/CurrentScope/TargetScope su
/// CapabilitySelectedScope.None kad capability u toj strani uopće ne postoji (implicitni BASE=None za capability
/// bez ijednog snapshot retka, vidi TemplateUpgradePlanningInput.BaseResolver napomenu):
/// 1. unchanged-by-tenant (Current == Base) — SLIJEDI TARGET bezuvjetno (Added ako Base=None&lt;Target!=None,
///    Removed ako Base!=None&amp;Target=None, Changed ako oboje != None i različiti, inače no-op).
/// 2. customized-by-tenant, target-nepromijenjen (Current != Base, Target == Base) — ZADRŽI CURRENT, bez diff/konflikt unosa.
/// 3. customized-by-tenant, target-promijenjen-ali-ISTO kao Current (Current != Base, Target != Base, Current == Target) —
///    zadrži (== prihvati Target, isto), bez konflikta (nema što razrješavati).
/// 4. customized-by-tenant, target-promijenjen-i-RAZLIČIT (Current != Base, Target != Base, Current != Target) —
///    KONFLIKT, DefaultResolution=PreserveCurrent, zahtijeva eksplicitnu Owner odluku (Part K strict gating).
/// </summary>
public class TemplateUpgradePlanner
{
    private readonly ICapabilityMaterializationService _capabilityMaterializationService;

    public TemplateUpgradePlanner(ICapabilityMaterializationService capabilityMaterializationService)
    {
        _capabilityMaterializationService = capabilityMaterializationService;
    }

    public TemplateUpgradePlanningResult Plan(TemplateUpgradePlanningInput input)
    {
        Dictionary<string, CapabilitySnapshotInput> currentByKey = input.CurrentSnapshots.ToDictionary(s => s.CapabilityKey);
        Dictionary<string, TemplateSelectionInput> targetByKey = input.TargetSelections.ToDictionary(s => s.CapabilityKey);

        HashSet<string> allKeys = new(currentByKey.Keys);
        allKeys.UnionWith(targetByKey.Keys);

        List<CapabilityDiffEntry> added = new();
        List<CapabilityDiffEntry> removed = new();
        List<CapabilityDiffEntry> changed = new();
        List<UpgradeConflict> conflicts = new();
        List<TemplateSelectionInput> resultingSelections = new();

        foreach (string capabilityKey in allKeys.OrderBy(k => k, StringComparer.Ordinal))
        {
            currentByKey.TryGetValue(capabilityKey, out CapabilitySnapshotInput current);
            targetByKey.TryGetValue(capabilityKey, out TemplateSelectionInput target);

            Guid resolveDefinitionId = target?.CapabilityDefinitionId ?? current.CapabilityDefinitionId;
            TemplateSelectionInput baseSelection = input.BaseResolver(resolveDefinitionId);

            CapabilitySelectedScope baseScope = baseSelection?.SelectedScope ?? CapabilitySelectedScope.None;
            CapabilitySelectedScope currentScope = current?.SelectedScope ?? CapabilitySelectedScope.None;
            CapabilitySelectedScope targetScope = target?.SelectedScope ?? CapabilitySelectedScope.None;

            TemplateSelectionInput resultingSelection;

            if (currentScope == baseScope)
            {
                // Case 1 — unchanged-by-tenant: slijedi TARGET bezuvjetno.
                resultingSelection = target;

                if (baseScope == CapabilitySelectedScope.None && targetScope != CapabilitySelectedScope.None)
                    added.Add(new CapabilityDiffEntry(capabilityKey, CurrentScope: null, targetScope));
                else if (baseScope != CapabilitySelectedScope.None && targetScope == CapabilitySelectedScope.None)
                    removed.Add(new CapabilityDiffEntry(capabilityKey, currentScope, TargetScope: null));
                else if (baseScope != targetScope)
                    changed.Add(new CapabilityDiffEntry(capabilityKey, currentScope, targetScope));
                // baseScope == targetScope (uključujući oboje None) — pravi no-op, bez diff unosa.
            }
            else if (targetScope == baseScope)
            {
                // Case 2 — tenant je prilagodio, TARGET ovu capability nije dirao: zadrži CURRENT, tiho.
                resultingSelection = current == null
                    ? null
                    : new TemplateSelectionInput(current.CapabilityDefinitionId, current.CapabilityKey, current.ScopeModel, current.SelectedScope, current.Grants);
            }
            else if (currentScope == targetScope)
            {
                // Case 3 — oboje odstupaju od BASE, ali su međusobno ISTI — nema stvarnog konflikta, prihvati.
                resultingSelection = target;
            }
            else
            {
                // Case 4 — pravi tro-smjerni konflikt.
                conflicts.Add(new UpgradeConflict(capabilityKey, baseScope, currentScope, targetScope));

                if (input.Resolutions.TryGetValue(capabilityKey, out ConflictResolution resolution) && resolution == ConflictResolution.UseTemplate)
                    resultingSelection = target;
                else
                    // PreserveCurrent (eksplicitan ILI privremeni fallback dok se ne provjeri IsFullyResolved).
                    resultingSelection = current == null
                        ? null
                        : new TemplateSelectionInput(current.CapabilityDefinitionId, current.CapabilityKey, current.ScopeModel, current.SelectedScope, current.Grants);
            }

            if (resultingSelection != null && resultingSelection.SelectedScope != CapabilitySelectedScope.None)
                resultingSelections.Add(resultingSelection);
        }

        List<string> unresolvedKeys = conflicts
            .Select(c => c.CapabilityKey)
            .Where(key => !input.Resolutions.ContainsKey(key))
            .OrderBy(k => k, StringComparer.Ordinal)
            .ToList();
        bool isFullyResolved = unresolvedKeys.Count == 0;

        // ManualAdvancedSet — vidi Part H, raw grantovi koje NIJEDAN CURRENT capability/template izvor ne
        // opravdava (Owner ih je ranije ručno dodao preko Advanced editora). Preživljava upgrade NEDIRNUT.
        HashSet<string> currentCapabilityDerivedSet = MaterializeAll(input.CurrentSnapshots.Select(ToTemplateSelection));
        HashSet<string> manualAdvancedSet = new(input.ExistingRawGrantKeys);
        manualAdvancedSet.ExceptWith(currentCapabilityDerivedSet);
        manualAdvancedSet.ExceptWith(input.CurrentTemplateCompatibilityGrantKeys);

        HashSet<string> resultingCapabilityDerivedSet = MaterializeAll(resultingSelections);

        HashSet<string> finalRawSet = new(resultingCapabilityDerivedSet);
        finalRawSet.UnionWith(input.TargetTemplateCompatibilityGrantKeys);
        finalRawSet.UnionWith(manualAdvancedSet);

        RawGrantSource ClassifyNew(string key) =>
            resultingCapabilityDerivedSet.Contains(key) ? RawGrantSource.Capability
            : input.TargetTemplateCompatibilityGrantKeys.Contains(key) ? RawGrantSource.TemplateCompatibility
            : RawGrantSource.ManualAdvanced;

        RawGrantSource ClassifyOld(string key) =>
            currentCapabilityDerivedSet.Contains(key) ? RawGrantSource.Capability
            : input.CurrentTemplateCompatibilityGrantKeys.Contains(key) ? RawGrantSource.TemplateCompatibility
            : RawGrantSource.ManualAdvanced;

        List<RawGrantChange> rawGrantsAdded = finalRawSet.Except(input.ExistingRawGrantKeys)
            .OrderBy(k => k, StringComparer.Ordinal)
            .Select(k => new RawGrantChange(k, ClassifyNew(k)))
            .ToList();

        List<RawGrantChange> rawGrantsRemoved = input.ExistingRawGrantKeys.Except(finalRawSet)
            .OrderBy(k => k, StringComparer.Ordinal)
            .Select(k => new RawGrantChange(k, ClassifyOld(k)))
            .ToList();

        List<RawGrantChange> rawGrantsUnchanged = finalRawSet.Intersect(input.ExistingRawGrantKeys)
            .OrderBy(k => k, StringComparer.Ordinal)
            .Select(k => new RawGrantChange(k, ClassifyNew(k)))
            .ToList();

        return new TemplateUpgradePlanningResult(
            AddedCapabilities: added,
            RemovedCapabilities: removed,
            ChangedCapabilities: changed,
            Conflicts: conflicts,
            ResultingCapabilitySelections: resultingSelections,
            ResultingTemplateCompatibilityGrantKeys: input.TargetTemplateCompatibilityGrantKeys,
            PreservedManualGrantKeys: manualAdvancedSet,
            RawGrantsAdded: rawGrantsAdded,
            RawGrantsRemoved: rawGrantsRemoved,
            RawGrantsUnchanged: rawGrantsUnchanged,
            IsFullyResolved: isFullyResolved,
            UnresolvedConflictCapabilityKeys: unresolvedKeys);
    }

    private HashSet<string> MaterializeAll(IEnumerable<TemplateSelectionInput> selections)
    {
        HashSet<string> result = new();
        foreach (TemplateSelectionInput selection in selections)
            result.UnionWith(_capabilityMaterializationService.Materialize(selection.ScopeModel, selection.SelectedScope, selection.Grants));
        return result;
    }

    private static TemplateSelectionInput ToTemplateSelection(CapabilitySnapshotInput snapshot) =>
        new(snapshot.CapabilityDefinitionId, snapshot.CapabilityKey, snapshot.ScopeModel, snapshot.SelectedScope, snapshot.Grants);
}
