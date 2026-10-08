#nullable disable
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using BlueDragon.DuneLight.Core.DTOs.Appointments;
using BlueDragon.DuneLight.Core.DTOs.Catalog;
using BlueDragon.DuneLight.Core.DTOs.Clients;
using BlueDragon.DuneLight.Core.DTOs.Groups;
using BlueDragon.DuneLight.Core.Enums;
using BlueDragon.DuneLight.Core.Interfaces.Catalog;
using BlueDragon.DuneLight.Core.Interfaces.Clients;
using BlueDragon.DuneLight.Core.Shared.Exceptions;
using BlueDragon.DuneLight.Infrastructure.Domain.Models.Appointments;
using BlueDragon.DuneLight.Infrastructure.Domain.Models.Catalog;
using BlueDragon.DuneLight.Infrastructure.Utils;
using BlueDragon.DuneLight.UnitTests.Scheduling;
using ServiceEntity = BlueDragon.DuneLight.Infrastructure.Domain.Models.Catalog.Service;

namespace BlueDragon.DuneLight.UnitTests.Memberships;

/// <summary>
/// P2 phase 2E — the membership price benefit and the general adjustment mechanism (Q1, Q2, P2_PLAN §10.4, decision log 2E):
/// explicit rule scope (service rule beats "all services"), rounding, validation (a plan covers a service OR has a benefit),
/// Q48 classification, best price among candidates, uncovered sessions only, repricing when coverage changes (old/new price and
/// the event recorded), protection of manual and already paid prices, pause, group check-in, and the explained evaluation.
/// </summary>
public class MembershipPriceBenefitTests
{
    private static DateTimeOffset At(int days, int hour) => new DateTimeOffset(DateTime.UtcNow.Date, TimeSpan.Zero).AddDays(days).AddHours(hour);
    private static DateOnly Today => DateOnly.FromDateTime(DateTime.UtcNow);

    private static MembershipPriceBenefitDto All(MembershipPriceBenefitType type, decimal value) =>
        new() { Scope = MembershipPriceBenefitScope.AllServices, Type = type, Value = value };

    private static MembershipPriceBenefitDto For(ServiceEntity service, MembershipPriceBenefitType type, decimal value) =>
        new() { Scope = MembershipPriceBenefitScope.Service, ServiceId = service.Id, Type = type, Value = value };

    private static Task<MembershipPlanDto> Plan(SchedulingWorld w, IEnumerable<ServiceEntity> covered, IEnumerable<MembershipUsageLimitDto> limits,
        params MembershipPriceBenefitDto[] benefits) =>
        w.Resolve<IMembershipPlanService>().Create(w.OrganizationId, w.ActorUserId, new MembershipPlanCreateRequest
        {
            Name = $"Plan-{Guid.NewGuid():N}", Price = 50m, BillingInterval = MembershipBillingInterval.Monthly,
            RenewalAnchor = MembershipRenewalAnchor.PurchaseDate, CompanyScope = MembershipCompanyScope.AllCompanies,
            Services = covered.Select(s => new MembershipPlanCoveredServiceRequest { ServiceId = s.Id }).ToList(),
            UsageLimits = limits.ToList(),
            PriceBenefits = benefits.ToList(),
            Pause = new MembershipPauseRulesDto { Allowed = true, MaxPauseDays = 30, ExtendsPeriod = true }
        });

    private static Task<ClientMembershipDto> Sell(SchedulingWorld w, MembershipPlanDto plan) =>
        w.Resolve<IClientMembershipService>().Sell(w.OrganizationId, w.ActorUserId, w.Client.Id.Value,
            new ClientMembershipSellRequest { MembershipPlanId = plan.Id, SoldCompanyId = w.Company.Id.Value });

    private static Task<AppointmentDto> Book(SchedulingWorld w, DateTimeOffset start, ServiceEntity service = null) =>
        w.CreateAppointment(w.CreateRequest(start, service: service, overrideAvailability: true));

    private static async Task<BookingParticipationDto> Only(SchedulingWorld w, Guid appointmentId) =>
        (await w.Appointments.GetById(w.OrganizationId, appointmentId)).Bookings.Single().Participations.Single();

