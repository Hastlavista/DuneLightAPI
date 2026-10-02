using System;
using System.Collections.Generic;
using System.Linq;
using BlueDragon.DuneLight.Core.Enums;
using BlueDragon.DuneLight.Core.Shared;
using BlueDragon.DuneLight.Core.Shared.Exceptions;
using BlueDragon.DuneLight.Infrastructure.Domain.Models.Appointments;

namespace BlueDragon.DuneLight.Infrastructure.Utils;

/// <summary>
/// Pomoćnici nad JEDNIM segmentom (izvršnom jedinicom). Phase M1B: termin nema "izvršni okvir" — više nema odabira
/// "jedinog" segmenta ovdje (legacy endpointi: <see cref="LegacySingleSegment"/>; grupe: <see cref="SingleGroupSegment"/>).
/// </summary>
public static class AppointmentSegments
{
    /// <summary>Zaposlenik segmenta (null = bez zaposlenika, npr. grupa bez trenera). Više zaposlenika na segmentu je
    /// OGRANIČENJE PROIZVODA (atribucija cijene/provizije je otvorena odluka), ne sheme — baca iznimku.</summary>
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
                $"Segment {segmentId} ima {employeeIds.Count} zaposlenika — više zaposlenika po segmentu još nije podržano.")
        };
    }

    /// <summary>Trajanje segmenta izvedeno iz planiranog raspona.</summary>
    public static int DurationMinutes(AppointmentSegment segment) => (int)(segment.PlannedEnd - segment.PlannedStart).TotalMinutes;

    public static bool HasEmployee(AppointmentSegment segment, Guid employeeId) => segment.Employees.Any(e => e.EmployeeId == employeeId);
}

/// <summary>
/// Phase M1E — granica LEGACY (plosnatih, jednosegmentnih) API operacija: plosnati Update/Move/CompleteExisting/AddBooking
/// i interni CompleteNew adresiraju TERMIN, ne segment. Za jednosegmentni termin razrješava taj segment; za višesegmentni
/// termin NIKAD ne pogađa (ni "prvi" segment, ni "svi") nego vraća poslovnu grešku SEGMENT_SELECTION_REQUIRED — ciljni put
/// su segmentne (SegmentId) i participation-native (ParticipationId) naredbe. Ne koristiti u ciljnim tokovima.
/// </summary>
public static class LegacySingleSegment
{
    public static AppointmentSegment Resolve(Appointment appointment)
    {
        ArgumentNullException.ThrowIfNull(appointment);

        return appointment.Segments.Count switch
        {
            1 => appointment.Segments[0],
            0 => throw new InvalidAppointmentSegmentStateException(
                $"Termin {appointment.Id} nema segment (ili segmenti nisu učitani)."),
            _ => throw new BusinessRuleException(ErrorCodes.SegmentSelectionRequired,
                "Termin ima više segmenata — ova operacija ne može odabrati segment; koristite operaciju nad segmentom (SegmentId) ili sudjelovanjem (ParticipationId).")
        };
    }
}

/// <summary>
/// Phase M1E — GRUPNI proizvod je (do GroupSegmentTemplates, M1F) namjerno jednosegmentan: generirani occurrence ima točno
/// jedan segment. Grupne operacije (AddMember, lista čekanja, gost na check-inu, close-out, Group.Capacity) ga razrješavaju
/// ovdje. Samo za Form=Group termine; drugi oblik segmenata je integritetna greška. Ne koristiti u generičkom kodu termina.
/// </summary>
public static class SingleGroupSegment
{
    public static AppointmentSegment Of(Appointment appointment)
    {
        ArgumentNullException.ThrowIfNull(appointment);
        if (appointment.Form != AppointmentForm.Group)
            throw new InvalidAppointmentSegmentStateException($"Termin {appointment.Id} nije grupni occurrence.");
        if (appointment.Segments.Count != 1)
            throw new InvalidAppointmentSegmentStateException(
                $"Grupni occurrence {appointment.Id} ima {appointment.Segments.Count} segmenata — grupe su jednosegmentne do GroupSegmentTemplates.");
        return appointment.Segments[0];
    }
}

/// <summary>Termin/segment nije u obliku koji operacija podržava (nula segmenata, više segmenata na single-segment
/// granici, više zaposlenika na segmentu). Integritetna greška, ne korisnička (500).</summary>
public sealed class InvalidAppointmentSegmentStateException : InvalidOperationException
{
    public InvalidAppointmentSegmentStateException(string message) : base(message)
    {
    }
}
