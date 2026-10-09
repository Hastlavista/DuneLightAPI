#nullable disable
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using BlueDragon.DuneLight.Core.DTOs.Catalog;
using BlueDragon.DuneLight.Core.DTOs.Clients;
using BlueDragon.DuneLight.Core.Enums;
using BlueDragon.DuneLight.Core.Interfaces.Catalog;
using BlueDragon.DuneLight.Core.Interfaces.Clients;
using BlueDragon.DuneLight.Core.Shared;
using BlueDragon.DuneLight.Core.Shared.Exceptions;
using BlueDragon.DuneLight.Infrastructure.Domain.Contexts;
using BlueDragon.DuneLight.Infrastructure.Domain.Models.Catalog;
using BlueDragon.DuneLight.Infrastructure.Domain.Models.Clients;
using BlueDragon.DuneLight.UnitTests.Scheduling;
using Microsoft.EntityFrameworkCore;
using ServiceEntity = BlueDragon.DuneLight.Infrastructure.Domain.Models.Catalog.Service;

namespace BlueDragon.DuneLight.UnitTests.Memberships;

/// <summary>
/// P2 phase 2B — client memberships through the real services against PostgreSQL (worlds use the UTC calendar, so "today" is
/// the UTC date): sale (start-date range, overlap Q10, plan capacity, inactive plan, sale-company warning), cancellation and
/// withdrawal (future pauses cancelled), pauses (rolling totals, cancel, early return), plan change (Q46), end override,
/// void, and a plan update applied to existing memberships (Q14/Q48).
/// </summary>
public class ClientMembershipServiceTests
{
    private static IClientMembershipService Memberships(SchedulingWorld w) => w.Resolve<IClientMembershipService>();
    private static IMembershipPlanService Plans(SchedulingWorld w) => w.Resolve<IMembershipPlanService>();
    private static DateOnly Today => DateOnly.FromDateTime(TestClock.UtcNow.UtcDateTime);

    private static MembershipPlanVersionPublishRequest Terms(
        IEnumerable<ServiceEntity> services, MembershipRenewalAnchor anchor = MembershipRenewalAnchor.PurchaseDate,
        int? commitment = null, int? notice = null, decimal price = 50m, int? maxPauseDays = 30, Guid[] companies = null) => new()
    {
        Price = price,
        StartFee = 0m,
        BillingInterval = MembershipBillingInterval.Monthly,
        RenewalAnchor = anchor,
        CompanyScope = companies == null ? MembershipCompanyScope.AllCompanies : MembershipCompanyScope.SelectedCompanies,
        CompanyIds = companies?.ToList() ?? new List<Guid>(),
        Services = services.Select(s => new MembershipPlanCoveredServiceRequest { ServiceId = s.Id }).ToList(),
        MinimumCommitmentPeriods = commitment,
        CancellationNoticeDays = notice,
        Pause = anchor == MembershipRenewalAnchor.PurchaseDate
            ? new MembershipPauseRulesDto { Allowed = true, MaxPauseDays = maxPauseDays, ExtendsPeriod = true }
            : new MembershipPauseRulesDto { Allowed = true, MaxPausePeriods = 2 }
    };

    private static async Task<MembershipPlanDto> Plan(SchedulingWorld w, string name, MembershipPlanVersionPublishRequest terms, int? capacity = null)
    {
        MembershipPlanCreateRequest create = new()
        {
            Name = name, MaxActiveMemberships = capacity, Price = terms.Price, StartFee = terms.StartFee,
            BillingInterval = terms.BillingInterval, RenewalAnchor = terms.RenewalAnchor, CompanyScope = terms.CompanyScope,
            CompanyIds = terms.CompanyIds, Services = terms.Services, UsageLimits = terms.UsageLimits,
            MinimumCommitmentPeriods = terms.MinimumCommitmentPeriods, CancellationNoticeDays = terms.CancellationNoticeDays, Pause = terms.Pause
        };
        return await Plans(w).Create(w.OrganizationId, w.ActorUserId, create);
    }

    private static Task<ClientMembershipDto> Sell(SchedulingWorld w, Guid planId, DateOnly? startsOn = null, Guid? clientId = null, Guid? companyId = null) =>
        Memberships(w).Sell(w.OrganizationId, w.ActorUserId, clientId ?? w.Client.Id.Value, new ClientMembershipSellRequest
        {
            MembershipPlanId = planId, StartsOn = startsOn, SoldCompanyId = companyId ?? w.Company.Id.Value
        });

    private static async Task<string> Code(Func<Task> action) => (await Assert.ThrowsAnyAsync<Exception>(action)) switch
    {
        BusinessRuleException b => b.Code,
        ValidationAppException v => v.Code,
        Exception other => other.GetType().Name
    };