    private static MembershipUsageLimitDto Limit(MembershipUsageWindow window, int max) => new() { Window = window, MaxUses = max };

    #region Pure rules

    private static MembershipPlanPriceBenefit Rule(MembershipPriceBenefitType type, decimal value, Guid? serviceId = null) => new()
    {
        Scope = serviceId.HasValue ? MembershipPriceBenefitScope.Service : MembershipPriceBenefitScope.AllServices,
        ServiceId = serviceId, Type = type, Value = value
    };

    [Fact]
    public void Benefit_ServiceRuleBeatsAllServices_RoundsToCents_AndNeverGoesBelowZero()
    {
        Guid yoga = Guid.NewGuid(), pilates = Guid.NewGuid();
        MembershipPlanVersion terms = new()
        {
            PriceBenefits = new() { Rule(MembershipPriceBenefitType.PercentOff, 10m), Rule(MembershipPriceBenefitType.FixedPrice, 20m, yoga) }
        };

        Assert.Equal(MembershipPriceBenefitType.FixedPrice, MembershipPriceBenefitRules.BenefitFor(terms, yoga).Type);
        Assert.Equal(MembershipPriceBenefitType.PercentOff, MembershipPriceBenefitRules.BenefitFor(terms, pilates).Type); // incl. later services
        Assert.Equal(33.34m, MembershipPriceBenefitRules.Apply(Rule(MembershipPriceBenefitType.PercentOff, 33.33m), 50m)); // 33.335 → 33.34
        Assert.Equal(0m, MembershipPriceBenefitRules.Apply(Rule(MembershipPriceBenefitType.AmountOff, 80m), 50m));
        Assert.Equal(60m, MembershipPriceBenefitRules.Apply(Rule(MembershipPriceBenefitType.FixedPrice, 60m), 50m));
    }

    [Fact]
    public void Resolver_PicksTheLowestPriceBelowBase_WithDeterministicTieBreak_AndExplainsEveryCandidate()
    {
        Guid a = Guid.NewGuid(), b = Guid.NewGuid(), c = Guid.NewGuid(), d = Guid.NewGuid();
        PriceAdjustmentResult result = PriceAdjustmentResolver.Resolve(50m, new List<PriceAdjustmentCandidate>
        {
            new(PriceAdjustmentType.Membership, b, 1, 40m, null, null),
            new(PriceAdjustmentType.Membership, a, 0, 40m, null, null),
            new(PriceAdjustmentType.Membership, c, 2, 45m, null, null),
            new(PriceAdjustmentType.Membership, d, 3, 60m, null, null)
        });

        Assert.Equal((40m, a), (result.SuggestedAmount, result.Applied.SourceId));
        Assert.Equal(new[] { PriceAdjustmentOutcome.Applied, PriceAdjustmentOutcome.LostOnTie, PriceAdjustmentOutcome.LostToBetterPrice, PriceAdjustmentOutcome.NoReduction },
            result.Evaluation.Select(e => e.Outcome));
    }

