using System;
using System.ComponentModel.DataAnnotations;

namespace BlueDragon.DuneLight.Core.DTOs.Employees;

public class EngagementTypeDto
{
    public Guid Id { get; set; }
    public string Name { get; set; }
    public bool IsActive { get; set; }
    public int SortOrder { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public Guid? CreatedBy { get; set; }
    public DateTimeOffset? UpdatedAt { get; set; }
    public Guid? UpdatedBy { get; set; }
}

public class EngagementTypeCreateRequest
{
    [Required]
    [MaxLength(255)]
    public string Name { get; set; }

    public int SortOrder { get; set; }
}

public class EngagementTypeUpdateRequest
{
    [Required]
    [MaxLength(255)]
    public string Name { get; set; }

    public int SortOrder { get; set; }
}
