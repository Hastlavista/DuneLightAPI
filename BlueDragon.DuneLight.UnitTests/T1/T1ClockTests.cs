#nullable disable
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using BlueDragon.DuneLight.Core.DTOs.Catalog;
using BlueDragon.DuneLight.Core.DTOs.Clients;
using BlueDragon.DuneLight.Core.DTOs.Organization;
using BlueDragon.DuneLight.Core.DTOs.TestTools;
using BlueDragon.DuneLight.Core.Enums;
using BlueDragon.DuneLight.Core.Interfaces.Catalog;
using BlueDragon.DuneLight.Core.Interfaces.Clients;
using BlueDragon.DuneLight.Core.Interfaces.Organization;
using BlueDragon.DuneLight.Core.Shared;
using BlueDragon.DuneLight.Core.Shared.Exceptions;
using BlueDragon.DuneLight.Infrastructure.Services;
using BlueDragon.DuneLight.Infrastructure.Time;
using BlueDragon.DuneLight.UnitTests.Scheduling;

namespace BlueDragon.DuneLight.UnitTests.T1;

/// <summary>
/// T1 — jedan poslovni sat: pomak sata organizacije samo naprijed (najviše 400 dana), propušteni prolazi obnove dan po dan,
/// pomak vrijedi samo za tu organizaciju i samo u njenom kontekstu, endpoint vremena organizacije.
/// </summary>
public class T1ClockTests
{
    private static ITestToolsService Tools(SchedulingWorld w) => w.Resolve<ITestToolsService>();

    [Fact]
    public async Task Advance_ByDays_RunsTheRenewalForEverySkippedDay_AndOpensTheMissedPeriods()
    {
        await using SchedulingWorld w = await SchedulingWorld.Create(nameof(Advance_ByDays_RunsTheRenewalForEverySkippedDay_AndOpensTheMissedPeriods));
        MembershipPlanDto plan = await w.Resolve<IMembershipPlanService>().Create(w.OrganizationId, w.ActorUserId, new MembershipPlanCreateRequest
        {
            Name = "Gold", Price = 50m, BillingInterval = MembershipBillingInterval.Monthly, RenewalAnchor = MembershipRenewalAnchor.PurchaseDate,
            CompanyScope = MembershipCompanyScope.AllCompanies,
            Services = new List<MembershipPlanCoveredServiceRequest> { new() { ServiceId = w.Service.Id } },
            Pause = new MembershipPauseRulesDto { Allowed = false }
        });
        ClientMembershipDto sold = await w.Resolve<IClientMembershipService>().Sell(w.OrganizationId, w.ActorUserId, w.Client.Id.Value,
            new ClientMembershipSellRequest { MembershipPlanId = plan.Id, SoldCompanyId = w.Company.Id.Value });

        TestClockAdvanceResultDto result = await Tools(w).AdvanceClock(w.OrganizationId, new TestClockAdvanceRequest { Days = 65 }, null);

        Assert.Equal(65, result.DaysProcessed);
        Assert.True(result.Offset >= TimeSpan.FromDays(65) - TimeSpan.FromSeconds(1));
        // Prodaja je otvorila prvi period; skok od 65 dana otvara još dva (obnova na +1 i +2 mjeseca), svaki sa zaduženjem.
        List<MembershipPeriodDto> periods = await w.Resolve<IClientMembershipService>().GetPeriods(w.OrganizationId, sold.Id);
        Assert.Equal(3, periods.Count);
        Assert.Equal(3, (await w.Resolve<IClientMembershipService>().GetCharges(w.OrganizationId, sold.Id)).Count(c => c.Kind == MembershipChargeKind.Period));
    }

