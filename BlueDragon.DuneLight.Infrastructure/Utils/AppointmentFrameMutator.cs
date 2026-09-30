using System;
using System.Linq;
using BlueDragon.DuneLight.Infrastructure.Domain.Models.Appointments;

namespace BlueDragon.DuneLight.Infrastructure.Utils;

/// <summary>
/// JEDINO mjesto koje piše izvršni okvir termina — od Phase D3A u njegov jedini segment (usluga, planirani raspon,
/// prostorija, dodjela zaposlenika). Stari stupci termina više ne postoje, pa nema dual-writea. Mijenja samo učitani
/// entitet u memoriji: bez čitanja iz baze, bez spremanja, bez validacije, bez audita — sve to ostaje kod pozivatelja.
/// Djelomične izmjene (Move: null = "bez promjene") pozivatelj izražava kroz <c>AppointmentFrame.Of(appointment) with
/// { ... }</c>. Navigacije se ne diraju (samo FK vrijednosti); spremanje dodjela zaposlenika radi AppointmentHandler.
/// </summary>
public static class AppointmentFrameMutator
{
    public static void Apply(Appointment appointment, AppointmentFrame frame, DateTimeOffset updatedAt)
    {
        ArgumentNullException.ThrowIfNull(frame);
        AppointmentSegment segment = AppointmentSegments.GetSingleExecutionSegment(appointment);
        Guid? currentEmployeeId = AppointmentSegments.GetSingleEmployeeId(segment);

        segment.ServiceId = frame.ServiceId;
        segment.RoomId = frame.RoomId;
        segment.PlannedStart = frame.StartsAt;
        segment.PlannedEnd = frame.EndsAt;
        segment.UpdatedAt = updatedAt;

        if (currentEmployeeId != frame.EmployeeId)
        {
            segment.Employees.Clear();
            if (frame.EmployeeId.HasValue)
                segment.Employees.Add(NewAssignment(segment, frame.EmployeeId.Value));
        }
    }

    /// <summary>Novi segment za upravo kreirani termin (vidi AppointmentFactory).</summary>
    public static AppointmentSegment NewSegment(Appointment appointment, AppointmentFrame frame)
    {
        ArgumentNullException.ThrowIfNull(appointment);
        ArgumentNullException.ThrowIfNull(frame);
        if (appointment.Segments.Count != 0)
            throw new InvalidAppointmentSegmentStateException($"Termin {appointment.Id} već ima segment.");

        AppointmentSegment segment = new AppointmentSegment
        {
            Id = Guid.NewGuid(),
            OrganizationId = appointment.OrganizationId,
            AppointmentId = appointment.Id.GetValueOrDefault(),
            ServiceId = frame.ServiceId,
            RoomId = frame.RoomId,
            PlannedStart = frame.StartsAt,
            PlannedEnd = frame.EndsAt,
            CreatedAt = appointment.CreatedAt
        };
        if (frame.EmployeeId.HasValue)
            segment.Employees.Add(NewAssignment(segment, frame.EmployeeId.Value));

        appointment.Segments.Add(segment);
        return segment;
    }

    private static AppointmentSegmentEmployee NewAssignment(AppointmentSegment segment, Guid employeeId) =>
        new() { AppointmentSegmentId = segment.Id.GetValueOrDefault(), EmployeeId = employeeId };
}
