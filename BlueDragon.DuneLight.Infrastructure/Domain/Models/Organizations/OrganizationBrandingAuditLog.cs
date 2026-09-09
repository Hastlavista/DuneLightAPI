using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace BlueDragon.DuneLight.Infrastructure.Domain.Models.Organizations;

/// <summary>
/// Audit log za promjene brandinga organizacije (boje, logo, favicon). Bilježi tko, kada
/// i koja promjena je napravljena.
/// </summary>
[Table("organization_branding_audit_log")]
public class OrganizationBrandingAuditLog
{
    [Key]
    [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
    [Column("id")]
    public Guid? Id { get; set; }

    [Column("organization_id")]
    public Guid OrganizationId { get; set; }

    /// <summary>"ColorsUpdated", "ColorsReset", "LogoUploaded", "LogoDeleted", "FaviconUploaded", "FaviconDeleted".</summary>
    [Column("change_type")]
    public string ChangeType { get; set; }

    [Column("old_value")]
    public string OldValue { get; set; }

    [Column("new_value")]
    public string NewValue { get; set; }

    [Column("changed_at")]
    public DateTimeOffset ChangedAt { get; set; }

    [Column("changed_by")]
    public Guid? ChangedBy { get; set; }
}
