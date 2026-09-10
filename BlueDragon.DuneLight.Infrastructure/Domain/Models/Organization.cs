using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace BlueDragon.DuneLight.Infrastructure.Domain.Models;

[Table("organizations")]
public class Organization
{
    [Key]
    [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
    [Column("id")]
    public Guid? Id { get; set; }

    [Column("name")]
    public string Name { get; set; }

    [Column("slug")]
    public string Slug { get; set; }

    /// <summary>Data-URI ili URL logotipa (sidebar + login screen).</summary>
    [Column("logo")]
    public string Logo { get; set; }

    /// <summary>Data-URI ili URL favicona.</summary>
    [Column("favicon")]
    public string Favicon { get; set; }

    /// <summary>Primarna boja — gumbi, linkovi, sidebar accent. HEX format (npr. #1A73E8).</summary>
    [Column("primary_color")]
    public string PrimaryColor { get; set; }

    /// <summary>Sekundarna boja — hover, focus. HEX format (npr. #185ABC).</summary>
    [Column("secondary_color")]
    public string SecondaryColor { get; set; }

    /// <summary>Boja površine (kartice/paneli). HEX format (npr. #F5F5F5). Nullable — nije obavezna.</summary>
    [Column("surface_color")]
    public string SurfaceColor { get; set; }

    [Column("created_at")]
    public DateTimeOffset? CreatedAt { get; set; }
}
