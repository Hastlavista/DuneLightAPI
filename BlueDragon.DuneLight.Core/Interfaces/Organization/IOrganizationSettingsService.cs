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
}
