using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using BlueDragon.DuneLight.Infrastructure.Domain.Settings;
using BlueDragon.DuneLight.Infrastructure.Handlers.Interfaces;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace BlueDragon.DuneLight.Infrastructure.Services;

/// <summary>
/// P2 (faza 2C) — periodički pokreće obnovu članarina za sve organizacije (IMembershipRenewalService.RunForOrganization; "danas"
/// u zoni organizacije, Q22). Obnova je idempotentna (unique članstvo + početak perioda), pa ponovljeni ili zakašnjeli prolaz
/// samo sustiže propušteno. Greška jedne organizacije ne zaustavlja ostale.
/// </summary>
public class MembershipRenewalBackgroundService : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IClientMembershipHandler _handler;
    private readonly MembershipRenewalSettings _settings;
    private readonly ILogger<MembershipRenewalBackgroundService> _logger;

    public MembershipRenewalBackgroundService(
        IServiceScopeFactory scopeFactory,
        IClientMembershipHandler handler,
        MembershipRenewalSettings settings,
        ILogger<MembershipRenewalBackgroundService> logger)
    {
        _scopeFactory = scopeFactory;
        _handler = handler;
        _settings = settings;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!_settings.Enabled)
            return;

        TimeSpan interval = TimeSpan.FromMinutes(Math.Max(_settings.IntervalMinutes, 1));
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                List<Guid> organizations = await _handler.GetOrganizationsWithActiveMemberships();
                foreach (Guid organizationId in organizations)
                {
                    stoppingToken.ThrowIfCancellationRequested();
                    try
                    {
                        using IServiceScope scope = _scopeFactory.CreateScope();
                        IMembershipRenewalService renewal = scope.ServiceProvider.GetRequiredService<IMembershipRenewalService>();
                        await renewal.RunForOrganization(organizationId);
                    }
                    catch (Exception ex) when (ex is not OperationCanceledException)
                    {
                        _logger.LogError(ex, "Obnova članarina organizacije {OrganizationId} nije uspjela.", organizationId);
                    }
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Prolaz obnove članarina nije uspio.");
            }

            try
            {
                await Task.Delay(interval, stoppingToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }
    }
}
