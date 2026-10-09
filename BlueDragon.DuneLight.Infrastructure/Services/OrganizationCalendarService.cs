using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using BlueDragon.DuneLight.Core.Shared;
using BlueDragon.DuneLight.Infrastructure.Handlers.Interfaces;
using BlueDragon.DuneLight.Infrastructure.Utils;

namespace BlueDragon.DuneLight.Infrastructure.Services;

/// <summary>Bez keša — mali upiti po operaciji, pa promjena zone (organizacije ili poslovnice) odmah vrijedi. Nepoznata
/// organizacija dobiva zadanu zonu, a poslovnica koja ne pripada organizaciji zonu organizacije (pozivatelji su već
/// tenant-scoped i poslovnicu validiraju sami); pohranjena nevaljana zona (ručna izmjena baze) baca iznimku umjesto
/// tihog pada na drugu zonu.</summary>
public class OrganizationCalendarService : IOrganizationCalendarService
{
    private readonly IOrganizationSettingsHandler _organizationSettingsHandler;
    private readonly ICompanyHandler _companyHandler;

    public OrganizationCalendarService(IOrganizationSettingsHandler organizationSettingsHandler, ICompanyHandler companyHandler)
    {
        _organizationSettingsHandler = organizationSettingsHandler;
        _companyHandler = companyHandler;
    }

    public async Task<OrganizationCalendar> GetCalendar(Guid organizationId)
    {
        string timeZone = await _organizationSettingsHandler.GetTimeZone(organizationId);
        return OrganizationCalendar.For(OrganizationTimeZones.Effective(null, timeZone));
    }

    public async Task<OrganizationCalendar> GetCompanyCalendar(Guid organizationId, Guid companyId)
    {
        Dictionary<Guid, OrganizationCalendar> calendars = await GetCompanyCalendars(organizationId, new[] { companyId });
        return calendars[companyId];
    }

    public Task<OrganizationCalendar> GetCompanyOrOrganizationCalendar(Guid organizationId, Guid? companyId) =>
        companyId.HasValue ? GetCompanyCalendar(organizationId, companyId.Value) : GetCalendar(organizationId);

    public async Task<Dictionary<Guid, OrganizationCalendar>> GetCompanyCalendars(Guid organizationId, IEnumerable<Guid> companyIds)
    {
        List<Guid> ids = companyIds.Distinct().ToList();
        Dictionary<Guid, OrganizationCalendar> result = new Dictionary<Guid, OrganizationCalendar>();
        if (ids.Count == 0)
            return result;

        string organizationTimeZone = await _organizationSettingsHandler.GetTimeZone(organizationId);
        Dictionary<Guid, string> overrides = await _companyHandler.GetTimeZoneOverrides(organizationId, ids);

        foreach (Guid id in ids)
        {
            string companyTimeZone = overrides.TryGetValue(id, out string value) ? value : null;
            result[id] = OrganizationCalendar.For(OrganizationTimeZones.Effective(companyTimeZone, organizationTimeZone));
        }

        return result;
    }
}
