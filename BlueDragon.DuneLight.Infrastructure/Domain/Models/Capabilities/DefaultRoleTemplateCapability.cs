using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using BlueDragon.DuneLight.Core.Enums;

namespace BlueDragon.DuneLight.Infrastructure.Domain.Models.Capabilities;

/// <summary>Jedan odabir opsega jedne EGZAKTNE CapabilityDefinition verzije unutar jednog DefaultRoleTemplate.
/// Odsutnost retka za neku capability znači isto što i SelectedScope=None (nije odabrana) — predlošci namjerno
/// sadrže samo retke za STVARNO odabrane capability-je.</summary>
[Table("default_role_template_capabilities")]
public class DefaultRoleTemplateCapability
{
    [Key]
    [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
    [Column("id")]
    public Guid? Id { get; set; }

    [Column("default_role_template_id")]
    public Guid DefaultRoleTemplateId { get; set; }

    [Column("capability_definition_id")]
    public Guid CapabilityDefinitionId { get; set; }

    [Column("selected_scope")]
    public CapabilitySelectedScope SelectedScope { get; set; }

    public DefaultRoleTemplate DefaultRoleTemplate { get; set; }
    public CapabilityDefinition CapabilityDefinition { get; set; }
}
