using System;
using System.Collections.Generic;
using System.Linq;
using BlueDragon.DuneLight.Core.Enums;
using BlueDragon.DuneLight.Infrastructure.Domain.Models.Catalog;
using BlueDragon.DuneLight.Infrastructure.Domain.Models.Clients;

namespace BlueDragon.DuneLight.Infrastructure.Utils;

/// <summary>Sudjelovanje kakvo vidi evaluacija pokrića: usluga segmenta, poslovnica termina, datum termina u kalendaru
/// poslovnice (prozori, Q17) i organizacije (period, Q22), te aktivno novčano namirenje.</summary>
public readonly record struct MembershipCoverageSubject(
    Guid ParticipationId, Guid ServiceId, Guid CompanyId, DateOnly ServiceDate, DateOnly PeriodDate, decimal ActiveMonetarySettlement);

/// <summary>Aktivan claim članstva (za brojanje limita).</summary>
public readonly record struct MembershipClaimView(Guid UsageId, Guid ParticipationId, Guid ServiceId, DateOnly ServiceDate, DateOnly PeriodDate);

/// <summary>Iscrpljeni limit (Q13.4): prozor, usluga (null = limit plana), maksimum i iskorišteno.</summary>
public readonly record struct MembershipExhaustedLimit(MembershipUsageWindow Window, Guid? ServiceId, int MaxUses, int Used);

/// <summary>Ishod evaluacije: Covered/Claimed, NotCovered(razlog), PendingEvaluation(BeyondHorizon, očekivani period).</summary>
public sealed record MembershipCoverageDecision(
    MembershipCoverageStatus Status, MembershipCoverageReason Reason, MembershipExhaustedLimit? Limit = null, DateOnly? ExpectedPeriodStartsOn = null)
{
    public bool IsCovered => Status == MembershipCoverageStatus.Covered;
}

/// <summary>
/// P2 (faza 2D) — čista, deterministička pravila pokrića sudjelovanja članarinom (bez I/O; P2_PLAN §3.1, §11, §15.3):
/// - odgovorno članstvo za (usluga, poslovnica, datum): ono čiji uvjeti na taj datum pokrivaju uslugu u poslovnici (preklapanje
///   je zabranjeno, Q10, pa je najviše jedno); inače prvo članstvo koje vrijedi na datum (razlog Service/CompanyNotCovered);
///   inače nedavno završeno članstvo čiji je kraj prije termina (AfterMembershipEnd, Q27); inače nijedno (ponašanje kao bez P2);
/// - redoslijed provjera (prvi neuspjeh je razlog): kraj članstva, usluga, poslovnica, pauza (i preskočen period), dug (Q15),
///   već plaćeno novcem (2D), horizont tekući + sljedeći period (Q27), limiti (AND, Q13; prvo limiti plana pa usluge, prozori
///   Period, Day, Week, Month, Quarter). Uvjeti na datum termina su uvjeti koji tada vrijede (zakazana promjena od svog datuma).
/// - brojač limita = aktivni claimovi članstva u prozoru (Period po datumu u kalendaru organizacije, ostali kalendarski po datumu
///   u kalendaru poslovnice; tjedan pon–ned), bez vlastitog claima sudjelovanja.
/// Zahtijeva članstvo s grafom (pauze, uvjeti i zakazani uvjeti s uslugama, poslovnicama i limitima).
/// </summary>
public static class MembershipCoverageRules
{
    private static readonly MembershipUsageWindow[] WindowOrder =
    {
        MembershipUsageWindow.Period, MembershipUsageWindow.Day, MembershipUsageWindow.Week, MembershipUsageWindow.Month,
        MembershipUsageWindow.Quarter
    };

    public static bool IsValidOn(ClientMembership membership, DateOnly date) =>
        membership.VoidedAt == null && membership.StartsOn <= date && (membership.EndsOn == null || date <= membership.EndsOn.Value);

    /// <summary>Uvjeti koji vrijede na datum: zakazana promjena od stvarnog datuma stupanja na snagu, inače trenutni.</summary>
    public static MembershipPlanVersion TermsOn(ClientMembership membership, DateOnly date)
    {
        if (membership.PendingPlanVersion == null)
            return membership.PlanVersion;
        DateOnly? pendingOn = MembershipTimelines.PendingEffectiveOn(membership);
        return pendingOn.HasValue && date >= pendingOn.Value ? membership.PendingPlanVersion : membership.PlanVersion;
    }

    public static bool CoversService(MembershipPlanVersion terms, Guid serviceId) => terms.Services.Any(s => s.ServiceId == serviceId);

    public static bool CoversCompany(MembershipPlanVersion terms, Guid companyId) =>
        terms.CompanyScope == MembershipCompanyScope.AllCompanies || terms.Companies.Any(c => c.CompanyId == companyId);

