using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using BlueDragon.DuneLight.Core.Enums;

namespace BlueDragon.DuneLight.Infrastructure.Domain.Models.Capabilities;

/// <summary>
/// Autorsko-vrijeme metapodatak (NE runtime autorizacija) — jedna verzija jedne "sposobnosti" koju tenant Owner
/// bira pri slaganju role-a. Runtime autorizacija i dalje isključivo čita raw GrantGroupGrant retke (vidi
/// RequireGrantAttribute/GrantResolver) — ova tablica samo opisuje KOJI raw grantovi stoje iza jedne capability-
/// scope kombinacije (preko CapabilityDefinitionGrant), radi budućeg role-editor UI-a.
///
/// Key je imutabilan nakon kreiranja i zajedno s Version čini identitet — nova semantika/mapiranje UVIJEK ide kao
/// nova Version (novi redak), nikad mutacija već referenciranog retka (vidi CapabilityVersionGuard).
/// </summary>
[Table("capability_definitions")]
public class CapabilityDefinition
{
    [Key]
    [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
    [Column("id")]
    public Guid? Id { get; set; }

    [Column("key")]
    public string Key { get; set; }

    [Column("version")]
    public int Version { get; set; }

    [Column("category_key")]
    public string CategoryKey { get; set; }

    [Column("scope_model")]
    public CapabilityScopeModel ScopeModel { get; set; }

    [Column("sensitivity")]
    public CapabilitySensitivity Sensitivity { get; set; }

    [Column("is_active")]
    public bool IsActive { get; set; }

    [Column("deprecated_at")]
    public DateTimeOffset? DeprecatedAt { get; set; }

    [Column("created_at")]
    public DateTimeOffset CreatedAt { get; set; }

    [Column("created_by")]
    public Guid? CreatedBy { get; set; }

    public List<CapabilityDefinitionGrant> Grants { get; set; } = new();
}
