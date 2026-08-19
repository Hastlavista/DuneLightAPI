using System;
using System.Collections.Generic;
using System.Linq;
using BlueDragon.DuneLight.Core.Enums;
using BlueDragon.DuneLight.Infrastructure.Domain.Models.Roster;

namespace BlueDragon.DuneLight.Infrastructure.Utils;

/// <summary>
/// Čisti izračun dostupnosti prema WorkingHoursTemplate (FAZA 1 dizajn). Bez DB pristupa — pozivatelj
/// (AppointmentService, WorkingHoursTemplateService) učitava predložak/roster redove i predaje ih ovdje,
/// što omogućava da se za /recurring predložak+roster učitaju JEDNOM za cijeli raspon, a provjera po
/// occurrenceu radi u memoriji (isti obrazac kao EnsureNoRecurringConflicts).
/// </summary>
public static class WorkingHoursCalculator
{
    public readonly record struct Interval(TimeSpan Start, TimeSpan End);

    /// <summary>0-based tjedan ciklusa za zadani datum, poravnat na dan-u-tjednu AnchorDate-a (ispravan modulo i za datume prije anchora).</summary>
    public static int GetCycleWeekIndex(WorkingHoursCycleType cycleType, DateTimeOffset anchorDate, DateTimeOffset targetDate)
    {
        int cycleLength = CycleLength(cycleType);
        int weeksSinceAnchor = (int)Math.Floor((targetDate.Date - anchorDate.Date).TotalDays / 7.0);
        return ((weeksSinceAnchor % cycleLength) + cycleLength) % cycleLength;
    }

    private static int CycleLength(WorkingHoursCycleType cycleType) => cycleType switch
    {
        WorkingHoursCycleType.Weekly => 1,
        WorkingHoursCycleType.Fortnightly => 2,
        WorkingHoursCycleType.FourWeekly => 4,
        _ => throw new ArgumentOutOfRangeException(nameof(cycleType))
    };

    /// <summary>Sirovi intervali predloška za taj datum (bez override/apsencije) — prazno = legitiman slobodan dan po predlošku.</summary>
    public static List<Interval> GetTemplateIntervals(WorkingHoursTemplate template, DateTimeOffset date)
    {
        int cycleWeekIndex = GetCycleWeekIndex(template.CycleType, template.AnchorDate, date);
        return template.Intervals
            .Where(i => i.CycleWeekIndex == cycleWeekIndex && i.DayOfWeek == date.DayOfWeek)
            .Select(i => new Interval(i.StartTime, i.EndTime))
            .ToList();
    }

    /// <summary>
    /// Employee-grana: apsencija uvijek pobjeđuje (dan potpuno nedostupan) &gt; override work-redovi potpuno
    /// zamjenjuju predložak za taj dan &gt; predložak. Predložak==null ili nema intervala za taj dan = nedostupno
    /// (fail-closed, vidi FAZA 1). rosterEntriesForDate = RosterEntry redovi za TOČNO taj EmployeeId+datum
    /// (RosterType uključen).
    /// </summary>
    public static (List<Interval> Intervals, AvailabilitySource Source) GetEffectiveEmployeeIntervals(
        WorkingHoursTemplate template, List<RosterEntry> rosterEntriesForDate, DateTimeOffset date)
    {
        if (rosterEntriesForDate.Any(e => e.RosterType.IsAbsence))
            return (new List<Interval>(), AvailabilitySource.Absence);

        List<RosterEntry> overrides = rosterEntriesForDate.Where(e => !e.RosterType.IsAbsence && e.IsOverride).ToList();
        if (overrides.Count > 0)
            return (overrides.Select(e => new Interval(e.StartTime!.Value, e.EndTime!.Value)).ToList(), AvailabilitySource.Override);

        if (template == null)
            return (new List<Interval>(), AvailabilitySource.None);

        return (GetTemplateIntervals(template, date), AvailabilitySource.Template);
    }

    /// <summary>Company-grana: FAZA 1 nema per-datum override za lokaciju (vidi otvoreno pitanje #2) — samo predložak ili "nema predloška".</summary>
    public static (List<Interval> Intervals, AvailabilitySource Source) GetEffectiveCompanyIntervals(WorkingHoursTemplate template, DateTimeOffset date)
    {
        if (template == null)
            return (new List<Interval>(), AvailabilitySource.None);

        return (GetTemplateIntervals(template, date), AvailabilitySource.Template);
    }

    /// <summary>Termin mora u cijelosti stati u JEDAN interval — ne smije premostiti pauzu između dva intervala istog dana.</summary>
    public static bool IsWithinIntervals(List<Interval> intervals, TimeSpan start, TimeSpan end)
    {
        return intervals.Any(iv => iv.Start <= start && end <= iv.End);
    }

    /// <summary>Presjek dva popisa intervala (npr. employee ∩ company za GET /api/availability) — sve preklapajuće podintervale bilo kojeg para.</summary>
    public static List<Interval> IntersectIntervals(List<Interval> a, List<Interval> b)
    {
        List<Interval> result = new List<Interval>();
        foreach (Interval x in a)
        foreach (Interval y in b)
        {
            TimeSpan start = x.Start > y.Start ? x.Start : y.Start;
            TimeSpan end = x.End < y.End ? x.End : y.End;
            if (start < end)
                result.Add(new Interval(start, end));
        }

        return result;
    }
}