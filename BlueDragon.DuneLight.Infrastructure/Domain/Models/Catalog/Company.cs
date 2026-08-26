using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace BlueDragon.DuneLight.Infrastructure.Domain.Models.Catalog;

[Table("companies")]
public class Company
{
    [Key]
    [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
    [Column("id")]
    public Guid? Id { get; set; }

    [Column("organization_id")]
    public Guid OrganizationId { get; set; }

    [Column("name")]
    public string Name { get; set; }

    [Column("address")]
    public string Address { get; set; }

    [Column("phone")]
    public string Phone { get; set; }

    [Column("color_hex")]
    public string ColorHex { get; set; }

    /// <summary>ISO 3166-1 alpha-2 kod (npr. "HR") — određuje koji katalog fiksnih praznika CompanyHolidayService.Generate koristi.</summary>
    [Column("country")]
    public string Country { get; set; }

    [Column("is_active")]
    public bool IsActive { get; set; }

    [Column("note")]
    public string Note { get; set; }

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
