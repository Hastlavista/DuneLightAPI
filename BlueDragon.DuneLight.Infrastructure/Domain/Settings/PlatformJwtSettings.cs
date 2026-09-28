namespace BlueDragon.DuneLight.Infrastructure.Domain.Settings;

/// <summary>
/// Signing configuration for the "PlatformBearer" JWT scheme — deliberately its OWN SecretKey/Issuer/Audience,
/// distinct from JwtSettings (tenant). A tenant-signed token must be cryptographically incapable of passing
/// PlatformBearer's TokenValidationParameters, not merely rejected by an ID lookup — see Startup.cs's second
/// AddJwtBearer registration.
/// </summary>
public class PlatformJwtSettings
{
    public string SecretKey { get; set; }
    public string Issuer { get; set; }
    public string Audience { get; set; }
    public int ExpirationHours { get; set; }
}
