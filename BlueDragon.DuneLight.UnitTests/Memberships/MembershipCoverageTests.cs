#nullable disable
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using BlueDragon.DuneLight.Core.DTOs.Appointments;
using BlueDragon.DuneLight.Core.DTOs.Catalog;
using BlueDragon.DuneLight.Core.DTOs.Checkouts;
using BlueDragon.DuneLight.Core.DTOs.Clients;
using BlueDragon.DuneLight.Core.DTOs.Groups;
using BlueDragon.DuneLight.Core.DTOs.Organization;
using BlueDragon.DuneLight.Core.Enums;
using BlueDragon.DuneLight.Core.Interfaces.Appointments;
using BlueDragon.DuneLight.Core.Interfaces.Catalog;
using BlueDragon.DuneLight.Core.Interfaces.Checkouts;
using BlueDragon.DuneLight.Core.Interfaces.Clients;
using BlueDragon.DuneLight.Core.Interfaces.Groups;
using BlueDragon.DuneLight.Core.Interfaces.Organization;
using BlueDragon.DuneLight.Core.Shared;
using BlueDragon.DuneLight.Core.Shared.Exceptions;
using BlueDragon.DuneLight.Infrastructure.Domain.Contexts;
using BlueDragon.DuneLight.Infrastructure.Domain.Models.Appointments;
using BlueDragon.DuneLight.Infrastructure.Domain.Models.Checkouts;
using BlueDragon.DuneLight.Infrastructure.Domain.Models.Clients;
using BlueDragon.DuneLight.Infrastructure.Services;
using BlueDragon.DuneLight.UnitTests.Scheduling;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using ServiceEntity = BlueDragon.DuneLight.Infrastructure.Domain.Models.Catalog.Service;

namespace BlueDragon.DuneLight.UnitTests.Memberships;

/// <summary>
/// P2 phase 2D — coverage of participations by a membership against PostgreSQL (UTC worlds; the membership starts today, so
/// sessions are booked a few days ahead): claim on booking with an explained decision, limits and the fallback / reject setting,
/// on-time cancellation releasing the credit and the freed slot covering the earliest uncovered booking, the P1 membership
/// action (ForfeitCredit with and without period credits, ReturnCreditChargeFee, waiver), already-paid sessions and their
/// re-evaluation after a voided payment, existing bookings at the sale, the horizon (current + next period), pause and debt
/// releasing future claims, removal and rescheduling, the group check-in precedence over packages, the void-sale guard, and
/// the race of two bookings for the last credit.
/// </summary>
public class MembershipCoverageTests
{
    private static IClientMembershipService Memberships(SchedulingWorld w) => w.Resolve<IClientMembershipService>();
    private static DateOnly Today => DateOnly.FromDateTime(DateTime.UtcNow);
    private static DateTimeOffset At(int days, int hour) => new DateTimeOffset(DateTime.UtcNow.Date, TimeSpan.Zero).AddDays(days).AddHours(hour);

    private static MembershipUsageLimitDto Limit(MembershipUsageWindow window, int max, Guid? serviceId = null) =>
        new() { Window = window, MaxUses = max, ServiceId = serviceId };

    private static Task<MembershipPlanDto> Plan(SchedulingWorld w, IEnumerable<ServiceEntity> services, params MembershipUsageLimitDto[] limits) =>
        w.Resolve<IMembershipPlanService>().Create(w.OrganizationId, w.ActorUserId, new MembershipPlanCreateRequest
        {
            Name = $"Plan-{Guid.NewGuid():N}", Price = 50m, BillingInterval = MembershipBillingInterval.Monthly,
            RenewalAnchor = MembershipRenewalAnchor.PurchaseDate, CompanyScope = MembershipCompanyScope.AllCompanies,
            Services = services.Select(s => new MembershipPlanCoveredServiceRequest { ServiceId = s.Id }).ToList(),
            UsageLimits = limits.ToList(),
            Pause = new MembershipPauseRulesDto { Allowed = true, MaxPauseDays = 30, ExtendsPeriod = true }
        });

    private static Task<MembershipPlanDto> Plan(SchedulingWorld w, params MembershipUsageLimitDto[] limits) => Plan(w, new[] { w.Service }, limits);

    private static Task<ClientMembershipDto> Sell(SchedulingWorld w, MembershipPlanDto plan, Client client = null) =>
        Memberships(w).Sell(w.OrganizationId, w.ActorUserId, (client ?? w.Client).Id.Value, new ClientMembershipSellRequest
        {
            MembershipPlanId = plan.Id, SoldCompanyId = w.Company.Id.Value
        });

    private static Task<AppointmentDto> Book(SchedulingWorld w, DateTimeOffset start, Client client = null) =>
        w.CreateAppointment(w.CreateRequest(start, client, overrideAvailability: true));

    private static async Task<BookingParticipationDto> Only(SchedulingWorld w, Guid appointmentId) =>
        (await w.Appointments.GetById(w.OrganizationId, appointmentId)).Bookings.Single().Participations.Single();

    private static async Task<List<MembershipUsage>> Usages(SchedulingWorld w)
    {
        await using DatabaseContext db = w.NewDb();
        return await db.Set<MembershipUsage>().AsNoTracking().Where(u => u.OrganizationId == w.OrganizationId).ToListAsync();
    }

    private static int LateWindow(DateTimeOffset start) => (int)(start - DateTimeOffset.UtcNow).TotalMinutes + 60;

    private static void AssertCoverage(BookingParticipationDto p, MembershipCoverageStatus status, MembershipCoverageReason reason) =>
        Assert.Equal((status, reason), (p.MembershipCoverage.Status, p.MembershipCoverage.Reason));

    #region Claim, limits, fallback and reject

    [Fact]
    public async Task Booking_IsCovered_WithAnExplainedDecision_RetailPriceKept_NothingDue_AndOneClaimInTheLedger()
    {
        await using SchedulingWorld w = await SchedulingWorld.Create(nameof(Booking_IsCovered_WithAnExplainedDecision_RetailPriceKept_NothingDue_AndOneClaimInTheLedger));
        ClientMembershipDto membership = await Sell(w, await Plan(w, Limit(MembershipUsageWindow.Period, 8)));

        AppointmentDto booked = await Book(w, At(2, 10));

        BookingParticipationDto p = await Only(w, booked.Id);
        AssertCoverage(p, MembershipCoverageStatus.Covered, MembershipCoverageReason.Claimed);
        Assert.Equal((membership.Id, MembershipCoverageEvent.Booking), (p.MembershipCoverage.ClientMembershipId.Value, p.MembershipCoverage.ChangedByEvent));
        Assert.Equal((50m, 0m, 0m, false), (p.Amount, p.MonetaryDue, p.OutstandingAmount, p.PackageCovered)); // Q9: retail price, no due
        MembershipUsage claim = Assert.Single(await Usages(w));
        Assert.Equal((MembershipUsageEntryType.Claim, (short)-1, true, p.Id), (claim.EntryType, claim.Units, claim.IsActive, claim.ParticipationId));

        // The covered session cannot be charged.
        CheckoutDto checkout = await w.Checkouts.Create(w.OrganizationId, w.ActorUserId, new CheckoutCreateRequest { ClientId = w.Client.Id.Value, CompanyId = w.Company.Id.Value });
        await SchedulingAssert.BusinessRule(ErrorCodes.ParticipationCoveredByMembership, () => w.Checkouts.AddBookingItem(
            w.OrganizationId, w.ActorUserId, checkout.Id, new CheckoutAddBookingItemRequest { ParticipationId = p.Id }));
    }

