using System;
using System.Collections.Generic;
using System.Linq;

namespace BlueDragon.DuneLight.Infrastructure.Utils;

/// <summary>Phase M1D — jedna količinska zauzetost u poluotvorenom intervalu [Start, End): osobe u prostoriji ili
/// QuantityRequired resursa.</summary>
public readonly record struct CapacityClaim(DateTimeOffset Start, DateTimeOffset End, int Amount);

/// <summary>Ishod provjere kapaciteta: najveća istovremena zauzetost unutar intervala PREDLOŽENIH zauzetosti i trenutak
/// njezina početka (null kad nema predloženih).</summary>
public readonly record struct CapacityEvaluation(int PeakUsage, DateTimeOffset? PeakAt, IReadOnlyList<int> ViolatingProposedIndexes)
{
    public bool Exceeds => ViolatingProposedIndexes.Count > 0;
}

/// <summary>
/// Phase M1D — JEDINI mehanizam vremenski raslojenog kapaciteta (prostorija: osobe; resurs: količina). Ne zbraja "sve što
/// se preklapa s kandidatom" (lažno pozitivno kad se postojeće zauzetosti ne preklapaju MEĐUSOBNO) nego računa stvarnu
/// istovremenu zauzetost: sweep po granicama intervala, poluotvoreno [start, end) — na istom trenutku KRAJ prestaje
/// doprinositi PRIJE nego POČETAK počne (susjedni intervali se ne zbrajaju).
///
/// Provjerava se samo vrijeme u kojem je aktivna barem jedna PREDLOŽENA zauzetost (upis koji se validira) — postojeće
/// preopterećenje negdje drugdje (npr. naknadno smanjen kapacitet) ne blokira nepovezan upis, ali svaki upis koji u to
/// vrijeme dodaje zauzetost biva odbijen.
/// </summary>
public static class IntervalCapacity
{
    /// <summary>Phase M1D.1: najveća istovremena zauzetost SVIH zadanih zauzetosti (isti sweep, isto [start, end)) i trenutak
    /// njezina početka — za provjeru smanjenja kapaciteta nad postojećim stanjem.</summary>
    public static (int PeakUsage, DateTimeOffset? PeakAt) Peak(IReadOnlyList<CapacityClaim> claims)
    {
        ArgumentNullException.ThrowIfNull(claims);
        int peak = 0;
        DateTimeOffset? peakAt = null;
        foreach ((DateTimeOffset from, DateTimeOffset _, int usage) in Slices(claims))
        {
            if (usage <= peak)
                continue;
            peak = usage;
            peakAt = from;
        }

        return (peak, peakAt);
    }

    public static CapacityEvaluation Evaluate(IReadOnlyList<CapacityClaim> existing, IReadOnlyList<CapacityClaim> proposed, int capacity)
    {
        ArgumentNullException.ThrowIfNull(existing);
        ArgumentNullException.ThrowIfNull(proposed);

        List<(DateTimeOffset From, DateTimeOffset To, int Usage)> slices = Slices(existing.Concat(proposed).ToList());

        int peak = 0;
        DateTimeOffset? peakAt = null;
        List<int> violating = new();
        for (int p = 0; p < proposed.Count; p++)
        {
            CapacityClaim claim = proposed[p];
            if (claim.Amount <= 0)
                continue;
            int claimPeak = 0;
            foreach ((DateTimeOffset from, DateTimeOffset to, int sliceUsage) in slices)
            {
                if (!SchedulingInterval.Overlaps(from, to, claim.Start, claim.End) || sliceUsage <= claimPeak)
                    continue;
                claimPeak = sliceUsage;
                if (sliceUsage > peak)
                {
                    peak = sliceUsage;
                    peakAt = from;
                }
            }

            if (claimPeak > capacity)
                violating.Add(p);
        }

        return new CapacityEvaluation(peak, peakAt, violating);
    }

    /// <summary>Konstantni odsječci [from, to) s pripadnom (pozitivnom) zauzetošću — sweep po granicama; na istom trenutku
    /// krajevi prije početaka.</summary>
    private static List<(DateTimeOffset From, DateTimeOffset To, int Usage)> Slices(IReadOnlyList<CapacityClaim> claims)
    {
        List<(DateTimeOffset At, int Delta)> events = new();
        foreach (CapacityClaim claim in claims)
        {
            if (claim.End <= claim.Start)
                throw new ArgumentException("Zauzetost mora završiti nakon početka.");
            if (claim.Amount <= 0)
                continue;
            events.Add((claim.Start, claim.Amount));
            events.Add((claim.End, -claim.Amount));
        }

        events.Sort((a, b) => a.At != b.At ? a.At.CompareTo(b.At) : a.Delta.CompareTo(b.Delta));

        List<(DateTimeOffset From, DateTimeOffset To, int Usage)> slices = new();
        int usage = 0;
        for (int i = 0; i < events.Count; i++)
        {
            usage += events[i].Delta;
            if (i + 1 < events.Count && events[i + 1].At > events[i].At && usage > 0)
                slices.Add((events[i].At, events[i + 1].At, usage));
        }

        return slices;
    }
}
