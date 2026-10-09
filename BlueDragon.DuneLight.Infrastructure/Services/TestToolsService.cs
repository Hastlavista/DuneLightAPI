using System;
using System.Threading.Tasks;
using BlueDragon.DuneLight.Core.DTOs.TestTools;
using BlueDragon.DuneLight.Core.Shared;
using BlueDragon.DuneLight.Core.Shared.Exceptions;
using BlueDragon.DuneLight.Infrastructure.Domain.Models.TestTools;
using BlueDragon.DuneLight.Infrastructure.Domain.Settings;
using BlueDragon.DuneLight.Infrastructure.Handlers.Interfaces;
using BlueDragon.DuneLight.Infrastructure.Time;
using BlueDragon.DuneLight.Infrastructure.Utils;

namespace BlueDragon.DuneLight.Infrastructure.Services;

/// <summary>
/// T1 — PRIVREMENI testni alati (vidi <see cref="ITestToolsService"/>). Jedini posao koji po rasporedu stvara zapise je prolaz
/// obnove članarina (periodi, zaduženja, grace i automatski završetak, kraj stajanja, pokriće, preskočeni članovi grupe); sve
/// ostalo se izvodi iz sata pri čitanju (zatvorenost termina, stanje članstva i pauza, istek paketa, otkazni prozori), a lista
/// čekanja ističe na događaj termina. Zato skok pokreće obnovu za svaki preskočeni lokalni dan, sa satom pomaknutim na početak
/// tog dana, kao da je dnevni pozadinski servis radio.
/// </summary>
public class TestToolsService : ITestToolsService
{
    private readonly BusinessTimeProvider _timeProvider;
    private readonly IClockOffsetStore _offsets;
    private readonly ITestToolOrganizationHandler _handler;
    private readonly IOrganizationCalendarService _organizationCalendarService;
    private readonly IMembershipRenewalService _renewalService;

    public TestToolsService(
        BusinessTimeProvider timeProvider,
        IClockOffsetStore offsets,
        ITestToolOrganizationHandler handler,
        IOrganizationCalendarService organizationCalendarService,
        IMembershipRenewalService renewalService)
    {
        _timeProvider = timeProvider;
        _offsets = offsets;
        _handler = handler;
        _organizationCalendarService = organizationCalendarService;
        _renewalService = renewalService;
    }

    public async Task<TestToolsOrganizationStatusDto> GetStatus(Guid organizationId)
    {
        await EnsureOrganization(organizationId);
        TestToolOrganization row = await _handler.Get(organizationId);
        OrganizationCalendar calendar = await _organizationCalendarService.GetCalendar(organizationId);
        DateTimeOffset real = _timeProvider.GetRealUtcNow();
        TimeSpan offset = _timeProvider.GetOffset(organizationId);

        return new TestToolsOrganizationStatusDto
        {
            OrganizationId = organizationId,
            IsDemo = row?.IsDemo == true,
            DemoLevel = row is { IsDemo: true } ? DemoSeedService.LevelOf(row) : null,
            RetiredAt = row?.RetiredAt,
            RealUtc = real,
            EffectiveUtc = real + offset,
            Offset = offset,
            LocalDate = calendar.LocalDate(real + offset),
            TimeZone = calendar.TimeZoneId,
            ClockAdvancedAt = row?.ClockAdvancedAt
        };
    }

    public async Task<TestClockAdvanceResultDto> AdvanceClock(Guid organizationId, TestClockAdvanceRequest request, Guid? platformAccountId)
    {
        await EnsureOrganization(organizationId);
        if (request == null || request.Days.HasValue == request.To.HasValue)
            throw new ValidationAppException(ErrorCodes.TestClockAdvanceInvalid, "Zadajte točno jedno: broj dana (Days) ili ciljni trenutak (To).");
        if (request.Days is <= 0)
            throw new ValidationAppException(ErrorCodes.TestClockAdvanceInvalid, "Broj dana mora biti veći od nule.");

        DateTimeOffset real = _timeProvider.GetRealUtcNow();
        TimeSpan currentOffset = _timeProvider.GetOffset(organizationId);
        DateTimeOffset previous = real + currentOffset;
        DateTimeOffset target = request.To?.ToUniversalTime() ?? previous.AddDays(request.Days!.Value);

        if (target < previous)
            throw new BusinessRuleException(ErrorCodes.TestClockBackwards,
                "Sat se može pomaknuti samo naprijed. Povratak na stvarno vrijeme ide resetom demo organizacije.");
        if (target - previous > TimeSpan.FromDays(TestToolsSettings.MaxAdvanceDays))
            throw new BusinessRuleException(ErrorCodes.TestClockAdvanceTooLarge,
                $"Jedan skok može biti najviše {TestToolsSettings.MaxAdvanceDays} dana; podijelite ga na više skokova.");

        OrganizationCalendar calendar = await _organizationCalendarService.GetCalendar(organizationId);
        DateOnly previousDate = calendar.LocalDate(previous);
        DateOnly targetDate = calendar.LocalDate(target);

        int days = 0;
        int memberships = 0;
        using (OrganizationClockContext.Use(organizationId))
        {
            // Dan po dan: sat na početku svakog preskočenog lokalnog dana, na zadnjem danu na cilju. Pomak se sprema na svakom
            // koraku, pa prekid usred skoka ostavlja dosljedno stanje (sat na zadnjem obrađenom danu).
            for (DateOnly day = previousDate.AddDays(1); day <= targetDate; day = day.AddDays(1))
            {
                DateTimeOffset step = day == targetDate ? target : calendar.ToInstant(day, TimeSpan.Zero);
                await SetOffset(organizationId, step - real, currentOffset, platformAccountId);
                memberships += await _renewalService.RunForOrganization(organizationId);
                days++;
            }

            if (days == 0)
            {
                await SetOffset(organizationId, target - real, currentOffset, platformAccountId);
                memberships += await _renewalService.RunForOrganization(organizationId);
            }
        }

        TimeSpan finalOffset = _timeProvider.GetOffset(organizationId);
        return new TestClockAdvanceResultDto
        {
            RealUtc = real,
            PreviousEffectiveUtc = previous,
            EffectiveUtc = real + finalOffset,
            Offset = finalOffset,
            LocalDate = calendar.LocalDate(real + finalOffset),
            DaysProcessed = days,
            MembershipsProcessed = memberships
        };
    }

    /// <summary>Sprema pomak u cijelim sekundama, nikad manji od prethodnog (samo naprijed).</summary>
    private async Task SetOffset(Guid organizationId, TimeSpan offset, TimeSpan previousOffset, Guid? platformAccountId)
    {
        long seconds = Math.Max((long)Math.Floor(offset.TotalSeconds), (long)Math.Ceiling(previousOffset.TotalSeconds));
        await _handler.SetClockOffset(organizationId, seconds, platformAccountId, _timeProvider.GetRealUtcNow());
        _offsets.Set(organizationId, TimeSpan.FromSeconds(seconds));
    }

    private async Task EnsureOrganization(Guid organizationId)
    {
        if (!await _handler.OrganizationExists(organizationId))
            throw new NotFoundAppException("Organization", organizationId);
    }
}