    [Fact]
    public void PlanRules_ACoveredServiceOrABenefitIsRequired_AndTheScopeIsExplicit()
    {
        Guid yoga = Guid.NewGuid();
        MembershipPlanTermsRequest Terms(List<MembershipPlanCoveredServiceRequest> services, params MembershipPriceBenefitDto[] benefits) => new()
        {
            Price = 50m, BillingInterval = MembershipBillingInterval.Monthly, RenewalAnchor = MembershipRenewalAnchor.PurchaseDate,
            CompanyScope = MembershipCompanyScope.AllCompanies, Services = services, PriceBenefits = benefits.ToList(),
            Pause = new MembershipPauseRulesDto { Allowed = false }
        };
        MembershipPriceBenefitDto all = new() { Scope = MembershipPriceBenefitScope.AllServices, Type = MembershipPriceBenefitType.PercentOff, Value = 10m };

        MembershipPlanRules.Validate(Terms(new(), all)); // benefit-only plan
        Assert.Throws<ValidationAppException>(() => MembershipPlanRules.Validate(Terms(new())));
        Assert.Throws<ValidationAppException>(() => MembershipPlanRules.Validate(Terms(new(),
            new MembershipPriceBenefitDto { Scope = MembershipPriceBenefitScope.Service, Type = MembershipPriceBenefitType.PercentOff, Value = 10m }))); // empty ≠ all
        Assert.Throws<ValidationAppException>(() => MembershipPlanRules.Validate(Terms(new(), all,
            new MembershipPriceBenefitDto { Scope = MembershipPriceBenefitScope.AllServices, Type = MembershipPriceBenefitType.AmountOff, Value = 5m })));
        Assert.Throws<ValidationAppException>(() => MembershipPlanRules.Validate(Terms(new(),
            new MembershipPriceBenefitDto { Scope = MembershipPriceBenefitScope.Service, ServiceId = yoga, Type = MembershipPriceBenefitType.PercentOff, Value = 10m },
            new MembershipPriceBenefitDto { Scope = MembershipPriceBenefitScope.Service, ServiceId = yoga, Type = MembershipPriceBenefitType.FixedPrice, Value = 20m })));
        Assert.Throws<ValidationAppException>(() => MembershipPlanRules.Validate(Terms(new(),
            new MembershipPriceBenefitDto { Scope = MembershipPriceBenefitScope.AllServices, Type = MembershipPriceBenefitType.PercentOff, Value = 120m })));
    }

    [Fact]
    public void Classifier_ComparesTheEffectiveBenefitPerService()
    {
        Guid yoga = Guid.NewGuid();
        MembershipPlanVersion With(params MembershipPlanPriceBenefit[] benefits) => new() { Price = 50m, PriceBenefits = benefits.ToList() };
        MembershipPlanVersion old = With(Rule(MembershipPriceBenefitType.PercentOff, 20m));

        Assert.Equal(MembershipPlanChangeClassification.Favorable,
            MembershipPlanChangeClassifier.Classify(old, With(Rule(MembershipPriceBenefitType.PercentOff, 25m))).Classification);
        Assert.Contains(nameof(MembershipPlanVersion.PriceBenefits),
            MembershipPlanChangeClassifier.Classify(old, With(Rule(MembershipPriceBenefitType.PercentOff, 15m))).WorsenedDimensions);
        Assert.Contains(nameof(MembershipPlanVersion.PriceBenefits),
            MembershipPlanChangeClassifier.Classify(old, With()).WorsenedDimensions); // benefit removed
        Assert.Contains(nameof(MembershipPlanVersion.PriceBenefits), MembershipPlanChangeClassifier.Classify(old,
            With(Rule(MembershipPriceBenefitType.PercentOff, 20m), Rule(MembershipPriceBenefitType.PercentOff, 5m, yoga))).WorsenedDimensions);
        Assert.Contains(nameof(MembershipPlanVersion.PriceBenefits), MembershipPlanChangeClassifier.Classify(old,
            With(Rule(MembershipPriceBenefitType.AmountOff, 10m))).WorsenedDimensions); // type change cannot be compared → mixed
    }

    #endregion

    #region Pricing of sessions

    [Fact]
    public async Task UncoveredSession_GetsTheBenefitPrice_CoveredSessionKeepsTheListPrice_AndBothAreExplained()
    {
        await using SchedulingWorld w = await SchedulingWorld.Create(nameof(UncoveredSession_GetsTheBenefitPrice_CoveredSessionKeepsTheListPrice_AndBothAreExplained));
        ServiceEntity massage = await w.AddService(60, 80m, name: "Massage");
        await w.AssignEmployeeToService(w.Employee, massage);
        ClientMembershipDto membership = await Sell(w, await Plan(w, new[] { w.Service }, new[] { Limit(MembershipUsageWindow.Period, 8) },
            All(MembershipPriceBenefitType.PercentOff, 25m)));

        BookingParticipationDto covered = (await Book(w, At(2, 9))).Bookings.Single().Participations.Single();
        BookingParticipationDto discounted = (await Book(w, At(2, 12), massage)).Bookings.Single().Participations.Single(); // response itself

        Assert.Equal((50m, 0m), (covered.Amount, covered.MonetaryDue));
        Assert.Equal((PriceAdjustmentOutcome.NotApplicable, PriceAdjustmentReason.CoveredByMembership),
            (covered.PriceAdjustment.Candidates.Single().Outcome, covered.PriceAdjustment.Candidates.Single().Reason.Value));
        Assert.Equal((60m, 80m, 60m, -20m), (discounted.Amount, discounted.BaseAmount.Value, discounted.MonetaryDue, discounted.PriceAdjustment.AdjustmentAmount.Value));
        Assert.Equal((PriceAdjustmentType.Membership, membership.Id), (discounted.PriceAdjustment.AppliedType.Value, discounted.PriceAdjustment.AppliedSourceId.Value));
        PriceAdjustmentCandidateDto applied = discounted.PriceAdjustment.Candidates.Single();
        Assert.Equal((PriceAdjustmentOutcome.Applied, 60m, MembershipPriceBenefitType.PercentOff, 25m),
            (applied.Outcome, applied.ResultingPrice.Value, applied.RuleType.Value, applied.RuleValue.Value));
    }

