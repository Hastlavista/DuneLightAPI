using System;
using System.Collections.Generic;
using System.Linq;
using BlueDragon.DuneLight.Core.Enums;
using BlueDragon.DuneLight.Core.Shared;
using BlueDragon.DuneLight.Core.Shared.Exceptions;
using BlueDragon.DuneLight.Infrastructure.Domain.Models.Appointments;

namespace BlueDragon.DuneLight.Infrastructure.Utils;

/// <summary>
/// Pomoćnici nad JEDNIM segmentom (izvršnom jedinicom). Termin nema "izvršni okvir" i nema "jedinog" segmenta — svaka
/// operacija adresira segment ili sudjelovanje eksplicitno.
/// </summary>
public static class AppointmentSegments
{
    /// <summary>Trajanje segmenta izvedeno iz planiranog raspona.</summary>
    public static int DurationMinutes(AppointmentSegment segment) => (int)(segment.PlannedEnd - segment.PlannedStart).TotalMinutes;

    public static bool HasEmployee(AppointmentSegment segment, Guid employeeId) => segment.Employees.Any(e => e.EmployeeId == employeeId);
}

/// <summary>
/// Phase M1H — segment GRUPNOG occurrencea adresiran EKSPLICITNO (SegmentId): occurrence ima po jedan segment za svaki
/// predložak grupe i operacija po segmentu (lista čekanja, gost, prisutnost) ga uvijek navodi — nikad se ne zaključuje
/// "jedini" ili "prvi" segment. Samo za Form=Group termine.
/// </summary>
public static class GroupOccurrenceSegments
{
    public static AppointmentSegment Require(Appointment appointment, Guid? segmentId)
    {
        ArgumentNullException.ThrowIfNull(appointment);
        if (appointment.Form != AppointmentForm.Group)
            throw new ValidationAppException("Operacija postoji samo za grupni termin.");
        if (!segmentId.HasValue)
            throw new ValidationAppException("SegmentId je obavezan — segment grupnog termina se navodi eksplicitno.");

        return appointment.Segments.SingleOrDefault(s => s.Id == segmentId.Value)
               ?? throw new NotFoundAppException("Segment", segmentId.Value);
    }
}

/// <summary>Termin/segment nije u integritetnom obliku (npr. nula segmenata, kraj prije početka). Integritetna greška,
/// ne korisnička (500).</summary>
public sealed class InvalidAppointmentSegmentStateException : InvalidOperationException
{
    public InvalidAppointmentSegmentStateException(string message) : base(message)
    {
    }
}