    #region Sale

    [Fact]
    public async Task Sell_StartsTheMembershipToday_WithTheLatestPlanVersionAsItsTerms()
    {
        await using SchedulingWorld w = await SchedulingWorld.Create(nameof(Sell_StartsTheMembershipToday_WithTheLatestPlanVersionAsItsTerms));
        MembershipPlanDto plan = await Plan(w, "Gold", Terms(new[] { w.Service }));

        ClientMembershipDto sold = await Sell(w, plan.Id);

        Assert.Equal((MembershipState.Active, Today, plan.LatestVersion.Id, MembershipSaleChannel.Staff),
            (sold.State, sold.StartsOn, sold.Terms.Id, sold.SoldVia));
        Assert.Equal((Today, Today.AddMonths(1).AddDays(-1)), (sold.CurrentPeriod.StartsOn, sold.CurrentPeriod.EndsOn));
        Assert.Equal(30, sold.PauseAllowance.RemainingDays);
        Assert.Null(sold.EndsOn);
        Assert.Single(await Memberships(w).GetByClient(w.OrganizationId, w.Client.Id.Value));
    }

    [Fact]
    public async Task Sell_AFutureStartIsScheduled_AndTheStartIsLimitedToOneMonthAhead()
    {
        await using SchedulingWorld w = await SchedulingWorld.Create(nameof(Sell_AFutureStartIsScheduled_AndTheStartIsLimitedToOneMonthAhead));
        MembershipPlanDto plan = await Plan(w, "Gold", Terms(new[] { w.Service }));

        Assert.Equal(ErrorCodes.MembershipStartDateOutOfRange, await Code(() => Sell(w, plan.Id, Today.AddMonths(1).AddDays(1))));
        Assert.Equal(ErrorCodes.MembershipStartDateOutOfRange, await Code(() => Sell(w, plan.Id, Today.AddDays(-1))));

        ClientMembershipDto scheduled = await Sell(w, plan.Id, Today.AddDays(10));
        Assert.Equal(MembershipState.Scheduled, scheduled.State);
        Assert.Equal(Today.AddDays(10), scheduled.CurrentPeriod.StartsOn);
    }

    [Fact]
    public async Task Sell_RejectsAnOverlappingMembership_ButAllowsOtherServicesAndAStartAfterTheScheduledEnd()
    {
        await using SchedulingWorld w = await SchedulingWorld.Create(nameof(Sell_RejectsAnOverlappingMembership_ButAllowsOtherServicesAndAStartAfterTheScheduledEnd));
        ServiceEntity pilates = await w.AddService(60, 30m, name: "Pilates");
        MembershipPlanDto yoga = await Plan(w, "Yoga", Terms(new[] { w.Service }));
        MembershipPlanDto yogaPlus = await Plan(w, "Yoga+", Terms(new[] { w.Service, pilates }));
        MembershipPlanDto pilatesOnly = await Plan(w, "Pilates", Terms(new[] { pilates }));

        ClientMembershipDto first = await Sell(w, yoga.Id);
        BusinessRuleException overlap = await Assert.ThrowsAsync<BusinessRuleException>(() => Sell(w, yogaPlus.Id));
        Assert.Equal(ErrorCodes.MembershipOverlappingCoverage, overlap.Code);

        await Sell(w, pilatesOnly.Id); // different services: allowed

        ClientMembershipDto cancelled = await Memberships(w).RequestCancellation(w.OrganizationId, w.ActorUserId, first.Id, new ClientMembershipCancelRequest());
        await Assert.ThrowsAsync<BusinessRuleException>(() => Sell(w, yoga.Id, cancelled.EndsOn.Value)); // still overlaps on the last day
        await Sell(w, yoga.Id, cancelled.EndsOn.Value.AddDays(1));
    }

    [Fact]
    public async Task Sell_RespectsPlanCapacityAndActivity_AndWarnsWhenSoldWhereThePlanIsNotValid()
    {
        await using SchedulingWorld w = await SchedulingWorld.Create(nameof(Sell_RespectsPlanCapacityAndActivity_AndWarnsWhenSoldWhereThePlanIsNotValid));
        Company branch = await w.AddCompany("Branch");
        MembershipPlanDto local = await Plan(w, "Local", Terms(new[] { w.Service }, companies: new[] { branch.Id.Value }), capacity: 1);

        ClientMembershipDto sold = await Sell(w, local.Id);
        WarningDto warning = Assert.Single(sold.Warnings);
        Assert.Equal(WarningCodes.MembershipPlanNotValidAtSaleCompany, warning.Code);

        Client other = await w.AddClient("Other");
        Assert.Equal(ErrorCodes.MembershipPlanFull, await Code(() => Sell(w, local.Id, clientId: other.Id)));

        await Plans(w).Deactivate(w.OrganizationId, w.ActorUserId, local.Id);
        Assert.Equal(ErrorCodes.MembershipPlanInactive, await Code(() => Sell(w, local.Id, clientId: other.Id)));
    }

