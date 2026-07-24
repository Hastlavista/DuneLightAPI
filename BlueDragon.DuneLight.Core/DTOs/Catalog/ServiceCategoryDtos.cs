using System;
using System.ComponentModel.DataAnnotations;
using BlueDragon.DuneLight.Core.Enums;

namespace BlueDragon.DuneLight.Core.DTOs.Catalog;

public class ServiceCategoryDto
{
    public Guid Id { get; set; }
    public string Name { get; set; }
    public ServiceExecutionMode ExecutionMode { get; set; }
    public string ColorHex { get; set; }
    public bool RequiresClient { get; set; }
    public bool CountsTowardRevenue { get; set; }
    public string Description { get; set; }
    public bool IsActive { get; set; }
    public int SortOrder { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public Guid? CreatedBy { get; set; }
    public DateTimeOffset? UpdatedAt { get; set; }
    public Guid? UpdatedBy { get; set; }
}

public class ServiceCategoryCreateRequest
{
    [Required]
    [MaxLength(255)]
    public string Name { get; set; }

    [Required]
    public ServiceExecutionMode ExecutionMode { get; set; }

    [MaxLength(7)]
    public string ColorHex { get; set; }

    public bool RequiresClient { get; set; }
    public bool CountsTowardRevenue { get; set; }
    public string Description { get; set; }
    public int SortOrder { get; set; }
}

public class ServiceCategoryUpdateRequest
{
    [Required]
    [MaxLength(255)]
    public string Name { get; set; }

    [Required]
    public ServiceExecutionMode ExecutionMode { get; set; }

    [MaxLength(7)]
    public string ColorHex { get; set; }

    public bool RequiresClient { get; set; }
    public bool CountsTowardRevenue { get; set; }
    public string Description { get; set; }
    public int SortOrder { get; set; }
}
