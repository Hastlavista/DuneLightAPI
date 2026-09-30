using System;

namespace BlueDragon.DuneLight.Infrastructure.Utils;

/// <summary>
/// Granica između kalendarskih datuma (<see cref="DateOnly"/>, PostgreSQL date) i postojećih API ugovora koji datume
/// još prenose kao DateTimeOffset (roster, praznici, sidro predloška). Ne ovisi o hostu ni o zoni organizacije:
/// datum je zidni datum kako ga je klijent napisao.
/// </summary>
public static class CalendarDates
{
    /// <summary>Zidni datum vrijednosti u njezinom VLASTITOM offsetu ("2031-03-03T00:00:00+01:00" → 2031-03-03).</summary>
    public static DateOnly FromWallDate(DateTimeOffset value) => DateOnly.FromDateTime(value.Date);

    public static DateOnly? FromWallDate(DateTimeOffset? value) => value.HasValue ? FromWallDate(value.Value) : null;

    /// <summary>Prikaz kalendarskog datuma u DateTimeOffset ugovoru: ponoć tog datuma s offsetom 0.</summary>
    public static DateTimeOffset ToUtcMidnight(DateOnly date) => new DateTimeOffset(date.ToDateTime(TimeOnly.MinValue), TimeSpan.Zero);

    public static DateTimeOffset? ToUtcMidnight(DateOnly? date) => date.HasValue ? ToUtcMidnight(date.Value) : null;
}
