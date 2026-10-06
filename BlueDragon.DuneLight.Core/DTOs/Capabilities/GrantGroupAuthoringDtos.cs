using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using BlueDragon.DuneLight.Core.DTOs.Permissions;
using BlueDragon.DuneLight.Core.Enums;

namespace BlueDragon.DuneLight.Core.DTOs.Capabilities;

/// <summary>Jedan capability-odabir unutar zahtjeva za autorstvo GrantGroup-e. Backend razrješava CapabilityKey u
/// capability iz statičnog CapabilityCatalog-a i materijalizira SelectedScope u raw grantove; klijent nikad ne šalje
/// gotov raw grant skup (vidi GrantGroupCapabilityWriteRequest).</summary>
public class GrantGroupCapabilitySelectionRequest
{
    [Required]
    public string CapabilityKey { get; set; }

    [Required]
    public CapabilitySelectedScope SelectedScope { get; set; }
}

/// <summary>Capability-aware create/update zahtjev za GrantGroup. Konačni raw grant skup =
/// materijalizirani capability odabiri ∪ ManualGrantKeys; sprema se samo taj skup (GrantGroupGrant), odabiri se ne
/// pamte (ADR-0023).</summary>
public class GrantGroupCapabilityWriteRequest
{
    [Required]
    [MaxLength(255)]
    public string Name { get; set; }

    [Required]
    public List<GrantGroupCapabilitySelectionRequest> CapabilitySelections { get; set; } = new();

    /// <summary>Dodatni raw grantovi — moraju postojati u Grants katalogu i ne smiju se preklapati s onim što odabrani
    /// capabilityji već proizvode (vidi ErrorCodes.GrantAlreadyCapabilityDerived).</summary>
    [Required]
    public List<string> ManualGrantKeys { get; set; } = new();
}

public record GrantGroupCapabilitySelectionDto(string CapabilityKey, CapabilitySelectedScope SelectedScope);

/// <summary>Authoring-state jedne GrantGroup-e, IZVEDEN iz njenih raw grantova (ADR-0023): za svaki capability najveći
/// opseg čiji je skup grantova u cijelosti sadržan u grupi; grantovi koje nijedan odabrani capability ne objašnjava
/// su ručni. Ništa od ovoga se ne sprema.</summary>
public class GrantGroupAuthoringDto
{
    public GrantGroupDto GrantGroup { get; set; }

    /// <summary>Samo capabilityji s opsegom različitim od None.</summary>
    public List<GrantGroupCapabilitySelectionDto> CapabilitySelections { get; set; } = new();

    /// <summary>Grantovi grupe koje nijedan izvedeni capability odabir ne objašnjava.</summary>
    public List<string> ManualGrantKeys { get; set; } = new();

    /// <summary>Grantovi grupe koje objašnjavaju izvedeni capability odabiri.</summary>
    public List<string> DerivedGrantKeys { get; set; } = new();
}
