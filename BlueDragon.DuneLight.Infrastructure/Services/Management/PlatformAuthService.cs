using System;
using System.Threading.Tasks;
using BlueDragon.DuneLight.Core.DTOs.Management;
using BlueDragon.DuneLight.Core.Interfaces.Management;
using BlueDragon.DuneLight.Core.Shared;
using BlueDragon.DuneLight.Core.Shared.Exceptions;
using BlueDragon.DuneLight.Infrastructure.Domain.Models.Management;
using BlueDragon.DuneLight.Infrastructure.Handlers.Interfaces;
using BlueDragon.DuneLight.Infrastructure.Utils;
using Microsoft.Extensions.Logging;

namespace BlueDragon.DuneLight.Infrastructure.Services.Management;

public class PlatformAuthService : IPlatformAuthService
{
    private readonly IPlatformAccountHandler _platformAccountHandler;
    private readonly IPlatformJwtService _platformJwtService;
    private readonly ILogger<PlatformAuthService> _logger;

    public PlatformAuthService(IPlatformAccountHandler platformAccountHandler, IPlatformJwtService platformJwtService, ILogger<PlatformAuthService> logger)
    {
        _platformAccountHandler = platformAccountHandler;
        _platformJwtService = platformJwtService;
        _logger = logger;
    }

    public async Task<PlatformAuthResponse> Login(PlatformLoginRequest request)
    {
        // TEMPORARY diagnostic logging (see PR discussion) - traces exactly which check fails without ever
        // logging plaintext password, hash, salt, JWT, or signing key. Remove once the production
        // AUTH_INVALID_CREDENTIALS investigation is closed. Behavior (which conditions map to
        // AUTH_INVALID_CREDENTIALS, and the fact that none of them are distinguishable to the client) is
        // unchanged - this only splits the previous single combined condition into sequential checks.
        _logger.LogInformation("Platform login attempt received.");

        PlatformAccount account = await _platformAccountHandler.GetByEmail(request.Email);
        bool accountFound = account != null;
        _logger.LogInformation("Platform login: account lookup succeeded={AccountFound}", accountFound);
        if (!accountFound)
            throw new UnauthorizedAppException(ErrorCodes.AuthInvalidCredentials, "Neispravan email ili lozinka.");

        _logger.LogInformation("Platform login: account active={IsActive}", account.IsActive);
        if (!account.IsActive)
            throw new UnauthorizedAppException(ErrorCodes.AuthInvalidCredentials, "Neispravan email ili lozinka.");

        // PBKDF2's salt is per-row, so unlike tenant GetUserByCredentials this can't filter by hash in the
        // query - fetch by email, verify in memory.
        bool passwordVerified = PlatformPasswordHasher.Verify(request.Password, account.PasswordHash);
        _logger.LogInformation("Platform login: password verification succeeded={PasswordVerified}", passwordVerified);
        if (!passwordVerified)
            throw new UnauthorizedAppException(ErrorCodes.AuthInvalidCredentials, "Neispravan email ili lozinka.");

        _logger.LogInformation("Platform login: JWT generation reached.");
        (string token, DateTime expiration) = _platformJwtService.GenerateToken(account.Id.GetValueOrDefault(), account.Email);
        _logger.LogInformation("Platform login: JWT generation succeeded.");

        return new PlatformAuthResponse
        {
            PlatformAccountId = account.Id.GetValueOrDefault(),
            Email = account.Email,
            Token = token,
            TokenExpiration = expiration,
        };
    }
}
