using System.ComponentModel.DataAnnotations;

namespace BlueDragon.DuneLight.Core.DTOs.Management;

/// <summary>No OrganizationSlug — a PlatformAccount has no Organization concept at all (unlike tenant
/// LoginRequest).</summary>
public class PlatformLoginRequest
{
    [Required] public string Email { get; set; }
    [Required] public string Password { get; set; }
}
