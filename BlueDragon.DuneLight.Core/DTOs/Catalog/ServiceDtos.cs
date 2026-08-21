using System;
using System.ComponentModel.DataAnnotations;
using BlueDragon.DuneLight.Core.Enums;

namespace BlueDragon.DuneLight.Core.DTOs.Catalog;

public class ServiceDto
{
    public Guid Id { get; set; }
    public string Name { get; set; }
    public ServiceExecutionMode ExecutionMode { get; set; }
    public string ColorHex { get; set; }
    public int DefaultDurationMinutes { get; set; }
    public decimal DefaultPrice { get; set; }
    public string Description { get; set; }
    public bool IsActive { get; set; }
    public int SortOrder { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public Guid? CreatedBy { get; set; }
    public DateTimeOffset? UpdatedAt { get; set; }
    public Guid? UpdatedBy { get; set; }
}

public class ServiceCreateRequest
{
    [Required]
    [MaxLength(255)]
    public string Name { get; set; }

    [Required]
    public ServiceExecutionMode ExecutionMode { get; set; }

    [Required]
    [RegularExpression("^#[0-9A-Fa-f]{6}$", ErrorMessage = "Boja mora biti u obliku #RRGGBB.")]
    public string ColorHex { get; set; }

    [Range(1, int.MaxValue, ErrorMessage = "Trajanje mora biti veće od nule.")]
    public int DefaultDurationMinutes { get; set; }

    [Range(0, double.MaxValue, ErrorMessage = "Cijena ne smije biti negativna.")]
    public decimal DefaultPrice { get; set; }

    public string Description { get; set; }

    public int SortOrder { get; set; }
}

public class ServiceUpdateRequest
{
    [Required]
    [MaxLength(255)]
    public string Name { get; set; }

    [Required]
    public ServiceExecutionMode ExecutionMode { get; set; }

    [Required]
    [RegularExpression("^#[0-9A-Fa-f]{6}$", ErrorMessage = "Boja mora biti u obliku #RRGGBB.")]
    public string ColorHex { get; set; }

    [Range(1, int.MaxValue, ErrorMessage = "Trajanje mora biti veće od nule.")]
    public int DefaultDurationMinutes { get; set; }

    [Range(0, double.MaxValue, ErrorMessage = "Cijena ne smije biti negativna.")]
    public decimal DefaultPrice { get; set; }

    public string Description { get; set; }

    public int SortOrder { get; set; }
}
