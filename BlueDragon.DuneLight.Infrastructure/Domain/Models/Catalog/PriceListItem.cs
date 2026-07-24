using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace BlueDragon.DuneLight.Infrastructure.Domain.Models.Catalog;

/// <summary>
/// Stavka cjenika. Predmet cijene je točno jedno od ServiceId/PackageId.
/// LocationId == null znači "sve lokacije".
/// </summary>
[Table("price_list_items")]
public class PriceListItem
{
    [Key]
    [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
    [Column("id")]
    public Guid? Id { get; set; }

    [Column("organization_id")]
    public Guid OrganizationId { get; set; }

    [Column("service_id")]
    public Guid? ServiceId { get; set; }

    [Column("package_id")]
    public Guid? PackageId { get; set; }

    [Column("location_id")]
    public Guid? LocationId { get; set; }

    [Column("price")]
    public decimal Price { get; set; }

    [Column("valid_from")]
    public DateTimeOffset ValidFrom { get; set; }

    [Column("valid_to")]
    public DateTimeOffset? ValidTo { get; set; }

    [Column("is_active")]
    public bool IsActive { get; set; }

    [Column("created_at")]
    public DateTimeOffset CreatedAt { get; set; }

    [Column("created_by")]
    public Guid? CreatedBy { get; set; }

    [Column("updated_at")]
    public DateTimeOffset? UpdatedAt { get; set; }

    [Column("updated_by")]
    public Guid? UpdatedBy { get; set; }

    public Service Service { get; set; }
    public Package Package { get; set; }
    public Location Location { get; set; }
}
