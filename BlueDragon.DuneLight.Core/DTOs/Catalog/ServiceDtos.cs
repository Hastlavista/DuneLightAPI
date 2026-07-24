using System;
using System.ComponentModel.DataAnnotations;

namespace BlueDragon.DuneLight.Core.DTOs.Catalog;

public class ServiceDto
{
    public Guid Id { get; set; }
    public string Name { get; set; }
    public Guid ServiceCategoryId { get; set; }
    public string ServiceCategoryName { get; set; }
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
    public Guid ServiceCategoryId { get; set; }

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
    public Guid ServiceCategoryId { get; set; }

    [Range(1, int.MaxValue, ErrorMessage = "Trajanje mora biti veće od nule.")]
    public int DefaultDurationMinutes { get; set; }

    [Range(0, double.MaxValue, ErrorMessage = "Cijena ne smije biti negativna.")]
    public decimal DefaultPrice { get; set; }

    public string Description { get; set; }

    public int SortOrder { get; set; }
}
