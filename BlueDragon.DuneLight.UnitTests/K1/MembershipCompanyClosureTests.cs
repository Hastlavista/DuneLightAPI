#nullable disable
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using BlueDragon.DuneLight.Core.DTOs.Appointments;
using BlueDragon.DuneLight.Core.DTOs.Catalog;
using BlueDragon.DuneLight.Core.DTOs.Clients;
using BlueDragon.DuneLight.Core.Enums;
using BlueDragon.DuneLight.Core.Interfaces.Catalog;
using BlueDragon.DuneLight.Core.Interfaces.Clients;
using BlueDragon.DuneLight.Core.Shared;
using BlueDragon.DuneLight.Infrastructure.Domain.Models.Catalog;
using BlueDragon.DuneLight.Infrastructure.Services;
using BlueDragon.DuneLight.UnitTests.Scheduling;

namespace BlueDragon.DuneLight.UnitTests.K1;

/// <summary>
/// K1-8 (bug b, P-1) — kad su sve poslovnice opsega plana neaktivne, članstvo "stoji": obnova ne otvara periode ni zaduženja
/// (sustavna pauza CompanyClosure), tekući period ostaje kakav jest; ponovna aktivacija nastavlja bez naknadnog zaduživanja (od
/// datuma kupnje: od dana aktivacije; kalendarski: od 1. sljedećeg mjeseca). Stajanje ne troši klijentove limite pauza; dok traje
/// nova pauza nije dopuštena, a otkaz djeluje odmah s posebnim razlogom.
/// </summary>
public class MembershipCompanyClosureTests
{
    private static IClientMembershipService Memberships(SchedulingWorld w) => w.Resolve<IClientMembershipService>();
    private static IMembershipRenewalService Renewal(SchedulingWorld w) => w.Resolve<IMembershipRenewalService>();
    private static DateOnly Today => DateOnly.FromDateTime(TestClock.UtcNow.UtcDateTime);

    private static Task<MembershipPlanDto> Plan(SchedulingWorld w, MembershipRenewalAnchor anchor, bool extendsPeriod = true,
        MembershipCompanyScope scope = MembershipCompanyScope.AllCompanies, params Guid[] companyIds) =>
        w.Resolve<IMembershipPlanService>().Create(w.OrganizationId, w.ActorUserId, new MembershipPlanCreateRequest
        {
            Name = $"Plan-{Guid.NewGuid():N}", Price = 50m, BillingInterval = MembershipBillingInterval.Monthly, RenewalAnchor = anchor,
            CompanyScope = scope, CompanyIds = companyIds.ToList(),
            Services = new List<MembershipPlanCoveredServiceRequest> { new() { ServiceId = w.Service.Id } },
            Pause = anchor == MembershipRenewalAnchor.PurchaseDate
                ? new MembershipPauseRulesDto { Allowed = true, MaxPauseDays = 30, ExtendsPeriod = extendsPeriod }
                : new MembershipPauseRulesDto { Allowed = true, MaxPausePeriods = 2 }
        });

    private static Task<ClientMembershipDto> Sell(SchedulingWorld w, Guid planId) =>
        Memberships(w).Sell(w.OrganizationId, w.ActorUserId, w.Client.Id.Value, new ClientMembershipSellRequest
        {
            MembershipPlanId = planId, SoldCompanyId = w.Company.Id.Value
        });

    private static async Task<List<MembershipPeriodDto>> Periods(SchedulingWorld w, Guid id) =>
        (await Memberships(w).GetPeriods(w.OrganizationId, id)).OrderBy(p => p.StartsOn).ToList();

    private static async Task<int> PeriodCharges(SchedulingWorld w, Guid id) =>
        (await Memberships(w).GetCharges(w.OrganizationId, id)).Count(c => c.Kind == MembershipChargeKind.Period);

