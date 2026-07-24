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
}
