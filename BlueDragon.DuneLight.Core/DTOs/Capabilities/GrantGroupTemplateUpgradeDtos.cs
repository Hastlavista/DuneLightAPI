using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using BlueDragon.DuneLight.Core.Enums;

namespace BlueDragon.DuneLight.Core.DTOs.Capabilities;

/// <summary>FAZA 3 (v2 template-upgrade) — "je li dostupan upgrade" indikator za banner na role-editoru. HasUpgrade
/// je NEOVISAN o IsCustomized (Part B — drift/prilagodba NIKAD ne skriva dostupnost novije predložak-verzije).</summary>
public class GrantGroupTemplateUpgradeStatusDto
{
    public Guid GrantGroupId { get; set; }
    public bool HasUpgrade { get; set; }
    public string TemplateKey { get; set; }
    public int? CurrentTemplateVersion { get; set; }
    public int? LatestTemplateVersion { get; set; }
    public bool IsCustomized { get; set; }
}

/// <summary>Jedan DODAT/PROMIJENJEN/UKLONJEN capability unos u diff prikazu — vidi CapabilityDiffEntry (Core
/// planner-model) za točnu semantiku null vrijednosti.</summary>
public record CapabilityDiffEntryDto(string CapabilityKey, CapabilitySelectedScope? CurrentScope, CapabilitySelectedScope? TargetScope);

/// <summary>Jedan raw grant unutar diff/plan prikaza, otagiran razlogom postojanja — "Tehnički detalji" sekcija na
/// frontendu, NIJE runtime autorizacija.</summary>
public record RawGrantChangeDto(string GrantKey, string Source);

/// <summary>Predložak-identitet (Key+Version) na jednoj strani jednog upgrade-diffa — vidi GrantGroupTemplateDiffDto.
/// Ugniježđeno (ne pljosnato Current/TargetTemplateKey/Version) da odgovara izvornom Part E ugovoru "Current
/// template: {key, version}" i frontend GrantGroupTemplateDiffDto sučelju.</summary>
public record TemplateVersionRefDto(string Key, int Version);

/// <summary>Jedan konflikt (CURRENT i TARGET oboje odstupaju od BASE i međusobno se razlikuju) — DefaultResolution
/// je UVIJEK "PreserveCurrent" (preporuka), Owner mora eksplicitno odabrati (Part K strict gating, backend to
/// neovisno provjerava preko GRANT_GROUP_UPGRADE_CONFLICT_RESOLUTION_REQUIRED).</summary>
public record ConflictDto(string CapabilityKey, CapabilitySelectedScope BaseScope, CapabilitySelectedScope CurrentScope, CapabilitySelectedScope TargetScope, string DefaultResolution);

/// <summary>Puni review-diff za jedan predloženi upgrade (GET .../template-upgrade-diff) — čisto read-only, ne piše
/// ništa. StateToken je deterministički hash trenutnog stanja (vidi GrantGroupTemplateUpgradeService) — Preview/Apply
/// MORAJU proslijediti ISTI token da backend otkrije "stanje se promijenilo negdje drugdje" (Part K).</summary>
public class GrantGroupTemplateDiffDto
{
    public TemplateVersionRefDto CurrentTemplate { get; set; }
    public TemplateVersionRefDto TargetTemplate { get; set; }

    public List<CapabilityDiffEntryDto> AddedCapabilities { get; set; } = new();
    public List<CapabilityDiffEntryDto> RemovedCapabilities { get; set; } = new();
    public List<CapabilityDiffEntryDto> ChangedCapabilities { get; set; } = new();

    public List<RawGrantChangeDto> RawGrantsAdded { get; set; } = new();
    public List<RawGrantChangeDto> RawGrantsRemoved { get; set; } = new();
    public List<RawGrantChangeDto> RawGrantsUnchanged { get; set; } = new();

    public List<ConflictDto> Conflicts { get; set; } = new();

    public string StateToken { get; set; }
}

public class ConflictResolutionEntryRequest
{
    [Required]
    public string CapabilityKey { get; set; }

    /// <summary>"PreserveCurrent" ili "UseTemplate" — vidi ConflictResolution enum.</summary>
    [Required]
    public string Resolution { get; set; }
}

/// <summary>FAZA 3 Part K — zajednički zahtjev za Preview i Apply. StateToken MORA odgovarati trenutnom
/// server-side stanju (isti izračun kao GetDiff) — inače GRANT_GROUP_UPGRADE_STATE_CHANGED (409). Resolutions mora
/// pokrivati SVAKI konflikt iz diff-a, inače GRANT_GROUP_UPGRADE_CONFLICT_RESOLUTION_REQUIRED (409) — backend to
/// provjerava neovisno o frontend gatingu (Part K strict-gating odluka).</summary>
public class GrantGroupTemplateUpgradePlanRequest
{
    [Required]
    public int TargetTemplateVersion { get; set; }

    [Required]
    public List<ConflictResolutionEntryRequest> Resolutions { get; set; } = new();

    [Required]
    public string StateToken { get; set; }
}

/// <summary>Odgovor na Preview — pokazuje TOČNO što bi Apply napravio (isti planner poziv, iste ulazne
/// resolutions), bez ikakvog upisa u bazu. Apply koristi identičan izračun, samo unutar transakcije.</summary>
public class GrantGroupTemplateUpgradePlanDto
{
    public List<GrantGroupCapabilitySelectionDto> ResultingCapabilitySelections { get; set; } = new();
    public List<string> PreservedManualGrantKeys { get; set; } = new();
    public List<string> ResultingTemplateCompatibilityGrants { get; set; } = new();

    public List<RawGrantChangeDto> RawGrantsAdded { get; set; } = new();
    public List<RawGrantChangeDto> RawGrantsRemoved { get; set; } = new();

    public List<string> ConflictsResolved { get; set; } = new();

    public string StateToken { get; set; }
}