    /// <summary>Odgovorno članstvo za sudjelovanje (vidi opis klase) ili null.</summary>
    public static ClientMembership Responsible(
        IEnumerable<ClientMembership> memberships, Guid serviceId, Guid companyId, DateOnly periodDate, DateOnly today)
    {
        List<ClientMembership> candidates = memberships.Where(m => m.VoidedAt == null).OrderBy(m => m.StartsOn).ThenBy(m => m.Id).ToList();
        List<ClientMembership> valid = candidates.Where(m => IsValidOn(m, periodDate)).ToList();
        ClientMembership covering = valid.FirstOrDefault(m =>
        {
            MembershipPlanVersion terms = TermsOn(m, periodDate);
            return CoversService(terms, serviceId) && CoversCompany(terms, companyId);
        });
        if (covering != null)
            return covering;
        if (valid.Count > 0)
            return valid[0];
        return candidates
            .Where(m => m.StartsOn <= periodDate && m.EndsOn.HasValue && m.EndsOn.Value < periodDate && m.EndsOn.Value >= today)
            .OrderByDescending(m => m.EndsOn)
            .FirstOrDefault();
    }

    /// <summary>Q26 — plan "s kreditima perioda" = postoji limit prozora Period koji vrijedi za uslugu (limit plana ili usluge).</summary>
    public static bool HasPeriodCredits(MembershipPlanVersion terms, Guid serviceId) =>
        terms.UsageLimits.Any(l => l.Window == MembershipUsageWindow.Period && (l.ServiceId == null || l.ServiceId == serviceId));

    /// <summary>Prihvatljivost bez limita, plaćenosti i horizonta (provjera postojećeg claima): null = prihvatljivo, inače razlog.</summary>
    public static MembershipCoverageReason? Ineligibility(
        ClientMembership membership, MembershipCoverageSubject subject, MembershipStanding standing, MembershipDebtBehavior debtBehavior)
    {
        if (membership.VoidedAt != null)
            return MembershipCoverageReason.MembershipVoided;
        if (membership.EndsOn.HasValue && subject.PeriodDate > membership.EndsOn.Value)
            return MembershipCoverageReason.AfterMembershipEnd;
        if (subject.PeriodDate < membership.StartsOn)
            return MembershipCoverageReason.BeforeMembershipStart;

        MembershipPlanVersion terms = TermsOn(membership, subject.PeriodDate);
        if (!CoversService(terms, subject.ServiceId))
            return MembershipCoverageReason.ServiceNotCovered;
        if (!CoversCompany(terms, subject.CompanyId))
            return MembershipCoverageReason.CompanyNotCovered;

        // K1-8: stajanje zbog zatvorenih poslovnica ima zaseban razlog (prikaz), mehanika je ista kao pauza.
        if (IsStandingStillOn(membership, subject.PeriodDate))
            return MembershipCoverageReason.MembershipStandingCompanyClosed;
        if (MembershipTimelines.PauseSpans(membership).Any(p => p.StartsOn <= subject.PeriodDate && p.EndsOn >= subject.PeriodDate)
            || PeriodOf(membership, subject.PeriodDate).Skipped)
            return MembershipCoverageReason.Paused;

        if (standing == MembershipStanding.Delinquent && debtBehavior != MembershipDebtBehavior.KeepCovering)
            return MembershipCoverageReason.DebtNotCovered;
        return null;
    }

    /// <summary>Puna evaluacija novog pokrića (sudjelovanje bez aktivnog claima, ili s vlastitim claimom koji se ne broji).</summary>
    public static MembershipCoverageDecision Evaluate(
        ClientMembership membership, MembershipCoverageSubject subject, IReadOnlyCollection<MembershipClaimView> activeClaims,
        DateOnly today, MembershipStanding standing, MembershipDebtBehavior debtBehavior)
    {
        MembershipCoverageReason? ineligible = Ineligibility(membership, subject, standing, debtBehavior);
        if (ineligible.HasValue)
            return new MembershipCoverageDecision(MembershipCoverageStatus.NotCovered, ineligible.Value);

        if (subject.ActiveMonetarySettlement > 0m)
            return new MembershipCoverageDecision(MembershipCoverageStatus.NotCovered, MembershipCoverageReason.AlreadyPaid);

        MembershipTimeline timeline = MembershipTimelines.WithPending(membership);
        MembershipPeriod target = PeriodOf(membership, subject.PeriodDate);
        if (subject.PeriodDate > today && target.Sequence >= 0)
        {
            MembershipPeriod current = MembershipPeriodCalendar.PeriodContaining(timeline, today > timeline.StartsOn ? today : timeline.StartsOn);
            if (target.Sequence > current.Sequence + 1)
                return new MembershipCoverageDecision(MembershipCoverageStatus.PendingEvaluation, MembershipCoverageReason.BeyondHorizon,
                    ExpectedPeriodStartsOn: target.StartsOn);
        }

        MembershipExhaustedLimit? exhausted = FirstExhaustedLimit(TermsOn(membership, subject.PeriodDate), target, subject, activeClaims);
        return exhausted.HasValue
            ? new MembershipCoverageDecision(MembershipCoverageStatus.NotCovered, MembershipCoverageReason.LimitReached, exhausted)
            : new MembershipCoverageDecision(MembershipCoverageStatus.Covered, MembershipCoverageReason.Claimed);
    }

