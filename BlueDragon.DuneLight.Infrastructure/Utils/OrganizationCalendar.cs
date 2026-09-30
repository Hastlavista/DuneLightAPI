using System;
using System.Collections.Generic;
using BlueDragon.DuneLight.Core.Shared;

namespace BlueDragon.DuneLight.Infrastructure.Utils;

/// <summary>
/// JEDINO mjesto koje prevodi između UTC instanta i poslovnog kalendara organizacije (IANA vremenska zona, vidi
/// Organization.TimeZone). Svi instanti (AppointmentSegment.PlannedStart, ScheduleBreak.StartsAt, ...) su UTC; kalendarski
/// datumi (odsutnost, praznik, sidro predloška) su <see cref="DateOnly"/>; radno vrijeme su lokalna vremena u zoni
/// organizacije. Nikad ne koristi TimeZoneInfo.Local niti offset učitane vrijednosti — rezultat ne ovisi o hostu.
///
/// DST pravila za lokalno → UTC: nepostojeće lokalno vrijeme (proljetni skok, npr. 02:30) tumači se s offsetom PRIJE
/// prijelaza, tj. pomiče se naprijed za duljinu skoka (02:30 → 03:30 ljetnog); dvosmisleno lokalno vrijeme (jesenski
/// povrat, npr. 02:30 dvaput) uzima PRVU pojavu (ljetni offset).
/// </summary>
public sealed class OrganizationCalendar
{
    private readonly TimeZoneInfo _zone;

    private OrganizationCalendar(string timeZoneId, TimeZoneInfo zone)
    {
        TimeZoneId = timeZoneId;
        _zone = zone;
    }

    public string TimeZoneId { get; }

    public static OrganizationCalendar For(string timeZoneId)
    {
        if (!OrganizationTimeZones.IsSupported(timeZoneId))
            throw new ArgumentException($"Nepodržana vremenska zona '{timeZoneId}'.", nameof(timeZoneId));

        return new OrganizationCalendar(timeZoneId, TimeZoneInfo.FindSystemTimeZoneById(timeZoneId));
    }

    /// <summary>Lokalni zidni datum-vrijeme instanta u zoni organizacije (Kind = Unspecified).</summary>
    public DateTime ToLocal(DateTimeOffset instant) =>
        DateTime.SpecifyKind(TimeZoneInfo.ConvertTime(instant, _zone).DateTime, DateTimeKind.Unspecified);

    public DateOnly LocalDate(DateTimeOffset instant) => DateOnly.FromDateTime(ToLocal(instant));

    public TimeSpan LocalTimeOfDay(DateTimeOffset instant) => ToLocal(instant).TimeOfDay;

    /// <summary>UTC instant lokalnog datuma + lokalnog vremena u zoni organizacije (Offset = 0).</summary>
    public DateTimeOffset ToInstant(DateOnly date, TimeSpan localTime)
    {
        DateTime local = DateTime.SpecifyKind(date.ToDateTime(TimeOnly.MinValue) + localTime, DateTimeKind.Unspecified);

        TimeSpan offset;
        if (_zone.IsInvalidTime(local))
            offset = _zone.GetUtcOffset(local.AddDays(-1)); // offset prije proljetnog skoka
        else if (_zone.IsAmbiguousTime(local))
            offset = Max(_zone.GetAmbiguousTimeOffsets(local)); // prva pojava (ljetni offset)
        else
            offset = _zone.GetUtcOffset(local);

        return new DateTimeOffset(local, offset).ToUniversalTime();
    }

    /// <summary>UTC instant lokalne ponoći tog datuma.</summary>
    public DateTimeOffset StartOfDay(DateOnly date) => ToInstant(date, TimeSpan.Zero);

    /// <summary>Ponavljanje po lokalnom zidnom vremenu organizacije: svakih <paramref name="stepDays"/> kalendarskih dana
    /// u ISTO lokalno vrijeme kao <paramref name="first"/> (i preko DST prijelaza), dok god je instant &lt;= end.
    /// Prvi element je točno <paramref name="first"/>.</summary>
    public List<DateTimeOffset> RepeatAtLocalTime(DateTimeOffset first, DateTimeOffset end, int stepDays)
    {
        if (stepDays < 1)
            throw new ArgumentOutOfRangeException(nameof(stepDays));

        DateOnly firstDate = LocalDate(first);
        TimeSpan localTime = LocalTimeOfDay(first);

        List<DateTimeOffset> occurrences = new List<DateTimeOffset>();
        for (int i = 0; ; i++)
        {
            DateTimeOffset occurrence = i == 0 ? first : ToInstant(firstDate.AddDays(i * stepDays), localTime);
            if (occurrence > end)
                break;
            occurrences.Add(occurrence);
        }

        return occurrences;
    }

    private static TimeSpan Max(TimeSpan[] offsets)
    {
        TimeSpan max = offsets[0];
        foreach (TimeSpan offset in offsets)
            if (offset > max)
                max = offset;
        return max;
    }
}
