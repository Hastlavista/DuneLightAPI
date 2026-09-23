using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace BlueDragon.DuneLight.Infrastructure.Domain.Models.Permissions;

/// <summary>
/// FAZA 3 (v2 template-upgrade) — audit log za jednu primijenjenu template-upgrade odluku (Owner je pregledao
/// diff, razriješio eventualne konflikte i kliknuo Primijeni). Bilježi SAMO stabilne ključeve/opsege (capability
/// ključevi, enum imena) — NIKAD lokaliziranu/display tekst (vidi GrantGroupTemplateUpgradeService.Apply). Za
/// razliku od OrganizationBrandingAuditLogHandler.Add (koji otvara vlastiti DbContext), ovaj redak MORA biti
/// upisan unutar ISTE transakcije/DbContext kao snapshot/provenance/raw-grant promjene da bude atomski s njima —
/// vidi Apply koji koristi uow.Context.GrantGroupTemplateUpgradeAuditLogs.Add(...) izravno, ne handler.Add.
/// </summary>
[Table("grant_group_template_upgrade_audit_log")]
public class GrantGroupTemplateUpgradeAuditLog
{
    [Key]
    [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
    [Column("id")]
    public Guid? Id { get; set; }

    [Column("grant_group_id")]
    public Guid GrantGroupId { get; set; }

    [Column("organization_id")]
    public Guid OrganizationId { get; set; }

    [Column("source_template_key")]
    public string SourceTemplateKey { get; set; }

    [Column("source_template_version")]
    public int SourceTemplateVersion { get; set; }

    [Column("target_template_key")]
    public string TargetTemplateKey { get; set; }

    [Column("target_template_version")]
    public int TargetTemplateVersion { get; set; }

    /// <summary>JSON niz stabilnih capability-promjena (ključ, staro/novo selectedScope, izvor) — vidi
    /// GrantGroupTemplateUpgradeService.Apply za točan oblik payloada.</summary>
    [Column("capability_changes_json")]
    public string CapabilityChangesJson { get; set; }

    /// <summary>JSON mapa capabilityKey -> ConflictResolution enum-ime za sve razriješene konflikte primijenjene
    /// ovim upgrade-om.</summary>
    [Column("conflict_resolutions_json")]
    public string ConflictResolutionsJson { get; set; }

    [Column("applied_at")]
    public DateTimeOffset AppliedAt { get; set; }

    [Column("applied_by")]
    public Guid? AppliedBy { get; set; }
}