    [Fact]
    public async Task PurchaseDate_StandsStillWhileAllCompaniesAreInactive_AndRestartsOnTheReactivationDay()
    {
        await using SchedulingWorld w = await SchedulingWorld.Create(nameof(PurchaseDate_StandsStillWhileAllCompaniesAreInactive_AndRestartsOnTheReactivationDay));
        ClientMembershipDto sold = await Sell(w, (await Plan(w, MembershipRenewalAnchor.PurchaseDate)).Id);
        DateOnly boundary = Today.AddMonths(1);
        await w.SetCompanyActive(w.Company, false);

        await Renewal(w).RunForOrganizationOn(w.OrganizationId, boundary);
        await Renewal(w).RunForOrganizationOn(w.OrganizationId, boundary.AddDays(10));

        ClientMembershipDto standing = await Memberships(w).GetById(w.OrganizationId, sold.Id);
        Assert.Equal(boundary, standing.StandingStillSince);
        Assert.Null(standing.CurrentPeriod);
        Assert.Contains(await Memberships(w).GetStandingStill(w.OrganizationId), m => m.Id == sold.Id);
        Assert.Equal((Today, boundary.AddDays(-1)), (Assert.Single(await Periods(w, sold.Id)).StartsOn, (await Periods(w, sold.Id))[0].EndsOn));
        Assert.Equal(1, await PeriodCharges(w, sold.Id));

        // Ponovna aktivacija: od toga dana novi period, bez zaduženja za propušteno; tekući period ostaje kakav jest.
        DateOnly reactivated = boundary.AddDays(20);
        await w.SetCompanyActive(w.Company, true);
        await Renewal(w).RunForOrganizationOn(w.OrganizationId, reactivated);

        List<MembershipPeriodDto> periods = await Periods(w, sold.Id);
        Assert.Equal(new[] { (Today, boundary.AddDays(-1)), (reactivated, reactivated.AddMonths(1).AddDays(-1)) },
            periods.Select(p => (p.StartsOn, p.EndsOn)));
        Assert.Equal(2, await PeriodCharges(w, sold.Id));
        ClientMembershipDto running = await Memberships(w).GetById(w.OrganizationId, sold.Id);
        Assert.Null(running.StandingStillSince);
        MembershipPauseDto closure = Assert.Single(running.Pauses);
        Assert.Equal((MembershipPauseSource.CompanyClosure, boundary, reactivated.AddDays(-1)), (closure.Source, closure.StartsOn, closure.ActualEndsOn.Value));
    }

    [Fact]
    public async Task StandingStill_IsShownDifferentlyFromAClientPause_OnTheMembershipAndOnSessions()
    {
        await using SchedulingWorld w = await SchedulingWorld.Create(nameof(StandingStill_IsShownDifferentlyFromAClientPause_OnTheMembershipAndOnSessions));
        ClientMembershipDto sold = await Sell(w, (await Plan(w, MembershipRenewalAnchor.PurchaseDate)).Id);
        DateOnly boundary = Today.AddMonths(1);
        // Termin u (budućem) razdoblju stajanja.
        DateTimeOffset start = new DateTimeOffset(boundary.ToDateTime(TimeOnly.MinValue), TimeSpan.Zero).AddDays(3).AddHours(10);
        AppointmentDto booked = await w.CreateAppointment(w.CreateRequest(start, overrideAvailability: true));

        await w.SetCompanyActive(w.Company, false);
        await Renewal(w).RunForOrganizationOn(w.OrganizationId, boundary);

        ClientMembershipDto standing = await Memberships(w).GetById(w.OrganizationId, sold.Id);
        Assert.Equal((MembershipState.StandingStill, (DateOnly?)boundary), (standing.State, standing.StandingStillSince));
        BookingParticipationDto session = (await w.Appointments.GetById(w.OrganizationId, booked.Id)).Bookings.Single().Participations.Single();
        Assert.NotEqual(MembershipCoverageStatus.Covered, session.MembershipCoverage.Status);
        Assert.Equal(MembershipCoverageReason.MembershipStandingCompanyClosed, session.MembershipCoverage.Reason);
    }

    [Fact]
    public async Task Calendar_AfterReactivation_ContinuesFromTheFirstOfTheNextMonth_WithoutChargingTheRest()
    {
        await using SchedulingWorld w = await SchedulingWorld.Create(nameof(Calendar_AfterReactivation_ContinuesFromTheFirstOfTheNextMonth_WithoutChargingTheRest));
        ClientMembershipDto sold = await Sell(w, (await Plan(w, MembershipRenewalAnchor.CalendarMonth)).Id);
        DateOnly nextMonth = new DateOnly(Today.Year, Today.Month, 1).AddMonths(1);
        await w.SetCompanyActive(w.Company, false);
        await Renewal(w).RunForOrganizationOn(w.OrganizationId, nextMonth);

        await w.SetCompanyActive(w.Company, true);
        await Renewal(w).RunForOrganizationOn(w.OrganizationId, nextMonth.AddDays(9));
        Assert.Single(await Periods(w, sold.Id));
        Assert.Null((await Memberships(w).GetById(w.OrganizationId, sold.Id)).StandingStillSince);

        await Renewal(w).RunForOrganizationOn(w.OrganizationId, nextMonth.AddMonths(1));
        List<MembershipPeriodDto> periods = await Periods(w, sold.Id);
        Assert.Equal(nextMonth.AddMonths(1), periods[^1].StartsOn);
        Assert.Equal(2, await PeriodCharges(w, sold.Id));
    }

