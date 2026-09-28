using System;

namespace BlueDragon.DuneLight.Core.DTOs.Management;

/// <summary>No ApiKey/OrganizationId/OrganizationName/OrganizationSlug — a PlatformAccount is organization-
/// independent (unlike tenant AuthResponse). No Role either: no platform RBAC in Phase 1.</summary>
public class PlatformAuthResponse
{
    public Guid PlatformAccountId { get; set; }
    public string Email { get; set; }
    public string Token { get; set; }
    public DateTime TokenExpiration { get; set; }
}
