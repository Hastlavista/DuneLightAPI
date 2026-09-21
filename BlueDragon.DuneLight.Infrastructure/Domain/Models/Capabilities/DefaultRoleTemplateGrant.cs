using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using BlueDragon.DuneLight.Core.Enums;

namespace BlueDragon.DuneLight.Infrastructure.Domain.Models.Capabilities;

/// <summary>
/// Predložak-vlasnički "compatibility extra" raw grant (mora postojati u Grants.Catalog) — NIJE ručni/Advanced
/// tenant grant. Postoji isključivo zato što odabrani capability-scope-ovi (npr. maksimalni "All"/"Manage") ne
/// mogu strukturno pokriti jedan legacy raw grant (vidi CapabilityMaterializationService — "All ne materijalizira
/// Own"), a predložak MORA točno reproducirati postojeće ponašanje. Vidi DefaultRoleTemplateGrantReason.
/// </summary>
[Table("default_role_template_grants")]
public class DefaultRoleTemplateGrant
{
    [Key]
    [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
    [Column("id")]
    public Guid? Id { get; set; }

    [Column("default_role_template_id")]
    public Guid DefaultRoleTemplateId { get; set; }

    [Column("grant_key")]
    public string GrantKey { get; set; }

    [Column("reason")]
    public DefaultRoleTemplateGrantReason Reason { get; set; }

    public DefaultRoleTemplate DefaultRoleTemplate { get; set; }
}