    [Fact]
    public async Task ExhaustedLimit_FallsBackToNormalSettlement_AndSaysWhichLimitIsFull()
    {
        await using SchedulingWorld w = await SchedulingWorld.Create(nameof(ExhaustedLimit_FallsBackToNormalSettlement_AndSaysWhichLimitIsFull));
        await Sell(w, await Plan(w, Limit(MembershipUsageWindow.Day, 1)));

        await Book(w, At(2, 9));
        AppointmentDto second = await Book(w, At(2, 14));

        BookingParticipationDto p = await Only(w, second.Id);
        AssertCoverage(p, MembershipCoverageStatus.NotCovered, MembershipCoverageReason.LimitReached);
        Assert.Equal((MembershipUsageWindow.Day, (Guid?)null, 1, 1),
            (p.MembershipCoverage.LimitWindow.Value, p.MembershipCoverage.LimitServiceId, p.MembershipCoverage.LimitMaxUses.Value, p.MembershipCoverage.LimitUsed.Value));
        Assert.Equal(50m, p.MonetaryDue); // next source: normal settlement
        Assert.Single(await Usages(w));
    }

    [Fact]
    public async Task RejectSetting_RejectsTheBookingThatExceedsTheLimit_AndNothingIsCreated()
    {
        await using SchedulingWorld w = await SchedulingWorld.Create(nameof(RejectSetting_RejectsTheBookingThatExceedsTheLimit_AndNothingIsCreated));
        await w.Resolve<IOrganizationSettingsService>().UpdateMembershipCoverageRules(w.OrganizationId, w.ActorUserId,
            new OrganizationMembershipCoverageUpdateRequest { LimitExceededBehavior = MembershipLimitExceededBehavior.Reject });
        await Sell(w, await Plan(w, Limit(MembershipUsageWindow.Day, 1)));
        await Book(w, At(2, 9));

        BusinessRuleException ex = await Assert.ThrowsAsync<BusinessRuleException>(() => Book(w, At(2, 14)));

        Assert.Equal(ErrorCodes.MembershipLimitExceeded, ex.Code);
        await using DatabaseContext db = w.NewDb();
        Assert.Equal(1, await db.Appointments.CountAsync(a => a.OrganizationId == w.OrganizationId));
    }

    #endregion

    #region Cancellation, freed slot and the P1 membership action

    [Fact]
    public async Task OnTimeCancellation_ReturnsTheCredit_AndTheFreedSlotCoversTheEarliestUncoveredBooking()
    {
        await using SchedulingWorld w = await SchedulingWorld.Create(nameof(OnTimeCancellation_ReturnsTheCredit_AndTheFreedSlotCoversTheEarliestUncoveredBooking));
        await Sell(w, await Plan(w, Limit(MembershipUsageWindow.Period, 1)));
        AppointmentDto first = await Book(w, At(2, 10));
        AppointmentDto third = await Book(w, At(4, 10));
        AppointmentDto second = await Book(w, At(3, 10));
        AssertCoverage(await Only(w, second.Id), MembershipCoverageStatus.NotCovered, MembershipCoverageReason.LimitReached);

        await w.SetBookingStatus(first.Id, w.Client, BookingStatus.Cancelled, "on time"); // neutral policy: 24 h window, start is days away

        AssertCoverage(await Only(w, first.Id), MembershipCoverageStatus.Released, MembershipCoverageReason.CancelledOnTime);
        BookingParticipationDto earliest = await Only(w, second.Id);
        AssertCoverage(earliest, MembershipCoverageStatus.Covered, MembershipCoverageReason.Claimed);
        Assert.Equal((MembershipCoverageEvent.SlotFreed, 0m), (earliest.MembershipCoverage.ChangedByEvent, earliest.MonetaryDue));
        AssertCoverage(await Only(w, third.Id), MembershipCoverageStatus.NotCovered, MembershipCoverageReason.LimitReached);

        List<MembershipUsage> usages = await Usages(w);
        MembershipUsage release = Assert.Single(usages, u => u.EntryType == MembershipUsageEntryType.Release);
        Assert.Equal(((short)1, MembershipUsageReleaseReason.CancelledOnTime), (release.Units, release.ReleaseReason.Value));
        Assert.Equal(release.ReversesUsageId, usages.Single(u => u.EntryType == MembershipUsageEntryType.Claim && !u.IsActive).Id);
        Assert.Equal(1, usages.Count(u => u.IsActive));
    }

    [Fact]
    public async Task LateCancellation_WithPeriodCredits_ForfeitsTheCreditInsteadOfTheFee_AndAWaiverReturnsIt()
    {
        await using SchedulingWorld w = await SchedulingWorld.Create(nameof(LateCancellation_WithPeriodCredits_ForfeitsTheCreditInsteadOfTheFee_AndAWaiverReturnsIt));
        await w.GrantUser(w.ActorUserId, Grants.AppointmentsPolicyOverride, Grants.AppointmentsWriteAll);
        await Sell(w, await Plan(w, Limit(MembershipUsageWindow.Period, 1)));
        DateTimeOffset start = At(2, 10);
        await w.PublishDefaultPolicyVersion(LateWindow(start), CancellationFeeType.Percentage, 40m);
        AppointmentDto booked = await Book(w, start);
        AppointmentDto waiting = await Book(w, At(3, 10));

        await w.SetBookingStatus(booked.Id, w.Client, BookingStatus.Cancelled, "late");

        BookingParticipationDto p = await Only(w, booked.Id);
        AssertCoverage(p, MembershipCoverageStatus.Covered, MembershipCoverageReason.CreditForfeited);
        Assert.Equal((CancellationMembershipAction.ForfeitCredit, true, 20m), (p.PolicyConsequence.MembershipAction.Value,
            p.PolicyConsequence.MembershipCreditForfeited, p.PolicyConsequence.CalculatedFeeAmount));
        Assert.Equal(0m, p.MonetaryDue); // the credit replaces the fee (Q26)
        AssertCoverage(await Only(w, waiting.Id), MembershipCoverageStatus.NotCovered, MembershipCoverageReason.LimitReached); // no slot freed

        await w.Bookings.WaivePolicyConsequence(w.OrganizationId, w.ActorUserId, p.Id, new PolicyConsequenceWaiveRequest { WaiverReason = "goodwill" });

        AssertCoverage(await Only(w, booked.Id), MembershipCoverageStatus.Released, MembershipCoverageReason.PolicyWaived);
        BookingParticipationDto freed = await Only(w, waiting.Id);
        AssertCoverage(freed, MembershipCoverageStatus.Covered, MembershipCoverageReason.Claimed);
        Assert.Equal(MembershipCoverageEvent.SlotFreed, freed.MembershipCoverage.ChangedByEvent);
    }

