using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace BlueDragon.DuneLight.Infrastructure.Domain.Models.Catalog;

/// <summary>Usluga iz kataloga (napr. "Masaža 30 min"). Varijante trajanja su zasebni zapisi.</summary>
[Table("services")]
public class Service
{
    [Key]
    [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
    [Column("id")]
    public Guid? Id { get; set; }

    [Column("organization_id")]
    public Guid OrganizationId { get; set; }

    [Column("name")]
    public string Name { get; set; }

    [Column("service_category_id")]
    public Guid ServiceCategoryId { get; set; }

    [Column("default_duration_minutes")]
    public int DefaultDurationMinutes { get; set; }

    [Column("default_price")]
    public decimal DefaultPrice { get; set; }

    [Column("description")]
    public string Description { get; set; }

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

    public ServiceCategory ServiceCategory { get; set; }
}
