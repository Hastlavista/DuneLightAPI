using System;
using System.Collections.Generic;
using System.Linq;
using BlueDragon.DuneLight.Core.Enums;

namespace BlueDragon.DuneLight.Infrastructure.Utils;

/// <summary>Uvjeti koji određuju granice perioda jednog dijela vremenske linije članstva.</summary>
public readonly record struct MembershipPeriodTerms(
    MembershipBillingInterval Interval, MembershipRenewalAnchor Anchor, bool PauseExtendsPeriod);

/// <summary>Pauza kakvu vidi matematika perioda: zadnji dan koji stvarno vrijedi (raniji povratak skraćuje planirani).
/// K1-8: IsSystem = sustavna pauza "članstvo stoji" (CompanyClosure) — pomiče granice i kad plan ne produljuje pauzom, smije
/// početi točno na granici perioda i ne troši klijentove limite pauza.</summary>
public readonly record struct MembershipPauseSpan(MembershipPauseKind Kind, DateOnly StartsOn, DateOnly EndsOn, bool IsSystem = false)
{
    /// <summary>K1-8 — kraj otvorene sustavne pauze (kraj još nije poznat).</summary>
    public static readonly DateOnly OpenEnd = new(2999, 12, 31);

    public int Days => EndsOn.DayNumber - StartsOn.DayNumber + 1;

    /// <summary>Broj dana ove pauze koji se preklapaju s drugom pauzom.</summary>
    public int OverlapDays(MembershipPauseSpan other)
    {
        DateOnly from = StartsOn > other.StartsOn ? StartsOn : other.StartsOn;
        DateOnly to = EndsOn < other.EndsOn ? EndsOn : other.EndsOn;
        return to < from ? 0 : to.DayNumber - from.DayNumber + 1;
    }
}

/// <summary>Jedan period članstva. Skipped = kalendarski period u cijelosti preskočen pauzom (ne otvara se, nema zaduženja).</summary>
public readonly record struct MembershipPeriod(int Sequence, DateOnly StartsOn, DateOnly EndsOn, bool Skipped)
{
    public bool Contains(DateOnly date) => date >= StartsOn && date <= EndsOn;
}

/// <summary>
/// Vremenska linija članstva: početak, trenutni uvjeti, pauze i opcionalna zakazana promjena uvjeta (od prve obnove na ili
/// nakon NotBefore — pauze mogu pomaknuti obnovu, pa je zakazani datum donja granica, ne fiksni datum).
/// </summary>
public sealed record MembershipTimeline(
    DateOnly StartsOn,
    MembershipPeriodTerms Terms,
    IReadOnlyList<MembershipPauseSpan> Pauses,
    MembershipPeriodTerms? PendingTerms = null,
    DateOnly? PendingNotBefore = null)
{
    /// <summary>Početak članstva za rolling 12-mjesečne prozore pauza (Q5); kad su uvjeti promijenjeni, StartsOn linije je
    /// sidro trenutnih uvjeta, a prozori i dalje kreću od početka članstva. Null = StartsOn.</summary>
    public DateOnly? MembershipStartsOn { get; init; }
}

/// <summary>
/// P2 (faza 2B) — čista matematika perioda članstva (bez I/O), u lokalnim datumima zone organizacije (Q22).
/// PurchaseDate (Q3): period k počinje na StartsOn + k intervala, uvijek od izvornog dana (DateOnly.AddMonths: 31.1. → 28.2. →
/// 31.3.); Days pauza uz PauseExtendsPeriod pomiče sve kasnije granice za stvarne dane pauze (Q5).
/// CalendarMonth (Q3): prvi period StartsOn → kraj mjeseca, dalje kalendarski mjeseci; SkipPeriods pauza preskače cijele
/// periode bez pomicanja granica, a raniji povratak (Q47) otvara period od dana povratka do kraja mjeseca.
/// Zakazana promjena uvjeta počinje novi dio linije na prvoj granici perioda na ili nakon PendingNotBefore; PurchaseDate s istim
/// intervalom zadržava izvorno sidro, inače je sidro datum promjene.
/// </summary>
public static class MembershipPeriodCalendar
{
    private const int SafetyLimit = 2400; // 200 godina mjesečnih perioda — zaštita od beskonačne petlje

