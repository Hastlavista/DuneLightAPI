using System;
using BlueDragon.DuneLight.Infrastructure.Domain.Models.Employees;
using BlueDragon.DuneLight.Infrastructure.Utils;

namespace BlueDragon.DuneLight.Infrastructure.Domain.Models.Appointments;

/// <summary>
/// Phase D3A — okvir termina za read-model/DTO (postojeći API ugovori ostaju isti): vrijednosti koje su DTO-i prije čitali
/// s Appointment polja i navigacija, sada iz jedinog segmenta (usluga, zaposlenik, prostorija s učitanim navigacijama).
/// Trajanje je izvedeno iz PlannedEnd - PlannedStart. Navigacije koje nisu učitane daju null naziv — ista semantika kao
/// prije s appointment.Service/Employee/Room.
/// </summary>
public sealed record AppointmentFrameView(
    DateTimeOffset StartsAt,
    int DurationMinutes,
    Guid ServiceId,
    string ServiceName,
    string ServiceColorHex,
    Guid? EmployeeId,
    string EmployeeName,
    Guid? RoomId,
    string RoomName)
{
    public static AppointmentFrameView Of(Appointment appointment)
    {
        AppointmentSegment segment = AppointmentSegments.GetSingleExecutionSegment(appointment);
        Guid? employeeId = AppointmentSegments.GetSingleEmployeeId(segment);
        Employee employee = employeeId.HasValue ? segment.Employees[0].Employee : null;

        return new AppointmentFrameView(
            segment.PlannedStart,
            AppointmentSegments.DurationMinutes(segment),
            segment.ServiceId,
            segment.Service?.Name,
            segment.Service?.ColorHex,
            employeeId,
            employee != null ? $"{employee.FirstName} {employee.LastName}" : null,
            segment.RoomId,
            segment.Room?.Name);
    }
}