    [Fact]
    public async Task LateCancellation_OnAPlanWithoutPeriodCredits_ChargesTheFee_AndTheWindowSlotStaysUsed()
    {
        await using SchedulingWorld w = await SchedulingWorld.Create(nameof(LateCancellation_OnAPlanWithoutPeriodCredits_ChargesTheFee_AndTheWindowSlotStaysUsed));
        await Sell(w, await Plan(w, Limit(MembershipUsageWindow.Day, 1)));
        DateTimeOffset start = At(2, 10);
        await w.PublishDefaultPolicyVersion(LateWindow(start), CancellationFeeType.Percentage, 40m);
        AppointmentDto booked = await Book(w, start);

        await w.SetBookingStatus(booked.Id, w.Client, BookingStatus.Cancelled, "late");

        BookingParticipationDto p = await Only(w, booked.Id);
        AssertCoverage(p, MembershipCoverageStatus.Covered, MembershipCoverageReason.SlotUsedFeeCharged);
        Assert.Equal((false, 20m), (p.PolicyConsequence.MembershipCreditForfeited, p.MonetaryDue));
        AppointmentDto sameDay = await Book(w, At(2, 15));
        AssertCoverage(await Only(w, sameDay.Id), MembershipCoverageStatus.NotCovered, MembershipCoverageReason.LimitReached);
    }

    [Fact]
    public async Task ReturnCreditChargeFee_ReleasesTheClaim_AndChargesTheFee()
    {
        await using SchedulingWorld w = await SchedulingWorld.Create(nameof(ReturnCreditChargeFee_ReleasesTheClaim_AndChargesTheFee));
        await Sell(w, await Plan(w, Limit(MembershipUsageWindow.Period, 4)));
        DateTimeOffset start = At(2, 10);
        await w.Resolve<ICancellationPolicyService>().PublishVersion(w.OrganizationId, w.ActorUserId, w.DefaultPolicyId, new CancellationPolicyRulesRequest
        {
            CancellationWindowMinutes = LateWindow(start),
            LateCancellation = new CancellationPolicyEventRuleDto
            {
                FeeType = CancellationFeeType.Percentage, FeeValue = 40m, PackageAction = CancellationPackageAction.None,
                MembershipAction = CancellationMembershipAction.ReturnCreditChargeFee
            },
            NoShow = new CancellationPolicyEventRuleDto { FeeType = CancellationFeeType.None, PackageAction = CancellationPackageAction.None }
        });
        AppointmentDto booked = await Book(w, start);

        await w.SetBookingStatus(booked.Id, w.Client, BookingStatus.Cancelled, "late");

        BookingParticipationDto p = await Only(w, booked.Id);
        AssertCoverage(p, MembershipCoverageStatus.Released, MembershipCoverageReason.CreditReturnedWithFee);
        Assert.Equal((CancellationMembershipAction.ReturnCreditChargeFee, false, 20m),
            (p.PolicyConsequence.MembershipAction.Value, p.PolicyConsequence.MembershipCreditForfeited, p.MonetaryDue));
        Assert.Equal(MembershipUsageReleaseReason.CreditReturnedWithFee, Assert.Single(await Usages(w), u => u.EntryType == MembershipUsageEntryType.Release).ReleaseReason);
    }

    #endregion

    #region Sale, already paid, horizon

    [Fact]
    public async Task Sale_CoversExistingFutureBookingsInTimeOrder_ButNotAlreadyPaidOnes_AndAVoidedPaymentReEvaluates()
    {
        await using SchedulingWorld w = await SchedulingWorld.Create(nameof(Sale_CoversExistingFutureBookingsInTimeOrder_ButNotAlreadyPaidOnes_AndAVoidedPaymentReEvaluates));
        AppointmentDto paid = await Book(w, At(2, 10));
        AppointmentDto later = await Book(w, At(5, 10));
        AppointmentDto earlier = await Book(w, At(3, 10));
        Guid checkoutId = await w.PayBookingViaCheckout(paid.Bookings.Single().Id, w.Client, 50m);
        Assert.Null((await Only(w, earlier.Id)).MembershipCoverage); // before the sale: no membership, nothing written

        await Sell(w, await Plan(w, Limit(MembershipUsageWindow.Period, 1)));

        AssertCoverage(await Only(w, paid.Id), MembershipCoverageStatus.NotCovered, MembershipCoverageReason.AlreadyPaid);
        BookingParticipationDto covered = await Only(w, earlier.Id);
        AssertCoverage(covered, MembershipCoverageStatus.Covered, MembershipCoverageReason.Claimed);
        Assert.Equal(MembershipCoverageEvent.MembershipSale, covered.MembershipCoverage.ChangedByEvent);
        AssertCoverage(await Only(w, later.Id), MembershipCoverageStatus.NotCovered, MembershipCoverageReason.LimitReached);

        // Voiding the money (checkout still open) puts the session back into evaluation — no slot left, so the limit explains it.
        Checkout checkout = await w.LoadCheckout(checkoutId);
        await w.Checkouts.VoidPayment(w.OrganizationId, w.ActorUserId, checkoutId, checkout.Payments.Single().Id.Value,
            new CheckoutPaymentVoidRequest { Reason = "moved to membership" });
        BookingParticipationDto reEvaluated = await Only(w, paid.Id);
        AssertCoverage(reEvaluated, MembershipCoverageStatus.NotCovered, MembershipCoverageReason.LimitReached);
        Assert.Equal(MembershipCoverageEvent.PaymentChanged, reEvaluated.MembershipCoverage.ChangedByEvent);
    }

