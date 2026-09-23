using System;
using System.Collections.Generic;
using BlueDragon.DuneLight.Core.Enums;

namespace BlueDragon.DuneLight.Core.Interfaces.Capabilities;

/// <summary>FAZA 3 (v2 template-upgrade) — Owner-ova eksplicitna odluka za jedan konflikt (CURRENT i TARGET oboje
/// odstupaju od BASE, i međusobno se razlikuju). PreserveCurrent zadržava Owner-ovu postojeću postavku (backend
/// default preporuka); UseTemplate prihvaća novu vrijednost iz ciljnog predloška.</summary>
public enum ConflictResolution
{
    PreserveCurrent,
    UseTemplate
}

/// <summary>Označava ZAŠTO se jedan raw grant nalazi u konačnom skupu nakon upgrade-a — čisto dijagnostički tag
/// za audit/diff prikaz, NIJE dio runtime autorizacije (vidi GrantGroupGrant/GrantResolver).</summary>
public enum RawGrantSource
{
    Capability,
    TemplateCompatibility,
    ManualAdvanced
}

/// <summary>Jedan CURRENT capability-odabir (snapshot) + dovoljno podataka (ScopeModel + Grants) za
/// materijalizaciju BEZ dodatnog upita u bazu — isti oblik kao TemplateCapabilitySelection, plus provenance
/// (SourceTemplateKey/Version) koju TemplateUpgradePlanner koristi SAMO za dijagnostiku, ne za BASE razrješavanje
/// (BASE dolazi isključivo preko baseResolver delegata, vidi TemplateUpgradePlanningInput).</summary>
public record CapabilitySnapshotInput(
    Guid CapabilityDefinitionId,
    string CapabilityKey,
    CapabilityScopeModel ScopeModel,
    CapabilitySelectedScope SelectedScope,
    IReadOnlyList<CapabilityGrantRoleEntry> Grants,
    string SourceTemplateKey,
    int? SourceTemplateVersion);

/// <summary>Jedan TARGET (ili BASE) predložak capability-odabir — isti oblik kao TemplateCapabilitySelection, bez
/// provenance polja (predložak-razrješenje nema tenant-specifičnu provenance).</summary>
public record TemplateSelectionInput(
    Guid CapabilityDefinitionId,
    string CapabilityKey,
    CapabilityScopeModel ScopeModel,
    CapabilitySelectedScope SelectedScope,
    IReadOnlyList<CapabilityGrantRoleEntry> Grants);

/// <summary>Jedan DODAT/PROMIJENJEN/UKLONJEN capability-unos u diff prikazu (Part G case 1/3, template-driven
/// promjena BEZ konflikta — tenant nije ovu capability prilagodio). CurrentScope/TargetScope su null kad capability
/// u toj strani uopće ne postoji (None se tretira kao "ne postoji" po Part G).</summary>
public record CapabilityDiffEntry(
    string CapabilityKey,
    CapabilitySelectedScope? CurrentScope,
    CapabilitySelectedScope? TargetScope);

/// <summary>Jedan nerazriješen/razrješiv konflikt (Part G case 4) — CURRENT i TARGET oboje odstupaju od BASE i
/// međusobno se razlikuju. DefaultResolution je UVIJEK PreserveCurrent (vidi Part K/strict-gating odluka) — samo
/// preporuka, backend nikad ne primjenjuje default sam od sebe bez eksplicitne Owner odluke.</summary>
public record UpgradeConflict(
    string CapabilityKey,
    CapabilitySelectedScope BaseScope,
    CapabilitySelectedScope CurrentScope,
    CapabilitySelectedScope TargetScope,
    ConflictResolution DefaultResolution = ConflictResolution.PreserveCurrent);

/// <summary>Jedan raw grant-ključ u FinalRawSet-u/diff-u, otagiran razlogom postojanja — vidi RawGrantSource.</summary>
public record RawGrantChange(string GrantKey, RawGrantSource Source);

/// <summary>FAZA 3 — čisti, bez-stanja ulaz za TemplateUpgradePlanner.Plan. Namjerno SVE plain records/delegate —
/// BEZ EF entiteta, BEZ baze — da algoritam ostane jedinično testabilan bez DbContext-a (vidi UnitTests projekt).
/// BaseResolver je jedini način na koji planner "vidi" BASE (točno onu predložak-verziju zabilježenu u
/// snapshot.SourceTemplateVersion) — servisni sloj ga puni preko DefaultRoleTemplateHandler.GetByKey(key,
/// exactVersion), planner sam nikad ne pristupa bazi.</summary>
public record TemplateUpgradePlanningInput(
    string TemplateKey,
    int CurrentTemplateVersion,
    int TargetTemplateVersion,
    IReadOnlyList<CapabilitySnapshotInput> CurrentSnapshots,
    IReadOnlyList<TemplateSelectionInput> TargetSelections,
    IReadOnlySet<string> CurrentTemplateCompatibilityGrantKeys,
    IReadOnlySet<string> TargetTemplateCompatibilityGrantKeys,
    IReadOnlySet<string> ExistingRawGrantKeys,
    Func<Guid, TemplateSelectionInput> BaseResolver,
    IReadOnlyDictionary<string, ConflictResolution> Resolutions);

/// <summary>Rezultat jednog TemplateUpgradePlanner.Plan poziva. IsFullyResolved=false znači: barem jedan Conflicts
/// unos NEMA odgovarajući Resolutions unos — u tom slučaju su ResultingXxx/RawGrantsXxx polja privremena/nevažeća
/// (planner ih interno računa s PreserveCurrent fallbackom da ostane deterministički, ali servisni sloj MORA
/// odbiti Preview/Apply s GRANT_GROUP_UPGRADE_CONFLICT_RESOLUTION_REQUIRED prije nego ih iskoristi — vidi Part G
/// "missing-resolution guard"). GetDiff (prazan Resolutions) NIKAD ne treba biti "fully resolved" — to je normalan
/// preview slučaj, ne greška.</summary>
public record TemplateUpgradePlanningResult(
    IReadOnlyList<CapabilityDiffEntry> AddedCapabilities,
    IReadOnlyList<CapabilityDiffEntry> RemovedCapabilities,
    IReadOnlyList<CapabilityDiffEntry> ChangedCapabilities,
    IReadOnlyList<UpgradeConflict> Conflicts,
    IReadOnlyList<TemplateSelectionInput> ResultingCapabilitySelections,
    IReadOnlySet<string> ResultingTemplateCompatibilityGrantKeys,
    IReadOnlySet<string> PreservedManualGrantKeys,
    IReadOnlyList<RawGrantChange> RawGrantsAdded,
    IReadOnlyList<RawGrantChange> RawGrantsRemoved,
    IReadOnlyList<RawGrantChange> RawGrantsUnchanged,
    bool IsFullyResolved,
    IReadOnlyList<string> UnresolvedConflictCapabilityKeys);
