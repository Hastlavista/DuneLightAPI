using System;
using System.Collections.Generic;
using System.Linq;
using BlueDragon.DuneLight.Core.Enums;
using BlueDragon.DuneLight.Core.Shared;
using BlueDragon.DuneLight.Core.Shared.Exceptions;

namespace BlueDragon.DuneLight.Infrastructure.Utils;

/// <summary>Pravila pauze iz verzije plana (Q5/Q12).</summary>
public readonly record struct MembershipPauseRules(
    bool Allowed, int? MaxPauseDays, int? MaxPausePeriods, int? MaxPausesPer12Months);

/// <summary>Iskorištenost i preostalo u jednom 12-mjesečnom prozoru od početka članstva (rolling, Q5).</summary>
public readonly record struct MembershipPauseUsage(
    DateOnly WindowStartsOn, DateOnly WindowEndsOn, int UsedDays, int UsedPeriods, int UsedPauses);

/// <summary>
/// P2 (faza 2B) — čista pravila lifecycla članstva (bez I/O), u lokalnim datumima zone organizacije (Q22):
/// izvedeno stanje, datum od kad otkaz djeluje (kraj perioda, otkazni rok, minimalna obveza; prije početka = kraj prvog
/// perioda), granice datuma početka (Q45) i pauze (Q5/Q12: zbroj dana/perioda i broj pauza u 12 mjeseci od početka).
/// </summary>
public static class MembershipLifecycleRules
{
    public static MembershipState State(
        DateOnly today, DateOnly startsOn, DateOnly? endsOn, bool voided, IEnumerable<MembershipPauseSpan> pauses)
    {
        if (voided)
            return MembershipState.Voided;
        if (endsOn.HasValue && endsOn.Value < today)
            return MembershipState.Ended;
        if (startsOn > today)
            return MembershipState.Scheduled;
        return pauses.Any(p => p.StartsOn <= today && p.EndsOn >= today) ? MembershipState.Paused : MembershipState.Active;
    }

    /// <summary>Q45 — datum početka od danas do najviše mjesec dana unaprijed (isti dan sljedećeg mjeseca, kraj mjeseca se
    /// skraćuje kao kod obnove).</summary>
    public static void EnsureStartDate(DateOnly startsOn, DateOnly today)
    {
        if (startsOn < today || startsOn > today.AddMonths(1))
            throw new ValidationAppException(ErrorCodes.MembershipStartDateOutOfRange,
                $"Datum početka mora biti od {today:dd.MM.yyyy.} do {today.AddMonths(1):dd.MM.yyyy.}.");
    }

    /// <summary>Datum od kad otkaz zatražen na requestedOn djeluje: max(kraj tekućeg perioda, kraj perioda koji počinje obnovom
    /// najmanje noticeDays nakon zahtjeva, kraj N-tog nepauziranog perioda). Prije početka članstva: kraj prvog perioda, bez
    /// obveze i otkaznog roka (Q51 b).</summary>
    /// <remarks>Pregled 2C (#8): obveza trenutnih uvjeta broji se od <paramref name="commitmentFrom"/> (datum zadnje promjene
    /// uvjeta; null = početak linije), a <paramref name="commitmentFloor"/> je dosadašnji kraj obveze iz ranijih uvjeta — otkaz
    /// djeluje najranije na prvom kraju perioda na ili nakon njega. Pauza produljuje periode, pa time i obvezu.</remarks>
    public static (DateOnly EffectiveOn, MembershipEndEffectiveReason Reason) CancellationEffective(
        MembershipTimeline timeline, DateOnly requestedOn, int? minimumCommitmentPeriods, int? cancellationNoticeDays,
        DateOnly? commitmentFrom = null, DateOnly? commitmentFloor = null)
    {
        if (requestedOn < timeline.StartsOn)
            return (MembershipPeriodCalendar.Periods(timeline).First().EndsOn, MembershipEndEffectiveReason.BeforeStart);

        DateOnly endOfPeriod = MembershipPeriodCalendar.PeriodContaining(timeline, requestedOn).EndsOn;
        DateOnly effective = endOfPeriod;
        MembershipEndEffectiveReason reason = MembershipEndEffectiveReason.EndOfPeriod;

        if (cancellationNoticeDays.HasValue)
        {
            // Obnova (dan nakon kraja perioda) mora biti najmanje noticeDays nakon zahtjeva.
            DateOnly earliestEnd = requestedOn.AddDays(cancellationNoticeDays.Value - 1);
            DateOnly noticeEnd = MembershipPeriodCalendar.Periods(timeline).First(p => p.EndsOn >= earliestEnd && p.EndsOn >= endOfPeriod).EndsOn;
            if (noticeEnd > effective)
                (effective, reason) = (noticeEnd, MembershipEndEffectiveReason.NoticePeriod);
        }

        if (minimumCommitmentPeriods.HasValue)
        {
            DateOnly commitmentEnd = MembershipPeriodCalendar.EndOfNthUnskippedPeriod(
                timeline, minimumCommitmentPeriods.Value, commitmentFrom ?? timeline.StartsOn);
            if (commitmentEnd > effective)
                (effective, reason) = (commitmentEnd, MembershipEndEffectiveReason.MinimumCommitment);
        }

        if (commitmentFloor.HasValue)
        {
            DateOnly floorEnd = MembershipPeriodCalendar.Periods(timeline).First(p => p.EndsOn >= commitmentFloor.Value).EndsOn;
            if (floorEnd > effective)
                (effective, reason) = (floorEnd, MembershipEndEffectiveReason.MinimumCommitment);
        }

        return (effective, reason);
    }

