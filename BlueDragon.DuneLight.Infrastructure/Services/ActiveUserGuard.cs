using System;
using System.Threading.Tasks;
using BlueDragon.DuneLight.Infrastructure.Domain.Models;
using BlueDragon.DuneLight.Infrastructure.Handlers.Interfaces;

namespace BlueDragon.DuneLight.Infrastructure.Services;

/// <summary>
/// The single decision of whether an authenticated User's session may still be used - deliberately
/// independent of legacy UserRole and of GrantGroup/permissions.manage: deactivation (User.IsActive=false)
/// must reject the session regardless of what role or grants the account otherwise has. Used from the JWT
/// bearer's OnTokenValidated event (see Startup.cs) so every authenticated request - not just
/// [RequireGrant]-protected ones - is covered centrally, mirroring ApiKeyAuthenticationHandler's own
/// IsActive check for the API-key scheme.
/// </summary>
public interface IActiveUserGuard
{
    Task<bool> IsUserActive(Guid userId);
}

public class ActiveUserGuard : IActiveUserGuard
{
    private readonly IAuthHandler _authHandler;

    public ActiveUserGuard(IAuthHandler authHandler)
    {
        _authHandler = authHandler;
    }

    public async Task<bool> IsUserActive(Guid userId)
    {
        User user = await _authHandler.GetUserById(userId);
        return user != null && user.IsActive;
    }
}
