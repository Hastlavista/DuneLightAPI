using System;
using System.ComponentModel.DataAnnotations;
using BlueDragon.DuneLight.Core.Enums;

namespace BlueDragon.DuneLight.Core.DTOs.ScheduleBreaks;

public class ScheduleBreakDto
{
    public Guid Id { get; set; }
    public Guid EmployeeId { get; set; }
    public string EmployeeName { get; set; }
    public Guid CompanyId { get; set; }
    public string CompanyName { get; set; }
    public DateTimeOffset StartsAt { get; set; }
    public int DurationMinutes { get; set; }
    public string Note { get; set; }
    public Guid? RecurrenceGroupId { get; set; }

    public DateTimeOffset CreatedAt { get; set; }
    public Guid? CreatedBy { get; set; }
    public DateTimeOffset? UpdatedAt { get; set; }
    public Guid? UpdatedBy { get; set; }
}

/// <summary>Lagani DTO za ćelije rasporeda — dio ScheduleFeedDto.Breaks u GET /api/appointments/schedule.</summary>
public class ScheduleBreakCellDto
{
    public Guid Id { get; set; }
    public DateTimeOffset StartsAt { get; set; }
    public int DurationMinutes { get; set; }
    public Guid EmployeeId { get; set; }
    public string EmployeeName { get; set; }
    public Guid CompanyId { get; set; }
    public string CompanyName { get; set; }
    public string Note { get; set; }
}

public class ScheduleBreakQuery
{
    [Required]
    public DateTimeOffset From { get; set; }

    [Required]
    public DateTimeOffset To { get; set; }

    public Guid? EmployeeId { get; set; }
    public Guid? CompanyId { get; set; }
}

public class ScheduleBreakCreateRequest
{
    [Required]
    public DateTimeOffset StartsAt { get; set; }

    [Required]
    public Guid EmployeeId { get; set; }

    [Required]
    public Guid CompanyId { get; set; }

    [Required]
    [Range(1, int.MaxValue, ErrorMessage = "Trajanje mora biti veće od nule.")]
    public int DurationMinutes { get; set; }

    public string Note { get; set; }
}

public class ScheduleBreakUpdateRequest : ScheduleBreakCreateRequest
{
}

public class RecurringScheduleBreakCreateRequest
{
    /// <summary>Daily = svaki kalendarski dan uključivo vikend; Weekly = +7 dana.</summary>
    [Required]
    public RecurrenceType RecurrenceType { get; set; }

    [Required]
    public Guid EmployeeId { get; set; }

    [Required]
    public Guid CompanyId { get; set; }

    /// <summary>Datum/vrijeme prve pauze — dan u tjednu i vrijeme se ponavljaju iz ovoga.</summary>
    [Required]
    public DateTimeOffset FirstOccurrenceStartsAt { get; set; }

    [Required]
    [Range(1, int.MaxValue, ErrorMessage = "Trajanje mora biti veće od nule.")]
    public int DurationMinutes { get; set; }

    [Required]
    public DateTimeOffset EndDate { get; set; }

    public string Note { get; set; }
}