    /// <summary>12-mjesečni prozor (od početka članstva) koji sadrži datum.</summary>
    public static (DateOnly StartsOn, DateOnly EndsOn) PauseWindow(DateOnly membershipStartsOn, DateOnly date)
    {
        int k = 0;
        while (membershipStartsOn.AddMonths(12 * (k + 1)) <= date)
            k++;
        DateOnly start = membershipStartsOn.AddMonths(12 * k);
        return (start, membershipStartsOn.AddMonths(12 * (k + 1)).AddDays(-1));
    }

    public static MembershipPauseUsage PauseUsage(MembershipTimeline timeline, DateOnly date)
    {
        (DateOnly windowStart, DateOnly windowEnd) = PauseWindow(timeline.MembershipStartsOn ?? timeline.StartsOn, date);
        int usedDays = timeline.Pauses
            .Where(p => p.Kind == MembershipPauseKind.Days)
            .Sum(p => OverlapDays(p.StartsOn, p.EndsOn, windowStart, windowEnd));
        int usedPeriods = MembershipPeriodCalendar.Periods(timeline)
            .TakeWhile(p => p.StartsOn <= windowEnd)
            .Count(p => p.Skipped && p.StartsOn >= windowStart);
        int usedPauses = timeline.Pauses.Count(p => p.StartsOn >= windowStart && p.StartsOn <= windowEnd);
        return new MembershipPauseUsage(windowStart, windowEnd, usedDays, usedPeriods, usedPauses);
    }

    /// <summary>Valjanost nove pauze. Timeline sadrži postojeće (neotkazane) pauze BEZ nove; candidate je nova pauza.
    /// Kalendarski plan: pauza počinje prvim danom perioda i traje cijele periode.</summary>
    public static void EnsurePauseAllowed(
        MembershipTimeline timeline, MembershipPauseRules rules, MembershipPauseSpan candidate, DateOnly today, bool endScheduled)
    {
        if (!rules.Allowed)
            throw new BusinessRuleException(ErrorCodes.MembershipPauseNotAllowed, "Plan članarine ne dopušta pauzu.");
        if (endScheduled)
            throw new BusinessRuleException(ErrorCodes.MembershipPauseNotAllowed,
                "Članstvo ima zakazan završetak; pauza nije dopuštena (povucite otkaz ako ga treba).");
        if (candidate.StartsOn < today || candidate.StartsOn < timeline.StartsOn)
            throw new BusinessRuleException(ErrorCodes.MembershipPauseNotAllowed, "Pauza počinje danas ili kasnije, ne prije početka članstva.");
        if (candidate.EndsOn < candidate.StartsOn)
            throw new BusinessRuleException(ErrorCodes.MembershipPauseNotAllowed, "Kraj pauze je prije početka.");
        if (timeline.Pauses.Any(p => p.StartsOn <= candidate.EndsOn && p.EndsOn >= candidate.StartsOn))
            throw new BusinessRuleException(ErrorCodes.MembershipPauseNotAllowed, "Pauza se preklapa s drugom pauzom.");

        bool calendar = timeline.Terms.Anchor == MembershipRenewalAnchor.CalendarMonth;
        if (calendar != (candidate.Kind == MembershipPauseKind.SkipPeriods))
            throw new BusinessRuleException(ErrorCodes.MembershipPauseNotAllowed,
                calendar ? "Kalendarski plan pauzira u cijelim periodima." : "Plan od datuma kupnje pauzira po danima.");
        if (calendar && !MembershipPeriodCalendar.Periods(timeline).TakeWhile(p => p.StartsOn <= candidate.StartsOn)
                .Any(p => p.StartsOn == candidate.StartsOn && !p.Skipped))
            throw new BusinessRuleException(ErrorCodes.MembershipPauseNotAllowed, "Kalendarska pauza počinje prvim danom perioda.");

        // Q5 — zbroj u svakom 12-mjesečnom prozoru koji pauza dotiče (dani ili preskočeni periodi) i broj pauza.
        MembershipTimeline withCandidate = timeline with { Pauses = timeline.Pauses.Append(candidate).ToList() };
        DateOnly windowDate = candidate.StartsOn;
        while (windowDate <= candidate.EndsOn)
        {
            MembershipPauseUsage usage = PauseUsage(withCandidate, windowDate);
            if (!calendar && rules.MaxPauseDays.HasValue && usage.UsedDays > rules.MaxPauseDays.Value)
                throw new BusinessRuleException(ErrorCodes.MembershipPauseLimitExceeded,
                    $"Pauza prelazi dopušteni zbroj od {rules.MaxPauseDays} dana u 12 mjeseci.");
            if (calendar && rules.MaxPausePeriods.HasValue && usage.UsedPeriods > rules.MaxPausePeriods.Value)
                throw new BusinessRuleException(ErrorCodes.MembershipPauseLimitExceeded,
                    $"Pauza prelazi dopušteni zbroj od {rules.MaxPausePeriods} perioda u 12 mjeseci.");
            if (rules.MaxPausesPer12Months.HasValue && usage.UsedPauses > rules.MaxPausesPer12Months.Value)
                throw new BusinessRuleException(ErrorCodes.MembershipPauseLimitExceeded,
                    $"Dopušteno je najviše {rules.MaxPausesPer12Months} pauza u 12 mjeseci.");
            windowDate = usage.WindowEndsOn.AddDays(1);
        }
    }

    private static int OverlapDays(DateOnly start, DateOnly end, DateOnly windowStart, DateOnly windowEnd)
    {
        DateOnly from = start > windowStart ? start : windowStart;
        DateOnly to = end < windowEnd ? end : windowEnd;
        return to < from ? 0 : to.DayNumber - from.DayNumber + 1;
    }
}
