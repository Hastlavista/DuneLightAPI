using System;
using System.Collections.Generic;

namespace BlueDragon.DuneLight.Core.DTOs.Roster;

public class RosterWorkHoursSumDto
{
    public Guid RosterTypeId { get; set; }
    public string RosterTypeName { get; set; }
    public decimal Hours { get; set; }
}

public class RosterAbsenceDaysSumDto
{
    public Guid RosterTypeId { get; set; }
    public string RosterTypeName { get; set; }
    public int Days { get; set; }
}

/// <summary>Jedan redak u dnevnoj ćeliji matrice — obično jedan (odsutnost/smjena) ili više (dvokratni rad).</summary>
public class RosterDayCellEntryDto
{
    public Guid RosterEntryId { get; set; }
    public Guid RosterTypeId { get; set; }
    public string RosterTypeName { get; set; }
    public string RosterTypeColorHex { get; set; }
    public bool IsAbsence { get; set; }
    public decimal? Hours { get; set; }

    /// <summary>"07:00-12:00" za oblik rad; null za odsutnost (cijeli dan).</summary>
    public string TimeRange { get; set; }
}

public class RosterDayCellDto
{
    public int Day { get; set; }
    public List<RosterDayCellEntryDto> Entries { get; set; } = new();
}

public class RosterEmployeeMonthDto
{
    public Guid EmployeeId { get; set; }
    public string EmployeeName { get; set; }
    public List<RosterDayCellDto> Days { get; set; } = new();
    public List<RosterWorkHoursSumDto> WorkHoursByType { get; set; } = new();
    public decimal TotalWorkHours { get; set; }
    public List<RosterAbsenceDaysSumDto> AbsenceDaysByType { get; set; } = new();
}

/// <summary>Timski mjesečni pregled — dani u mjesecu × zaposlenici, kao Excel "R S".</summary>
public class RosterTeamMonthlyDto
{
    public int Year { get; set; }
    public int Month { get; set; }
    public List<RosterEmployeeMonthDto> Employees { get; set; } = new();
}

/// <summary>Osobni pregled za jednog zaposlenika u proizvoljnom razdoblju.</summary>
public class RosterPersonalReviewDto
{
    public Guid EmployeeId { get; set; }
    public string EmployeeName { get; set; }
    public DateTimeOffset From { get; set; }
    public DateTimeOffset To { get; set; }
    public List<RosterEntryDto> Entries { get; set; } = new();
    public List<RosterWorkHoursSumDto> WorkHoursByType { get; set; } = new();
    public decimal TotalWorkHours { get; set; }
    public List<RosterAbsenceDaysSumDto> AbsenceDaysByType { get; set; } = new();
}
