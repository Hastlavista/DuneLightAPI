using System;
using System.Collections.Generic;
using System.Linq;
using BlueDragon.DuneLight.Infrastructure.Utils;

namespace BlueDragon.DuneLight.Infrastructure.Domain.Models.Appointments;

/// <summary>
/// Phase M1B — IZVEDENI planirani raspon termina iz njegovih segmenata (termin nema vlastiti početak/trajanje):
/// PlannedStart = MIN(segment.PlannedStart), PlannedEnd = MAX(segment.PlannedEnd). Segmenti smiju biti uzastopni,
/// paralelni ili s razmacima — raspon NIJE zbroj trajanja segmenata: <see cref="Span"/> = PlannedEnd − PlannedStart i
/// UKLJUČUJE razmake. Termin mora imati barem jedan segment (invarijanta; bez segmenata nema raspona).
/// </summary>
public readonly record struct AppointmentRange(DateTimeOffset PlannedStart, DateTimeOffset PlannedEnd)
{
    /// <summary>Raspon od najranijeg početka do najkasnijeg kraja (uključuje razmake između segmenata).</summary>
    public TimeSpan Span => PlannedEnd - PlannedStart;

    public int SpanMinutes => (int)Span.TotalMinutes;

    public static AppointmentRange Of(Appointment appointment)
    {
        ArgumentNullException.ThrowIfNull(appointment);
        if (appointment.Segments.Count == 0)
            throw new InvalidAppointmentSegmentStateException(
                $"Termin {appointment.Id} nema segment (ili segmenti nisu učitani) — raspon nije definiran.");
        return Of(appointment.Segments);
    }

    public static AppointmentRange Of(IEnumerable<AppointmentSegment> segments)
    {
        List<AppointmentSegment> list = segments.ToList();
        if (list.Count == 0)
            throw new InvalidAppointmentSegmentStateException("Raspon termina zahtijeva barem jedan segment.");
        return new AppointmentRange(list.Min(s => s.PlannedStart), list.Max(s => s.PlannedEnd));
    }
}