    [Fact]
    public async Task WhenCoverageChanges_ThePriceFollows_AndTheOldAndNewPriceAndTheEventAreRecorded()
    {
        await using SchedulingWorld w = await SchedulingWorld.Create(nameof(WhenCoverageChanges_ThePriceFollows_AndTheOldAndNewPriceAndTheEventAreRecorded));
        ServiceEntity service = w.Service;
        await Sell(w, await Plan(w, new[] { service }, new[] { Limit(MembershipUsageWindow.Period, 1) },
            For(service, MembershipPriceBenefitType.FixedPrice, 30m)));
        AppointmentDto first = await Book(w, At(2, 10));
        AppointmentDto second = await Book(w, At(3, 10));
        BookingParticipationDto overLimit = await Only(w, second.Id);
        Assert.Equal((MembershipCoverageReason.LimitReached, 30m), (overLimit.MembershipCoverage.Reason, overLimit.Amount)); // benefit on the fallback

        await w.SetBookingStatus(first.Id, w.Client, BookingStatus.Cancelled, "on time");

        BookingParticipationDto nowCovered = await Only(w, second.Id);
        Assert.Equal((MembershipCoverageStatus.Covered, 50m, 0m), (nowCovered.MembershipCoverage.Status, nowCovered.Amount, nowCovered.MonetaryDue));
        Assert.Equal((30m, 50m, MembershipCoverageEvent.SlotFreed),
            (nowCovered.MembershipCoverage.LastPriceChangeOldAmount.Value, nowCovered.MembershipCoverage.LastPriceChangeNewAmount.Value,
                nowCovered.MembershipCoverage.LastPriceChangeEvent.Value));
        Assert.Null(nowCovered.PriceAdjustment.AppliedType);
        Assert.Contains(await w.LoadAuditLog(second.Id), a => a.ChangeType == "AmountRepricedByMembership" && a.OldValue == "30.00" && a.NewValue == "50.00;SlotFreed");
    }

    [Fact]
    public async Task ManualAndAlreadyPaidPrices_AreProtected_FromAutomaticRepricing()
    {
        await using SchedulingWorld w = await SchedulingWorld.Create(nameof(ManualAndAlreadyPaidPrices_AreProtected_FromAutomaticRepricing));
        ClientMembershipDto membership = await Sell(w, await Plan(w, new[] { w.Service }, new[] { Limit(MembershipUsageWindow.Period, 8) },
            All(MembershipPriceBenefitType.PercentOff, 20m)));
        AppointmentDto manual = await Book(w, At(5, 9));
        AppointmentDto paid = await Book(w, At(5, 12));
        await w.SetParticipationPrice(manual.Id, w.Client, 45m);

        // The pause makes both sessions uncovered: the benefit (40) would apply, but neither price may change automatically.
        await w.Resolve<IClientMembershipService>().Pause(w.OrganizationId, w.ActorUserId, membership.Id,
            new ClientMembershipPauseRequest { StartsOn = Today.AddDays(4), EndsOn = Today.AddDays(6) });
        // Paid only now (it was covered before): pay the list price through checkout, then end the pause early — still protected.
        BookingParticipationDto manualP = await Only(w, manual.Id);
        Assert.Equal((MembershipCoverageReason.Paused, 45m, PriceProtectionReason.ManualAmount),
            (manualP.MembershipCoverage.Reason, manualP.Amount, manualP.MembershipCoverage.PriceProtectedReason.Value));
        BookingParticipationDto pausedP = await Only(w, paid.Id);
        Assert.Equal(50m, pausedP.Amount); // Q2/Q48: paused membership gives no benefit either
        Assert.Equal(PriceAdjustmentReason.Paused, pausedP.PriceAdjustment.Candidates.Single().Reason);

        await w.PayBookingViaCheckout(paid.Bookings.Single().Id, w.Client, 10m);
        await w.Resolve<IClientMembershipService>().CancelPause(w.OrganizationId, w.ActorUserId, membership.Id,
            (await w.Resolve<IClientMembershipService>().GetById(w.OrganizationId, membership.Id)).Pauses.Single().Id);

        BookingParticipationDto stillPaid = await Only(w, paid.Id);
        Assert.Equal((MembershipCoverageReason.AlreadyPaid, 50m, PriceProtectionReason.AlreadyPaid),
            (stillPaid.MembershipCoverage.Reason, stillPaid.Amount, stillPaid.MembershipCoverage.PriceProtectedReason.Value));
    }

