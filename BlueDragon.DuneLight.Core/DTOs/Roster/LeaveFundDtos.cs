using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;

namespace BlueDragon.DuneLight.Core.DTOs.Roster;

public class EmployeeLeaveSettingsDto
{
    public Guid EmployeeId { get; set; }
    public int AnnualDays { get; set; }
    public int RenewalMonth { get; set; }
    public int RenewalDay { get; set; }
    public int CarryoverExpiryMonth { get; set; }
    public int CarryoverExpiryDay { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public Guid? CreatedBy { get; set; }
    public DateTimeOffset? UpdatedAt { get; set; }
    public Guid? UpdatedBy { get; set; }
}

/// <summary>RenewalMonth/Day i CarryoverExpiryMonth/Day su mjesec+dan BEZ godine (ponavljaju se svake godine) — 29.2. na neprijestupnu godinu klipa se na 28.2. (vidi LeaveFundYearCalculator).</summary>
public class EmployeeLeaveSettingsUpsertRequest
{
    [Range(0, 365)]
    public int AnnualDays { get; set; }

    [Range(1, 12)]
    public int RenewalMonth { get; set; }

    [Range(1, 31)]
    public int RenewalDay { get; set; }

    [Range(1, 12)]
    public int CarryoverExpiryMonth { get; set; }

    [Range(1, 31)]
    public int CarryoverExpiryDay { get; set; }
}

public class LeaveFundDto
{
    public Guid Id { get; set; }
    public int FundYear { get; set; }
    public DateTimeOffset OpenedAt { get; set; }
    public DateTimeOffset ExpiresAt { get; set; }
    public int AllocatedDays { get; set; }
    public int UsedDays { get; set; }
    public int RemainingDays => AllocatedDays - UsedDays;
    public bool IsExpired { get; set; }
}

/// <summary>Sve što treba za prikaz "preostalo X dana" — postavke (null ako još nisu podešene) + svi fondovi zaposlenika (uobičajeno tekući + eventualni neistekli prijenos iz prošle godine).</summary>
public class EmployeeLeaveFundsDto
{
    public Guid EmployeeId { get; set; }
    public string EmployeeName { get; set; }
    public EmployeeLeaveSettingsDto Settings { get; set; }
    public List<LeaveFundDto> Funds { get; set; } = new();
}

/// <summary>Ručna korekcija/otvaranje fonda za točno određenu obračunsku godinu (rijetko, admin) — upsert po (employeeId, FundYear), ne dira UsedDays/potrošnju.</summary>
public class LeaveFundManualUpsertRequest
{
    [Required]
    public int FundYear { get; set; }

    [Range(0, 365)]
    public int AllocatedDays { get; set; }
}