    [Fact]
    public async Task Capacity_CountsScheduledPlanChangesToThePlan_AndAPlanChangeCannotBypassIt()
    {
        // Review 2B (#8): capacity = memberships on the plan at the change date + every scheduled change towards it.
        await using SchedulingWorld w = await SchedulingWorld.Create(nameof(Capacity_CountsScheduledPlanChangesToThePlan_AndAPlanChangeCannotBypassIt));
        MembershipPlanDto basic = await Plan(w, "Basic", Terms(new[] { w.Service }));
        MembershipPlanDto premium = await Plan(w, "Premium", Terms(new[] { w.Service }, price: 80m), capacity: 1);
        Client second = await w.AddClient("Second");
        ClientMembershipDto first = await Sell(w, basic.Id);
        ClientMembershipDto other = await Sell(w, basic.Id, clientId: second.Id);

        await Memberships(w).ChangePlan(w.OrganizationId, w.ActorUserId, first.Id, new ClientMembershipPlanChangeRequest { MembershipPlanId = premium.Id });

        // The scheduled arrival already takes the only seat: another change and a direct sale are both refused.
        Assert.Equal(ErrorCodes.MembershipPlanFull, await Code(() => Memberships(w).ChangePlan(w.OrganizationId, w.ActorUserId, other.Id,
            new ClientMembershipPlanChangeRequest { MembershipPlanId = premium.Id })));
        Client third = await w.AddClient("Third");
        Assert.Equal(ErrorCodes.MembershipPlanFull, await Code(() => Sell(w, premium.Id, clientId: third.Id)));

        // Withdrawing the first change frees the seat.
        await Memberships(w).WithdrawPlanChange(w.OrganizationId, w.ActorUserId, first.Id);
        await Memberships(w).ChangePlan(w.OrganizationId, w.ActorUserId, other.Id, new ClientMembershipPlanChangeRequest { MembershipPlanId = premium.Id });
    }

    [Fact]
    public async Task Capacity_ADepartureByTheChangeDate_FreesTheSeat()
    {
        await using SchedulingWorld w = await SchedulingWorld.Create(nameof(Capacity_ADepartureByTheChangeDate_FreesTheSeat));
        MembershipPlanDto basic = await Plan(w, "Basic", Terms(new[] { w.Service }));
        MembershipPlanDto premium = await Plan(w, "Premium", Terms(new[] { w.Service }, price: 80m), capacity: 1);
        Client second = await w.AddClient("Second");
        ClientMembershipDto leaving = await Sell(w, premium.Id);
        ClientMembershipDto arriving = await Sell(w, basic.Id, clientId: second.Id);

        // Both renew on the same day: the premium member leaves exactly when the basic member arrives.
        await Memberships(w).ChangePlan(w.OrganizationId, w.ActorUserId, leaving.Id, new ClientMembershipPlanChangeRequest { MembershipPlanId = basic.Id });
        ClientMembershipDto changed = await Memberships(w).ChangePlan(w.OrganizationId, w.ActorUserId, arriving.Id,
            new ClientMembershipPlanChangeRequest { MembershipPlanId = premium.Id });
        Assert.Equal(premium.Id, changed.PendingChange.MembershipPlanId);
    }

    [Fact]
    public async Task ShortChangeNotice_ReturnsAWarning()
    {
        await using SchedulingWorld w = await SchedulingWorld.Create(nameof(ShortChangeNotice_ReturnsAWarning));
        var settings = w.Resolve<Core.Interfaces.Organization.IOrganizationSettingsService>();

        var shortNotice = await settings.UpdateMembershipChangeNoticeDays(w.OrganizationId, w.ActorUserId,
            new Core.DTOs.Organization.OrganizationMembershipChangeNoticeUpdateRequest { Days = 7 });
        Assert.Equal(7, shortNotice.MembershipChangeNoticeDays);
        Assert.Equal(WarningCodes.MembershipChangeNoticeShort, Assert.Single(shortNotice.Warnings).Code);

        var enough = await settings.UpdateMembershipChangeNoticeDays(w.OrganizationId, w.ActorUserId,
            new Core.DTOs.Organization.OrganizationMembershipChangeNoticeUpdateRequest { Days = 14 });
        Assert.Empty(enough.Warnings);
        Assert.Equal(14, await settings.GetMembershipChangeNoticeDays(w.OrganizationId));
    }

    #endregion

    #region Cancellation and end