    public static IEnumerable<MembershipPeriod> Periods(MembershipTimeline timeline)
    {
        ArgumentNullException.ThrowIfNull(timeline);
        DateOnly segmentStart = timeline.StartsOn;
        DateOnly anchor = timeline.StartsOn;
        MembershipPeriodTerms terms = timeline.Terms;
        bool pendingApplied = timeline.PendingTerms == null;
        int sequence = 0;
        int indexInSegment = 0;
        int shiftDays = 0;
        HashSet<int> countedPauses = new();
        DateOnly start = timeline.StartsOn;

        while (sequence < SafetyLimit)
        {
            DateOnly nextNominal = NominalStart(anchor, segmentStart, terms, indexInSegment + 1);
            if (terms.Anchor == MembershipRenewalAnchor.PurchaseDate)
                shiftDays = ShiftDays(nextNominal, timeline.Pauses, terms, shiftDays, countedPauses);
            DateOnly nextStart = terms.Anchor == MembershipRenewalAnchor.PurchaseDate ? nextNominal.AddDays(shiftDays) : nextNominal;

            // Zakazana promjena uvjeta: prva granica na ili nakon donje granice.
            if (!pendingApplied && nextStart >= timeline.PendingNotBefore.Value)
            {
                foreach (MembershipPeriod period in Split(sequence, start, nextStart.AddDays(-1), timeline.Pauses, terms))
                {
                    yield return period;
                    sequence = period.Sequence + 1;
                }

                MembershipPeriodTerms pending = timeline.PendingTerms.Value;
                bool keepAnchor = terms.Anchor == MembershipRenewalAnchor.PurchaseDate && pending.Anchor == MembershipRenewalAnchor.PurchaseDate
                                  && terms.Interval == pending.Interval;
                if (!keepAnchor)
                {
                    anchor = nextStart;
                    segmentStart = nextStart;
                    indexInSegment = 0;
                    shiftDays = 0;
                }
                else
                {
                    indexInSegment++;
                }

                terms = pending;
                pendingApplied = true;
                start = nextStart;
                continue;
            }

            foreach (MembershipPeriod period in Split(sequence, start, nextStart.AddDays(-1), timeline.Pauses, terms))
            {
                yield return period;
                sequence = period.Sequence + 1;
            }

            start = nextStart;
            indexInSegment++;

            // K1-8: stajanje na granici (od datuma kupnje) — preskočeni razmak do kraja stajanja, granice se pomiču za njega.
            if (terms.Anchor == MembershipRenewalAnchor.PurchaseDate && StandstillAt(timeline.Pauses, nextStart) is MembershipPauseSpan gap)
            {
                yield return new MembershipPeriod(sequence, nextStart, gap.EndsOn, Skipped: true);
                sequence++;
                shiftDays += gap.EndsOn.DayNumber - nextStart.DayNumber + 1;
                start = gap.EndsOn.AddDays(1);
            }
        }
    }

    public static MembershipPeriod PeriodContaining(MembershipTimeline timeline, DateOnly date)
    {
        if (date < timeline.StartsOn)
            throw new ArgumentOutOfRangeException(nameof(date), "Datum je prije početka članstva.");
        return Periods(timeline).First(p => p.EndsOn >= date);
    }

    /// <summary>Prvi period koji počinje na ili nakon datuma.</summary>
    public static MembershipPeriod FirstPeriodStartingOnOrAfter(MembershipTimeline timeline, DateOnly date) =>
        Periods(timeline).First(p => p.StartsOn >= date);

    /// <summary>Kraj N-tog perioda koji nije preskočen pauzom (minimalna obveza broji samo nepauzirane periode, Q5).</summary>
    public static DateOnly EndOfNthUnskippedPeriod(MembershipTimeline timeline, int n) =>
        EndOfNthUnskippedPeriod(timeline, n, timeline.StartsOn);

    /// <summary>Kraj N-tog nepreskočenog perioda koji počinje na ili nakon datuma (obveza nakon promjene uvjeta, pregled 2C).</summary>
    public static DateOnly EndOfNthUnskippedPeriod(MembershipTimeline timeline, int n, DateOnly countFrom)
    {
        if (n < 1)
            throw new ArgumentOutOfRangeException(nameof(n));
        return Periods(timeline).Where(p => !p.Skipped && p.StartsOn >= countFrom).Skip(n - 1).First().EndsOn;
    }

