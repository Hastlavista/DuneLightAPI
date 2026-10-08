using System;
using System.Threading.Tasks;
using BlueDragon.DuneLight.Infrastructure.Domain.Models.Organizations;

namespace BlueDragon.DuneLight.Infrastructure.Handlers.Interfaces;

public interface IOrganizationSettingsHandler
{
    /// <summary>Null ako organizacija (još) nema eksplicitan redak postavki — vidi OrganizationSettingsService
    /// za default-primjenu.</summary>
    Task<OrganizationSettings> GetByOrganizationId(Guid organizationId);

    /// <summary>Organization.TimeZone; null ako organizacija ne postoji.</summary>
    Task<string> GetTimeZone(Guid organizationId);

    /// <summary>Postavlja Organization.TimeZone (pozivatelj je već validirao id); false ako organizacija ne postoji.</summary>
    Task<bool> UpdateTimeZone(Guid organizationId, string timeZone);

    /// <summary>Mijenja redak postavki organizacije; stvara ga s defaultima ako ne postoji.</summary>
    Task Upsert(Guid organizationId, Guid userId, Action<OrganizationSettings> change);
}
