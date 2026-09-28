namespace BlueDragon.DuneLight.Infrastructure.Domain.Settings;

/// <summary>
/// Dev/production bootstrap configuration for the FIRST PlatformAccount — see PlatformAccountBootstrapper.
/// BootstrapPassword is a genuine secret and must never be committed to appsettings.json (gitignored
/// appsettings.Development.json locally, PlatformSettings__BootstrapPassword env var in real deployments —
/// same convention DatabaseConfiguration's Production connection string already uses). Blank/omitted
/// email-or-password = no seed, identical to this feature not existing.
/// </summary>
public class PlatformSettings
{
    public string BootstrapEmail { get; set; }
    public string BootstrapPassword { get; set; }
}
