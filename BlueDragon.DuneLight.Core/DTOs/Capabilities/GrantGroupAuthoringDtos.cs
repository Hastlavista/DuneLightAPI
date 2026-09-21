using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using BlueDragon.DuneLight.Core.DTOs.Permissions;
using BlueDragon.DuneLight.Core.Enums;

namespace BlueDragon.DuneLight.Core.DTOs.Capabilities;

/// <summary>FAZA 2 Part B — jedan capability-odabir unutar zahtjeva za autorstvo GrantGroup-e. Backend razrješava
/// CapabilityKey+CapabilityVersion u konkretnu CapabilityDefinition i materijalizira SelectedScope u raw grantove;
/// klijent NIKAD ne šalje gotov raw grant skup (vidi GrantGroupCapabilityWriteRequest).</summary>
public class GrantGroupCapabilitySelectionRequest
{
    [Required]
    public string CapabilityKey { get; set; }

    [Required]
    public int CapabilityVersion { get; set; }

    [Required]
    public CapabilitySelectedScope SelectedScope { get; set; }
}

/// <summary>FAZA 2 Part B — capability-aware create/update zahtjev za GrantGroup. Backend je autoritativan izvor
/// konačnog raw grant skupa (CapabilityDerivedSet ∪ TemplateCompatibilitySet ∪ ManualGrantKeys) — vidi
/// GrantGroupCapabilityAuthoringService.</summary>
public class GrantGroupCapabilityWriteRequest
{
    [Required]
    [MaxLength(255)]
    public string Name { get; set; }

    [Required]
    public List<GrantGroupCapabilitySelectionRequest> CapabilitySelections { get; set; } = new();

    /// <summary>Legitimni ručni/Advanced raw grantovi — MORAJU postojati u Grants katalogu i NE SMIJU se
    /// preklapati s onim što odabrane capability-je/predložak već proizvode (vidi ErrorCodes.GrantAlreadyCapabilityDerived).</summary>
    [Required]
    public List<string> ManualGrantKeys { get; set; } = new();
}

public record GrantGroupCapabilitySelectionDto(string CapabilityKey, int CapabilityVersion, CapabilitySelectedScope SelectedScope);

/// <summary>FAZA 2 Part H/I — puno autoritativno stanje jedne GrantGroup nakon capability-aware create/update ILI
/// pri čitanju za role-editor (GET .../authoring-state). Frontend NE treba rekonstruirati provenance sam.</summary>
public class GrantGroupAuthoringDto
{
    public GrantGroupDto GrantGroup { get; set; }

    /// <summary>Prazno za legacy/drifted grupu bez snapshot metapodataka (vidi HasCapabilityMetadata).</summary>
    public List<GrantGroupCapabilitySelectionDto> CapabilitySelections { get; set; } = new();

    /// <summary>Za legacy/drifted grupu (HasCapabilityMetadata=false) namjerno prazno — sirovi grantovi ostaju u
    /// GrantGroup.Grants bez pretpostavljene provenance (vidi FAZA 2 Part J).</summary>
    public List<string> ManualGrantKeys { get; set; } = new();

    /// <summary>Dijagnostika/read-only prikaz Advanced sekcije — unija capability-derived i template-compatibility
    /// raw grantova. NIJE runtime autorizacija.</summary>
    public List<string> DerivedGrantKeys { get; set; } = new();

    public string TemplateSourceKey { get; set; }
    public int? TemplateSourceVersion { get; set; }

    /// <summary>False znači "nema stabilnu capability provenance" — legacy/predviđena prije FAZE 1, ili nastala
    /// prije prvog capability-aware save-a. Vidi FAZA 2 Part J — GET je nikad ne konvertira.</summary>
    public bool HasCapabilityMetadata { get; set; }

    /// <summary>True ako grupa nema template provenance, ima Manual Advanced grantove, ili njeni trenutni capability
    /// odabiri/compatibility grantovi odstupaju od izvornog predloška (vidi FAZA 2 Part F).</summary>
    public bool IsCustomized { get; set; }
}
