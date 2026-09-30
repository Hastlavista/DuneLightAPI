using System;
using System.Threading.Tasks;
using BlueDragon.DuneLight.Infrastructure.Utils;

namespace BlueDragon.DuneLight.Infrastructure.Services;

/// <summary>
/// Razrješava poslovni kalendar (<see cref="OrganizationCalendar"/>) organizacije iz Organization.TimeZone. Jedini ulaz
/// kroz koji zakazivanje i poslovna kalendarska pravila dobivaju vremensku zonu — nikad TimeZoneInfo.Local niti offset
/// iz zahtjeva/baze. Poslovnica (Company) za sada nema vlastitu zonu; kad je dobije, ovo sučelje dobiva preopterećenje
/// po poslovnici, a pozivatelji već prolaze kroz njega.
/// </summary>
public interface IOrganizationCalendarService
{
    Task<OrganizationCalendar> GetCalendar(Guid organizationId);
}