    [Fact]
    public async Task BookingBeyondTheHorizon_WaitsForEvaluation_WithoutDebt_AndIsCoveredWhenItsPeriodEntersTheHorizon()
    {
        await using SchedulingWorld w = await SchedulingWorld.Create(nameof(BookingBeyondTheHorizon_WaitsForEvaluation_WithoutDebt_AndIsCoveredWhenItsPeriodEntersTheHorizon));
        // The simulated renewal day is a month ahead, when the unpaid first charge would already be debt — keep covering here.
        await w.Resolve<IOrganizationSettingsService>().UpdateMembershipDebtRules(w.OrganizationId, w.ActorUserId,
            new OrganizationMembershipDebtUpdateRequest { GraceDays = 7, DebtBehavior = MembershipDebtBehavior.KeepCovering });
        await Sell(w, await Plan(w, Limit(MembershipUsageWindow.Period, 4)));
        DateOnly sessionDay = Today.AddMonths(2).AddDays(3); // third period: beyond current + next
        AppointmentDto far = await Book(w, new DateTimeOffset(sessionDay.ToDateTime(new TimeOnly(10, 0)), TimeSpan.Zero));

        BookingParticipationDto p = await Only(w, far.Id);
        AssertCoverage(p, MembershipCoverageStatus.PendingEvaluation, MembershipCoverageReason.BeyondHorizon);
        Assert.Equal((Today.AddMonths(2), 0m), (p.MembershipCoverage.ExpectedPeriodStartsOn.Value, p.MonetaryDue));
        CheckoutDto checkout = await w.Checkouts.Create(w.OrganizationId, w.ActorUserId, new CheckoutCreateRequest { ClientId = w.Client.Id.Value, CompanyId = w.Company.Id.Value });
        await SchedulingAssert.BusinessRule(ErrorCodes.MembershipCoveragePending, () => w.Checkouts.AddBookingItem(
            w.OrganizationId, w.ActorUserId, checkout.Id, new CheckoutAddBookingItemRequest { ParticipationId = p.Id }));

        await w.Resolve<IMembershipRenewalService>().RunForOrganization(w.OrganizationId, Today.AddMonths(1));

        BookingParticipationDto evaluated = await Only(w, far.Id);
        AssertCoverage(evaluated, MembershipCoverageStatus.Covered, MembershipCoverageReason.Claimed);
        Assert.Equal(MembershipCoverageEvent.Renewal, evaluated.MembershipCoverage.ChangedByEvent);
    }

    #endregion

    #region Pause, debt

    [Fact]
    public async Task Pause_ReleasesTheClaimsOfSessionsInsideIt_WithoutCancellingThem()
    {
        await using SchedulingWorld w = await SchedulingWorld.Create(nameof(Pause_ReleasesTheClaimsOfSessionsInsideIt_WithoutCancellingThem));
        ClientMembershipDto membership = await Sell(w, await Plan(w, Limit(MembershipUsageWindow.Period, 4)));
        AppointmentDto inPause = await Book(w, At(5, 10));
        AppointmentDto outside = await Book(w, At(9, 10));

        await Memberships(w).Pause(w.OrganizationId, w.ActorUserId, membership.Id, new ClientMembershipPauseRequest
        {
            StartsOn = Today.AddDays(3), EndsOn = Today.AddDays(7)
        });

        BookingParticipationDto paused = await Only(w, inPause.Id);
        AssertCoverage(paused, MembershipCoverageStatus.NotCovered, MembershipCoverageReason.Paused);
        Assert.Equal((BookingStatus.Confirmed, 50m, MembershipCoverageEvent.MembershipChanged),
            (paused.Status, paused.MonetaryDue, paused.MembershipCoverage.ChangedByEvent));
        AssertCoverage(await Only(w, outside.Id), MembershipCoverageStatus.Covered, MembershipCoverageReason.Claimed);
        Assert.Equal(MembershipUsageReleaseReason.Paused, Assert.Single(await Usages(w), u => u.EntryType == MembershipUsageEntryType.Release).ReleaseReason);
    }

    [Fact]
    public async Task DebtAfterGrace_StopsCoveringFutureSessions_AndPayingTheChargeRestoresCoverage()
    {
        await using SchedulingWorld w = await SchedulingWorld.Create(nameof(DebtAfterGrace_StopsCoveringFutureSessions_AndPayingTheChargeRestoresCoverage));
        await w.Resolve<IOrganizationSettingsService>().UpdateMembershipDebtRules(w.OrganizationId, w.ActorUserId,
            new OrganizationMembershipDebtUpdateRequest { GraceDays = 0, DebtBehavior = MembershipDebtBehavior.StopCovering });
        ClientMembershipDto membership = await Sell(w, await Plan(w, Limit(MembershipUsageWindow.Period, 4)));
        AppointmentDto booked = await Book(w, At(4, 10));
        AssertCoverage(await Only(w, booked.Id), MembershipCoverageStatus.Covered, MembershipCoverageReason.Claimed);

        // The daily run two days later: the first charge is overdue after the (zero) grace period.
        await w.Resolve<IMembershipRenewalService>().RunForOrganization(w.OrganizationId, Today.AddDays(2));
        BookingParticipationDto uncovered = await Only(w, booked.Id);
        AssertCoverage(uncovered, MembershipCoverageStatus.NotCovered, MembershipCoverageReason.DebtNotCovered);
        Assert.Equal((BookingStatus.Confirmed, 50m), (uncovered.Status, uncovered.MonetaryDue));

        MembershipChargeDto charge = Assert.Single(await Memberships(w).GetCharges(w.OrganizationId, membership.Id));
        CheckoutDto checkout = await w.Checkouts.Create(w.OrganizationId, w.ActorUserId, new CheckoutCreateRequest { ClientId = w.Client.Id.Value, CompanyId = w.Company.Id.Value });
        await w.Checkouts.AddMembershipChargeItem(w.OrganizationId, w.ActorUserId, checkout.Id, new CheckoutAddMembershipChargeItemRequest { MembershipChargeId = charge.Id });
        await w.Checkouts.RecordPayment(w.OrganizationId, w.ActorUserId, checkout.Id, new CheckoutPaymentCreateRequest { Amount = 50m, Method = PaymentMethod.Cash });

        BookingParticipationDto restored = await Only(w, booked.Id);
        AssertCoverage(restored, MembershipCoverageStatus.Covered, MembershipCoverageReason.Claimed);
        Assert.Equal(MembershipCoverageEvent.DebtChanged, restored.MembershipCoverage.ChangedByEvent);
    }

    #endregion

    #region Removal, rescheduling, group check-in, void

    [Fact]
    public async Task DeletingAnUntouchedBooking_ReturnsItsCredit_AndTheFreedSlotCoversTheNextBooking()
    {
        await using SchedulingWorld w = await SchedulingWorld.Create(nameof(DeletingAnUntouchedBooking_ReturnsItsCredit_AndTheFreedSlotCoversTheNextBooking));
        await Sell(w, await Plan(w, Limit(MembershipUsageWindow.Period, 1)));
        AppointmentDto first = await Book(w, At(2, 10));
        AppointmentDto second = await Book(w, At(3, 10));
        Guid firstParticipation = (await Only(w, first.Id)).Id;

        await w.Appointments.Delete(w.OrganizationId, w.ActorUserId, first.Id);

        AssertCoverage(await Only(w, second.Id), MembershipCoverageStatus.Covered, MembershipCoverageReason.Claimed);
        List<MembershipUsage> usages = await Usages(w);
        Assert.Equal(MembershipUsageReleaseReason.ParticipationRemoved,
            Assert.Single(usages, u => u.EntryType == MembershipUsageEntryType.Release && u.ParticipationId == firstParticipation).ReleaseReason);
        await using DatabaseContext db = w.NewDb();
        Assert.False(await db.Set<ParticipationMembershipCoverage>().AnyAsync(c => c.ParticipationId == firstParticipation));
    }

