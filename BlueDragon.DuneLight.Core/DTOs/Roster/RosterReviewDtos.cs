using System;
using System.Collections.Generic;
using BlueDragon.DuneLight.Core.Enums;

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

public class RosterPlannedIntervalDto
{
    public TimeSpan Start { get; set; }
    public TimeSpan End { get; set; }
}

public class RosterDayCellDto
{
    public int Day { get; set; }
    public List<RosterDayCellEntryDto> Entries { get; set; } = new();

    /// <summary>Actual ako Entries nije prazan; Planned samo za danas/budućnost bez stvarnog zapisa kad postoji WorkingHoursTemplate; inače None.</summary>
    public RosterCellSource Source { get; set; } = RosterCellSource.None;

    /// <summary>Popunjeno samo kad Source==Planned — projekcija iz WorkingHoursTemplate, prazno = predložak kaže "slobodan dan".</summary>
    public List<RosterPlannedIntervalDto> PlannedIntervals { get; set; } = new();
}

/// <summary>Jedan dan bez stvarnog roster zapisa (danas/budućnost) za koji postoji WorkingHoursTemplate — vidi RosterPersonalReviewDto.PlannedDays.</summary>
public class RosterPlannedDayDto
{
    public DateTimeOffset Date { get; set; }

    /// <summary>Prazno = predložak kaže "slobodan dan" za taj datum.</summary>
    public List<RosterPlannedIntervalDto> Intervals { get; set; } = new();
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

    /// <summary>Dani u [From,To] bez stvarnog zapisa (danas/budućnost) za koje postoji WorkingHoursTemplate — TotalWorkHours ih NE uključuje (vidi FAZA 2 odluka).</summary>
    public List<RosterPlannedDayDto> PlannedDays { get; set; } = new();
}
