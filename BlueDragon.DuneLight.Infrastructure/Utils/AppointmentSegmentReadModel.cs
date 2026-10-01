using System;
using System.Collections.Generic;
using System.Linq;
using BlueDragon.DuneLight.Core.DTOs.Appointments;
using BlueDragon.DuneLight.Infrastructure.Domain.Models.Appointments;

namespace BlueDragon.DuneLight.Infrastructure.Utils;

/// <summary>
/// Phase M1B — JEDINO mapiranje segmenata termina u read-model: segmenti (izvršne jedinice) i IZVEDENI raspon termina
/// (<see cref="AppointmentRange"/>). Nema "okvira termina". Uz to, PRIVREMENA jednosegmentna projekcija
/// (<see cref="SingleSegmentProjection"/>) za plosnata polja postojećih DTO-a — popunjena samo za termin s točno jednim
/// segmentom, nikad autoritativna. Nenapunjene navigacije daju null nazive.
/// </summary>
public static class AppointmentSegmentReadModel
{
    public static List<AppointmentSegmentDto> ToDtos(Appointment appointment) => appointment.Segments
        .OrderBy(s => s.PlannedStart).ThenBy(s => s.Id)
        .Select(ToDto)
        .ToList();

    public static AppointmentSegmentDto ToDto(AppointmentSegment segment) => new()
    {
        Id = segment.Id.GetValueOrDefault(),
        ServiceId = segment.ServiceId,
        ServiceName = segment.Service?.Name,
        ServiceCategoryColorHex = segment.Service?.ColorHex,
        PlannedStart = segment.PlannedStart,
        PlannedEnd = segment.PlannedEnd,
        ActualStart = segment.ActualStart,
        ActualEnd = segment.ActualEnd,
        RoomId = segment.RoomId,
        RoomName = segment.Room?.Name,
        Employees = segment.Employees
            .Select(e => new AppointmentSegmentEmployeeDto
            {
                EmployeeId = e.EmployeeId,
                EmployeeName = e.Employee != null ? $"{e.Employee.FirstName} {e.Employee.LastName}" : null
            })
            .ToList(),
        Resources = segment.Resources
            .Select(r => new AppointmentSegmentResourceDto
            {
                ResourceId = r.ResourceId,
                ResourceName = r.Resource?.Name,
                QuantityRequired = r.QuantityRequired
            })
            .ToList()
    };
}

/// <summary>
/// PRIVREMENA KOMPATIBILNOST (Phase M1B): plosnata polja postojećih DTO-a (ServiceId/Name/boja, EmployeeId/Name,
/// RoomId/Name) projicirana iz segmenta SAMO kad termin ima točno jedan segment (i segment najviše jednog zaposlenika);
/// inače sve null. StartsAt/DurationMinutes kompatibilnih DTO-a dolaze iz izvedenog raspona, ne odavde.
/// </summary>
public sealed record SingleSegmentProjection(
    Guid? ServiceId, string ServiceName, string ServiceColorHex, Guid? EmployeeId, string EmployeeName, Guid? RoomId, string RoomName)
{
    public static readonly SingleSegmentProjection None = new(null, null, null, null, null, null, null);

    public static SingleSegmentProjection Of(Appointment appointment)
    {
        if (appointment.Segments.Count != 1)
            return None;

        AppointmentSegment segment = appointment.Segments[0];
        AppointmentSegmentEmployee employee = segment.Employees.Count == 1 ? segment.Employees[0] : null;
        return new SingleSegmentProjection(
            segment.ServiceId,
            segment.Service?.Name,
            segment.Service?.ColorHex,
            employee?.EmployeeId,
            employee?.Employee != null ? $"{employee.Employee.FirstName} {employee.Employee.LastName}" : null,
            segment.RoomId,
            segment.Room?.Name);
    }
}
