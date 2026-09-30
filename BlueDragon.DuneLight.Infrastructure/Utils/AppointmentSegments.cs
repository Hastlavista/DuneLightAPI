using System;
using System.Collections.Generic;
using System.Linq;
using BlueDragon.DuneLight.Infrastructure.Domain.Models.Appointments;

namespace BlueDragon.DuneLight.Infrastructure.Utils;

/// <summary>
/// Phase D3A — JEDINO mjesto koje iz termina bira njegov autoritativni izvršni segment. Današnji (jednostruki) tokovi
/// podržavaju točno jedan segment s najviše jednim zaposlenikom; sve ostalo je nepodržano stanje za te tokove i baca
/// <see cref="InvalidAppointmentSegmentStateException"/> umjesto da se proizvoljno odabere segment/zaposlenik.
/// Pozivatelj mora učitati appointment.Segments (i Employees gdje treba zaposlenik) — neučitani segmenti se vide kao nula
/// segmenata i također bacaju, što otkriva nepotpun Include umjesto tihog krivog rezultata.
/// </summary>
public static class AppointmentSegments
{
    public static AppointmentSegment GetSingleExecutionSegment(Appointment appointment)
    {
        ArgumentNullException.ThrowIfNull(appointment);

        return appointment.Segments.Count switch
        {
            1 => appointment.Segments[0],
            0 => throw new InvalidAppointmentSegmentStateException(
                $"Termin {appointment.Id} nema izvršni segment (ili segmenti nisu učitani) — jednostruki tok ne može nastaviti."),
            _ => throw new InvalidAppointmentSegmentStateException(
                $"Termin {appointment.Id} ima {appointment.Segments.Count} segmenata — tok podržava samo jedan izvršni segment.")
        };
    }

    /// <summary>Zaposlenik jedinog segmenta (null = bez zaposlenika, npr. grupa bez trenera). Više zaposlenika na segmentu
    /// je nepodržano za današnje jednostruke tokove (vlasništvo/zauzetost/provizija) i baca iznimku.</summary>
    public static Guid? GetSingleEmployeeId(AppointmentSegment segment)
    {
        ArgumentNullException.ThrowIfNull(segment);
        return GetSingleEmployeeId(segment.Id, segment.Employees.Select(e => e.EmployeeId).ToList());
    }

    /// <summary>Isto pravilo nad projekcijom (bez učitanog entiteta) — vidi SchedulingOccupancyHandler.</summary>
    public static Guid? GetSingleEmployeeId(Guid? segmentId, IReadOnlyList<Guid> employeeIds)
    {
        return employeeIds.Count switch
        {
            0 => null,
            1 => employeeIds[0],
            _ => throw new InvalidAppointmentSegmentStateException(
                $"Segment {segmentId} ima {employeeIds.Count} zaposlenika — jednostruki tok podržava najviše jednog.")
        };
    }

    /// <summary>Trajanje je izvedeno iz planiranog raspona (nema drugog autoritativnog DurationMinutes).</summary>
    public static int DurationMinutes(AppointmentSegment segment) => (int)(segment.PlannedEnd - segment.PlannedStart).TotalMinutes;

    public static bool HasEmployee(AppointmentSegment segment, Guid employeeId) => segment.Employees.Any(e => e.EmployeeId == employeeId);
}

/// <summary>Termin nije u obliku koji jednostruki (legacy) tok podržava — nula ili više segmenata, ili više zaposlenika
/// na segmentu. Integritetna greška, ne korisnička (500).</summary>
public sealed class InvalidAppointmentSegmentStateException : InvalidOperationException
{
    public InvalidAppointmentSegmentStateException(string message) : base(message)
    {
    }
}
