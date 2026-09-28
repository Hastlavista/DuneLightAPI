using System;
using System.Threading.Tasks;
using BlueDragon.DuneLight.Core.DTOs.Management;
using BlueDragon.DuneLight.Core.Interfaces.Management;
using BlueDragon.DuneLight.Core.Shared;
using BlueDragon.DuneLight.Core.Shared.Exceptions;
using BlueDragon.DuneLight.Infrastructure.Domain.Models.Management;
using BlueDragon.DuneLight.Infrastructure.Handlers.Interfaces;
using BlueDragon.DuneLight.Infrastructure.Utils;

namespace BlueDragon.DuneLight.Infrastructure.Services.Management;

public class PlatformAuthService : IPlatformAuthService
{
    private readonly IPlatformAccountHandler _platformAccountHandler;
    private readonly IPlatformJwtService _platformJwtService;

    public PlatformAuthService(IPlatformAccountHandler platformAccountHandler, IPlatformJwtService platformJwtService)
    {
        _platformAccountHandler = platformAccountHandler;
        _platformJwtService = platformJwtService;
    }

    public async Task<PlatformAuthResponse> Login(PlatformLoginRequest request)
    {
        PlatformAccount account = await _platformAccountHandler.GetByEmail(request.Email);

        // PBKDF2's salt is per-row, so unlike tenant GetUserByCredentials this can't filter by hash in the
        // query - fetch by email, verify in memory.
        if (account == null || !account.IsActive || !PlatformPasswordHasher.Verify(request.Password, account.PasswordHash))
            throw new UnauthorizedAppException(ErrorCodes.AuthInvalidCredentials, "Neispravan email ili lozinka.");

        (string token, DateTime expiration) = _platformJwtService.GenerateToken(account.Id.GetValueOrDefault(), account.Email);

        return new PlatformAuthResponse
        {
            PlatformAccountId = account.Id.GetValueOrDefault(),
            Email = account.Email,
            Token = token,
            TokenExpiration = expiration,
        };
    }
}
