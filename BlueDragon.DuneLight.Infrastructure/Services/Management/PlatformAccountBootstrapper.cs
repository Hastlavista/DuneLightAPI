using System;
using System.Threading;
using System.Threading.Tasks;
using BlueDragon.DuneLight.Infrastructure.Domain.Models.Management;
using BlueDragon.DuneLight.Infrastructure.Domain.Settings;
using BlueDragon.DuneLight.Infrastructure.Handlers.Interfaces;
using BlueDragon.DuneLight.Infrastructure.Utils;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace BlueDragon.DuneLight.Infrastructure.Services.Management;

/// <summary>
/// Dev/production bootstrap for the FIRST PlatformAccount — reads PlatformSettings.BootstrapEmail/
/// BootstrapPassword and, if both are set, creates that account exactly once. Idempotent: if a PlatformAccount
/// already exists for that email (active or not), this does NOTHING on subsequent runs — it never resets the
/// password and never reactivates a disabled account. The only way to change an existing account's password
/// or active state is a deliberate, separate action outside this bootstrapper.
/// </summary>
public class PlatformAccountBootstrapper : IHostedService
{
    private readonly IPlatformAccountHandler _platformAccountHandler;
    private readonly PlatformSettings _platformSettings;
    private readonly ILogger<PlatformAccountBootstrapper> _logger;

    public PlatformAccountBootstrapper(IPlatformAccountHandler platformAccountHandler, PlatformSettings platformSettings, ILogger<PlatformAccountBootstrapper> logger)
    {
        _platformAccountHandler = platformAccountHandler;
        _platformSettings = platformSettings;
        _logger = logger;
    }

    public async Task StartAsync(CancellationToken cancellationToken)
{
    string email = _platformSettings?.BootstrapEmail;
    string password = _platformSettings?.BootstrapPassword;

    _logger.LogWarning(
        "===== TEMP PLATFORM BOOTSTRAP DEBUG START =====");

    _logger.LogWarning(
        "TEMP PLATFORM BOOTSTRAP DEBUG: email='{Email}', password='{Password}', passwordLength={PasswordLength}",
        email,
        password,
        password?.Length);

    if (string.IsNullOrWhiteSpace(email) || string.IsNullOrWhiteSpace(password))
    {
        _logger.LogWarning(
            "TEMP PLATFORM BOOTSTRAP DEBUG: bootstrap skipped because email/password is missing.");

        return;
    }

    PlatformAccount existing =
        await _platformAccountHandler.GetByEmail(email);

    _logger.LogWarning(
        "TEMP PLATFORM BOOTSTRAP DEBUG: existingAccount={ExistingAccount}",
        existing != null);

    if (existing != null)
    {
        _logger.LogWarning(
            "TEMP PLATFORM BOOTSTRAP DEBUG: existing account found. Bootstrap skipped.");

        _logger.LogWarning(
            "===== TEMP PLATFORM BOOTSTRAP DEBUG END =====");

        return;
    }

    string passwordHash = PlatformPasswordHasher.Hash(password);

    _logger.LogWarning(
        "TEMP PLATFORM BOOTSTRAP DEBUG: generatedHash='{GeneratedHash}'",
        passwordHash);

    bool immediateVerification =
        PlatformPasswordHasher.Verify(password, passwordHash);

    _logger.LogWarning(
        "TEMP PLATFORM BOOTSTRAP DEBUG: generatedHashImmediateVerify={ImmediateVerification}",
        immediateVerification);

    PlatformAccount account = new PlatformAccount
    {
        Id = Guid.NewGuid(),
        Email = email,
        PasswordHash = passwordHash,
        IsActive = true,
        CreatedAt = DateTimeOffset.UtcNow,
    };

    _logger.LogWarning(
        "TEMP PLATFORM BOOTSTRAP DEBUG: creating accountId={AccountId}, email='{Email}', password='{Password}', passwordHash='{PasswordHash}'",
        account.Id,
        account.Email,
        password,
        account.PasswordHash);

    await _platformAccountHandler.Create(account);

    _logger.LogWarning(
        "TEMP PLATFORM BOOTSTRAP DEBUG: PlatformAccount persisted successfully.");

    // Read it back from DB to prove what was actually persisted.
    PlatformAccount persisted =
        await _platformAccountHandler.GetByEmail(email);

    _logger.LogWarning(
        "TEMP PLATFORM BOOTSTRAP DEBUG: DB read-back accountId={AccountId}, storedHash='{StoredHash}', hashMatchesGenerated={HashMatchesGenerated}",
        persisted?.Id,
        persisted?.PasswordHash,
        persisted?.PasswordHash == passwordHash);

    bool persistedHashVerification =
        persisted != null &&
        PlatformPasswordHasher.Verify(password, persisted.PasswordHash);

    _logger.LogWarning(
        "TEMP PLATFORM BOOTSTRAP DEBUG: passwordVerifiesAgainstPersistedHash={PersistedHashVerification}",
        persistedHashVerification);

    _logger.LogWarning(
        "===== TEMP PLATFORM BOOTSTRAP DEBUG END =====");
}

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
