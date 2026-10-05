using System;
using System.Collections.Generic;
using System.Linq;
using BlueDragon.DuneLight.Core.DTOs.Appointments;
using BlueDragon.DuneLight.Infrastructure.Domain.Models.Appointments;

namespace BlueDragon.DuneLight.Infrastructure.Utils;

/// <summary>
/// Phase M1B — JEDINO mapiranje segmenata termina u read-model: segmenti (izvršne jedinice) i IZVEDENI raspon termina
/// (<see cref="AppointmentRange"/>). Nema "okvira termina" ni jednosegmentne projekcije (Phase M1H) — izvršni podaci su
/// isključivo u segmentima. Nenapunjene navigacije daju null nazive.
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
        PricingMode = segment.PricingMode,
        PricingEmployeeId = segment.PricingEmployeeId,
        PricingEmployeeName = segment.Employees.FirstOrDefault(e => e.EmployeeId == segment.PricingEmployeeId)?.Employee is { } pricing
            ? $"{pricing.FirstName} {pricing.LastName}"
            : null,
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
