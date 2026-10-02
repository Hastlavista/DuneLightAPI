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

    /// <summary>Phase M1G — cijena zaposlenika (samo stavke usluge); null = cijena bez zaposlenika.</summary>
    public Guid? EmployeeId { get; set; }
    public string EmployeeName { get; set; }
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

    /// <summary>Phase M1G — cijena određenog zaposlenika (samo za SubjectType = Service; paket je uvijek bez zaposlenika).
    /// Null = cijena bez zaposlenika.</summary>
    public Guid? EmployeeId { get; set; }

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

    /// <summary>Phase M1G — izvor cijene: zaposlenik čije razine cjenika imaju prednost (Employee način). Null = Standard
    /// (razine zaposlenika se preskaču). Smije se navesti samo za SubjectType = Service.</summary>
    public Guid? EmployeeId { get; set; }

    public DateTimeOffset? Date { get; set; }
}

public class ResolvePriceResponse
{
    public PricingSubjectType SubjectType { get; set; }
    public Guid SubjectId { get; set; }
    public Guid? CompanyId { get; set; }

    /// <summary>Zaposlenik čije su razine razmatrane (izvor Employee); null = Standard.</summary>
    public Guid? EmployeeId { get; set; }
    public DateTimeOffset Date { get; set; }
    public decimal Price { get; set; }
    public PriceSource Source { get; set; }
}

/// <summary>Razina cjenika iz koje je cijena razriješena. Phase M1G: Employee* razine postoje samo za izvor cijene Employee.</summary>
public enum PriceSource
{
    EmployeeCompanySpecific,
    EmployeeAllCompanies,
    CompanySpecific,
    AllCompanies,
    Default
}
