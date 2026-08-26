using System;
using System.ComponentModel.DataAnnotations;

namespace BlueDragon.DuneLight.Core.DTOs.Catalog;

public class CompanyDto
{
    public Guid Id { get; set; }
    public string Name { get; set; }
    public string Address { get; set; }
    public string Phone { get; set; }
    public string ColorHex { get; set; }

    /// <summary>ISO 3166-1 alpha-2 kod (npr. "HR") — određuje koji katalog fiksnih praznika Generate koristi.</summary>
    public string Country { get; set; }
    public bool IsActive { get; set; }
    public string Note { get; set; }
    public int SortOrder { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public Guid? CreatedBy { get; set; }
    public DateTimeOffset? UpdatedAt { get; set; }
    public Guid? UpdatedBy { get; set; }
}

public class CompanyCreateRequest
{
    [Required]
    [MaxLength(255)]
    public string Name { get; set; }

    [MaxLength(500)]
    public string Address { get; set; }

    [MaxLength(50)]
    public string Phone { get; set; }

    [MaxLength(7)]
    public string ColorHex { get; set; }

    /// <summary>ISO 3166-1 alpha-2 kod — samo "HR" trenutno podržan u katalogu fiksnih praznika (vidi DefaultCompanyHolidays).</summary>
    [Required]
    [MaxLength(2)]
    public string Country { get; set; } = "HR";

    public string Note { get; set; }

    public int SortOrder { get; set; }
}

public class CompanyUpdateRequest
{
    [Required]
    [MaxLength(255)]
    public string Name { get; set; }

    [MaxLength(500)]
    public string Address { get; set; }

    [MaxLength(50)]
    public string Phone { get; set; }

    [MaxLength(7)]
    public string ColorHex { get; set; }

    /// <summary>ISO 3166-1 alpha-2 kod — samo "HR" trenutno podržan u katalogu fiksnih praznika (vidi DefaultCompanyHolidays).</summary>
    [Required]
    [MaxLength(2)]
    public string Country { get; set; } = "HR";

    public string Note { get; set; }

    public int SortOrder { get; set; }
}
