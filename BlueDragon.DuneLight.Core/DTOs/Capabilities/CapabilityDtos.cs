using System.Collections.Generic;
using BlueDragon.DuneLight.Core.Enums;

namespace BlueDragon.DuneLight.Core.DTOs.Capabilities;

/// <summary>Read-only projekcija capabilityja iz statičnog CapabilityCatalog-a (ADR-0023) za role editor.</summary>
public record CapabilityDefinitionGrantDto(string GrantKey, CapabilityGrantRole Role);

public record CapabilityDefinitionDto(
    string Key,
    string CategoryKey,
    CapabilityScopeModel ScopeModel,
    CapabilitySensitivity Sensitivity,
    List<CapabilityDefinitionGrantDto> Grants);
