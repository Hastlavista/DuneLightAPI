using System;
using System.Collections.Generic;
using BlueDragon.DuneLight.Core.Enums;

namespace BlueDragon.DuneLight.Core.DTOs.Capabilities;

/// <summary>FAZA 1 Part R — read-only projekcije za budući role-editor UI. Ne izlažu se mutacijski endpointi u
/// ovoj fazi (vidi CapabilitiesController).</summary>
public record CapabilityDefinitionGrantDto(string GrantKey, CapabilityGrantRole Role);

public record CapabilityDefinitionDto(
    Guid Id,
    string Key,
    int Version,
    string CategoryKey,
    CapabilityScopeModel ScopeModel,
    CapabilitySensitivity Sensitivity,
    bool IsActive,
    DateTimeOffset? DeprecatedAt,
    List<CapabilityDefinitionGrantDto> Grants);

public record DefaultRoleTemplateCapabilityDto(
    Guid CapabilityDefinitionId,
    string CapabilityKey,
    int CapabilityVersion,
    CapabilitySelectedScope SelectedScope);

public record DefaultRoleTemplateGrantDto(string GrantKey, DefaultRoleTemplateGrantReason Reason);

public record DefaultRoleTemplateDto(
    Guid Id,
    string Key,
    int Version,
    string DisplayNameHr,
    bool IsActive,
    List<DefaultRoleTemplateCapabilityDto> Capabilities,
    List<DefaultRoleTemplateGrantDto> CompatibilityGrants);

public record GrantGroupCapabilitySnapshotDto(
    Guid CapabilityDefinitionId,
    string CapabilityKey,
    int CapabilityVersion,
    CapabilitySelectedScope SelectedScope,
    string SourceTemplateKey,
    int? SourceTemplateVersion,
    DateTimeOffset AppliedAt);

public record GrantGroupTemplateGrantDto(string GrantKey, string SourceTemplateKey, int SourceTemplateVersion, DateTimeOffset AppliedAt);

/// <summary>Je li GrantGroup nastala iz predloška — "based on" je isključivo iz snapshot metapodataka (stabilno),
/// NE iz Name-a (vidi DefaultGrantGroupDriftChecker ograničenje koje ovo rješava kad je snapshot dostupan).</summary>
public record GrantGroupTemplateMatchDto(
    Guid GrantGroupId,
    bool HasSnapshotMetadata,
    string SourceTemplateKey,
    int? SourceTemplateVersion,
    List<GrantGroupCapabilitySnapshotDto> Snapshots,
    List<GrantGroupTemplateGrantDto> TemplateGrants);
