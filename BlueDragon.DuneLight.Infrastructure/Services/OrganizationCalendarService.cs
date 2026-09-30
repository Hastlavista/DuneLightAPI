using System;
using System.Threading.Tasks;
using BlueDragon.DuneLight.Core.Shared;
using BlueDragon.DuneLight.Infrastructure.Handlers.Interfaces;
using BlueDragon.DuneLight.Infrastructure.Utils;

namespace BlueDragon.DuneLight.Infrastructure.Services;

public class OrganizationCalendarService : IOrganizationCalendarService
{
    private readonly IOrganizationSettingsHandler _organizationSettingsHandler;

    public OrganizationCalendarService(IOrganizationSettingsHandler organizationSettingsHandler)
    {
        _organizationSettingsHandler = organizationSettingsHandler;
    }

    /// <summary>Bez keša — jedan mali upit po operaciji, pa promjena zone odmah vrijedi. Nepoznata organizacija dobiva
    /// zadanu zonu (pozivatelji su već tenant-scoped); pohranjena nevaljana zona (ručna izmjena baze) baca iznimku umjesto
    /// tihog pada na drugu zonu.</summary>
    public async Task<OrganizationCalendar> GetCalendar(Guid organizationId)
    {
        string timeZone = await _organizationSettingsHandler.GetTimeZone(organizationId);
        return OrganizationCalendar.For(timeZone ?? OrganizationTimeZones.Default);
    }
}
