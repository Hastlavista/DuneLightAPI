using System;

namespace BlueDragon.DuneLight.Infrastructure.Utils;

/// <summary>Čista provjera preklapanja dva raspona. Null u "do" polju = otvoreno prema naprijed. Oba kraja su uključena: raspon
/// koji završava na dan kad drugi počinje (A.ValidTo == B.ValidFrom) se PREKLAPA.</summary>
public static class DateRangeOverlap
{
    /// <summary>T1-7: kalendarski dani (cjenik ValidFrom/ValidTo), oba kraja uključena.</summary>
    public static bool Overlaps(DateOnly aFrom, DateOnly? aTo, DateOnly bFrom, DateOnly? bTo)
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