    [Fact]
    public async Task Rescheduling_ReEvaluatesOnTheNewDay_AndTheSlotFreedOnTheOldDayCoversAnotherBooking()
    {
        await using SchedulingWorld w = await SchedulingWorld.Create(nameof(Rescheduling_ReEvaluatesOnTheNewDay_AndTheSlotFreedOnTheOldDayCoversAnotherBooking));
        await Sell(w, await Plan(w, Limit(MembershipUsageWindow.Day, 1)));
        AppointmentDto dayTwo = await Book(w, At(2, 10));
        AppointmentDto moved = await Book(w, At(3, 10));
        AppointmentDto waiting = await Book(w, At(3, 15));
        AssertCoverage(await Only(w, waiting.Id), MembershipCoverageStatus.NotCovered, MembershipCoverageReason.LimitReached);

        await w.MoveOnlySegment(moved.Id, At(2, 15));

        BookingParticipationDto movedP = await Only(w, moved.Id);
        AssertCoverage(movedP, MembershipCoverageStatus.NotCovered, MembershipCoverageReason.LimitReached);
        Assert.Equal(MembershipCoverageEvent.Rescheduled, movedP.MembershipCoverage.ChangedByEvent);
        AssertCoverage(await Only(w, dayTwo.Id), MembershipCoverageStatus.Covered, MembershipCoverageReason.Claimed);
        BookingParticipationDto freed = await Only(w, waiting.Id);
        AssertCoverage(freed, MembershipCoverageStatus.Covered, MembershipCoverageReason.Claimed);
        Assert.Equal(MembershipCoverageEvent.SlotFreed, freed.MembershipCoverage.ChangedByEvent);
    }

    [Fact]
    public async Task GroupCheckIn_OfACoveredMember_TakesNoPackageAndNoMoney_AndAnExplicitPackageIsRejected()
    {
        await using SchedulingWorld w = await SchedulingWorld.Create(nameof(GroupCheckIn_OfACoveredMember_TakesNoPackageAndNoMoney_AndAnExplicitPackageIsRejected));
        ServiceEntity groupService = await w.AddGroupService(price: 15m);
        await Sell(w, await Plan(w, new[] { groupService }, Limit(MembershipUsageWindow.Period, 4)));
        ClientPackage package = await w.AddClientPackage(w.Client, groupService, 5, Today.AddYears(1));
        DateTimeOffset day = At(2, 0);
        GroupDto group = await w.CreateGroup(groupService, capacity: 5, slots: (day.DayOfWeek, TimeSpan.FromHours(10)));
        await w.AddGroupMember(group, w.Client);
        GenerateGroupAppointmentsResult generated = await w.GenerateOccurrences(group, day, overrideAvailability: true);
        Guid occurrenceId = Assert.Single(generated.Created).Id;
        BookingParticipationDto member = await Only(w, occurrenceId);
        AssertCoverage(member, MembershipCoverageStatus.Covered, MembershipCoverageReason.Claimed);

        await SchedulingAssert.BusinessRule(ErrorCodes.ParticipationCoveredByMembership, () => w.Bookings.SetParticipationStatus(
            w.OrganizationId, w.ActorUserId, true, member.Id,
            new BookingSetStatusRequest { Status = BookingStatus.Completed, ClientPackageId = package.Id }));

        await w.Bookings.SetParticipationStatus(w.OrganizationId, w.ActorUserId, true, member.Id,
            new BookingSetStatusRequest { Status = BookingStatus.Completed, PaymentMethod = PaymentMethod.Cash, IsPaid = true });

        BookingParticipationDto checkedIn = await Only(w, occurrenceId);
        Assert.Equal((BookingStatus.Completed, 15m, 0m, 0m, false), (checkedIn.Status, checkedIn.Amount, checkedIn.MonetaryDue, checkedIn.PaidAmount, checkedIn.PackageCovered));
        AssertCoverage(checkedIn, MembershipCoverageStatus.Covered, MembershipCoverageReason.Claimed);
        Assert.Equal(5, (await w.LoadClientPackage(package.Id.Value)).ServiceEntries.Single().RemainingEntries);
    }

    [Fact]
    public async Task VoidSale_ReturnsTheClaimsOfFutureSessions_TheyBecomeNormallyCharged_AndAreListedForReception()
    {
        await using SchedulingWorld w = await SchedulingWorld.Create(nameof(VoidSale_ReturnsTheClaimsOfFutureSessions_TheyBecomeNormallyCharged_AndAreListedForReception));
        ClientMembershipDto membership = await Sell(w, await Plan(w, Limit(MembershipUsageWindow.Period, 4)));
        AppointmentDto booked = await Book(w, At(2, 10));
        AssertCoverage(await Only(w, booked.Id), MembershipCoverageStatus.Covered, MembershipCoverageReason.Claimed);

        ClientMembershipDto voided = await Memberships(w).VoidSale(w.OrganizationId, w.ActorUserId, membership.Id,
            new ClientMembershipVoidRequest { Reason = "sold by mistake" });

        BookingParticipationDto p = await Only(w, booked.Id);
        AssertCoverage(p, MembershipCoverageStatus.NotCovered, MembershipCoverageReason.MembershipVoided);
        Assert.Equal((BookingStatus.Confirmed, 50m), (p.Status, p.MonetaryDue));
        WarningDto warning = Assert.Single(voided.Warnings, x => x.Code == WarningCodes.MembershipVoidedSessionsUncovered);
        WarningMembershipSession session = Assert.Single(Assert.IsType<WarningMembershipSessionsDetails>(warning.Details).Sessions);
        Assert.Equal((p.Id, booked.Id), (session.ParticipationId, session.AppointmentId));
        Assert.Equal(MembershipUsageReleaseReason.MembershipVoided,
            Assert.Single(await Usages(w), u => u.EntryType == MembershipUsageEntryType.Release).ReleaseReason);
    }

    [Fact]
    public async Task VoidSale_IsRefusedOnceTheMembershipWasActuallyUsed_AForfeitedCredit()
    {
        await using SchedulingWorld w = await SchedulingWorld.Create(nameof(VoidSale_IsRefusedOnceTheMembershipWasActuallyUsed_AForfeitedCredit));
        ClientMembershipDto membership = await Sell(w, await Plan(w, Limit(MembershipUsageWindow.Period, 4)));
        DateTimeOffset start = At(2, 10);
        await w.PublishDefaultPolicyVersion(LateWindow(start), CancellationFeeType.Percentage, 40m);
        AppointmentDto booked = await Book(w, start);
        await w.SetBookingStatus(booked.Id, w.Client, BookingStatus.Cancelled, "late"); // credit forfeited

        await SchedulingAssert.BusinessRule(ErrorCodes.MembershipSaleHasUsage, () => Memberships(w).VoidSale(
            w.OrganizationId, w.ActorUserId, membership.Id, new ClientMembershipVoidRequest { Reason = "mistake" }));
    }