    private static DateOnly NominalStart(DateOnly anchor, DateOnly segmentStart, MembershipPeriodTerms terms, int index)
    {
        if (index == 0)
            return segmentStart;
        if (terms.Anchor == MembershipRenewalAnchor.CalendarMonth)
        {
            DateOnly firstOfMonth = new(segmentStart.Year, segmentStart.Month, 1);
            return firstOfMonth.AddMonths(index);
        }

        int months = terms.Interval == MembershipBillingInterval.Yearly ? 12 : 1;
        return anchor.AddMonths(index * months);
    }

    /// <summary>Days pauza (uz produljenje) pomiče granicu za svoje dane ako je počela prije (pomaknute) granice. Vraća ukupni
    /// pomak u danima. K1-8: sustavna pauza (stajanje) ovdje ne pomiče — ona je preskočeni razmak (vidi <see cref="Periods"/>);
    /// klijentova pauza koja se preklapa sa stajanjem pomiče samo za dane izvan njega (dani se ne broje dvaput).</summary>
    private static int ShiftDays(
        DateOnly nominal, IReadOnlyList<MembershipPauseSpan> pauses, MembershipPeriodTerms terms, int shiftDays, HashSet<int> counted)
    {
        if (!terms.PauseExtendsPeriod)
            return shiftDays;

        List<MembershipPauseSpan> system = pauses.Where(p => p.IsSystem).ToList();
        bool changed = true;
        while (changed)
        {
            changed = false;
            DateOnly candidate = nominal.AddDays(shiftDays);
            for (int i = 0; i < pauses.Count; i++)
            {
                MembershipPauseSpan pause = pauses[i];
                if (pause.IsSystem || pause.Kind != MembershipPauseKind.Days || counted.Contains(i) || pause.StartsOn >= candidate)
                    continue;
                shiftDays += pause.Days - system.Sum(s => pause.OverlapDays(s));
                counted.Add(i);
                changed = true;
            }
        }

        return shiftDays;
    }

    /// <summary>K1-8 — zatvoreno stajanje plana od datuma kupnje koje počinje na (ili prije) granice: razmak se preskače, a
    /// sljedeći period počinje dan nakon njega (tekući period ostaje kakav jest).</summary>
    private static MembershipPauseSpan? StandstillAt(IReadOnlyList<MembershipPauseSpan> pauses, DateOnly boundary) => pauses
        .Where(p => p.IsSystem && p.Kind == MembershipPauseKind.Days && p.StartsOn <= boundary && p.EndsOn >= boundary)
        .Select(p => (MembershipPauseSpan?)p)
        .FirstOrDefault();

    /// <summary>Kalendarski period: preskočen u cijelosti, ili (raniji povratak, Q47) otvoren od dana povratka.</summary>
    private static IEnumerable<MembershipPeriod> Split(
        int sequence, DateOnly start, DateOnly end, IReadOnlyList<MembershipPauseSpan> pauses, MembershipPeriodTerms terms)
    {
        if (terms.Anchor != MembershipRenewalAnchor.CalendarMonth)
        {
            yield return new MembershipPeriod(sequence, start, end, Skipped: false);
            yield break;
        }

        MembershipPauseSpan? skip = pauses
            .Where(p => p.Kind == MembershipPauseKind.SkipPeriods && p.StartsOn <= start && p.EndsOn >= start)
            .Select(p => (MembershipPauseSpan?)p)
            .FirstOrDefault();
        if (skip == null)
        {
            yield return new MembershipPeriod(sequence, start, end, Skipped: false);
            yield break;
        }

        if (skip.Value.EndsOn >= end)
        {
            yield return new MembershipPeriod(sequence, start, end, Skipped: true);
            yield break;
        }

        // Raniji povratak usred preskočenog perioda: dio do povratka je preskočen, od povratka se otvara novi period.
        yield return new MembershipPeriod(sequence, start, skip.Value.EndsOn, Skipped: true);
        yield return new MembershipPeriod(sequence + 1, skip.Value.EndsOn.AddDays(1), end, Skipped: false);
    }
}
