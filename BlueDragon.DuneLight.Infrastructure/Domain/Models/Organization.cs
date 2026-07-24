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

    [Column("created_at")]
    public DateTimeOffset? CreatedAt { get; set; }
}