    #endregion

    #region Debt with "block booking" (Q15.4, Q18, Q53, Q54)

    /// <summary>Setup: the membership's first charge became due days ago, so with grace 0 it is delinquent today.</summary>
    private static async Task<ClientMembershipDto> DelinquentUnderBlockBooking(SchedulingWorld w, MembershipPlanDto plan)
    {
        await w.Resolve<IOrganizationSettingsService>().UpdateMembershipDebtRules(w.OrganizationId, w.ActorUserId,
            new OrganizationMembershipDebtUpdateRequest { GraceDays = 0, DebtBehavior = MembershipDebtBehavior.BlockBooking });
        ClientMembershipDto membership = await Sell(w, plan);
        await using DatabaseContext db = w.NewDb();
        await db.Database.ExecuteSqlRawAsync(
            "UPDATE dunelight.membership_charges SET due_on = {0} WHERE client_membership_id = {1}", Today.AddDays(-3), membership.Id);
        return membership;
    }

    private static async Task PayAllCharges(SchedulingWorld w, ClientMembershipDto membership)
    {
        MembershipChargeDto charge = Assert.Single(await Memberships(w).GetCharges(w.OrganizationId, membership.Id));
        CheckoutDto checkout = await w.Checkouts.Create(w.OrganizationId, w.ActorUserId, new CheckoutCreateRequest { ClientId = w.Client.Id.Value, CompanyId = w.Company.Id.Value });
        await w.Checkouts.AddMembershipChargeItem(w.OrganizationId, w.ActorUserId, checkout.Id, new CheckoutAddMembershipChargeItemRequest { MembershipChargeId = charge.Id });
        await w.Checkouts.RecordPayment(w.OrganizationId, w.ActorUserId, checkout.Id, new CheckoutPaymentCreateRequest { Amount = charge.Amount, Method = PaymentMethod.Cash });
    }

    [Fact]
    public async Task BlockBooking_RefusesABookingTheDelinquentMembershipWouldCover_UnlessTheOverrideGrantAllowsItUncovered()
    {
        await using SchedulingWorld w = await SchedulingWorld.Create(nameof(BlockBooking_RefusesABookingTheDelinquentMembershipWouldCover_UnlessTheOverrideGrantAllowsItUncovered));
        await DelinquentUnderBlockBooking(w, await Plan(w, Limit(MembershipUsageWindow.Period, 4)));

        await SchedulingAssert.BusinessRule(ErrorCodes.MembershipBookingBlocked, () => Book(w, At(2, 10)));

        await w.GrantUser(w.ActorUserId, Grants.AppointmentsMembershipBlockOverride);
        AppointmentDto booked = await Book(w, At(2, 10));
        BookingParticipationDto p = await Only(w, booked.Id);
        AssertCoverage(p, MembershipCoverageStatus.NotCovered, MembershipCoverageReason.DebtNotCovered);
        Assert.Equal(50m, p.MonetaryDue);
    }

    [Fact]
    public async Task GroupGeneration_SkipsTheDelinquentMember_AndPayingTheDebtAddsThemBackWhileThereIsRoom()
    {
        await using SchedulingWorld w = await SchedulingWorld.Create(nameof(GroupGeneration_SkipsTheDelinquentMember_AndPayingTheDebtAddsThemBackWhileThereIsRoom));
        ServiceEntity groupService = await w.AddGroupService(price: 15m);
        ClientMembershipDto membership = await DelinquentUnderBlockBooking(w, await Plan(w, new[] { groupService }, Limit(MembershipUsageWindow.Period, 8)));
        Client other = await w.AddClient("Other", "Member");
        DateTimeOffset dayA = At(2, 0), dayB = At(3, 0);
        GroupDto roomy = await w.CreateGroup(groupService, capacity: 5, slots: (dayA.DayOfWeek, TimeSpan.FromHours(10)));
        GroupDto tight = await w.CreateGroup(groupService, capacity: 2, slots: (dayB.DayOfWeek, TimeSpan.FromHours(10)));
        foreach (GroupDto g in new[] { roomy, tight })
            await w.AddGroupMember(g, w.Client);
        await w.AddGroupMember(tight, other);

        GenerateGroupAppointmentsResult roomyGenerated = await w.GenerateOccurrences(roomy, dayA, overrideAvailability: true);
        GenerateGroupAppointmentsResult tightGenerated = await w.GenerateOccurrences(tight, dayB, overrideAvailability: true);

        // Q18: skipped (still a group member), listed for reception; the other member fills the tight occurrence.
        GroupMembershipSkipDto skipped = Assert.Single(roomyGenerated.MembershipSkips);
        Assert.Equal((w.Client.Id.Value, membership.Id, (GroupMembershipSkipResolution?)null), (skipped.ClientId, skipped.ClientMembershipId, skipped.Resolution));
        Assert.Empty((await w.Appointments.GetById(w.OrganizationId, Assert.Single(roomyGenerated.Created).Id)).Bookings);
        Assert.Single(tightGenerated.MembershipSkips);
        await w.AddGuest(await w.LoadAppointment(Assert.Single(tightGenerated.Created).Id), await w.AddClient("Walk", "In")); // now full
        Assert.Single(await w.Resolve<IGroupMembershipSkipService>().GetForGroup(w.OrganizationId, roomy.Id));

        await PayAllCharges(w, membership);

        // Q53: added back where there is room (covered again), the full occurrence stays on the reception list.
        GroupMembershipSkipDto added = Assert.Single(await w.Resolve<IGroupMembershipSkipService>().GetForGroup(w.OrganizationId, roomy.Id));
        Assert.Equal(GroupMembershipSkipResolution.Added, added.Resolution);
        BookingParticipationDto back = await Only(w, Assert.Single(roomyGenerated.Created).Id);
        AssertCoverage(back, MembershipCoverageStatus.Covered, MembershipCoverageReason.Claimed);
        Assert.Equal(GroupMembershipSkipResolution.CapacityFull,
            Assert.Single(await w.Resolve<IGroupMembershipSkipService>().GetForGroup(w.OrganizationId, tight.Id)).Resolution);
    }

    #endregion

    #region Review of 2D (#1, #8, #11, #13)

    private static async Task<Guid> MemberId(SchedulingWorld w, Guid groupId, Client client)
    {
        await using DatabaseContext db = w.NewDb();
        return (await db.GroupMembers.AsNoTracking().SingleAsync(m => m.GroupId == groupId && m.ClientId == client.Id && m.IsActive)).Id.Value;
    }

