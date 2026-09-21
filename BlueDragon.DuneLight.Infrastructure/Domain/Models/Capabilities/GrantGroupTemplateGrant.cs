using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using BlueDragon.DuneLight.Infrastructure.Domain.Models.Permissions;

namespace BlueDragon.DuneLight.Infrastructure.Domain.Models.Capabilities;

/// <summary>
/// Zabilježba da je jedan template compatibility-extra raw grant (vidi DefaultRoleTemplateGrant) stvarno
/// primijenjen na ovu tenant GrantGroup — provenance-only, NIKAD izvor runtime ovlasti (to je i dalje isključivo
/// GrantGroupGrant). Namjerno odvojeno od GrantGroupCapabilitySnapshot (vidi FAZA 1 Part G/H) — ovo su predložak-
/// vlasnički compatibility grantovi, ne capability-scope odabiri.
/// </summary>
[Table("grant_group_template_grants")]
public class GrantGroupTemplateGrant
{
    [Key]
    [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
    [Column("id")]
    public Guid? Id { get; set; }

    [Column("grant_group_id")]
    public Guid GrantGroupId { get; set; }

    [Column("grant_key")]
    public string GrantKey { get; set; }

    [Column("source_template_key")]
    public string SourceTemplateKey { get; set; }

    [Column("source_template_version")]
    public int SourceTemplateVersion { get; set; }

    [Column("applied_at")]
    public DateTimeOffset AppliedAt { get; set; }

    [Column("applied_by")]
    public Guid? AppliedBy { get; set; }

    public GrantGroup GrantGroup { get; set; }
}
