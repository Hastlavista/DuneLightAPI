#nullable disable
using System;
using System.Collections.Generic;
using System.Linq;
using BlueDragon.DuneLight.Core.DTOs.Catalog;
using BlueDragon.DuneLight.Core.Enums;
using BlueDragon.DuneLight.Core.Shared;
using BlueDragon.DuneLight.Core.Shared.Exceptions;
using BlueDragon.DuneLight.Infrastructure.Utils;

namespace BlueDragon.DuneLight.UnitTests.Memberships;

/// <summary>
/// P2 phase 2A — pure plan-terms rules (docs/p2/P2_DECISION_RECORD.md): calendar renewal only for monthly plans (2A),
/// explicit company scope (Q29), at least one covered service (2A), usage limits (Q13) and the window-vs-period rule (Q49),
/// pause rules by renewal anchor (Q5/Q12), money precision.
/// </summary>
public class MembershipPlanRulesTests
{
    private static readonly Guid Yoga = Guid.NewGuid();
    private static readonly Guid Pilates = Guid.NewGuid();

    public static MembershipPlanTermsRequest Terms(
        MembershipBillingInterval interval = MembershipBillingInterval.Monthly,
        MembershipRenewalAnchor anchor = MembershipRenewalAnchor.PurchaseDate,
        params MembershipUsageLimitDto[] limits) => new()
    {
        Price = 50m,
        StartFee = 10m,
        BillingInterval = interval,
        RenewalAnchor = anchor,
        CompanyScope = MembershipCompanyScope.AllCompanies,
        Services = new List<MembershipPlanCoveredServiceRequest> { new() { ServiceId = Yoga }, new() { ServiceId = Pilates } },
        UsageLimits = limits.ToList(),
        Pause = new MembershipPauseRulesDto { Allowed = false }
    };

    private static MembershipUsageLimitDto Limit(MembershipUsageWindow window, int max, Guid? serviceId = null) =>
        new() { Window = window, MaxUses = max, ServiceId = serviceId };

    private static void AssertInvalid(MembershipPlanTermsRequest terms, string code = null)
    {
        ValidationAppException ex = Assert.Throws<ValidationAppException>(() => MembershipPlanRules.Validate(terms));
        Assert.Equal(code, ex.Code);
    }

    private static List<WarningDto> Warnings(MembershipBillingInterval interval, params MembershipUsageLimitDto[] limits) =>
        MembershipPlanRules.LimitWarnings(interval, limits.Select(l => new MembershipLimitSpec(l.ServiceId, l.Window.Value, l.MaxUses)).ToList());

    [Fact]
    public void ValidTerms_Pass()
    {
        MembershipPlanRules.Validate(Terms(limits: new[] { Limit(MembershipUsageWindow.Period, 8), Limit(MembershipUsageWindow.Day, 1) }));
    }

    [Fact]
    public void CalendarRenewal_IsOnlyForMonthlyPlans()
    {
        MembershipPlanRules.Validate(Terms(MembershipBillingInterval.Monthly, MembershipRenewalAnchor.CalendarMonth));
        AssertInvalid(Terms(MembershipBillingInterval.Yearly, MembershipRenewalAnchor.CalendarMonth));
    }

    [Fact]
    public void SelectedCompanies_RequireAtLeastOne_AndAnEmptyListNeverMeansAll()
    {
        MembershipPlanTermsRequest selected = Terms();
        selected.CompanyScope = MembershipCompanyScope.SelectedCompanies;
        AssertInvalid(selected, ErrorCodes.MembershipPlanCompaniesRequired);

        selected.CompanyIds = new List<Guid> { Guid.NewGuid() };
        MembershipPlanRules.Validate(selected);

        MembershipPlanTermsRequest all = Terms();
        all.CompanyIds = new List<Guid> { Guid.NewGuid() };
        AssertInvalid(all);
    }

    [Fact]
    public void Plan_MustCoverAtLeastOneService_WithoutDuplicates()
    {
        MembershipPlanTermsRequest none = Terms();
        none.Services = new List<MembershipPlanCoveredServiceRequest>();
        AssertInvalid(none);

        MembershipPlanTermsRequest duplicate = Terms();
        duplicate.Services.Add(new MembershipPlanCoveredServiceRequest { ServiceId = Yoga });
        AssertInvalid(duplicate);
    }