    private static async Task AssertNoOrphanCoverage(SchedulingWorld w)
    {
        await using DatabaseContext db = w.NewDb();
        List<Guid> participations = await db.BookingSegmentParticipations.AsNoTracking()
            .Where(p => p.OrganizationId == w.OrganizationId).Select(p => p.Id.Value).ToListAsync();
        Assert.Empty(await db.Set<ParticipationMembershipCoverage>().AsNoTracking()
            .Where(c => c.OrganizationId == w.OrganizationId && !participations.Contains(c.ParticipationId)).ToListAsync());
        Assert.Empty(await db.Set<MembershipUsage>().AsNoTracking()
            .Where(u => u.OrganizationId == w.OrganizationId && u.IsActive && !participations.Contains(u.ParticipationId)).ToListAsync());
    }

    [Fact]
    public async Task DeletingAppointmentsParticipationsSeriesOrGroupMembership_LeavesNoCoverageOrActiveClaimWithoutAParticipation()
    {
        await using SchedulingWorld w = await SchedulingWorld.Create(nameof(DeletingAppointmentsParticipationsSeriesOrGroupMembership_LeavesNoCoverageOrActiveClaimWithoutAParticipation));
        ServiceEntity groupService = await w.AddGroupService(price: 15m);
        await Sell(w, await Plan(w, new[] { w.Service, groupService }, Limit(MembershipUsageWindow.Period, 20)));
        Client other = await w.AddClient("Other", "Client");

        // Appointment delete (same day), participation removal (shared appointment), a whole recurring series.
        AppointmentDto deleted = await Book(w, At(2, 9));
        AppointmentDto shared = await w.CreateAppointment(w.CreateRequest(At(2, 12), overrideAvailability: true, extraClients: other));
        List<AppointmentDto> series = await w.Appointments.CreateRecurring(w.OrganizationId, w.ActorUserId, true, new RecurringAppointmentCreateRequest
        {
            RecurrenceType = RecurrenceType.Daily, ServiceId = w.Service.Id.Value, EmployeeId = w.Employee.Id.Value, CompanyId = w.Company.Id.Value,
            ClientIds = new List<Guid> { w.Client.Id.Value }, FirstOccurrenceStartsAt = At(4, 15), EndDate = At(6, 15), OverrideAvailability = true
        });
        Assert.Equal(3, series.Count);
        Assert.All(series, s => Assert.Equal(MembershipCoverageStatus.Covered,
            s.Bookings.Single().Participations.Single().MembershipCoverage.Status)); // #13: the response itself explains coverage

        // Group: covered member in a generated occurrence, then removed from the group (untouched participation is deleted).
        DateTimeOffset day = At(3, 0);
        GroupDto group = await w.CreateGroup(groupService, capacity: 5, slots: (day.DayOfWeek, TimeSpan.FromHours(10)));
        await w.AddGroupMember(group, w.Client);
        await w.GenerateOccurrences(group, day, overrideAvailability: true);
        Assert.Equal(6, (await Usages(w)).Count(u => u.IsActive));

        await w.Appointments.Delete(w.OrganizationId, w.ActorUserId, deleted.Id);
        await w.RemoveClientFromOnlySegment(shared.Id, w.Client);
        foreach (AppointmentDto occurrence in series)
            await w.Appointments.Delete(w.OrganizationId, w.ActorUserId, occurrence.Id);
        await w.Groups.RemoveMember(w.OrganizationId, w.ActorUserId, group.Id, await MemberId(w, group.Id, w.Client));

        await AssertNoOrphanCoverage(w);
        List<MembershipUsage> usages = await Usages(w);
        Assert.Empty(usages.Where(u => u.IsActive));
        Assert.Equal(6, usages.Count(u => u.EntryType == MembershipUsageEntryType.Release && u.ReleaseReason == MembershipUsageReleaseReason.ParticipationRemoved));
    }