    [Fact]
    public async Task Advance_OnlyForward_AtMost400Days_AndExactlyOneOfDaysOrTo()
    {
        await using SchedulingWorld w = await SchedulingWorld.Create(nameof(Advance_OnlyForward_AtMost400Days_AndExactlyOneOfDaysOrTo));
        await Tools(w).AdvanceClock(w.OrganizationId, new TestClockAdvanceRequest { Days = 2 }, null);

        BusinessRuleException backwards = await Assert.ThrowsAsync<BusinessRuleException>(() =>
            Tools(w).AdvanceClock(w.OrganizationId, new TestClockAdvanceRequest { To = TestClock.UtcNow.AddDays(1) }, null));
        Assert.Equal(ErrorCodes.TestClockBackwards, backwards.Code);

        BusinessRuleException tooLarge = await Assert.ThrowsAsync<BusinessRuleException>(() =>
            Tools(w).AdvanceClock(w.OrganizationId, new TestClockAdvanceRequest { Days = 401 }, null));
        Assert.Equal(ErrorCodes.TestClockAdvanceTooLarge, tooLarge.Code);

        ValidationAppException both = await Assert.ThrowsAsync<ValidationAppException>(() =>
            Tools(w).AdvanceClock(w.OrganizationId, new TestClockAdvanceRequest { Days = 1, To = TestClock.UtcNow.AddDays(10) }, null));
        Assert.Equal(ErrorCodes.TestClockAdvanceInvalid, both.Code);

        TestToolsOrganizationStatusDto status = await Tools(w).GetStatus(w.OrganizationId);
        Assert.InRange(status.Offset, TimeSpan.FromDays(2) - TimeSpan.FromSeconds(1), TimeSpan.FromDays(2) + TimeSpan.FromSeconds(1));
        Assert.False(status.IsDemo);
        Assert.True(status.OffsetIsPermanent);
    }

    [Fact]
    public async Task TheOffset_AppliesOnlyInsideThatOrganizationsClockContext()
    {
        await using SchedulingWorld w = await SchedulingWorld.Create(nameof(TheOffset_AppliesOnlyInsideThatOrganizationsClockContext));
        await Tools(w).AdvanceClock(w.OrganizationId, new TestClockAdvanceRequest { Days = 10 }, null);
        BusinessTimeProvider clock = w.Resolve<BusinessTimeProvider>();

        DateTimeOffset outside = clock.GetUtcNow();
        DateTimeOffset inside;
        DateTimeOffset otherOrganization;
        using (OrganizationClockContext.Use(w.OrganizationId))
            inside = clock.GetUtcNow();
        using (OrganizationClockContext.Use(Guid.NewGuid()))
            otherOrganization = clock.GetUtcNow();

        Assert.InRange(inside - outside, TimeSpan.FromDays(10) - TimeSpan.FromSeconds(2), TimeSpan.FromDays(10) + TimeSpan.FromSeconds(2));
        Assert.InRange(otherOrganization - outside, TimeSpan.Zero, TimeSpan.FromSeconds(2));
    }

    [Fact]
    public async Task OrganizationClock_ReportsRealAndEffectiveTime_TheOffset_AndTheLocalDateInTheOrganizationZone()
    {
        await using SchedulingWorld w = await SchedulingWorld.Create(
            nameof(OrganizationClock_ReportsRealAndEffectiveTime_TheOffset_AndTheLocalDateInTheOrganizationZone), "Europe/Zagreb");
        IOrganizationClockService clocks = w.Resolve<IOrganizationClockService>();

        OrganizationClockDto real = await clocks.GetClock(w.OrganizationId);
        Assert.False(real.IsSimulated);
        Assert.Equal(TimeSpan.Zero, real.Offset);
        Assert.Equal("Europe/Zagreb", real.TimeZone);

        await Tools(w).AdvanceClock(w.OrganizationId, new TestClockAdvanceRequest { Days = 3 }, null);
        OrganizationClockDto simulated = await clocks.GetClock(w.OrganizationId);

        Assert.True(simulated.IsSimulated);
        Assert.Equal(simulated.RealUtc + simulated.Offset, simulated.EffectiveUtc);
        Assert.Equal(DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(simulated.EffectiveUtc,
            TimeZoneInfo.FindSystemTimeZoneById("Europe/Zagreb")).DateTime), simulated.LocalDate);
        Assert.Equal(real.LocalDate.AddDays(3), simulated.LocalDate);
    }
}
