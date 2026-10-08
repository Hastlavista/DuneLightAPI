#nullable disable
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using BlueDragon.DuneLight.Core.Enums;
using BlueDragon.DuneLight.Core.Shared;
using BlueDragon.DuneLight.Core.Shared.Exceptions;
using BlueDragon.DuneLight.Infrastructure.Domain.Models.Catalog;
using BlueDragon.DuneLight.Infrastructure.Utils;

namespace BlueDragon.DuneLight.UnitTests.Memberships;

/// <summary>
/// P2 phase 2B — pure lifecycle rules: derived state, the cancellation effective date (end of period, notice, minimum
/// commitment over unpaused periods, before start), the start-date range (Q45), pause limits as rolling 12-month TOTALS (2B),
/// coverage overlap by time + services + companies (Q10/Q46) and the plan-change classifier (Q48).
/// </summary>
public class MembershipLifecycleRulesTests
{
    private static readonly MembershipPeriodTerms Monthly = new(MembershipBillingInterval.Monthly, MembershipRenewalAnchor.PurchaseDate, PauseExtendsPeriod: true);
    private static readonly MembershipPeriodTerms Calendar = new(MembershipBillingInterval.Monthly, MembershipRenewalAnchor.CalendarMonth, PauseExtendsPeriod: false);

    private static DateOnly D(int y, int m, int d) => new(y, m, d);

    private static MembershipTimeline Timeline(DateOnly start, MembershipPeriodTerms terms, params MembershipPauseSpan[] pauses) =>
        new(start, terms, pauses.ToList());

    private static MembershipPauseSpan Days(DateOnly from, DateOnly to) => new(MembershipPauseKind.Days, from, to);

    #region State, start date, cancellation

    [Fact]
    public void State_IsDerivedInPriorityOrder()
    {
        DateOnly today = D(2027, 5, 10);
        Assert.Equal(MembershipState.Voided, MembershipLifecycleRules.State(today, D(2027, 1, 1), null, voided: true, Array.Empty<MembershipPauseSpan>()));
        Assert.Equal(MembershipState.Ended, MembershipLifecycleRules.State(today, D(2027, 1, 1), D(2027, 5, 9), false, Array.Empty<MembershipPauseSpan>()));
        Assert.Equal(MembershipState.Scheduled, MembershipLifecycleRules.State(today, D(2027, 5, 11), null, false, Array.Empty<MembershipPauseSpan>()));
        Assert.Equal(MembershipState.Paused, MembershipLifecycleRules.State(today, D(2027, 1, 1), D(2027, 5, 10), false, new[] { Days(D(2027, 5, 1), D(2027, 5, 10)) }));
        Assert.Equal(MembershipState.Active, MembershipLifecycleRules.State(today, D(2027, 1, 1), null, false, new[] { Days(D(2027, 4, 1), D(2027, 4, 5)) }));
    }

    [Fact]
    public void StartDate_IsTodayUpToOneMonthAhead()
    {
        DateOnly today = D(2027, 1, 31);
        MembershipLifecycleRules.EnsureStartDate(today, today);
        MembershipLifecycleRules.EnsureStartDate(D(2027, 2, 28), today);
        Assert.Equal(ErrorCodes.MembershipStartDateOutOfRange,
            Assert.Throws<ValidationAppException>(() => MembershipLifecycleRules.EnsureStartDate(D(2027, 3, 1), today)).Code);
        Assert.Equal(ErrorCodes.MembershipStartDateOutOfRange,
            Assert.Throws<ValidationAppException>(() => MembershipLifecycleRules.EnsureStartDate(D(2027, 1, 30), today)).Code);
    }

    [Fact]
    public void Cancellation_EndsWithTheCurrentPeriod_UnlessNoticeOrCommitmentRequireLater()
    {
        MembershipTimeline timeline = Timeline(D(2027, 1, 10), Monthly);

        Assert.Equal((D(2027, 3, 9), MembershipEndEffectiveReason.EndOfPeriod),
            MembershipLifecycleRules.CancellationEffective(timeline, D(2027, 2, 20), null, null));

        // Notice 15 days: the renewal on 10.3. is only 18 days away → fine; requested 1.3. → renewal 10.3. is 9 days away → next.
        Assert.Equal((D(2027, 3, 9), MembershipEndEffectiveReason.EndOfPeriod),
            MembershipLifecycleRules.CancellationEffective(timeline, D(2027, 2, 20), null, 15));
        Assert.Equal((D(2027, 4, 9), MembershipEndEffectiveReason.NoticePeriod),
            MembershipLifecycleRules.CancellationEffective(timeline, D(2027, 3, 1), null, 15));

        // Minimum commitment of 3 periods.
        Assert.Equal((D(2027, 4, 9), MembershipEndEffectiveReason.MinimumCommitment),
            MembershipLifecycleRules.CancellationEffective(timeline, D(2027, 1, 20), 3, null));
    }

    [Fact]
    public void Cancellation_BeforeStart_EndsWithTheFirstPeriod_WithoutCommitmentOrNotice()
    {
        MembershipTimeline timeline = Timeline(D(2027, 2, 1), Calendar);
        Assert.Equal((D(2027, 2, 28), MembershipEndEffectiveReason.BeforeStart),
            MembershipLifecycleRules.CancellationEffective(timeline, D(2027, 1, 20), 6, 30));
    }

