using System;

namespace BlueDragon.DuneLight.Core.DTOs.Auth;

public class AuthResponse
{
    public Guid? UserId { get; set; }
    public string Email { get; set; }
    public string ApiKey { get; set; }
    public Guid? OrganizationId { get; set; }
    public string OrganizationName { get; set; }
    public string OrganizationSlug { get; set; }
    public string Token { get; set; }
    public DateTimeOffset? TokenExpiration { get; set; }
}
