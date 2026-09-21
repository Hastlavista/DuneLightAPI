using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace BlueDragon.DuneLight.Infrastructure.Domain.Models.Capabilities;

/// <summary>
/// Jedna verzija jednog default role predloška (admin/trener/recepcija). Key je imutabilan; Key+Version je
/// identitet. Jednom kad je verzija primijenjena/referencirana (DefaultRoleTemplateCapability ili
/// GrantGroupCapabilitySnapshot.SourceTemplateVersion), njen odabir capability-ja i compatibility-extra grantovi
/// se ne smiju mijenjati in-place — nova promjena ide kao nova Version (vidi CapabilityVersionGuard).
/// </summary>
[Table("default_role_templates")]
public class DefaultRoleTemplate
{
    [Key]
    [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
    [Column("id")]
    public Guid? Id { get; set; }

    [Column("key")]
    public string Key { get; set; }

    [Column("version")]
    public int Version { get; set; }

    [Column("display_name_hr")]
    public string DisplayNameHr { get; set; }

    [Column("is_active")]
    public bool IsActive { get; set; }

    [Column("created_at")]
    public DateTimeOffset CreatedAt { get; set; }

    [Column("created_by")]
    public Guid? CreatedBy { get; set; }

    public List<DefaultRoleTemplateCapability> Capabilities { get; set; } = new();
    public List<DefaultRoleTemplateGrant> CompatibilityGrants { get; set; } = new();
}
