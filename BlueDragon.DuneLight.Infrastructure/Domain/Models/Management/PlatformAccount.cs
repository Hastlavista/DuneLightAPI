using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace BlueDragon.DuneLight.Infrastructure.Domain.Models.Management;

/// <summary>
/// A DuneLight platform operator's identity — structurally independent of tenant <c>User</c>/<c>Organization</c>.
/// No FK to either. A platform operator is NOT a tenant User and has no OrganizationId, UserGrantGroups,
/// Employee, or CompanyContext. If a customer Organization wants to give a platform operator tenant-app access
/// for support purposes, that Organization creates an ordinary tenant User for them — email equality between a
/// PlatformAccount and a User never implies any linkage or authorization (see PlatformAuthService).
/// </summary>
[Table("platform_accounts")]
public class PlatformAccount
{
    [Key]
    [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
    [Column("id")]
    public Guid? Id { get; set; }

    [Column("email")]
    public string Email { get; set; }

    [Column("password_hash")]
    public string PasswordHash { get; set; }

    [Column("is_active")]
    public bool IsActive { get; set; }

    [Column("created_at")]
    public DateTimeOffset CreatedAt { get; set; }
}
