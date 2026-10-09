using System;
using System.ComponentModel.DataAnnotations;
using BlueDragon.DuneLight.Core.Shared;

namespace BlueDragon.DuneLight.Core.DTOs.Catalog;

public class ResourceDto
{
    public Guid Id { get; set; }
    public Guid CompanyId { get; set; }
    public string? CompanyName { get; set; }
    public string Name { get; set; }

    /// <summary>Broj raspoloživih jedinica resursa (≥ 1).</summary>
    public int Capacity { get; set; }
    public bool IsActive { get; set; }
    public string? Note { get; set; }
    public int SortOrder { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public Guid? CreatedBy { get; set; }
    public DateTimeOffset? UpdatedAt { get; set; }
    public Guid? UpdatedBy { get; set; }
}

public class ResourceCreateRequest
{
    [Required]
    public Guid CompanyId { get; set; }

    [Required]
    [MaxLength(255)]
    public string Name { get; set; }

    [Range(CatalogCapacity.Min, int.MaxValue, ErrorMessage = "Kapacitet resursa mora biti najmanje 1.")]
    public int Capacity { get; set; }

    public string? Note { get; set; }

    public int SortOrder { get; set; }
}

/// <summary>CompanyId je namjerno izostavljen — resurs se ne premješta u drugu poslovnicu nakon kreiranja (isto kao
/// RoomUpdateRequest). Za premještaj: deaktivirati stari i kreirati novi u ciljnoj poslovnici.</summary>
public class ResourceUpdateRequest
{
    [Required]
    [MaxLength(255)]
    public string Name { get; set; }

    [Range(CatalogCapacity.Min, int.MaxValue, ErrorMessage = "Kapacitet resursa mora biti najmanje 1.")]
    public int Capacity { get; set; }

    public string? Note { get; set; }

    public int SortOrder { get; set; }
}
