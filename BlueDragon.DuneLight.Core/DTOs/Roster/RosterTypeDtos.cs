using System;
using System.ComponentModel.DataAnnotations;

namespace BlueDragon.DuneLight.Core.DTOs.Roster;

public class RosterTypeDto
{
    public Guid Id { get; set; }
    public string Name { get; set; }
    public string ColorHex { get; set; }
    public bool CountsAsWork { get; set; }
    public bool IsAbsence { get; set; }
    public bool RequiresTime { get; set; }

    /// <summary>Troši li se fond godišnjeg odmora kod upisa ovog tipa (mora biti IsAbsence=true).</summary>
    public bool DeductsFromLeaveFund { get; set; }
    public bool IsActive { get; set; }
    public int SortOrder { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public Guid? CreatedBy { get; set; }
    public DateTimeOffset? UpdatedAt { get; set; }
    public Guid? UpdatedBy { get; set; }
}

public class RosterTypeCreateRequest
{
    [Required]
    [MaxLength(255)]
    public string Name { get; set; }

    [MaxLength(7)]
    public string ColorHex { get; set; }

    public bool CountsAsWork { get; set; }
    public bool IsAbsence { get; set; }
    public bool RequiresTime { get; set; }

    /// <summary>Mora biti IsAbsence=true — validirano u servisu (LEAVE_FUND_TYPE_MUST_BE_ABSENCE).</summary>
    public bool DeductsFromLeaveFund { get; set; }
    public int SortOrder { get; set; }
}

public class RosterTypeUpdateRequest
{
    [Required]
    [MaxLength(255)]
    public string Name { get; set; }

    [MaxLength(7)]
    public string ColorHex { get; set; }

    public bool CountsAsWork { get; set; }
    public bool IsAbsence { get; set; }
    public bool RequiresTime { get; set; }

    /// <summary>Mora biti IsAbsence=true — validirano u servisu (LEAVE_FUND_TYPE_MUST_BE_ABSENCE).</summary>
    public bool DeductsFromLeaveFund { get; set; }
    public int SortOrder { get; set; }
}
