using System;

namespace BlueDragon.DuneLight.Core.DTOs.Auth;

public class AuthResponse
{
    public Guid? UserId { get; set; }
    public string Email { get; set; }
    public string ApiKey { get; set; }
    public string Role { get; set; }
    public Guid? OrganizationId { get; set; }
    public string OrganizationName { get; set; }
    public string OrganizationSlug { get; set; }
    public string Token { get; set; }
    public DateTime? TokenExpiration { get; set; }
}
