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

    /// <summary>Organization.TimeZone; null ako organizacija ne postoji.</summary>
    Task<string> GetTimeZone(Guid organizationId);

    /// <summary>Postavlja Organization.TimeZone (pozivatelj je već validirao id); false ako organizacija ne postoji.</summary>
    Task<bool> UpdateTimeZone(Guid organizationId, string timeZone);
}
