using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace BlueDragon.DuneLight.Infrastructure.Domain.Models.Permissions;

/// <summary>Poslovna oznaka (npr. "Trener", "Voditelj") — samo naziv za prikaz/filtriranje. NE utječe na autorizaciju, vidi GrantGroup.</summary>
[Table("roles")]
public class Role
{
    [Key]
    [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
    [Column("id")]
    public Guid? Id { get; set; }

    [Column("organization_id")]
    public Guid OrganizationId { get; set; }

    [Column("name")]
    public string Name { get; set; }

    [Column("created_at")]
    public DateTimeOffset? CreatedAt { get; set; }

    [Column("created_by")]
    public Guid? CreatedBy { get; set; }
}