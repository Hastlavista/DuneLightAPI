using System;
using System.Threading.Tasks;
using BlueDragon.DuneLight.Core.Enums;
using BlueDragon.DuneLight.Core.DTOs.Organization;
using BlueDragon.DuneLight.Core.Interfaces.Organization;
using BlueDragon.DuneLight.Core.Shared;
using BlueDragon.DuneLight.Core.Shared.Exceptions;
using BlueDragon.DuneLight.Infrastructure.Domain.Models.Organizations;
using BlueDragon.DuneLight.Infrastructure.Handlers.Interfaces;
using Microsoft.EntityFrameworkCore;

namespace BlueDragon.DuneLight.Infrastructure.Services;

/// <summary>
/// Poslovne postavke organizacije — trenutno samo CancellationCutoffMinutes (vidi OrganizationSettings.cs).
/// Redak je opcionalan po Organization: nepostojeći redak znači "koristi platformski default", ne "nije
/// podešeno = 0" — postojeće organizacije rade bez ikakve ručne migracije podataka.
/// </summary>
public class OrganizationSettingsService : IOrganizationSettingsService
{
    /// <summary>1440 minuta = 24h. Primjenjuje se samo dok organizacija nema eksplicitan redak — vidi klasnu napomenu.</summary>
    public const int DefaultCancellationCutoffMinutes = 1440;

    private readonly IOrganizationSettingsHandler _handler;

    public OrganizationSettingsService(IOrganizationSettingsHandler handler)
    {
        _handler = handler;
    }

    public async Task<int> GetCancellationCutoffMinutes(Guid organizationId)
    {
        OrganizationSettings settings = await _handler.GetByOrganizationId(organizationId);
        return settings?.CancellationCutoffMinutes ?? DefaultCancellationCutoffMinutes;
    }

    /// <summary>Phase D3B3A — jedino trenutno podržano ponašanje (vidi PackageConsumptionTiming).</summary>
    public const PackageConsumptionTiming DefaultPackageConsumptionTiming = PackageConsumptionTiming.OnCompletion;

    public async Task<PackageConsumptionTiming> GetPackageConsumptionTiming(Guid organizationId)
    {
        OrganizationSettings settings = await _handler.GetByOrganizationId(organizationId);
        return settings?.PackageConsumptionTiming ?? DefaultPackageConsumptionTiming;
    }

    public async Task<OrganizationSettingsDto> GetSettings(Guid organizationId)
    {
        return new OrganizationSettingsDto
        {
            CancellationCutoffMinutes = await GetCancellationCutoffMinutes(organizationId),
            PackageConsumptionTiming = await GetPackageConsumptionTiming(organizationId),
            TimeZone = await _handler.GetTimeZone(organizationId) ?? OrganizationTimeZones.Default
        };
    }

    public async Task<OrganizationSettingsDto> UpdateCancellationCutoff(Guid organizationId, Guid userId, OrganizationSettingsUpdateRequest request)
    {
        DateTimeOffset now = DateTimeOffset.UtcNow;
        OrganizationSettings existing = await _handler.GetByOrganizationId(organizationId);

        if (existing == null)
        {
            OrganizationSettings created = new OrganizationSettings
            {
                Id = Guid.NewGuid(),
                OrganizationId = organizationId,
                CancellationCutoffMinutes = request.CancellationCutoffMinutes,
                CreatedAt = now,
                CreatedBy = userId
            };

            try
            {
                await _handler.Add(created);
                return await GetSettings(organizationId);
            }
            catch (DbUpdateException)
            {
                // Rijedak race: dva istovremena PUT-a bez postojećeg retka — drugi upit gubi unique constraint
                // na organization_id. Umjesto propagiranja sirove DB greške, ponašamo se kao da smo od početka
                // pogodili redak koji je pobjednik upravo umetnuo (isti tenant, isti krajnji cilj — postavi cutoff).
                existing = await _handler.GetByOrganizationId(organizationId);
            }
        }

        existing.CancellationCutoffMinutes = request.CancellationCutoffMinutes;
        existing.UpdatedAt = now;
        existing.UpdatedBy = userId;
        await _handler.Update(existing);

        return await GetSettings(organizationId);
    }

    public async Task<OrganizationSettingsDto> UpdateTimeZone(Guid organizationId, Guid userId, OrganizationTimeZoneUpdateRequest request)
    {
        string timeZone = request?.TimeZone;
        if (!OrganizationTimeZones.IsSupported(timeZone))
            throw new ValidationAppException($"Nepodržana vremenska zona '{timeZone}' — očekuje se IANA oznaka, npr. \"Europe/Zagreb\".");

        if (!await _handler.UpdateTimeZone(organizationId, timeZone))
            throw new NotFoundAppException("Organization", organizationId);

        return await GetSettings(organizationId);
    }
}
