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
/// "jedinog" segmenta ovdje (legacy endpointi: <see cref="LegacySingleSegment"/>; grupe: <see cref="GroupOccurrenceSegments"/>).
/// </summary>
public static class AppointmentSegments
{
    /// <summary>Trajanje segmenta izvedeno iz planiranog raspona.</summary>
    public static int DurationMinutes(AppointmentSegment segment) => (int)(segment.PlannedEnd - segment.PlannedStart).TotalMinutes;

    public static bool HasEmployee(AppointmentSegment segment, Guid employeeId) => segment.Employees.Any(e => e.EmployeeId == employeeId);
}

/// <summary>
/// Phase M1G — granica LEGACY plosnatih operacija s JEDNIM zaposlenikom (plosnati Update/Move/CompleteExisting nose jedan
/// EmployeeId). Segment s 2+ zaposlenika se ovdje nikad ne sažima na jednog (ni "prvi", ni "cjenovni"): poslovna greška
/// EMPLOYEE_SET_COMMAND_REQUIRED — ciljni put je segmentna naredba nad skupom zaposlenika. Ne koristiti u ciljnim tokovima.
/// </summary>
public static class LegacySingleEmployee
{
    /// <summary>Jedini zaposlenik segmenta (null = bez zaposlenika).</summary>
    public static Guid? Of(AppointmentSegment segment)
    {
        ArgumentNullException.ThrowIfNull(segment);
        return segment.Employees.Count switch
        {
            0 => null,
            1 => segment.Employees[0].EmployeeId,
            _ => throw new BusinessRuleException(ErrorCodes.EmployeeSetCommandRequired,
                "Segment ima više zaposlenika — ova operacija nosi jednog zaposlenika; koristite izmjenu zaposlenika segmenta (EmployeeIds).")
        };
    }

    /// <summary>Plosnata operacija postavlja JEDNOG zaposlenika: izvor cijene je automatski Employee/taj zaposlenik.</summary>
    public static void Assign(AppointmentSegment segment, Guid employeeId, DateTimeOffset updatedAt)
    {
        SegmentMutator.AssignEmployees(segment, new[] { employeeId }, updatedAt);
        SegmentPricingSource.Apply(segment, SegmentPricingSource.Normalize(new[] { employeeId }, null, null), updatedAt);
    }
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
/// Phase M1F — razrješavanje SEGMENTA grupnog occurrencea (occurrence ima po jedan segment za svaki predložak grupe).
/// Ciljne operacije navode SegmentId; legacy operacija bez selektora smije razriješiti segment SAMO kad occurrence ima
/// točno jedan segment (jednopredloška grupa — kompatibilnost). Za višesegmentni occurrence bez selektora: poslovna greška
/// SEGMENT_SELECTION_REQUIRED — nikad "prvi" segment niti "svi". Samo za Form=Group termine.
/// </summary>
public static class GroupOccurrenceSegments
{
    public static AppointmentSegment Resolve(Appointment appointment, Guid? segmentId)
    {
        ArgumentNullException.ThrowIfNull(appointment);
        if (appointment.Form != AppointmentForm.Group)
            throw new InvalidAppointmentSegmentStateException($"Termin {appointment.Id} nije grupni occurrence.");

        if (segmentId.HasValue)
            return appointment.Segments.SingleOrDefault(s => s.Id == segmentId.Value)
                   ?? throw new NotFoundAppException("Segment", segmentId.Value);

        return appointment.Segments.Count switch
        {
            1 => appointment.Segments[0],
            0 => throw new InvalidAppointmentSegmentStateException($"Grupni occurrence {appointment.Id} nema segment (ili nisu učitani)."),
            _ => throw new BusinessRuleException(ErrorCodes.SegmentSelectionRequired,
                "Grupni termin ima više segmenata — navedite segment (SegmentId).")
        };
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
