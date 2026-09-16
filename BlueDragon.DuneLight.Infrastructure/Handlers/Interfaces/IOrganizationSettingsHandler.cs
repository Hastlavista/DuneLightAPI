using System;
using System.Threading.Tasks;
using BlueDragon.DuneLight.Infrastructure.Domain.Models.Organizations;

namespace BlueDragon.DuneLight.Infrastructure.Handlers.Interfaces;

public interface IOrganizationSettingsHandler
{
    /// <summary>Null ako organizacija (još) nema eksplicitan redak postavki — vidi OrganizationSettingsService
    /// za default-primjenu.</summary>
    Task<OrganizationSettings> GetByOrganizationId(Guid organizationId);

    Task Add(OrganizationSettings settings);
    Task Update(OrganizationSettings settings);
}
