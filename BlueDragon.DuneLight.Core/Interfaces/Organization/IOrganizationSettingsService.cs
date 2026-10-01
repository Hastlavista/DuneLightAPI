using System;
using System.Threading.Tasks;
using BlueDragon.DuneLight.Core.DTOs.Organization;

namespace BlueDragon.DuneLight.Core.Interfaces.Organization;

public interface IOrganizationSettingsService
{
    Task<OrganizationSettingsDto> GetSettings(Guid organizationId);
    Task<OrganizationSettingsDto> UpdateCancellationCutoff(Guid organizationId, Guid userId, OrganizationSettingsUpdateRequest request);

    /// <summary>Vrijednost koju koristi BookingCancellationPolicy — platformski default (1440 = 24h) ako
    /// organizacija nema eksplicitan redak postavki.</summary>
    Task<int> GetCancellationCutoffMinutes(Guid organizationId);

    /// <summary>Phase D3B3A — kad se paket troši (default OnCompletion ako organizacija nema redak postavki).</summary>
    Task<Enums.PackageConsumptionTiming> GetPackageConsumptionTiming(Guid organizationId);

    /// <summary>Postavlja IANA vremensku zonu organizacije; nepodržan id baca ValidationAppException.</summary>
    Task<OrganizationSettingsDto> UpdateTimeZone(Guid organizationId, Guid userId, OrganizationTimeZoneUpdateRequest request);
}
