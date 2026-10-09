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

        if (string.IsNullOrWhiteSpace(email) || string.IsNullOrWhiteSpace(password))
            return;

        PlatformAccount existing = await _platformAccountHandler.GetByEmail(email);
        if (existing != null)
            return;

        string passwordHash = PlatformPasswordHasher.Hash(password);

        await _platformAccountHandler.Create(new PlatformAccount
        {
            Id = Guid.NewGuid(),
            Email = email,
            PasswordHash = passwordHash,
            IsActive = true,
            CreatedAt = TimeProvider.System.GetUtcNow(),
        });

        _logger.LogInformation(
            "Bootstrapped PlatformAccount {Email}.",
            email);
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
