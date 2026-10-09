using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using BlueDragon.DuneLight.Core.DTOs.Organization;
using BlueDragon.DuneLight.Core.Interfaces.Organization;
using BlueDragon.DuneLight.Infrastructure.Handlers.Interfaces;
using BlueDragon.DuneLight.Infrastructure.Time;
using BlueDragon.DuneLight.Infrastructure.Utils;

namespace BlueDragon.DuneLight.Infrastructure.Services;

/// <summary>
/// T1 — trenutno vrijeme organizacije: stvarni i poslovni trenutak, pomak i lokalni datum/vrijeme u zoni organizacije te
/// poslovnica s vlastitom (različitom) zonom. Sat se čita za traženu organizaciju neovisno o kontekstu zahtjeva.
/// </summary>
public class OrganizationClockService : IOrganizationClockService
{
    private readonly BusinessTimeProvider _timeProvider;
    private readonly IOrganizationCalendarService _organizationCalendarService;
    private readonly ICompanyHandler _companyHandler;

    public OrganizationClockService(
        BusinessTimeProvider timeProvider, IOrganizationCalendarService organizationCalendarService, ICompanyHandler companyHandler)
    {
        _timeProvider = timeProvider;
        _organizationCalendarService = organizationCalendarService;
        _companyHandler = companyHandler;
    }

    public async Task<OrganizationClockDto> GetClock(Guid organizationId)
    {
        DateTimeOffset real = _timeProvider.GetRealUtcNow();
        TimeSpan offset = _timeProvider.GetOffset(organizationId);
        DateTimeOffset effective = real + offset;

        OrganizationCalendar calendar = await _organizationCalendarService.GetCalendar(organizationId);
        Dictionary<Guid, string> overrides = await _companyHandler.GetAllTimeZoneOverrides(organizationId);
        List<Guid> differing = overrides
            .Where(o => !string.Equals(o.Value, calendar.TimeZoneId, StringComparison.Ordinal))
            .Select(o => o.Key)
            .ToList();
        Dictionary<Guid, OrganizationCalendar> companyCalendars = differing.Count == 0
            ? new Dictionary<Guid, OrganizationCalendar>()
            : await _organizationCalendarService.GetCompanyCalendars(organizationId, differing);

        return new OrganizationClockDto
        {
            RealUtc = real,
            EffectiveUtc = effective,
            Offset = offset,
            IsSimulated = offset != TimeSpan.Zero,
            TimeZone = calendar.TimeZoneId,
            LocalDate = calendar.LocalDate(effective),
            LocalTime = TimeOnly.FromTimeSpan(calendar.LocalTimeOfDay(effective)),
            Companies = companyCalendars
                .Select(c => new CompanyClockDto
                {
                    CompanyId = c.Key,
                    TimeZone = c.Value.TimeZoneId,
                    LocalDate = c.Value.LocalDate(effective),
                    LocalTime = TimeOnly.FromTimeSpan(c.Value.LocalTimeOfDay(effective))
                })
                .OrderBy(c => c.CompanyId)
                .ToList()
        };
    }
}
