using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using BlueDragon.DuneLight.Core.Enums;

namespace BlueDragon.DuneLight.Infrastructure.Domain.Models.Capabilities;

/// <summary>Jedan raw grant-ključ (mora postojati u Grants.Catalog — validira se u servisu, isti obrazac kao
/// GrantGroupGrant) koji pripada jednoj CapabilityDefinition verziji, s ulogom koja određuje pri kojem
/// CapabilitySelectedScope se materijalizira (vidi CapabilityMaterializationService).</summary>
[Table("capability_definition_grants")]
public class CapabilityDefinitionGrant
{
    [Key]
    [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
    [Column("id")]
    public Guid? Id { get; set; }

    [Column("capability_definition_id")]
    public Guid CapabilityDefinitionId { get; set; }

    [Column("grant_key")]
    public string GrantKey { get; set; }

    [Column("role")]
    public CapabilityGrantRole Role { get; set; }

    public CapabilityDefinition CapabilityDefinition { get; set; }
}
