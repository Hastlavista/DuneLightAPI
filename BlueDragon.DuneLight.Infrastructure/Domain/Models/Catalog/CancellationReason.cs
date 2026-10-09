using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace BlueDragon.DuneLight.Infrastructure.Domain.Models.Catalog;

/// <summary>
/// K1-4 (12.3) — šifra razloga otkazivanja/izostanka organizacije. Ne briše se (sudjelovanja je referenciraju i čuvaju naziv iz
/// trenutka događaja); deaktivacija je skriva iz odabira. Aktivni naziv je jedinstven u organizaciji. Bez utjecaja na politiku.
/// </summary>
[Table("cancellation_reasons")]
public class CancellationReason
{
    [Key]
    [Column("id")]
    public Guid Id { get; set; }

    [Column("organization_id")]
    public Guid OrganizationId { get; set; }

    [Column("name")]
    public string Name { get; set; }

    [Column("is_active")]
    public bool IsActive { get; set; }

    [Column("sort_order")]
    public int SortOrder { get; set; }

    [Column("applies_to_client_cancellation")]
    public bool AppliesToClientCancellation { get; set; }

    [Column("applies_to_business_cancellation")]
    public bool AppliesToBusinessCancellation { get; set; }

    [Column("applies_to_no_show")]
    public bool AppliesToNoShow { get; set; }

    [Column("created_at")]
    public DateTimeOffset CreatedAt { get; set; }

    [Column("created_by")]
    public Guid? CreatedBy { get; set; }

    [Column("updated_at")]
    public DateTimeOffset? UpdatedAt { get; set; }

    [Column("updated_by")]
    public Guid? UpdatedBy { get; set; }
}