    [Fact]
    public async Task GroupCheckIn_OfAnUncoveredMember_ChargesTheBenefitPrice()
    {
        await using SchedulingWorld w = await SchedulingWorld.Create(nameof(GroupCheckIn_OfAnUncoveredMember_ChargesTheBenefitPrice));
        ServiceEntity groupService = await w.AddGroupService(price: 20m);
        await Sell(w, await Plan(w, Array.Empty<ServiceEntity>(), Array.Empty<MembershipUsageLimitDto>(),
            For(groupService, MembershipPriceBenefitType.AmountOff, 5m))); // benefit-only plan
        DateTimeOffset day = At(2, 0);
        GroupDto group = await w.CreateGroup(groupService, capacity: 5, slots: (day.DayOfWeek, TimeSpan.FromHours(10)));
        await w.AddGroupMember(group, w.Client);
        Guid occurrenceId = Assert.Single((await w.GenerateOccurrences(group, day, overrideAvailability: true)).Created).Id;
        BookingParticipationDto booked = await Only(w, occurrenceId);
        Assert.Equal((15m, MembershipCoverageReason.ServiceNotCovered), (booked.Amount, booked.MembershipCoverage.Reason));

        await w.Bookings.SetParticipationStatus(w.OrganizationId, w.ActorUserId, true, booked.Id,
            new BookingSetStatusRequest { Status = BookingStatus.Completed, PaymentMethod = PaymentMethod.Cash, IsPaid = true });

        BookingParticipationDto checkedIn = await Only(w, occurrenceId);
        Assert.Equal((15m, 15m, true), (checkedIn.Amount, checkedIn.PaidAmount, checkedIn.IsPaid));
        Assert.Equal(PriceAdjustmentType.Membership, checkedIn.PriceAdjustment.AppliedType);
    }

    #endregion

    #region Review of 2E (#1, #4)

    [Fact]
    public async Task FixedMemberPriceNotBelowTheListPrice_IsWarnedWhenThePlanIsRead()
    {
        await using SchedulingWorld w = await SchedulingWorld.Create(nameof(FixedMemberPriceNotBelowTheListPrice_IsWarnedWhenThePlanIsRead));
        ServiceEntity cheap = await w.AddService(60, 30m, name: "Cheap");
        MembershipPlanDto plan = await Plan(w, new[] { w.Service }, Array.Empty<MembershipUsageLimitDto>(),
            For(w.Service, MembershipPriceBenefitType.FixedPrice, 40m), All(MembershipPriceBenefitType.FixedPrice, 35m));

        Core.Shared.WarningDto warning = Assert.Single(plan.Warnings, x => x.Code == Core.Shared.WarningCodes.MembershipBenefitWithoutEffect);
        Core.Shared.WarningMembershipBenefitPrice price = Assert.Single(Assert.IsType<Core.Shared.WarningMembershipBenefitDetails>(warning.Details).Prices);
        Assert.Equal((cheap.Id.Value, w.Company.Id.Value, 30m, 35m), (price.ServiceId, price.CompanyId, price.ListPrice, price.MemberPrice)); // 40 < 50 is fine

        // Computed at read time: a later list-price change makes the warning disappear.
        await w.UpdateService(cheap, defaultPrice: 45m);
        Assert.DoesNotContain((await w.Resolve<IMembershipPlanService>().GetById(w.OrganizationId, plan.Id)).Warnings,
            x => x.Code == Core.Shared.WarningCodes.MembershipBenefitWithoutEffect);
    }

