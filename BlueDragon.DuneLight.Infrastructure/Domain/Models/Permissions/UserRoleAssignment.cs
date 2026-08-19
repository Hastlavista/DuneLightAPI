using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace BlueDragon.DuneLight.Infrastructure.Domain.Models.Permissions;

/// <summary>Korisnik &lt;-&gt; Role (poslovna oznaka), više-na-više. Namjerno drugo ime od starog UserRole enuma
/// (koji se uklanja) da ne bude zabune između "role kao ovlast" (staro) i "role kao oznaka" (novo).</summary>
[Table("user_role_assignments")]
public class UserRoleAssignment
{
    [Key]
    [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
    [Column("id")]
    public Guid? Id { get; set; }

    [Column("user_id")]
    public Guid UserId { get; set; }

    [Column("role_id")]
    public Guid RoleId { get; set; }

    public User User { get; set; }
    public Role Role { get; set; }
}