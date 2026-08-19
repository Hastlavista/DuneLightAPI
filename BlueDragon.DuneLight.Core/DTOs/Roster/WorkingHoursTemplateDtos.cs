using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using BlueDragon.DuneLight.Core.Enums;

namespace BlueDragon.DuneLight.Core.DTOs.Roster;

public class WorkingHoursIntervalDto
{
    public int CycleWeekIndex { get; set; }
    public DayOfWeek DayOfWeek { get; set; }
    public TimeSpan StartTime { get; set; }
    public TimeSpan EndTime { get; set; }
}

public class WorkingHoursIntervalRequest
{
    [Required]
    public int CycleWeekIndex { get; set; }

    [Required]
    public DayOfWeek DayOfWeek { get; set; }

    [Required]
    public TimeSpan StartTime { get; set; }

    [Required]
    public TimeSpan EndTime { get; set; }
}

/// <summary>Singleton po vlasniku (Employee ILI Company, ovisno o endpointu). Prazan Intervals popis je legitiman ("nema predloška intervala", sve dane blokirano po predlošku).</summary>
public class WorkingHoursTemplateDto
{
    public Guid Id { get; set; }
    public Guid? EmployeeId { get; set; }
    public string EmployeeName { get; set; }
    public Guid? CompanyId { get; set; }
    public string CompanyName { get; set; }
    public WorkingHoursCycleType CycleType { get; set; }

    /// <summary>Uvijek ponedjeljak — normalizirano pri spremanju (vidi WorkingHoursTemplateService).</summary>
    public DateTimeOffset AnchorDate { get; set; }

    public List<WorkingHoursIntervalDto> Intervals { get; set; } = new();

    public DateTimeOffset CreatedAt { get; set; }
    public Guid? CreatedBy { get; set; }
    public DateTimeOffset? UpdatedAt { get; set; }
    public Guid? UpdatedBy { get; set; }
}

/// <summary>Pun zamjenski payload (upsert) — vlasnik (EmployeeId/CompanyId) dolazi iz rute, ne iz tijela zahtjeva.</summary>
public class WorkingHoursTemplateUpsertRequest
{
    [Required]
    public WorkingHoursCycleType CycleType { get; set; }

    /// <summary>Bilo koji datum — servis ga normalizira na ponedjeljak istog tjedna prije spremanja.</summary>
    [Required]
    public DateTimeOffset AnchorDate { get; set; }

    public List<WorkingHoursIntervalRequest> Intervals { get; set; } = new();
}