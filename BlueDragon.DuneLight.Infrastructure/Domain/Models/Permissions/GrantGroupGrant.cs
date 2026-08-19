using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace BlueDragon.DuneLight.Infrastructure.Domain.Models.Permissions;

/// <summary>Jedan grant-ključ dodijeljen GrantGroup-i. Ključ mora postojati u Grants katalogu (validira se u servisu, ne ovdje).</summary>
[Table("grant_group_grants")]
public class GrantGroupGrant
{
    [Key]
    [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
    [Column("id")]
    public Guid? Id { get; set; }

    [Column("grant_group_id")]
    public Guid GrantGroupId { get; set; }

    [Column("grant_key")]
    public string GrantKey { get; set; }

    public GrantGroup GrantGroup { get; set; }
}