    [Fact]
    public async Task Cancellation_EndsWithTheCommitment_CancelsScheduledPauses_AndCanBeWithdrawn()
    {
        await using SchedulingWorld w = await SchedulingWorld.Create(nameof(Cancellation_EndsWithTheCommitment_CancelsScheduledPauses_AndCanBeWithdrawn));
        MembershipPlanDto plan = await Plan(w, "Gold", Terms(new[] { w.Service }, commitment: 3));
        ClientMembershipDto sold = await Sell(w, plan.Id);
        await Memberships(w).Pause(w.OrganizationId, w.ActorUserId, sold.Id, new ClientMembershipPauseRequest
        {
            StartsOn = Today.AddDays(5), EndsOn = Today.AddDays(7)
        });

        MembershipCancellationPreviewDto preview = await Memberships(w).PreviewCancellation(w.OrganizationId, sold.Id);
        Assert.Equal((Today.AddMonths(3).AddDays(-1), MembershipEndEffectiveReason.MinimumCommitment), (preview.EffectiveOn, preview.Reason));

        ClientMembershipDto cancelled = await Memberships(w).RequestCancellation(w.OrganizationId, w.ActorUserId, sold.Id,
            new ClientMembershipCancelRequest { Reason = "Seli se" });
        Assert.Equal((preview.EffectiveOn, MembershipEndReason.Cancelled, "Seli se"), (cancelled.EndsOn.Value, cancelled.EndReason.Value, cancelled.CancellationReason));
        Assert.Equal(WarningCodes.MembershipScheduledPauseCancelled, Assert.Single(cancelled.Warnings).Code);
        Assert.Equal(MembershipPauseCancellationReason.MembershipCancellation, Assert.Single(cancelled.Pauses).CancellationReason);

        // Pause and a second cancellation are refused while the end is scheduled.
        Assert.Equal(ErrorCodes.MembershipPauseNotAllowed, await Code(() => Memberships(w).Pause(w.OrganizationId, w.ActorUserId, sold.Id,
            new ClientMembershipPauseRequest { StartsOn = Today.AddDays(10), EndsOn = Today.AddDays(11) })));
        Assert.Equal(ErrorCodes.MembershipEndAlreadyScheduled, await Code(() =>
            Memberships(w).RequestCancellation(w.OrganizationId, w.ActorUserId, sold.Id, new ClientMembershipCancelRequest())));

        ClientMembershipDto withdrawn = await Memberships(w).WithdrawCancellation(w.OrganizationId, w.ActorUserId, sold.Id);
        Assert.Null(withdrawn.EndsOn);
        Assert.Equal(ErrorCodes.MembershipNoScheduledCancellation, await Code(() =>
            Memberships(w).WithdrawCancellation(w.OrganizationId, w.ActorUserId, sold.Id)));
    }

    [Fact]
    public async Task EndOverride_IsOnlyEarlierThanTheComputedEnd_AndNeverInThePast()
    {
        await using SchedulingWorld w = await SchedulingWorld.Create(nameof(EndOverride_IsOnlyEarlierThanTheComputedEnd_AndNeverInThePast));
        MembershipPlanDto plan = await Plan(w, "Gold", Terms(new[] { w.Service }, commitment: 6));
        ClientMembershipDto sold = await Sell(w, plan.Id);
        DateOnly computed = (await Memberships(w).PreviewCancellation(w.OrganizationId, sold.Id)).EffectiveOn;

        Assert.Equal(ErrorCodes.MembershipEndDateOutOfRange, await Code(() => Memberships(w).EndEarly(w.OrganizationId, w.ActorUserId, sold.Id,
            new ClientMembershipEndRequest { EndsOn = computed.AddDays(1), Reason = "x" })));
        Assert.Equal(ErrorCodes.MembershipEndDateOutOfRange, await Code(() => Memberships(w).EndEarly(w.OrganizationId, w.ActorUserId, sold.Id,
            new ClientMembershipEndRequest { EndsOn = Today.AddDays(-1), Reason = "x" })));

        ClientMembershipDto ended = await Memberships(w).EndEarly(w.OrganizationId, w.ActorUserId, sold.Id,
            new ClientMembershipEndRequest { EndsOn = Today.AddDays(14), Reason = "Ozljeda" });
        Assert.Equal((Today.AddDays(14), MembershipEndReason.EndOverride), (ended.EndsOn.Value, ended.EndReason.Value));
    }

