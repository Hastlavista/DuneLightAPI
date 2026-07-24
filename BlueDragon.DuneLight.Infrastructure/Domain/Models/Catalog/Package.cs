using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using BlueDragon.DuneLight.Core.Enums;

namespace BlueDragon.DuneLight.Infrastructure.Domain.Models.Catalog;

[Table("packages")]
public class Package
{
    [Key]
    [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
    [Column("id")]
    public Guid? Id { get; set; }

    [Column("organization_id")]
    public Guid OrganizationId { get; set; }

    [Column("name")]
    public string Name { get; set; }

    [Column("description")]
    public string Description { get; set; }

    [Column("entry_mode")]
    public PackageEntryMode EntryMode { get; set; }

    /// <summary>Ukupan broj ulazaka kod SharedPool. Null = neograničeno.</summary>
    [Column("total_entry_count")]
    public int? TotalEntryCount { get; set; }

    [Column("validity_type")]
    public PackageValidityType ValidityType { get; set; }

    /// <summary>Obavezno kad je ValidityType = DayCount.</summary>
    [Column("validity_days")]
    public int? ValidityDays { get; set; }

    /// <summary>Obavezno kad je ValidityType = FixedDate.</summary>
    [Column("validity_fixed_date")]
    public DateTimeOffset? ValidityFixedDate { get; set; }

    [Column("default_price")]
    public decimal DefaultPrice { get; set; }

    [Column("is_active")]
    public bool IsActive { get; set; }

    [Column("sort_order")]
    public int SortOrder { get; set; }

    [Column("created_at")]
    public DateTimeOffset CreatedAt { get; set; }

    [Column("created_by")]
    public Guid? CreatedBy { get; set; }

    [Column("updated_at")]
    public DateTimeOffset? UpdatedAt { get; set; }

    [Column("updated_by")]
    public Guid? UpdatedBy { get; set; }

    public List<PackageServiceItem> Services { get; set; } = new();
}
