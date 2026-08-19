using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace BlueDragon.DuneLight.Infrastructure.Domain.Models.Permissions;

/// <summary>Korisnik &lt;-&gt; GrantGroup, više-na-više. Efektivne dozvole korisnika = unija grantova svih njegovih grupa.</summary>
[Table("user_grant_groups")]
public class UserGrantGroup
{
    [Key]
    [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
    [Column("id")]
    public Guid? Id { get; set; }

    [Column("user_id")]
    public Guid UserId { get; set; }

    [Column("grant_group_id")]
    public Guid GrantGroupId { get; set; }

    public User User { get; set; }
    public GrantGroup GrantGroup { get; set; }
}