    [Fact]
    public async Task VoidSale_VoidsTheMembership_AndFurtherCommandsAreRefused()
    {
        await using SchedulingWorld w = await SchedulingWorld.Create(nameof(VoidSale_VoidsTheMembership_AndFurtherCommandsAreRefused));
        MembershipPlanDto plan = await Plan(w, "Gold", Terms(new[] { w.Service }));
        ClientMembershipDto sold = await Sell(w, plan.Id);

        ClientMembershipDto voided = await Memberships(w).VoidSale(w.OrganizationId, w.ActorUserId, sold.Id, new ClientMembershipVoidRequest { Reason = "Greška" });
        Assert.Equal((MembershipState.Voided, "Greška"), (voided.State, voided.VoidReason));
        Assert.Equal(ErrorCodes.MembershipNotActive, await Code(() =>
            Memberships(w).RequestCancellation(w.OrganizationId, w.ActorUserId, sold.Id, new ClientMembershipCancelRequest())));

        // A voided membership no longer blocks a new sale of the same services.
        await Sell(w, plan.Id);

        await using DatabaseContext db = w.NewDb();
        Assert.Contains("SaleVoided", await db.ClientMembershipAuditLog.Where(a => a.ClientMembershipId == sold.Id).Select(a => a.ChangeType).ToListAsync());
    }

    #endregion

    #region Pauses

    [Fact]
    public async Task Pauses_UseTheRolling12MonthTotal_CanBeCancelledBeforeStart_AndExtendThePeriod()
    {
        await using SchedulingWorld w = await SchedulingWorld.Create(nameof(Pauses_UseTheRolling12MonthTotal_CanBeCancelledBeforeStart_AndExtendThePeriod));
        MembershipPlanDto plan = await Plan(w, "Gold", Terms(new[] { w.Service }, maxPauseDays: 10));
        ClientMembershipDto sold = await Sell(w, plan.Id);

        ClientMembershipDto paused = await Memberships(w).Pause(w.OrganizationId, w.ActorUserId, sold.Id,
            new ClientMembershipPauseRequest { StartsOn = Today.AddDays(3), EndsOn = Today.AddDays(9) }); // 7 days
        Assert.Equal(3, paused.PauseAllowance.RemainingDays);
        Assert.Equal(Today.AddMonths(1).AddDays(6), paused.CurrentPeriod.EndsOn); // period extended by 7 days

        Assert.Equal(ErrorCodes.MembershipPauseLimitExceeded, await Code(() => Memberships(w).Pause(w.OrganizationId, w.ActorUserId, sold.Id,
            new ClientMembershipPauseRequest { StartsOn = Today.AddDays(20), EndsOn = Today.AddDays(23) }))); // 7 + 4 > 10

        Guid pauseId = Assert.Single(paused.Pauses).Id;
        ClientMembershipDto cancelled = await Memberships(w).CancelPause(w.OrganizationId, w.ActorUserId, sold.Id, pauseId);
        Assert.Equal(10, cancelled.PauseAllowance.RemainingDays);
        Assert.Equal(MembershipPauseCancellationReason.Withdrawn, Assert.Single(cancelled.Pauses).CancellationReason);
    }

    [Fact]
    public async Task EarlyReturn_ShortensTheExtension_ToTheDaysActuallyPaused()
    {
        await using SchedulingWorld w = await SchedulingWorld.Create(nameof(EarlyReturn_ShortensTheExtension_ToTheDaysActuallyPaused));
        MembershipPlanDto plan = await Plan(w, "Gold", Terms(new[] { w.Service }));
        ClientMembershipDto sold = await Sell(w, plan.Id, Today.AddDays(1)); // scheduled, so a pause can have started "yesterday"

        // Seed a running pause directly (a pause can never start in the past through the service).
        await using (DatabaseContext db = w.NewDb())
        {
            DateOnly start = Today.AddDays(-10);
            await db.Database.ExecuteSqlInterpolatedAsync(
                $"UPDATE dunelight.client_memberships SET starts_on = {start}, terms_anchor_on = {start} WHERE id = {sold.Id}");
            await db.Database.ExecuteSqlInterpolatedAsync(
                $"UPDATE dunelight.membership_periods SET starts_on = {start}, ends_on = {start.AddMonths(1).AddDays(-1)} WHERE client_membership_id = {sold.Id}");
            db.MembershipPauses.Add(new MembershipPause
            {
                Id = Guid.NewGuid(), OrganizationId = w.OrganizationId, ClientMembershipId = sold.Id, Kind = MembershipPauseKind.Days,
                StartsOn = Today.AddDays(-2), PlannedEndsOn = Today.AddDays(7), CreatedAt = TestClock.UtcNow
            });
            await db.SaveChangesAsync();
        }

        ClientMembershipDto before = await Memberships(w).GetById(w.OrganizationId, sold.Id);
        Assert.Equal(MembershipState.Paused, before.State);
        Guid pauseId = Assert.Single(before.Pauses).Id;

        ClientMembershipPauseEndEarlyResultDto result = await Memberships(w).EndPauseEarly(w.OrganizationId, w.ActorUserId, sold.Id, pauseId,
            new ClientMembershipPauseEndEarlyRequest());
        Assert.True(result.Applied);
        Assert.Equal(MembershipState.Active, result.Membership.State);
        Assert.Equal(Today.AddDays(-1), Assert.Single(result.Membership.Pauses).ActualEndsOn);
        Assert.Equal(Today.AddDays(-10).AddMonths(1).AddDays(1), result.Membership.CurrentPeriod.EndsOn); // 2 days paused
    }

