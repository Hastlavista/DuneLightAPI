using System;

namespace BlueDragon.DuneLight.Infrastructure.Utils;

/// <summary>Čista provjera preklapanja dva datumska raspona. Null u "do" polju = otvoreno prema naprijed.</summary>
public static class DateRangeOverlap
{
    public static bool Overlaps(DateTimeOffset aFrom, DateTimeOffset? aTo, DateTimeOffset bFrom, DateTimeOffset? bTo)
    {
        bool aStartsBeforeOrOnBEnd = bTo is null || aFrom <= bTo.Value;
        bool aEndsAfterOrOnBStart = aTo is null || aTo.Value >= bFrom;
        return aStartsBeforeOrOnBEnd && aEndsAfterOrOnBStart;
    }

    /// <summary>Isto pravilo nad lokalnim zidnim vremenima (bez offseta) — roster rasponi su kalendarski datum + lokalno
    /// vrijeme u poslovnom kalendaru organizacije, ne instanti.</summary>
    public static bool Overlaps(DateTime aFrom, DateTime? aTo, DateTime bFrom, DateTime? bTo)
    {
        bool aStartsBeforeOrOnBEnd = bTo is null || aFrom <= bTo.Value;
        bool aEndsAfterOrOnBStart = aTo is null || aTo.Value >= bFrom;
        return aStartsBeforeOrOnBEnd && aEndsAfterOrOnBStart;
    }
}
