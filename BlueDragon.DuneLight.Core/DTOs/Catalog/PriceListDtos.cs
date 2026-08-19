using System;
using System.ComponentModel.DataAnnotations;
using BlueDragon.DuneLight.Core.Enums;

namespace BlueDragon.DuneLight.Core.DTOs.Catalog;

public class PriceListItemDto
{
    public Guid Id { get; set; }
    public PricingSubjectType SubjectType { get; set; }
    public Guid? ServiceId { get; set; }
    public string ServiceName { get; set; }
    public Guid? PackageId { get; set; }
    public string PackageName { get; set; }
    public Guid? CompanyId { get; set; }
    public string CompanyName { get; set; }
    public decimal Price { get; set; }
    public DateTimeOffset ValidFrom { get; set; }
    public DateTimeOffset? ValidTo { get; set; }
    public bool IsActive { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public Guid? CreatedBy { get; set; }
    public DateTimeOffset? UpdatedAt { get; set; }
    public Guid? UpdatedBy { get; set; }
}

public class PriceListItemCreateRequest
{
    [Required]
    public PricingSubjectType SubjectType { get; set; }

    /// <summary>Obavezno kad je SubjectType = Service.</summary>
    public Guid? ServiceId { get; set; }

    /// <summary>Obavezno kad je SubjectType = Package.</summary>
    public Guid? PackageId { get; set; }

    /// <summary>Null = vrijedi za sve tvrtke.</summary>
    public Guid? CompanyId { get; set; }

    [Range(0, double.MaxValue, ErrorMessage = "Cijena ne smije biti negativna.")]
    public decimal Price { get; set; }

    [Required]
    public DateTimeOffset ValidFrom { get; set; }

    public DateTimeOffset? ValidTo { get; set; }
}

public class PriceListItemUpdateRequest
{
    [Range(0, double.MaxValue, ErrorMessage = "Cijena ne smije biti negativna.")]
    public decimal Price { get; set; }

    [Required]
    public DateTimeOffset ValidFrom { get; set; }

    public DateTimeOffset? ValidTo { get; set; }
}

/// <summary>Stavka u pregledu trenutno važećeg cjenika za tvrtku.</summary>
public class EffectivePriceDto
{
    public PricingSubjectType SubjectType { get; set; }
    public Guid SubjectId { get; set; }
    public string SubjectName { get; set; }
    public decimal Price { get; set; }
    public PriceSource Source { get; set; }
}

public class ResolvePriceRequest
{
    [Required]
    public PricingSubjectType SubjectType { get; set; }

    [Required]
    public Guid SubjectId { get; set; }

    public Guid? CompanyId { get; set; }

    public DateTimeOffset? Date { get; set; }
}

public class ResolvePriceResponse
{
    public PricingSubjectType SubjectType { get; set; }
    public Guid SubjectId { get; set; }
    public Guid? CompanyId { get; set; }
    public DateTimeOffset Date { get; set; }
    public decimal Price { get; set; }
    public PriceSource Source { get; set; }
}

public enum PriceSource
{
    CompanySpecific,
    AllCompanies,
    Default
}