    [Fact]
    public void Commitment_CountsOnlyUnpausedPeriods()
    {
        MembershipTimeline timeline = Timeline(D(2027, 1, 1), Calendar,
            new MembershipPauseSpan(MembershipPauseKind.SkipPeriods, D(2027, 2, 1), D(2027, 2, 28)));
        Assert.Equal((D(2027, 4, 30), MembershipEndEffectiveReason.MinimumCommitment),
            MembershipLifecycleRules.CancellationEffective(timeline, D(2027, 1, 5), 3, null));
    }

    #endregion

    #region Pauses

    private static readonly MembershipPauseRules DaysRules = new(Allowed: true, MaxPauseDays: 30, MaxPausePeriods: null, MaxPausesPer12Months: 2);

    [Fact]
    public void MaxPauseDays_IsTheTotalInTheRolling12MonthWindow_NotPerPause()
    {
        MembershipTimeline timeline = Timeline(D(2027, 1, 1), Monthly, Days(D(2027, 3, 1), D(2027, 3, 20))); // 20 days used
        DateOnly today = D(2027, 2, 1);

        MembershipLifecycleRules.EnsurePauseAllowed(timeline, DaysRules, Days(D(2027, 6, 1), D(2027, 6, 10)), today, false);
        BusinessRuleException ex = Assert.Throws<BusinessRuleException>(() =>
            MembershipLifecycleRules.EnsurePauseAllowed(timeline, DaysRules, Days(D(2027, 6, 1), D(2027, 6, 11)), today, false));
        Assert.Equal(ErrorCodes.MembershipPauseLimitExceeded, ex.Code);

        // A new 12-month window (from 1.1.2028.) starts with a fresh allowance.
        MembershipLifecycleRules.EnsurePauseAllowed(timeline, DaysRules, Days(D(2028, 1, 5), D(2028, 2, 3)), today, false);
    }

    [Fact]
    public void PauseCount_IsLimitedPerWindow_AndEmptyMeansUnlimited()
    {
        MembershipTimeline timeline = Timeline(D(2027, 1, 1), Monthly, Days(D(2027, 3, 1), D(2027, 3, 2)), Days(D(2027, 4, 1), D(2027, 4, 2)));
        Assert.Equal(ErrorCodes.MembershipPauseLimitExceeded, Assert.Throws<BusinessRuleException>(() =>
            MembershipLifecycleRules.EnsurePauseAllowed(timeline, DaysRules, Days(D(2027, 5, 1), D(2027, 5, 2)), D(2027, 2, 1), false)).Code);

        MembershipLifecycleRules.EnsurePauseAllowed(timeline, DaysRules with { MaxPausesPer12Months = null },
            Days(D(2027, 5, 1), D(2027, 5, 2)), D(2027, 2, 1), false);
    }

    [Fact]
    public void Pause_IsRejected_WhenDisallowed_Overlapping_Retroactive_OrWithAScheduledEnd()
    {
        MembershipTimeline timeline = Timeline(D(2027, 1, 1), Monthly, Days(D(2027, 3, 1), D(2027, 3, 5)));
        DateOnly today = D(2027, 2, 1);
        void Rejected(MembershipPauseRules rules, MembershipPauseSpan candidate, bool endScheduled = false) =>
            Assert.Equal(ErrorCodes.MembershipPauseNotAllowed, Assert.Throws<BusinessRuleException>(() =>
                MembershipLifecycleRules.EnsurePauseAllowed(timeline, rules, candidate, today, endScheduled)).Code);

        Rejected(DaysRules with { Allowed = false }, Days(D(2027, 4, 1), D(2027, 4, 2)));
        Rejected(DaysRules, Days(D(2027, 3, 4), D(2027, 3, 8)));
        Rejected(DaysRules, Days(D(2027, 1, 31), D(2027, 2, 2)));
        Rejected(DaysRules, Days(D(2027, 4, 1), D(2027, 4, 2)), endScheduled: true);
    }

    [Fact]
    public void CalendarPause_StartsOnAPeriodStart_AndCountsSkippedPeriodsAgainstTheTotal()
    {
        MembershipTimeline timeline = Timeline(D(2027, 1, 15), Calendar);
        MembershipPauseRules rules = new(Allowed: true, MaxPauseDays: null, MaxPausePeriods: 2, MaxPausesPer12Months: null);
        DateOnly today = D(2027, 1, 20);

        MembershipLifecycleRules.EnsurePauseAllowed(timeline, rules,
            new MembershipPauseSpan(MembershipPauseKind.SkipPeriods, D(2027, 3, 1), D(2027, 4, 30)), today, false);
        Assert.Equal(ErrorCodes.MembershipPauseNotAllowed, Assert.Throws<BusinessRuleException>(() =>
            MembershipLifecycleRules.EnsurePauseAllowed(timeline, rules,
                new MembershipPauseSpan(MembershipPauseKind.SkipPeriods, D(2027, 3, 2), D(2027, 4, 30)), today, false)).Code);
        Assert.Equal(ErrorCodes.MembershipPauseLimitExceeded, Assert.Throws<BusinessRuleException>(() =>
            MembershipLifecycleRules.EnsurePauseAllowed(timeline, rules,
                new MembershipPauseSpan(MembershipPauseKind.SkipPeriods, D(2027, 3, 1), D(2027, 5, 31)), today, false)).Code);
    }