    #endregion

    #region Plan change and plan update

    [Fact]
    public async Task PlanChange_IsScheduledForTheNextRenewal_AndRespectsOverlapWithOtherMemberships()
    {
        await using SchedulingWorld w = await SchedulingWorld.Create(nameof(PlanChange_IsScheduledForTheNextRenewal_AndRespectsOverlapWithOtherMemberships));
        ServiceEntity pilates = await w.AddService(60, 30m, name: "Pilates");
        MembershipPlanDto yoga = await Plan(w, "Yoga", Terms(new[] { w.Service }));
        MembershipPlanDto both = await Plan(w, "Both", Terms(new[] { w.Service, pilates }));
        MembershipPlanDto pilatesOnly = await Plan(w, "Pilates", Terms(new[] { pilates }));
        ClientMembershipDto yogaMembership = await Sell(w, yoga.Id);
        await Sell(w, pilatesOnly.Id);

        // "Both" would overlap the Pilates membership from the next renewal on.
        Assert.Equal(ErrorCodes.MembershipOverlappingCoverage, await Code(() => Memberships(w).ChangePlan(w.OrganizationId, w.ActorUserId,
            yogaMembership.Id, new ClientMembershipPlanChangeRequest { MembershipPlanId = both.Id })));
        Assert.Equal(ErrorCodes.MembershipPlanChangeNotAllowed, await Code(() => Memberships(w).ChangePlan(w.OrganizationId, w.ActorUserId,
            yogaMembership.Id, new ClientMembershipPlanChangeRequest { MembershipPlanId = yoga.Id })));

        MembershipPlanDto yogaGold = await Plan(w, "Yoga Gold", Terms(new[] { w.Service }, price: 70m));
        ClientMembershipDto changed = await Memberships(w).ChangePlan(w.OrganizationId, w.ActorUserId, yogaMembership.Id,
            new ClientMembershipPlanChangeRequest { MembershipPlanId = yogaGold.Id });
        Assert.Equal((MembershipPendingChangeSource.ClientPlanChange, yogaGold.Id, Today.AddMonths(1)),
            (changed.PendingChange.Source, changed.PendingChange.MembershipPlanId, changed.PendingChange.EffectiveOn));
        Assert.Equal(yoga.LatestVersion.Id, changed.Terms.Id); // current terms never change retroactively

        ClientMembershipDto withdrawn = await Memberships(w).WithdrawPlanChange(w.OrganizationId, w.ActorUserId, yogaMembership.Id);
        Assert.Null(withdrawn.PendingChange);
    }

    [Fact]
    public async Task PlanUpdate_ForExistingMemberships_FavorableFromTheNextRenewal_MixedAfterTheNotice()
    {
        await using SchedulingWorld w = await SchedulingWorld.Create(nameof(PlanUpdate_ForExistingMemberships_FavorableFromTheNextRenewal_MixedAfterTheNotice));
        MembershipPlanDto plan = await Plan(w, "Gold", Terms(new[] { w.Service }, price: 50m));
        ClientMembershipDto sold = await Sell(w, plan.Id);

        MembershipPlanVersionPublishRequest cheaper = Terms(new[] { w.Service }, price: 45m);
        cheaper.ApplyTo = MembershipPlanApplyTo.NewSalesAndExisting;
        MembershipPlanVersionPublishResultDto favorable = await Plans(w).PublishVersion(w.OrganizationId, w.ActorUserId, plan.Id, cheaper);
        Assert.Equal(MembershipPlanChangeClassification.Favorable, favorable.Classification);
        MembershipPlanAffectedMembershipDto affected = Assert.Single(favorable.AffectedMemberships);
        Assert.Equal((sold.Id, Today.AddMonths(1), false), (affected.MembershipId, affected.EffectiveOn, affected.Skipped));

        // Default notice is 30 days: a price rise lands on the first renewal at least 30 days away.
        MembershipPlanVersionPublishRequest pricier = Terms(new[] { w.Service }, price: 60m);
        pricier.ApplyTo = MembershipPlanApplyTo.NewSalesAndExisting;
        MembershipPlanVersionPublishResultDto mixed = await Plans(w).PublishVersion(w.OrganizationId, w.ActorUserId, plan.Id, pricier);
        Assert.Equal(MembershipPlanChangeClassification.Mixed, mixed.Classification);
        Assert.Equal(nameof(MembershipPlanVersion.Price), Assert.Single(mixed.WorsenedDimensions));
        DateOnly expected = Today.AddMonths(1) >= Today.AddDays(30) ? Today.AddMonths(1) : Today.AddMonths(2);
        Assert.Equal(expected, Assert.Single(mixed.AffectedMemberships).EffectiveOn);

        ClientMembershipDto membership = await Memberships(w).GetById(w.OrganizationId, sold.Id);
        Assert.Equal((MembershipPendingChangeSource.PlanUpdate, 3), (membership.PendingChange.Source, membership.PendingChange.PlanVersion));
        Assert.Equal(1, membership.Terms.Version);
    }