    [Fact]
    public void Limits_ReferToCoveredServices_AndAreUniquePerScopeAndWindow()
    {
        AssertInvalid(Terms(limits: Limit(MembershipUsageWindow.Day, 1, Guid.NewGuid())));
        AssertInvalid(Terms(limits: new[] { Limit(MembershipUsageWindow.Day, 1), Limit(MembershipUsageWindow.Day, 2) }));
        MembershipPlanRules.Validate(Terms(limits: new[] { Limit(MembershipUsageWindow.Day, 1), Limit(MembershipUsageWindow.Day, 1, Yoga) }));
    }

    [Fact]
    public void Q49_MonthlyPlanWithCredits_MonthWindowIsRejected_ShorterMustBeSmaller_LongerMustBeLarger()
    {
        MembershipUsageLimitDto credits = Limit(MembershipUsageWindow.Period, 8);
        AssertInvalid(Terms(limits: new[] { credits, Limit(MembershipUsageWindow.Month, 4) }), ErrorCodes.MembershipUsageLimitInvalid);
        AssertInvalid(Terms(limits: new[] { credits, Limit(MembershipUsageWindow.Week, 8) }), ErrorCodes.MembershipUsageLimitInvalid);
        AssertInvalid(Terms(limits: new[] { credits, Limit(MembershipUsageWindow.Quarter, 8) }), ErrorCodes.MembershipUsageLimitInvalid);

        MembershipPlanRules.Validate(Terms(limits: new[] { credits, Limit(MembershipUsageWindow.Week, 2), Limit(MembershipUsageWindow.Quarter, 20) }));
    }

    [Fact]
    public void Q49_YearlyPlan_AllowsAMonthlyWindowBelowTheYearlyCredits()
    {
        MembershipPlanRules.Validate(Terms(MembershipBillingInterval.Yearly,
            limits: new[] { Limit(MembershipUsageWindow.Period, 100), Limit(MembershipUsageWindow.Month, 10) }));
        AssertInvalid(Terms(MembershipBillingInterval.Yearly,
            limits: new[] { Limit(MembershipUsageWindow.Period, 100), Limit(MembershipUsageWindow.Quarter, 100) }), ErrorCodes.MembershipUsageLimitInvalid);
    }

    [Fact]
    public void Q49_PlanWithoutCredits_AllowsEveryWindow()
    {
        MembershipPlanRules.Validate(Terms(limits: new[] { Limit(MembershipUsageWindow.Month, 30), Limit(MembershipUsageWindow.Day, 1) }));
    }

    [Fact]
    public void Q49_ServiceWindow_ComparesWithServiceCredits_ElseWithPlanCredits()
    {
        // Service credits (2) win over plan credits (8): a service week window of 3 is not below them.
        AssertInvalid(Terms(limits: new[]
        {
            Limit(MembershipUsageWindow.Period, 8), Limit(MembershipUsageWindow.Period, 2, Yoga), Limit(MembershipUsageWindow.Week, 3, Yoga)
        }), ErrorCodes.MembershipUsageLimitInvalid);

        // Without service credits the plan credits apply.
        AssertInvalid(Terms(limits: new[] { Limit(MembershipUsageWindow.Period, 8), Limit(MembershipUsageWindow.Week, 8, Pilates) }),
            ErrorCodes.MembershipUsageLimitInvalid);
        MembershipPlanRules.Validate(Terms(limits: new[] { Limit(MembershipUsageWindow.Period, 8), Limit(MembershipUsageWindow.Week, 3, Pilates) }));
    }