    #endregion

    #region Overlap and classification

    private static readonly Guid Yoga = Guid.NewGuid();
    private static readonly Guid Pilates = Guid.NewGuid();
    private static readonly Guid BranchA = Guid.NewGuid();
    private static readonly Guid BranchB = Guid.NewGuid();

    private static MembershipCoverageScope Scope(DateOnly from, DateOnly? to, Guid[] services, Guid[] companies = null) =>
        new(Guid.NewGuid(), "Plan", from, to, services, companies == null, companies ?? Array.Empty<Guid>());

    [Fact]
    public void Overlap_NeedsTimeAndServiceAndCompanyInCommon()
    {
        MembershipCoverageScope existing = Scope(D(2027, 1, 1), D(2027, 3, 31), new[] { Yoga }, new[] { BranchA });

        Assert.NotNull(MembershipCoverageOverlap.FindConflict(Scope(D(2027, 3, 1), null, new[] { Yoga }), new[] { existing }));
        Assert.Null(MembershipCoverageOverlap.FindConflict(Scope(D(2027, 4, 1), null, new[] { Yoga }), new[] { existing }));
        Assert.Null(MembershipCoverageOverlap.FindConflict(Scope(D(2027, 2, 1), null, new[] { Pilates }), new[] { existing }));
        Assert.Null(MembershipCoverageOverlap.FindConflict(Scope(D(2027, 2, 1), null, new[] { Yoga }, new[] { BranchB }), new[] { existing }));

        MembershipCoverageConflict conflict = MembershipCoverageOverlap.FindConflict(
            Scope(D(2027, 2, 1), null, new[] { Yoga, Pilates }, new[] { BranchA, BranchB }), new[] { existing });
        Assert.Equal((Yoga, BranchA), (Assert.Single(conflict.SharedServiceIds), Assert.Single(conflict.SharedCompanyIds)));
    }

    private static MembershipPlanVersion Version(decimal price = 50m, int? notice = 15, int? maxPauseDays = 30, params (Guid? Service, MembershipUsageWindow Window, int Max)[] limits) => new()
    {
        Price = price,
        StartFee = 10m,
        BillingInterval = MembershipBillingInterval.Monthly,
        RenewalAnchor = MembershipRenewalAnchor.PurchaseDate,
        CompanyScope = MembershipCompanyScope.AllCompanies,
        CancellationNoticeDays = notice,
        PauseAllowed = true,
        MaxPauseDays = maxPauseDays,
        Services = new() { new MembershipPlanVersionService { ServiceId = Yoga } },
        UsageLimits = limits.Select(l => new MembershipPlanUsageLimit { ServiceId = l.Service, Window = l.Window, MaxUses = l.Max }).ToList()
    };

    [Fact]
    public void Classifier_IsFavorableOnlyWhenNothingIsWorse()
    {
        MembershipPlanVersion old = Version(limits: (null, MembershipUsageWindow.Period, 8));

        Assert.Equal(MembershipPlanChangeClassification.Favorable, MembershipPlanChangeClassifier.Classify(old,
            Version(price: 45m, notice: 10, maxPauseDays: 40, limits: (null, MembershipUsageWindow.Period, 10))).Classification);
        Assert.Equal(MembershipPlanChangeClassification.Favorable, MembershipPlanChangeClassifier.Classify(old, Version()).Classification);

        var mixed = MembershipPlanChangeClassifier.Classify(old,
            Version(price: 45m, limits: new[] { ((Guid?)null, MembershipUsageWindow.Period, 8), ((Guid?)null, MembershipUsageWindow.Day, 1) }));
        Assert.Equal(MembershipPlanChangeClassification.Mixed, mixed.Classification);
        Assert.Equal(nameof(MembershipPlanVersion.UsageLimits), Assert.Single(mixed.WorsenedDimensions));

        Assert.Contains(nameof(MembershipPlanVersion.CancellationNoticeDays),
            MembershipPlanChangeClassifier.Classify(old, Version(notice: 30, limits: (null, MembershipUsageWindow.Period, 8))).WorsenedDimensions);
    }

    [Fact]
    public void Classifier_ComparesOrExplicitlyIgnoresEveryPlanVersionProperty()
    {
        // A new term added to MembershipPlanVersion must get a comparison rule (or an explicit reason to be ignored), otherwise
        // a worse change could pass as "favorable" and skip the notice period (Q48).
        IEnumerable<string> properties = typeof(MembershipPlanVersion)
            .GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Select(p => p.Name);
        Assert.Empty(properties.Except(MembershipPlanChangeClassifier.ComparedProperties).Except(MembershipPlanChangeClassifier.IgnoredProperties));
    }

    #endregion
}
