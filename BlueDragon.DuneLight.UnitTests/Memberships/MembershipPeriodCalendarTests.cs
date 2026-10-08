#nullable disable
using System;
using System.Collections.Generic;
using System.Linq;
using BlueDragon.DuneLight.Core.Enums;
using BlueDragon.DuneLight.Infrastructure.Utils;

namespace BlueDragon.DuneLight.UnitTests.Memberships;

/// <summary>
/// P2 phase 2B — pure period math (docs/p2): purchase-date renewal from the ORIGINAL anchor with month-end clamping (Q3),
/// calendar months with a short full-price first period (Q3), Days pauses extending later boundaries (Q5), SkipPeriods
/// pauses skipping whole calendar periods and the early return opening a period from the return day (Q47), and a scheduled
/// change of terms starting at the first renewal on or after its date.
/// </summary>
public class MembershipPeriodCalendarTests
{
    private static readonly MembershipPeriodTerms MonthlyPurchase = new(MembershipBillingInterval.Monthly, MembershipRenewalAnchor.PurchaseDate, PauseExtendsPeriod: true);
    private static readonly MembershipPeriodTerms YearlyPurchase = new(MembershipBillingInterval.Yearly, MembershipRenewalAnchor.PurchaseDate, PauseExtendsPeriod: true);
    private static readonly MembershipPeriodTerms Calendar = new(MembershipBillingInterval.Monthly, MembershipRenewalAnchor.CalendarMonth, PauseExtendsPeriod: false);

    private static DateOnly D(int y, int m, int d) => new(y, m, d);

    private static MembershipTimeline Timeline(DateOnly start, MembershipPeriodTerms terms, params MembershipPauseSpan[] pauses) =>
        new(start, terms, pauses.ToList());

    private static List<(DateOnly, DateOnly, bool)> First(MembershipTimeline timeline, int count) =>
        MembershipPeriodCalendar.Periods(timeline).Take(count).Select(p => (p.StartsOn, p.EndsOn, p.Skipped)).ToList();

    [Fact]
    public void PurchaseDate_RenewsFromTheOriginalAnchor_ClampingShortMonths()
    {
        List<(DateOnly, DateOnly, bool)> periods = First(Timeline(D(2027, 1, 31), MonthlyPurchase), 4);

        Assert.Equal(new[]
        {
            (D(2027, 1, 31), D(2027, 2, 27), false),
            (D(2027, 2, 28), D(2027, 3, 30), false),
            (D(2027, 3, 31), D(2027, 4, 29), false),
            (D(2027, 4, 30), D(2027, 5, 30), false)
        }, periods);
    }

    [Fact]
    public void Yearly_FromTheLeapDay_ClampsToFebruary28_AndReturnsToThe29th()
    {
        List<(DateOnly, DateOnly, bool)> periods = First(Timeline(D(2028, 2, 29), YearlyPurchase), 5);
        Assert.Equal(new[] { D(2028, 2, 29), D(2029, 2, 28), D(2030, 2, 28), D(2031, 2, 28), D(2032, 2, 29) }, periods.Select(p => p.Item1));
    }

    [Fact]
    public void CalendarMonth_FirstPeriodRunsToMonthEnd_ThenWholeMonths()
    {
        Assert.Equal(new[]
        {
            (D(2027, 1, 15), D(2027, 1, 31), false),
            (D(2027, 2, 1), D(2027, 2, 28), false),
            (D(2027, 3, 1), D(2027, 3, 31), false)
        }, First(Timeline(D(2027, 1, 15), Calendar), 3));
    }

    [Fact]
    public void DaysPause_ExtendsThePeriodItStartsIn_AndShiftsEveryLaterBoundary()
    {
        MembershipTimeline timeline = Timeline(D(2027, 1, 10), MonthlyPurchase,
            new MembershipPauseSpan(MembershipPauseKind.Days, D(2027, 1, 20), D(2027, 1, 29))); // 10 days

        Assert.Equal(new[]
        {
            (D(2027, 1, 10), D(2027, 2, 19), false),
            (D(2027, 2, 20), D(2027, 3, 19), false)
        }, First(timeline, 2));

        MembershipTimeline noExtension = timeline with { Terms = MonthlyPurchase with { PauseExtendsPeriod = false } };
        Assert.Equal(D(2027, 2, 9), MembershipPeriodCalendar.Periods(noExtension).First().EndsOn);
    }

    [Fact]
    public void SkipPeriods_SkipWholeCalendarPeriods_AndAnEarlyReturnOpensAPeriodFromTheReturnDay()
    {
        MembershipTimeline skipped = Timeline(D(2027, 1, 1), Calendar,
            new MembershipPauseSpan(MembershipPauseKind.SkipPeriods, D(2027, 2, 1), D(2027, 3, 31)));
        Assert.Equal(new[]
        {
            (D(2027, 1, 1), D(2027, 1, 31), false),
            (D(2027, 2, 1), D(2027, 2, 28), true),
            (D(2027, 3, 1), D(2027, 3, 31), true),
            (D(2027, 4, 1), D(2027, 4, 30), false)
        }, First(skipped, 4));

        MembershipTimeline returnedEarly = Timeline(D(2027, 1, 1), Calendar,
            new MembershipPauseSpan(MembershipPauseKind.SkipPeriods, D(2027, 2, 1), D(2027, 2, 14)));
        Assert.Equal(new[]
        {
            (D(2027, 2, 1), D(2027, 2, 14), true),
            (D(2027, 2, 15), D(2027, 2, 28), false),
            (D(2027, 3, 1), D(2027, 3, 31), false)
        }, First(returnedEarly, 4).Skip(1));
    }

    [Fact]
    public void PendingTerms_StartAtTheFirstRenewalOnOrAfterTheScheduledDate()
    {
        // Same purchase-date interval keeps the original anchor (31st).
        MembershipTimeline samePlanType = Timeline(D(2027, 1, 31), MonthlyPurchase) with
        {
            PendingTerms = MonthlyPurchase, PendingNotBefore = D(2027, 3, 1)
        };
        Assert.Equal(new[] { D(2027, 1, 31), D(2027, 2, 28), D(2027, 3, 31), D(2027, 4, 30) },
            MembershipPeriodCalendar.Periods(samePlanType).Take(4).Select(p => p.StartsOn));

        // Switching to a calendar plan: the first calendar period runs from the change to the end of that month.
        MembershipTimeline toCalendar = Timeline(D(2027, 1, 10), MonthlyPurchase) with
        {
            PendingTerms = Calendar, PendingNotBefore = D(2027, 2, 1)
        };
        Assert.Equal(new[]
        {
            (D(2027, 1, 10), D(2027, 2, 9), false),
            (D(2027, 2, 10), D(2027, 2, 28), false),
            (D(2027, 3, 1), D(2027, 3, 31), false)
        }, First(toCalendar, 3));
    }

    [Fact]
    public void EndOfNthUnskippedPeriod_IgnoresSkippedPeriods()
    {
        MembershipTimeline timeline = Timeline(D(2027, 1, 1), Calendar,
            new MembershipPauseSpan(MembershipPauseKind.SkipPeriods, D(2027, 2, 1), D(2027, 2, 28)));
        Assert.Equal(D(2027, 4, 30), MembershipPeriodCalendar.EndOfNthUnskippedPeriod(timeline, 3));
    }
}
