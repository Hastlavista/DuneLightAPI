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
        {
            List<Interval> overrideIntervals = overrides.Select(e => new Interval(e.StartTime!.Value, e.EndTime!.Value)).ToList();
            return (MergeIntervals(overrideIntervals), AvailabilitySource.Override);
        }

        if (template == null)
            return (new List<Interval>(), AvailabilitySource.None);

        return (GetTemplateIntervals(template, date), AvailabilitySource.Template);
    }

    /// <summary>Company-grana: FAZA 1 nema per-datum override za poslovnicu (vidi otvoreno pitanje #2) — samo predložak,
    /// praznik, ili "nema predloška". Praznik pobjeđuje predložak (poslovnica ne radi taj dan bez obzira što predložak
    /// možda ima radne intervale) — holidaysForDate mora sadržavati SAMO praznike TE poslovnice (filtrirano od pozivatelja).</summary>
    public static (List<Interval> Intervals, AvailabilitySource Source) GetEffectiveCompanyIntervals(
        WorkingHoursTemplate template, List<CompanyHoliday> holidaysForDate, DateTimeOffset date)
    {
        if (holidaysForDate.Any(h => h.Date.Date == date.Date))
            return (new List<Interval>(), AvailabilitySource.Holiday);

        if (template == null)
            return (new List<Interval>(), AvailabilitySource.None);

        return (GetTemplateIntervals(template, date), AvailabilitySource.Template);
    }

    /// <summary>Bi li zaposlenik prema planu TREBAO raditi taj dan, potpuno zanemarujući apsencije — koristi se
    /// isključivo za LeaveFund working-day izračun (vidi RosterEntryService.CountExpectedWorkDays), NE za
    /// stvarnu dostupnost (za to vidi GetEffectiveEmployeeIntervals, koje apsenciju stavlja na prvo mjesto).
    /// Namjerno bez apsencijske grane — apsencije se ovdje ne smiju rekurzivno gledati (postojeći bolovanje/
    /// godišnji zapis ne smije "sakriti" činjenicu da bi taj dan inače bio radni). workEntriesForDate mora
    /// sadržavati SAMO ne-apsencijske (rad) RosterEntry redove za TOČNO taj EmployeeId+datum — override pobjeđuje
    /// predložak (bilo koji override work-zapis znači radni dan, jer work-zapis uvijek ima StartTime/EndTime);
    /// bez override-a koristi se predložak; bez predloška dan se ne broji kao radni (fail-closed).</summary>
    public static bool IsExpectedWorkDay(WorkingHoursTemplate template, List<RosterEntry> workEntriesForDate, DateTimeOffset date)
    {
        bool hasWorkOverride = workEntriesForDate.Any(e => e.IsOverride);
        if (hasWorkOverride)
            return true;

        if (template == null)
            return false;

        return GetTemplateIntervals(template, date).Count > 0;
    }

    /// <summary>Termin mora u cijelosti stati u JEDAN interval — ne smije premostiti pauzu između dva intervala istog dana.</summary>
    public static bool IsWithinIntervals(List<Interval> intervals, TimeSpan start, TimeSpan end)
    {
        return intervals.Any(iv => iv.Start <= start && end <= iv.End);
    }

    /// <summary>Spaja preklapajuće/dodirujuće intervale u jedan (npr. dva preklapajuća override work-zapisa istog dana,
    /// vidi ROSTER_ENTRY_OVERLAP upozorenje — preklapanje se dopušta, ali efektivna dostupnost mora ostati
    /// deterministična i bez duplih raspona).</summary>
    private static List<Interval> MergeIntervals(List<Interval> intervals)
    {
        if (intervals.Count <= 1)
            return intervals;

        List<Interval> ordered = intervals.OrderBy(i => i.Start).ToList();
        List<Interval> merged = new List<Interval> { ordered[0] };

        for (int i = 1; i < ordered.Count; i++)
        {
            Interval current = ordered[i];
            Interval last = merged[^1];

            if (current.Start <= last.End)
                merged[^1] = new Interval(last.Start, current.End > last.End ? current.End : last.End);
            else
                merged.Add(current);
        }

        return merged;
    }

    /// <summary>Oduzima busy raspone (npr. ScheduleBreak) od slobodnih intervala — vraća preostale slobodne
    /// pod-intervale nakon uklanjanja SVAKOG preklapajućeg dijela (djelomično preklapanje, potpuno pokrivanje
    /// cijelog intervala, više busy raspona odjednom, dodirujuće granice — sve podržano). "Preklapanje" je isti
    /// pojam kao AppointmentService.GenerateSlots (start &lt; busy.End &amp;&amp; busy.Start &lt; end, strogo —
    /// dodirujuće granice se NE broje kao preklapanje), samo primijenjen na cijele intervale umjesto na
    /// kandidat-slotove — koristi OperationalDashboardService da operativna dostupnost osoblja ne kaže "radi"
    /// kad zakazivanje (GenerateSlots) tu istu minutu smatra blokiranom pauzom.</summary>
    public static List<Interval> SubtractIntervals(List<Interval> source, List<(TimeSpan Start, TimeSpan End)> busy)
    {
        if (busy.Count == 0)
            return source;

        List<Interval> remaining = source;

        foreach ((TimeSpan Start, TimeSpan End) b in busy)
        {
            List<Interval> next = new List<Interval>();

            foreach (Interval interval in remaining)
            {
                if (b.End <= interval.Start || b.Start >= interval.End)
                {
                    next.Add(interval);
                    continue;
                }

                if (b.Start > interval.Start)
                    next.Add(new Interval(interval.Start, b.Start));

                if (b.End < interval.End)
                    next.Add(new Interval(b.End, interval.End));
            }

            remaining = next;
        }

        return remaining;
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