    [Fact]
    public async Task PlanUpdate_DoesNotOverrideAClientPlanChange_AndNewSalesOnlyLeavesMembersAlone()
    {
        await using SchedulingWorld w = await SchedulingWorld.Create(nameof(PlanUpdate_DoesNotOverrideAClientPlanChange_AndNewSalesOnlyLeavesMembersAlone));
        MembershipPlanDto plan = await Plan(w, "Gold", Terms(new[] { w.Service }));
        MembershipPlanDto other = await Plan(w, "Silver", Terms(new[] { w.Service }, price: 30m));
        ClientMembershipDto sold = await Sell(w, plan.Id);

        MembershipPlanVersionPublishResultDto newSalesOnly = await Plans(w).PublishVersion(w.OrganizationId, w.ActorUserId, plan.Id, Terms(new[] { w.Service }, price: 40m));
        Assert.Empty(newSalesOnly.AffectedMemberships);

        await Memberships(w).ChangePlan(w.OrganizationId, w.ActorUserId, sold.Id, new ClientMembershipPlanChangeRequest { MembershipPlanId = other.Id });
        MembershipPlanVersionPublishRequest update = Terms(new[] { w.Service }, price: 35m);
        update.ApplyTo = MembershipPlanApplyTo.NewSalesAndExisting;
        MembershipPlanVersionPublishResultDto result = await Plans(w).PublishVersion(w.OrganizationId, w.ActorUserId, plan.Id, update);

        // The client's change keeps priority; the update is held (not applied) for the case the change is withdrawn.
        Assert.True(Assert.Single(result.AffectedMemberships).HeldByClientPlanChange);
        ClientMembershipDto membership = await Memberships(w).GetById(w.OrganizationId, sold.Id);
        Assert.Equal(MembershipPendingChangeSource.ClientPlanChange, membership.PendingChange.Source);
        Assert.Equal(3, membership.DisplacedPlanUpdate.PlanVersion);
    }

    [Fact]
    public async Task WithdrawingAPlanChange_RestoresTheDisplacedPlanUpdate_WithItsOriginalDate()
    {
        // Review 2B: scheduling and withdrawing a plan change must not let the client escape a price rise.
        await using SchedulingWorld w = await SchedulingWorld.Create(nameof(WithdrawingAPlanChange_RestoresTheDisplacedPlanUpdate_WithItsOriginalDate));
        MembershipPlanDto gold = await Plan(w, "Gold", Terms(new[] { w.Service }, price: 50m));
        MembershipPlanDto silver = await Plan(w, "Silver", Terms(new[] { w.Service }, price: 30m));
        ClientMembershipDto sold = await Sell(w, gold.Id);

        MembershipPlanVersionPublishRequest rise = Terms(new[] { w.Service }, price: 60m);
        rise.ApplyTo = MembershipPlanApplyTo.NewSalesAndExisting;
        DateOnly original = Assert.Single((await Plans(w).PublishVersion(w.OrganizationId, w.ActorUserId, gold.Id, rise)).AffectedMemberships).EffectiveOn;

        ClientMembershipDto changed = await Memberships(w).ChangePlan(w.OrganizationId, w.ActorUserId, sold.Id,
            new ClientMembershipPlanChangeRequest { MembershipPlanId = silver.Id });
        Assert.Equal(MembershipPendingChangeSource.ClientPlanChange, changed.PendingChange.Source);
        Assert.Equal((2, original), (changed.DisplacedPlanUpdate.PlanVersion, changed.DisplacedPlanUpdate.EffectiveOn));

        ClientMembershipDto withdrawn = await Memberships(w).WithdrawPlanChange(w.OrganizationId, w.ActorUserId, sold.Id);
        Assert.Equal((MembershipPendingChangeSource.PlanUpdate, 2, original),
            (withdrawn.PendingChange.Source, withdrawn.PendingChange.PlanVersion, withdrawn.PendingChange.EffectiveOn));
        Assert.Null(withdrawn.DisplacedPlanUpdate);
    }

