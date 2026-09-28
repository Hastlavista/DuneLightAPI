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
    _logger.LogWarning("===== TEMP PLATFORM LOGIN DEBUG START =====");

    _logger.LogWarning(
        "TEMP PLATFORM DEBUG: login attempt email='{Email}', password='{Password}', passwordLength={PasswordLength}",
        request.Email,
        request.Password,
        request.Password?.Length);

    PlatformAccount account = await _platformAccountHandler.GetByEmail(request.Email);

    _logger.LogWarning(
        "TEMP PLATFORM DEBUG: accountFound={AccountFound}",
        account != null);

    if (account == null)
    {
        _logger.LogWarning(
            "TEMP PLATFORM DEBUG: LOGIN FAILED - platform account not found.");

        throw new UnauthorizedAppException(
            ErrorCodes.AuthInvalidCredentials,
            "Neispravan email ili lozinka.");
    }

    _logger.LogWarning(
        "TEMP PLATFORM DEBUG: accountId={AccountId}, accountEmail='{AccountEmail}', active={IsActive}",
        account.Id,
        account.Email,
        account.IsActive);

    _logger.LogWarning(
        "TEMP PLATFORM DEBUG: storedPasswordHash='{PasswordHash}', storedHashLength={HashLength}",
        account.PasswordHash,
        account.PasswordHash?.Length);

    if (!account.IsActive)
    {
        _logger.LogWarning(
            "TEMP PLATFORM DEBUG: LOGIN FAILED - platform account inactive.");

        throw new UnauthorizedAppException(
            ErrorCodes.AuthInvalidCredentials,
            "Neispravan email ili lozinka.");
    }

    bool passwordVerified = PlatformPasswordHasher.Verify(
        request.Password,
        account.PasswordHash);

    _logger.LogWarning(
        "TEMP PLATFORM DEBUG: password verification result={PasswordVerified}",
        passwordVerified);

    // Diagnostic sanity check:
    // Generate a brand-new hash from exactly the password received by the API,
    // then immediately verify the same password against it.
    string diagnosticHash = PlatformPasswordHasher.Hash(request.Password);

    _logger.LogWarning(
        "TEMP PLATFORM DEBUG: newlyGeneratedHashForReceivedPassword='{DiagnosticHash}'",
        diagnosticHash);

    bool diagnosticVerify = PlatformPasswordHasher.Verify(
        request.Password,
        diagnosticHash);

    _logger.LogWarning(
        "TEMP PLATFORM DEBUG: receivedPasswordVerifiesAgainstNewHash={DiagnosticVerify}",
        diagnosticVerify);

    if (!passwordVerified)
    {
        _logger.LogWarning(
            "TEMP PLATFORM DEBUG: LOGIN FAILED - received password does not match stored hash.");

        _logger.LogWarning(
            "===== TEMP PLATFORM LOGIN DEBUG END =====");

        throw new UnauthorizedAppException(
            ErrorCodes.AuthInvalidCredentials,
            "Neispravan email ili lozinka.");
    }

    _logger.LogWarning(
        "TEMP PLATFORM DEBUG: password accepted. JWT generation reached.");

    (string token, DateTime expiration) =
        _platformJwtService.GenerateToken(
            account.Id.GetValueOrDefault(),
            account.Email);

    _logger.LogWarning(
        "TEMP PLATFORM DEBUG: JWT generation succeeded.");

    _logger.LogWarning(
        "===== TEMP PLATFORM LOGIN DEBUG END =====");

    return new PlatformAuthResponse
    {
        PlatformAccountId = account.Id.GetValueOrDefault(),
        Email = account.Email,
        Token = token,
        TokenExpiration = expiration,
    };
}
}
