using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using BlueDragon.DuneLight.Infrastructure.Utils;

namespace BlueDragon.DuneLight.Infrastructure.Services;

/// <summary>
/// Razrješava poslovni kalendar (<see cref="OrganizationCalendar"/>) — jedini ulaz kroz koji zakazivanje i poslovna
/// kalendarska pravila dobivaju vremensku zonu (nikad TimeZoneInfo.Local niti offset iz zahtjeva/baze).
/// Pravilo razrješavanja je na jednom mjestu: efektivna zona poslovnice = Company.TimeZone ?? Organization.TimeZone
/// (<see cref="Core.Shared.OrganizationTimeZones.Effective"/>). Sve što se tiče jedne poslovnice (radno vrijeme,
/// roster, odsutnosti, praznici, pauze, slobodni termini, ponavljanja, grupe, dashboard) koristi kalendar poslovnice;
/// kalendar organizacije ostaje samo za pravila bez poslovnice.
/// </summary>
public interface IOrganizationCalendarService
{
    /// <summary>Kalendar u zoni organizacije (Organization.TimeZone) — za pravila koja nisu vezana uz poslovnicu.</summary>
    Task<OrganizationCalendar> GetCalendar(Guid organizationId);

    /// <summary>Kalendar u efektivnoj zoni poslovnice.</summary>
    Task<OrganizationCalendar> GetCompanyCalendar(Guid organizationId, Guid companyId);

    /// <summary>T1-7: kalendar poslovnice kad je zadana, inače kalendar organizacije (npr. cjenik "za sve tvrtke", prodaja bez
    /// poslovnice).</summary>
    Task<OrganizationCalendar> GetCompanyOrOrganizationCalendar(Guid organizationId, Guid? companyId);

    /// <summary>Kalendari više poslovnica odjednom (dva upita ukupno) — za operacije koje obuhvaćaju više poslovnica.</summary>
    Task<Dictionary<Guid, OrganizationCalendar>> GetCompanyCalendars(Guid organizationId, IEnumerable<Guid> companyIds);
}
