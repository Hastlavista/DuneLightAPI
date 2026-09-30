using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace BlueDragon.DuneLight.Infrastructure.Domain.Models.Catalog;

/// <summary>
/// Resurs poslovnice — konačan, višekratno upotrebljiv kapacitet (npr. masažni stolovi, reformeri, bicikli, mjesta u
/// sauni). Generički model bez tipa resursa. Pripada točno jednoj Company (CompanyId se nakon kreiranja ne mijenja) i
/// njezinoj Organization; nema vlastitu vremensku zonu — buduće zakazivanje nasljeđuje efektivnu zonu poslovnice.
/// Za sada samo katalog: zakazivanje ga još ne koristi (rezervacije količina dolaze s segmentima termina).
/// </summary>
[Table("resources")]
public class Resource
{
    [Key]
    [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
    [Column("id")]
    public Guid? Id { get; set; }

    [Column("organization_id")]
    public Guid OrganizationId { get; set; }

    [Column("company_id")]
    public Guid CompanyId { get; set; }

    [Column("name")]
    public string Name { get; set; }

    /// <summary>Broj raspoloživih jedinica resursa (≥ 1).</summary>
    [Column("capacity")]
    public int Capacity { get; set; }

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

    public Company Company { get; set; }
}