    [Fact]
    public async Task RemovalHelper_RefusesToDeleteAParticipationWhoseClaimWasNotReleasedFirst()
    {
        await using SchedulingWorld w = await SchedulingWorld.Create(nameof(RemovalHelper_RefusesToDeleteAParticipationWhoseClaimWasNotReleasedFirst));
        await Sell(w, await Plan(w, Limit(MembershipUsageWindow.Period, 4)));
        AppointmentDto booked = await Book(w, At(2, 10));

        await using DatabaseContext db = w.NewDb();
        Booking booking = await db.Bookings.SingleAsync(b => b.AppointmentId == booked.Id);
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            Infrastructure.Utils.ParticipationHistory.RemoveUntouched(db, new[] { booking }, "test"));
    }

    [Fact]
    public async Task PolicyWithoutAFee_ReturnsTheCreditOnALateCancellation_UnlessForfeitCreditWasChosenExplicitly()
    {
        await using SchedulingWorld w = await SchedulingWorld.Create(nameof(PolicyWithoutAFee_ReturnsTheCreditOnALateCancellation_UnlessForfeitCreditWasChosenExplicitly));
        await Sell(w, await Plan(w, Limit(MembershipUsageWindow.Period, 4)));
        DateTimeOffset start = At(2, 10);
        ICancellationPolicyService policies = w.Resolve<ICancellationPolicyService>();
        CancellationPolicyRulesRequest NoFee(CancellationMembershipAction? action) => new()
        {
            CancellationWindowMinutes = LateWindow(start),
            LateCancellation = new CancellationPolicyEventRuleDto { FeeType = CancellationFeeType.None, PackageAction = CancellationPackageAction.None, MembershipAction = action },
            NoShow = new CancellationPolicyEventRuleDto { FeeType = CancellationFeeType.Fixed, FeeValue = 10m, PackageAction = CancellationPackageAction.None }
        };

        // Not chosen: an event without a fee punishes nobody, members included; an event with a fee keeps ForfeitCredit.
        CancellationPolicyDto published = await policies.PublishVersion(w.OrganizationId, w.ActorUserId, w.DefaultPolicyId, NoFee(null));
        CancellationPolicyVersionDto latest = published.Versions.OrderByDescending(v => v.Version).First();
        Assert.Equal((CancellationMembershipAction.ReturnCreditChargeFee, CancellationMembershipAction.ForfeitCredit),
            (latest.LateCancellation.MembershipAction.Value, latest.NoShow.MembershipAction.Value));
        AppointmentDto returned = await Book(w, start);
        await w.SetBookingStatus(returned.Id, w.Client, BookingStatus.Cancelled, "late");
        BookingParticipationDto p = await Only(w, returned.Id);
        AssertCoverage(p, MembershipCoverageStatus.Released, MembershipCoverageReason.CreditReturned);
        Assert.Equal((false, 0m), (p.PolicyConsequence.MembershipCreditForfeited, p.MonetaryDue));

        // Chosen explicitly: the organization forfeits the credit even without a fee.
        await policies.PublishVersion(w.OrganizationId, w.ActorUserId, w.DefaultPolicyId, NoFee(CancellationMembershipAction.ForfeitCredit));
        AppointmentDto forfeited = await Book(w, start.AddHours(-2)); // still inside the late window
        await w.SetBookingStatus(forfeited.Id, w.Client, BookingStatus.Cancelled, "late");
        AssertCoverage(await Only(w, forfeited.Id), MembershipCoverageStatus.Covered, MembershipCoverageReason.CreditForfeited);
    }

    [Fact]
    public async Task NewGroupMemberInDebt_IsSkippedInAlreadyGeneratedOccurrences_AndAddedBackAfterPayment()
    {
        await using SchedulingWorld w = await SchedulingWorld.Create(nameof(NewGroupMemberInDebt_IsSkippedInAlreadyGeneratedOccurrences_AndAddedBackAfterPayment));
        ServiceEntity groupService = await w.AddGroupService(price: 15m);
        ClientMembershipDto membership = await DelinquentUnderBlockBooking(w, await Plan(w, new[] { groupService }, Limit(MembershipUsageWindow.Period, 8)));
        DateTimeOffset day = At(2, 0);
        GroupDto group = await w.CreateGroup(groupService, capacity: 5, slots: (day.DayOfWeek, TimeSpan.FromHours(10)));
        Guid occurrenceId = Assert.Single((await w.GenerateOccurrences(group, day, overrideAvailability: true)).Created).Id;

        await w.AddGroupMember(group, w.Client);

        Assert.Empty((await w.Appointments.GetById(w.OrganizationId, occurrenceId)).Bookings);
        Assert.Null(Assert.Single(await w.Resolve<IGroupMembershipSkipService>().GetForGroup(w.OrganizationId, group.Id)).Resolution);

        await PayAllCharges(w, membership);

        Assert.Equal(GroupMembershipSkipResolution.Added,
            Assert.Single(await w.Resolve<IGroupMembershipSkipService>().GetForGroup(w.OrganizationId, group.Id)).Resolution);
        AssertCoverage(await Only(w, occurrenceId), MembershipCoverageStatus.Covered, MembershipCoverageReason.Claimed);
    }

    [Fact]
    public async Task Waitlist_JoiningIsBlockedForADelinquentMember_ButAnAllowedEntryIsPromotedUncovered_WithTheReasonForReception()
    {
        await using SchedulingWorld w = await SchedulingWorld.Create(nameof(Waitlist_JoiningIsBlockedForADelinquentMember_ButAnAllowedEntryIsPromotedUncovered_WithTheReasonForReception));
        ServiceEntity groupService = await w.AddGroupService(price: 15m);
        await DelinquentUnderBlockBooking(w, await Plan(w, new[] { groupService }, Limit(MembershipUsageWindow.Period, 8)));
        Client other = await w.AddClient("Seat", "Holder");
        DateTimeOffset day = At(2, 0);
        GroupDto group = await w.CreateGroup(groupService, capacity: 1, slots: (day.DayOfWeek, TimeSpan.FromHours(10)));
        await w.AddGroupMember(group, other);
        Guid occurrenceId = Assert.Single((await w.GenerateOccurrences(group, day, overrideAvailability: true)).Created).Id;
        Guid segmentId = (await w.LoadAppointment(occurrenceId)).Segments.Single().Id.Value;
        WaitlistJoinRequest join = new() { ClientId = w.Client.Id.Value, SegmentId = segmentId };

        await SchedulingAssert.BusinessRule(ErrorCodes.MembershipBookingBlocked,
            () => w.Waitlist.Join(w.OrganizationId, w.ActorUserId, true, occurrenceId, join));

        await w.GrantUser(w.ActorUserId, Grants.AppointmentsMembershipBlockOverride, Grants.AppointmentsWriteAll);
        await w.Waitlist.Join(w.OrganizationId, w.ActorUserId, true, occurrenceId, join);
        await w.SetBookingStatus(occurrenceId, other, BookingStatus.Cancelled, "seat freed", initiator: CancellationInitiator.Business);

        BookingParticipationDto promoted = (await w.Appointments.GetById(w.OrganizationId, occurrenceId)).Bookings
            .Single(b => b.ClientId == w.Client.Id).Participations.Single();
        AssertCoverage(promoted, MembershipCoverageStatus.NotCovered, MembershipCoverageReason.DebtNotCovered);
        Infrastructure.Domain.Models.Outbox.OutboxMessage message = Assert.Single(await w.LoadOutbox(), m => m.Type == Core.Events.OutboxEventTypes.WaitlistPromotedV1);
        Core.Events.WaitlistPromotedEvent payload = System.Text.Json.JsonSerializer.Deserialize<Core.Events.WaitlistPromotedEvent>(
            message.Payload, new System.Text.Json.JsonSerializerOptions(System.Text.Json.JsonSerializerDefaults.Web)
            { Converters = { new System.Text.Json.Serialization.JsonStringEnumConverter() } });
        Assert.Equal((MembershipCoverageStatus.NotCovered, MembershipCoverageReason.DebtNotCovered),
            (payload.MembershipCoverageStatus.Value, payload.MembershipCoverageReason.Value));
    }

    #endregion

    #region Concurrency

    [Fact]
    public async Task TwoConcurrentBookings_ForTheLastCredit_ExactlyOneIsCovered()
    {
        await using SchedulingWorld w = await SchedulingWorld.Create(nameof(TwoConcurrentBookings_ForTheLastCredit_ExactlyOneIsCovered));
        await Sell(w, await Plan(w, Limit(MembershipUsageWindow.Period, 1)));

        // Two independent scopes = two connections/transactions; the claim locks the membership row before counting.
        using IServiceScope scopeA = SchedulingTestHost.CreateScope();
        using IServiceScope scopeB = SchedulingTestHost.CreateScope();
        Task<AppointmentDto> Attempt(IServiceScope scope, DateTimeOffset start) => Task.Run(() =>
            scope.ServiceProvider.GetRequiredService<IAppointmentService>().Create(w.OrganizationId, w.ActorUserId, true,
                w.CreateRequest(start, overrideAvailability: true).ToTarget()));

        AppointmentDto[] created = await Task.WhenAll(Attempt(scopeA, At(2, 10)), Attempt(scopeB, At(3, 10)));

        List<BookingParticipationDto> participations = new();
        foreach (AppointmentDto appointment in created)
            participations.Add(await Only(w, appointment.Id));
        Assert.Equal(1, participations.Count(p => p.MembershipCoverage.Status == MembershipCoverageStatus.Covered));
        Assert.Equal(1, participations.Count(p => p.MembershipCoverage.Reason == MembershipCoverageReason.LimitReached));
        Assert.Equal(1, (await Usages(w)).Count(u => u.IsActive));
    }

    #endregion
}