    [Fact]
    public async Task OneActiveCompanyInTheScope_KeepsRenewing()
    {
        await using SchedulingWorld w = await SchedulingWorld.Create(nameof(OneActiveCompanyInTheScope_KeepsRenewing));
        Company other = await w.AddCompany("Druga");
        ClientMembershipDto sold = await Sell(w, (await Plan(w, MembershipRenewalAnchor.PurchaseDate,
            scope: MembershipCompanyScope.SelectedCompanies, companyIds: new[] { w.Company.Id.Value, other.Id.Value })).Id);
        await w.SetCompanyActive(w.Company, false);

        await Renewal(w).RunForOrganizationOn(w.OrganizationId, Today.AddMonths(1));

        Assert.Equal(2, (await Periods(w, sold.Id)).Count);
        Assert.Null((await Memberships(w).GetById(w.OrganizationId, sold.Id)).StandingStillSince);
    }

    [Fact]
    public async Task WhileStandingStill_NoNewPause_AndCancellationEndsImmediatelyWithItsOwnReason()
    {
        await using SchedulingWorld w = await SchedulingWorld.Create(nameof(WhileStandingStill_NoNewPause_AndCancellationEndsImmediatelyWithItsOwnReason));
        ClientMembershipDto sold = await Sell(w, (await Plan(w, MembershipRenewalAnchor.PurchaseDate)).Id);
        await w.SetCompanyActive(w.Company, false);
        await Renewal(w).RunForOrganizationOn(w.OrganizationId, Today.AddMonths(1));

        await SchedulingAssert.BusinessRule(ErrorCodes.MembershipPauseNotAllowed, () => Memberships(w).Pause(w.OrganizationId, w.ActorUserId, sold.Id,
            new ClientMembershipPauseRequest { StartsOn = Today.AddMonths(2), EndsOn = Today.AddMonths(2).AddDays(3) }));

        Assert.Equal(MembershipEndEffectiveReason.CompanyClosure, (await Memberships(w).PreviewCancellation(w.OrganizationId, sold.Id)).Reason);
        ClientMembershipDto cancelled = await Memberships(w).RequestCancellation(w.OrganizationId, w.ActorUserId, sold.Id, new ClientMembershipCancelRequest());
        Assert.Equal(MembershipEndReason.CancelledDuringCompanyClosure, cancelled.EndReason);
        // Zadnji otvoreni period završava kasnije od danas (simulirana obnova je u budućnosti) → kraj je njegov kraj.
        Assert.Equal(Today.AddMonths(1).AddDays(-1), cancelled.EndsOn);
        Assert.Null(cancelled.StandingStillSince);
    }

    [Fact]
    public async Task ClientPauseOverlappingTheStandstill_DoesNotSpendTheOverlapFromTheLimit_AndKeepsItsEnd()
    {
        await using SchedulingWorld w = await SchedulingWorld.Create(nameof(ClientPauseOverlappingTheStandstill_DoesNotSpendTheOverlapFromTheLimit_AndKeepsItsEnd));
        ClientMembershipDto sold = await Sell(w, (await Plan(w, MembershipRenewalAnchor.PurchaseDate, extendsPeriod: false)).Id);
        DateOnly boundary = Today.AddMonths(1);
        DateOnly pauseStart = boundary.AddDays(-5), pauseEnd = boundary.AddDays(10); // 16 dana, preko granice
        await Memberships(w).Pause(w.OrganizationId, w.ActorUserId, sold.Id, new ClientMembershipPauseRequest { StartsOn = pauseStart, EndsOn = pauseEnd });
        Assert.Equal(30 - 16, (await Memberships(w).GetById(w.OrganizationId, sold.Id)).PauseAllowance.RemainingDays);

        await w.SetCompanyActive(w.Company, false);
        await Renewal(w).RunForOrganizationOn(w.OrganizationId, boundary);
        await w.SetCompanyActive(w.Company, true);
        await Renewal(w).RunForOrganizationOn(w.OrganizationId, boundary.AddDays(5)); // stajanje boundary..boundary+4 (5 dana)

        ClientMembershipDto after = await Memberships(w).GetById(w.OrganizationId, sold.Id);
        Assert.Equal(30 - (16 - 5), after.PauseAllowance.RemainingDays);
        MembershipPauseDto clientPause = after.Pauses.Single(p => p.Source == MembershipPauseSource.Client);
        Assert.Equal((pauseEnd, (DateOnly?)null), (clientPause.PlannedEndsOn.Value, clientPause.ActualEndsOn));
    }
}
