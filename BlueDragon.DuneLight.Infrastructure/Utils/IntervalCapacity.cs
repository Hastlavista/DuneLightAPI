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
    public static CapacityEvaluation Evaluate(IReadOnlyList<CapacityClaim> existing, IReadOnlyList<CapacityClaim> proposed, int capacity)
    {
        ArgumentNullException.ThrowIfNull(existing);
        ArgumentNullException.ThrowIfNull(proposed);

        List<(DateTimeOffset At, int Delta)> events = new();
        foreach (CapacityClaim claim in existing.Concat(proposed))
        {
            if (claim.End <= claim.Start)
                throw new ArgumentException("Zauzetost mora završiti nakon početka.");
            if (claim.Amount <= 0)
                continue;
            events.Add((claim.Start, claim.Amount));
            events.Add((claim.End, -claim.Amount));
        }

        // Isti trenutak: negativne promjene (krajevi) prije pozitivnih (počeci) — [start, end).
        events.Sort((a, b) => a.At != b.At ? a.At.CompareTo(b.At) : a.Delta.CompareTo(b.Delta));

        // Konstantni odsječci [from, to) s pripadnom zauzetošću.
        List<(DateTimeOffset From, DateTimeOffset To, int Usage)> slices = new();
        int usage = 0;
        for (int i = 0; i < events.Count; i++)
        {
            usage += events[i].Delta;
            if (i + 1 < events.Count && events[i + 1].At > events[i].At && usage > 0)
                slices.Add((events[i].At, events[i + 1].At, usage));
        }

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
}
