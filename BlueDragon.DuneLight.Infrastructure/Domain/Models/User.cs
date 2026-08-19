using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using BlueDragon.DuneLight.Core.Enums;

namespace BlueDragon.DuneLight.Infrastructure.Domain.Models;

[Table("users")]
public class User
{
    [Key]
    [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
    [Column("id")]
    public Guid? Id { get; set; }

    [Column("organization_id")]
    public Guid OrganizationId { get; set; }

    [Column("email")]
    public string Email { get; set; }

    [Column("password_hash")]
    public string PasswordHash { get; set; }

    [Column("api_key")]
    public string ApiKey { get; set; }

    [Column("role")]
    public UserRole Role { get; set; }

    [Column("is_active")]
    public bool IsActive { get; set; }

    /// <summary>Točno jedan po organizaciji — trajno zaobilazi cijeli grant sustav (vidi RequireGrantAttribute). Postavlja se pri Register-u, ne kroz UI.</summary>
    [Column("is_owner")]
    public bool IsOwner { get; set; }

    [Column("must_change_credentials_on_first_login")]
    public bool MustChangeCredentialsOnFirstLogin { get; set; }

    /// <summary>Hashirani inicijalni PIN (isti PasswordHasher kao za lozinku). Samo polje — PIN-login/quick-switch tok nije implementiran.</summary>
    [Column("pin_hash")]
    public string PinHash { get; set; }

    [Column("created_at")]
    public DateTimeOffset? CreatedAt { get; set; }
}
