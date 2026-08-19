using System;
using System.Collections.Generic;
using BlueDragon.DuneLight.Core.Enums;

namespace BlueDragon.DuneLight.Core.DTOs.Roster;

public class AvailabilityIntervalDto
{
    public TimeSpan Start { get; set; }
    public TimeSpan End { get; set; }
}

/// <summary>Dostupnost za jedan dan — EffectiveIntervals je presjek (intersection) Employee/Company intervala,
/// isti kriterij koji AppointmentService.EnsureWithinWorkingHours provjerava kod zakazivanja.</summary>
public class AvailabilityDto
{
    public DateTimeOffset Date { get; set; }
    public List<AvailabilityIntervalDto> EmployeeIntervals { get; set; } = new();
    public List<AvailabilityIntervalDto> CompanyIntervals { get; set; } = new();
    public List<AvailabilityIntervalDto> EffectiveIntervals { get; set; } = new();
    public AvailabilitySource EmployeeSource { get; set; }
    public AvailabilitySource CompanySource { get; set; }
}