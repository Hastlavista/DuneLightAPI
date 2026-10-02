using System;
using System.Collections.Generic;
using BlueDragon.DuneLight.Core.Enums;

namespace BlueDragon.DuneLight.Core.DTOs.Commissions;

public class CommissionEntryDto
{
    public Guid Id { get; set; }
    public Guid EmployeeId { get; set; }
    public string EmployeeName { get; set; }
    public Guid CompanyId { get; set; }
    public string CompanyName { get; set; }
    public CommissionSourceType SourceType { get; set; }
    public Guid? AppointmentId { get; set; }
    public Guid? BookingId { get; set; }

    /// <summary>Phase M1G — izvorno sudjelovanje (IndividualService) ili segment sesije (GroupService); jedno sudjelovanje
    /// smije imati više zapisa (po jedan za svakog zaposlenika segmenta).</summary>
    public Guid? BookingSegmentParticipationId { get; set; }
    public Guid? AppointmentSegmentId { get; set; }
    public Guid? CheckoutItemId { get; set; }
    public decimal BaseAmount { get; set; }
    public CommissionCalculationType CalculationType { get; set; }
    public decimal RuleValue { get; set; }
    public decimal CommissionAmount { get; set; }
    public CommissionEntryStatus Status { get; set; }
    public DateTimeOffset EarnedAt { get; set; }
}

/// <summary>Ograđen datumski raspon je obavezan (From/To) — namjerno bez "sva povijest" endpointa (vidi spec
/// section 46). EmployeeId/CompanyId su opcionalni filtri.</summary>
public class CommissionEntryQuery
{
    public Guid? EmployeeId { get; set; }
    public Guid? CompanyId { get; set; }
    public DateTimeOffset From { get; set; }
    public DateTimeOffset To { get; set; }
    public int Page { get; set; } = 1;
    public int PageSize { get; set; } = 20;
}

public class CommissionSummaryQuery
{
    public Guid? EmployeeId { get; set; }
    public Guid? CompanyId { get; set; }
    public DateTimeOffset From { get; set; }
    public DateTimeOffset To { get; set; }
}

public class EmployeeCommissionSummaryDto
{
    public Guid EmployeeId { get; set; }
    public string EmployeeName { get; set; }
    public decimal EarnedAmount { get; set; }
    public decimal ReversedAmount { get; set; }
    public decimal NetAmount { get; set; }
    public int EntryCount { get; set; }
}

public class CommissionSummaryResultDto
{
    public List<EmployeeCommissionSummaryDto> Employees { get; set; } = new();
    public decimal TotalNetAmount { get; set; }
}
