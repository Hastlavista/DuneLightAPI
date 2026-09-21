using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using BlueDragon.DuneLight.Core.Enums;
using BlueDragon.DuneLight.Infrastructure.Domain.Models.Permissions;

namespace BlueDragon.DuneLight.Infrastructure.Domain.Models.Capabilities;

/// <summary>
/// Snima TOČNO koja CapabilityDefinition verzija + koji SelectedScope je materijaliziran u jednu GrantGroup —
/// čisto provenance/UI metapodatak. NIKAD se ne koristi za izvođenje runtime ovlasti (to ostaje isključivo
/// GrantGroupGrant, vidi GrantResolver) — samo omogućuje da budući role-editor prikaže "ova grupa ima X capability
/// na Y opsegu" umjesto da nagađa iz sirovih grantova.
/// </summary>
[Table("grant_group_capability_snapshots")]
public class GrantGroupCapabilitySnapshot
{
    [Key]
    [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
    [Column("id")]
    public Guid? Id { get; set; }

    [Column("grant_group_id")]
    public Guid GrantGroupId { get; set; }

    [Column("capability_definition_id")]
    public Guid CapabilityDefinitionId { get; set; }

    [Column("selected_scope")]
    public CapabilitySelectedScope SelectedScope { get; set; }

    [Column("source_template_key")]
    public string SourceTemplateKey { get; set; }

    [Column("source_template_version")]
    public int? SourceTemplateVersion { get; set; }

    [Column("applied_at")]
    public DateTimeOffset AppliedAt { get; set; }

    [Column("applied_by")]
    public Guid? AppliedBy { get; set; }

    public GrantGroup GrantGroup { get; set; }
    public CapabilityDefinition CapabilityDefinition { get; set; }
}
