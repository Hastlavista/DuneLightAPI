namespace BlueDragon.DuneLight.API.Authentication;

/// <summary>Name of the JWT bearer scheme issued to/validated for PlatformAccount identities — registered
/// separately from JwtBearerDefaults.AuthenticationScheme (tenant) in Startup.cs, with its own signing key, and
/// deliberately NOT part of the default authorization policy. Management controllers opt in explicitly via
/// [Authorize(AuthenticationSchemes = PlatformAuthenticationDefaults.Scheme)].</summary>
public static class PlatformAuthenticationDefaults
{
    public const string Scheme = "PlatformBearer";
}
