using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using BlueDragon.DuneLight.Core.Enums;

namespace BlueDragon.DuneLight.Core.DTOs.Catalog;

public class PackageServiceItemDto
{
    public Guid ServiceId { get; set; }
    public string ServiceName { get; set; }

    /// <summary>Relevantno samo kod PerService načina trošenja.</summary>
    public int? EntryCount { get; set; }
}

public class PackageServiceItemRequest
{
    [Required]
    public Guid ServiceId { get; set; }

    public int? EntryCount { get; set; }
}

public class PackageDto
{
    public Guid Id { get; set; }
    public string Name { get; set; }
    public string Description { get; set; }
    public PackageEntryMode EntryMode { get; set; }
    public int? TotalEntryCount { get; set; }
    public PackageValidityType ValidityType { get; set; }
    public int? ValidityDays { get; set; }
    public DateTimeOffset? ValidityFixedDate { get; set; }
    public decimal DefaultPrice { get; set; }
    public bool IsActive { get; set; }
    public int SortOrder { get; set; }
    public List<PackageServiceItemDto> Services { get; set; } = new();
    public DateTimeOffset CreatedAt { get; set; }
    public Guid? CreatedBy { get; set; }
    public DateTimeOffset? UpdatedAt { get; set; }
    public Guid? UpdatedBy { get; set; }
}

public class PackageCreateRequest
{
    [Required]
    [MaxLength(255)]
    public string Name { get; set; }

    public string Description { get; set; }

    [Required]
    public PackageEntryMode EntryMode { get; set; }

    /// <summary>Koristi se kod SharedPool. Null = neograničeno.</summary>
    [Range(1, int.MaxValue)]
    public int? TotalEntryCount { get; set; }

    [Required]
    public PackageValidityType ValidityType { get; set; }

    [Range(1, int.MaxValue)]
    public int? ValidityDays { get; set; }

    public DateTimeOffset? ValidityFixedDate { get; set; }

    [Range(0, double.MaxValue, ErrorMessage = "Cijena ne smije biti negativna.")]
    public decimal DefaultPrice { get; set; }

    public int SortOrder { get; set; }

    [Required]
    [MinLength(1, ErrorMessage = "Paket mora imati barem jednu uslugu.")]
    public List<PackageServiceItemRequest> Services { get; set; } = new();
}

public class PackageUpdateRequest
{
    [Required]
    [MaxLength(255)]
    public string Name { get; set; }

    public string Description { get; set; }

    [Required]
    public PackageEntryMode EntryMode { get; set; }

    [Range(1, int.MaxValue)]
    public int? TotalEntryCount { get; set; }

    [Required]
    public PackageValidityType ValidityType { get; set; }

    [Range(1, int.MaxValue)]
    public int? ValidityDays { get; set; }

    public DateTimeOffset? ValidityFixedDate { get; set; }

    [Range(0, double.MaxValue, ErrorMessage = "Cijena ne smije biti negativna.")]
    public decimal DefaultPrice { get; set; }

    public int SortOrder { get; set; }

    [Required]
    [MinLength(1, ErrorMessage = "Paket mora imati barem jednu uslugu.")]
    public List<PackageServiceItemRequest> Services { get; set; } = new();
}
