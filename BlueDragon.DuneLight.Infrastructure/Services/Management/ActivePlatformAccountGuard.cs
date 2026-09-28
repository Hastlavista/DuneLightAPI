using System;
using System.Threading.Tasks;
using BlueDragon.DuneLight.Infrastructure.Handlers.Interfaces;

namespace BlueDragon.DuneLight.Infrastructure.Services.Management;

/// <summary>
/// PlatformAccount equivalent of IActiveUserGuard — an already-issued PlatformBearer token for an account
/// deactivated since it was signed must stop working immediately. Wired into the "PlatformBearer" JWT scheme's
/// OnTokenValidated event (see Startup.cs), mirroring the tenant scheme's wiring exactly. Deliberately queries
/// ONLY platform_accounts — never touches the tenant Users table.
/// </summary>
public interface IActivePlatformAccountGuard
{
    Task<bool> IsActive(Guid platformAccountId);
}

public class ActivePlatformAccountGuard : IActivePlatformAccountGuard
{
    private readonly IPlatformAccountHandler _platformAccountHandler;

    public ActivePlatformAccountGuard(IPlatformAccountHandler platformAccountHandler)
    {
        _platformAccountHandler = platformAccountHandler;
    }

    public Task<bool> IsActive(Guid platformAccountId)
    {
        return _platformAccountHandler.IsActive(platformAccountId);
    }
}
