using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using BlueDragon.DuneLight.Core.Enums;

namespace BlueDragon.DuneLight.Infrastructure.Domain.Models.Catalog;

/// <summary>
/// Konfigurabilna kategorija usluge (npr. Bowen, Pilates, Sauna, EMS) — admin je uređuje
/// bez izmjene koda. Jedino programski relevantno svojstvo je ExecutionMode.
/// </summary>
[Table("service_categories")]
public class ServiceCategory
{
    [Key]
    [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
    [Column("id")]
    public Guid? Id { get; set; }

    [Column("organization_id")]
    public Guid OrganizationId { get; set; }

    [Column("name")]
    public string Name { get; set; }

    [Column("execution_mode")]
    public ServiceExecutionMode ExecutionMode { get; set; }

    [Column("color_hex")]
    public string ColorHex { get; set; }

    [Column("requires_client")]
    public bool RequiresClient { get; set; }

    [Column("counts_toward_revenue")]
    public bool CountsTowardRevenue { get; set; }

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
}