    [Fact]
    public void Q49_LongerWindowAtOrAboveCreditsTimesPeriods_IsAWarningNotAnError()
    {
        MembershipUsageLimitDto[] limits = { Limit(MembershipUsageWindow.Period, 8), Limit(MembershipUsageWindow.Quarter, 30) };
        MembershipPlanRules.Validate(Terms(limits: limits));

        WarningDto warning = Assert.Single(Warnings(MembershipBillingInterval.Monthly, limits));
        Assert.Equal(WarningCodes.MembershipLimitWithoutEffect, warning.Code);
        WarningMembershipLimitDetails details = Assert.IsType<WarningMembershipLimitDetails>(warning.Details);
        Assert.Equal(("Quarter", 30, "Period", 8), (details.Window, details.MaxUses, details.ComparedWithWindow, details.ComparedWithMaxUses));

        Assert.Empty(Warnings(MembershipBillingInterval.Monthly, Limit(MembershipUsageWindow.Period, 8), Limit(MembershipUsageWindow.Quarter, 20)));
    }

    [Fact]
    public void Q49_ServiceLimitAtOrAbovePlanLimitForTheSameWindow_IsAWarning()
    {
        WarningDto warning = Assert.Single(Warnings(MembershipBillingInterval.Monthly,
            Limit(MembershipUsageWindow.Day, 1), Limit(MembershipUsageWindow.Day, 2, Yoga)));
        Assert.Equal(WarningCodes.MembershipLimitWithoutEffect, warning.Code);

        Assert.Empty(Warnings(MembershipBillingInterval.Monthly, Limit(MembershipUsageWindow.Period, 8), Limit(MembershipUsageWindow.Period, 2, Yoga)));
    }

    [Fact]
    public void Pause_ByDaysForPurchaseDatePlans_ByWholePeriodsForCalendarPlans()
    {
        MembershipPlanTermsRequest byDays = Terms();
        byDays.Pause = new MembershipPauseRulesDto { Allowed = true, MaxPauseDays = 30, MaxPausesPer12Months = 2, ExtendsPeriod = true };
        MembershipPlanRules.Validate(byDays);
        byDays.Pause.MaxPausePeriods = 1;
        AssertInvalid(byDays);

        MembershipPlanTermsRequest calendar = Terms(anchor: MembershipRenewalAnchor.CalendarMonth);
        calendar.Pause = new MembershipPauseRulesDto { Allowed = true, MaxPausePeriods = 1 };
        MembershipPlanRules.Validate(calendar);
        calendar.Pause.ExtendsPeriod = true;
        AssertInvalid(calendar);
        calendar.Pause = new MembershipPauseRulesDto { Allowed = true, MaxPauseDays = 10 };
        AssertInvalid(calendar);

        MembershipPlanTermsRequest disabled = Terms();
        disabled.Pause = new MembershipPauseRulesDto { Allowed = false, MaxPauseDays = 10 };
        AssertInvalid(disabled);
    }

    [Fact]
    public void AllowedPause_RequiresAMaximumDuration_ButTheCountPer12MonthsMayBeUnlimited()
    {
        MembershipPlanTermsRequest byDays = Terms();
        byDays.Pause = new MembershipPauseRulesDto { Allowed = true, MaxPausesPer12Months = 2 };
        AssertInvalid(byDays);
        byDays.Pause = new MembershipPauseRulesDto { Allowed = true, MaxPauseDays = 30 };
        MembershipPlanRules.Validate(byDays);

        MembershipPlanTermsRequest calendar = Terms(anchor: MembershipRenewalAnchor.CalendarMonth);
        calendar.Pause = new MembershipPauseRulesDto { Allowed = true, MaxPausesPer12Months = 2 };
        AssertInvalid(calendar);
        calendar.Pause = new MembershipPauseRulesDto { Allowed = true, MaxPausePeriods = 2 };
        MembershipPlanRules.Validate(calendar);
    }

    [Fact]
    public void Money_HasAtMostTwoDecimals_AndCommitmentAndNoticeAreAtLeastOne()
    {
        MembershipPlanTermsRequest price = Terms();
        price.Price = 10.005m;
        AssertInvalid(price);

        MembershipPlanTermsRequest commitment = Terms();
        commitment.MinimumCommitmentPeriods = 0;
        AssertInvalid(commitment);

        MembershipPlanTermsRequest notice = Terms();
        notice.CancellationNoticeDays = 0;
        AssertInvalid(notice);
    }
}