    /// <summary>Period koji sadrži datum: od sidra trenutnih uvjeta računa ga kalendar perioda; raniji datumi (prije promjene
    /// uvjeta) čitaju se iz otvorenih (materijaliziranih) perioda, Sequence = -1.</summary>
    public static MembershipPeriod PeriodOf(ClientMembership membership, DateOnly date)
    {
        MembershipTimeline timeline = MembershipTimelines.WithPending(membership);
        if (date >= timeline.StartsOn)
            return MembershipPeriodCalendar.PeriodContaining(timeline, date);
        ClientMembershipPeriod opened = membership.Periods.FirstOrDefault(p => p.StartsOn <= date && p.EndsOn >= date);
        return opened != null
            ? new MembershipPeriod(-1, opened.StartsOn, opened.EndsOn, Skipped: false)
            : new MembershipPeriod(-1, date, date, Skipped: false);
    }

    private static MembershipExhaustedLimit? FirstExhaustedLimit(
        MembershipPlanVersion terms, MembershipPeriod period, MembershipCoverageSubject subject, IReadOnlyCollection<MembershipClaimView> activeClaims)
    {
        IEnumerable<MembershipPlanUsageLimit> applicable = terms.UsageLimits
            .Where(l => l.ServiceId == null || l.ServiceId == subject.ServiceId)
            .OrderBy(l => l.ServiceId == null ? 0 : 1)
            .ThenBy(l => Array.IndexOf(WindowOrder, l.Window));
        foreach (MembershipPlanUsageLimit limit in applicable)
        {
            int used = activeClaims.Count(c =>
                c.ParticipationId != subject.ParticipationId
                && (limit.ServiceId == null || c.ServiceId == limit.ServiceId)
                && InWindow(limit.Window, period, subject.ServiceDate, c));
            if (used >= limit.MaxUses)
                return new MembershipExhaustedLimit(limit.Window, limit.ServiceId, limit.MaxUses, used);
        }

        return null;
    }

    private static bool InWindow(MembershipUsageWindow window, MembershipPeriod period, DateOnly serviceDate, MembershipClaimView claim)
    {
        if (window == MembershipUsageWindow.Period)
            return period.Contains(claim.PeriodDate);
        (DateOnly start, DateOnly end) = CalendarWindow(window, serviceDate);
        return claim.ServiceDate >= start && claim.ServiceDate <= end;
    }

    /// <summary>Q17 — kalendarski prozor koji sadrži datum: dan, tjedan pon–ned, kalendarski mjesec, kvartal.</summary>
    public static (DateOnly Start, DateOnly End) CalendarWindow(MembershipUsageWindow window, DateOnly date)
    {
        switch (window)
        {
            case MembershipUsageWindow.Day:
                return (date, date);
            case MembershipUsageWindow.Week:
                int sinceMonday = ((int)date.DayOfWeek + 6) % 7;
                DateOnly monday = date.AddDays(-sinceMonday);
                return (monday, monday.AddDays(6));
            case MembershipUsageWindow.Month:
                DateOnly first = new(date.Year, date.Month, 1);
                return (first, first.AddMonths(1).AddDays(-1));
            case MembershipUsageWindow.Quarter:
                DateOnly quarter = new(date.Year, (date.Month - 1) / 3 * 3 + 1, 1);
                return (quarter, quarter.AddMonths(3).AddDays(-1));
            default:
                throw new ArgumentOutOfRangeException(nameof(window), "Period nije kalendarski prozor.");
        }
    }

    /// <summary>K1-8 — dan pod sustavnom pauzom (stajanje zbog zatvorenih poslovnica), otvorenom ili zatvorenom.</summary>
    public static bool IsStandingStillOn(ClientMembership membership, DateOnly date) =>
        MembershipTimelines.PauseSpans(membership).Any(p => p.IsSystem && p.StartsOn <= date && p.EndsOn >= date);

    /// <summary>Razlog storna claima kad članstvo više ne pokriva sudjelovanje.</summary>
    public static MembershipUsageReleaseReason ReleaseReasonFor(MembershipCoverageReason reason) => reason switch
    {
        MembershipCoverageReason.Paused or MembershipCoverageReason.MembershipStandingCompanyClosed => MembershipUsageReleaseReason.Paused,
        MembershipCoverageReason.AfterMembershipEnd => MembershipUsageReleaseReason.AfterMembershipEnd,
        MembershipCoverageReason.DebtNotCovered => MembershipUsageReleaseReason.DebtNotCovered,
        _ => MembershipUsageReleaseReason.TermsChanged
    };
}
