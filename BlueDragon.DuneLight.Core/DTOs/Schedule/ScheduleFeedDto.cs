using System.Collections.Generic;
using BlueDragon.DuneLight.Core.DTOs.Appointments;
using BlueDragon.DuneLight.Core.DTOs.ScheduleBreaks;

namespace BlueDragon.DuneLight.Core.DTOs.Schedule;

/// <summary>Odgovor GET /api/appointments/schedule — termini i pauze kao zasebni nizovi za istu mrežu rasporeda.</summary>
public class ScheduleFeedDto
{
    public List<AppointmentScheduleCellDto> Appointments { get; set; } = new();
    public List<ScheduleBreakCellDto> Breaks { get; set; } = new();
}
