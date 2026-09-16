using System;
using System.ComponentModel.DataAnnotations;

namespace BlueDragon.DuneLight.Core.DTOs.Products;

public class ProductDto
{
    public Guid Id { get; set; }
    public string Name { get; set; }
    public string Description { get; set; }
    public string Sku { get; set; }
    public decimal DefaultPrice { get; set; }
    public bool IsActive { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public Guid? CreatedBy { get; set; }
    public DateTimeOffset? UpdatedAt { get; set; }
    public Guid? UpdatedBy { get; set; }
}

public class ProductCreateRequest
{
    [Required]
    [MaxLength(255)]
    public string Name { get; set; }

    public string Description { get; set; }

    [MaxLength(100)]
    public string Sku { get; set; }

    [Range(0, double.MaxValue, ErrorMessage = "Cijena ne smije biti negativna.")]
    public decimal DefaultPrice { get; set; }
}

public class ProductUpdateRequest
{
    [Required]
    [MaxLength(255)]
    public string Name { get; set; }

    public string Description { get; set; }

    [MaxLength(100)]
    public string Sku { get; set; }

    [Range(0, double.MaxValue, ErrorMessage = "Cijena ne smije biti negativna.")]
    public decimal DefaultPrice { get; set; }
}