    /// <summary>Simulates a membership-side repricing that had to skip a locked participation: the stored price is the stale list
    /// price and the projection is marked PriceStale.</summary>
    private static async Task<Guid> StalePricedBooking(SchedulingWorld w)
    {
        await Sell(w, await Plan(w, Array.Empty<ServiceEntity>(), Array.Empty<MembershipUsageLimitDto>(), All(MembershipPriceBenefitType.PercentOff, 20m)));
        AppointmentDto booked = await Book(w, At(2, 10));
        BookingParticipationDto p = await Only(w, booked.Id);
        Assert.Equal(40m, p.Amount);
        await using Infrastructure.Domain.Contexts.DatabaseContext db = w.NewDb();
        await Microsoft.EntityFrameworkCore.RelationalDatabaseFacadeExtensions.ExecuteSqlRawAsync(db.Database, @"
            UPDATE dunelight.booking_segment_participations SET amount = 50, suggested_amount = 50, adjustment_amount = NULL,
                adjustment_type = NULL, adjustment_source_id = NULL, adjustment_rule_snapshot = NULL WHERE id = {0};
            UPDATE dunelight.participation_membership_coverages SET price_stale = true WHERE participation_id = {0};", p.Id);
        BookingParticipationDto stale = await Only(w, booked.Id);
        Assert.True(stale.MembershipCoverage.PriceStale); // #4c: visible to reception while it lasts
        return booked.Id;
    }

    [Fact]
    public async Task Checkout_NeverTakesAStalePrice_ItRefreshesItFirst()
    {
        await using SchedulingWorld w = await SchedulingWorld.Create(nameof(Checkout_NeverTakesAStalePrice_ItRefreshesItFirst));
        Guid appointmentId = await StalePricedBooking(w);
        BookingParticipationDto p = await Only(w, appointmentId);

        Core.DTOs.Checkouts.CheckoutDto checkout = await w.Checkouts.Create(w.OrganizationId, w.ActorUserId, new Core.DTOs.Checkouts.CheckoutCreateRequest
        {
            ClientId = w.Client.Id.Value, CompanyId = w.Company.Id.Value
        });
        Core.DTOs.Checkouts.CheckoutDto withItem = await w.Checkouts.AddBookingItem(w.OrganizationId, w.ActorUserId, checkout.Id,
            new Core.DTOs.Checkouts.CheckoutAddBookingItemRequest { ParticipationId = p.Id });

        Core.DTOs.Checkouts.CheckoutItemDto item = Assert.Single(withItem.Items);
        Assert.Equal((40m, 40m, 40m), (item.UnitPrice, item.RetailAmount, item.OutstandingAmount));
        BookingParticipationDto refreshed = await Only(w, appointmentId);
        Assert.Equal((40m, false, MembershipCoverageEvent.PriceRefresh),
            (refreshed.Amount, refreshed.MembershipCoverage.PriceStale, refreshed.MembershipCoverage.LastPriceChangeEvent.Value));
    }

    [Fact]
    public async Task TheDailyPass_RefreshesEveryStalePrice()
    {
        await using SchedulingWorld w = await SchedulingWorld.Create(nameof(TheDailyPass_RefreshesEveryStalePrice));
        Guid appointmentId = await StalePricedBooking(w);

        await w.Resolve<Infrastructure.Services.IMembershipRenewalService>().RunForOrganization(w.OrganizationId);

        BookingParticipationDto refreshed = await Only(w, appointmentId);
        Assert.Equal((40m, false), (refreshed.Amount, refreshed.MembershipCoverage.PriceStale));
    }

    #endregion
}