    [Fact]
    public async Task WithdrawingAPlanChange_AfterTheOriginalRenewalPassed_AppliesTheUpdateAtTheNextRenewal()
    {
        await using SchedulingWorld w = await SchedulingWorld.Create(nameof(WithdrawingAPlanChange_AfterTheOriginalRenewalPassed_AppliesTheUpdateAtTheNextRenewal));
        MembershipPlanDto gold = await Plan(w, "Gold", Terms(new[] { w.Service }, price: 50m));
        MembershipPlanDto silver = await Plan(w, "Silver", Terms(new[] { w.Service }, price: 30m));
        ClientMembershipDto sold = await Sell(w, gold.Id);
        MembershipPlanVersionPublishRequest rise = Terms(new[] { w.Service }, price: 60m);
        rise.ApplyTo = MembershipPlanApplyTo.NewSalesAndExisting;
        await Plans(w).PublishVersion(w.OrganizationId, w.ActorUserId, gold.Id, rise);
        await Memberships(w).ChangePlan(w.OrganizationId, w.ActorUserId, sold.Id, new ClientMembershipPlanChangeRequest { MembershipPlanId = silver.Id });

        // Simulate that the original renewal (today = the membership's first period start) has already passed.
        await using (DatabaseContext db = w.NewDb())
            await db.Database.ExecuteSqlInterpolatedAsync($"UPDATE dunelight.client_memberships SET displaced_effective_on = {Today} WHERE id = {sold.Id}");

        ClientMembershipDto withdrawn = await Memberships(w).WithdrawPlanChange(w.OrganizationId, w.ActorUserId, sold.Id);
        Assert.Equal((MembershipPendingChangeSource.PlanUpdate, 2, Today.AddMonths(1)),
            (withdrawn.PendingChange.Source, withdrawn.PendingChange.PlanVersion, withdrawn.PendingChange.EffectiveOn));
    }

    [Fact]
    public async Task APlanUpdateSkippedForOverlap_LeavesAPersistentMark_UntilALaterUpdateSucceeds()
    {
        await using SchedulingWorld w = await SchedulingWorld.Create(nameof(APlanUpdateSkippedForOverlap_LeavesAPersistentMark_UntilALaterUpdateSucceeds));
        ServiceEntity pilates = await w.AddService(60, 30m, name: "Pilates");
        MembershipPlanDto yoga = await Plan(w, "Yoga", Terms(new[] { w.Service }));
        MembershipPlanDto pilatesOnly = await Plan(w, "Pilates", Terms(new[] { pilates }));
        ClientMembershipDto yogaMembership = await Sell(w, yoga.Id);
        await Sell(w, pilatesOnly.Id);

        MembershipPlanVersionPublishRequest addPilates = Terms(new[] { w.Service, pilates });
        addPilates.ApplyTo = MembershipPlanApplyTo.NewSalesAndExisting;
        MembershipPlanAffectedMembershipDto skipped = Assert.Single(
            (await Plans(w).PublishVersion(w.OrganizationId, w.ActorUserId, yoga.Id, addPilates)).AffectedMemberships);
        Assert.Equal((true, ErrorCodes.MembershipOverlappingCoverage), (skipped.Skipped, skipped.SkipReason));

        ClientMembershipDto marked = await Memberships(w).GetById(w.OrganizationId, yogaMembership.Id);
        Assert.Equal((2, ErrorCodes.MembershipOverlappingCoverage), (marked.PlanUpdateNotApplied.PlanVersion, marked.PlanUpdateNotApplied.Reason));
        Assert.Null(marked.PendingChange);
        Assert.Equal(yogaMembership.Id, Assert.Single(await Memberships(w).GetPlanUpdateNotApplied(w.OrganizationId, yoga.Id)).Id);
        Assert.NotNull(Assert.Single(await Memberships(w).GetByClient(w.OrganizationId, w.Client.Id.Value), m => m.Id == yogaMembership.Id).PlanUpdateNotApplied);

        // A later update without the overlap resolves the mark.
        MembershipPlanVersionPublishRequest yogaOnly = Terms(new[] { w.Service }, price: 45m);
        yogaOnly.ApplyTo = MembershipPlanApplyTo.NewSalesAndExisting;
        await Plans(w).PublishVersion(w.OrganizationId, w.ActorUserId, yoga.Id, yogaOnly);
        ClientMembershipDto resolved = await Memberships(w).GetById(w.OrganizationId, yogaMembership.Id);
        Assert.Null(resolved.PlanUpdateNotApplied);
        Assert.Equal(3, resolved.PendingChange.PlanVersion);
        Assert.Empty(await Memberships(w).GetPlanUpdateNotApplied(w.OrganizationId, yoga.Id));
    }

    #endregion
}
