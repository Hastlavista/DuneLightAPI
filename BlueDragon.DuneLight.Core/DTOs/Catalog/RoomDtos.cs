using System;
using System.ComponentModel.DataAnnotations;

namespace BlueDragon.DuneLight.Core.DTOs.Catalog;

public class RoomDto
{
    public Guid Id { get; set; }
    public Guid CompanyId { get; set; }
    public string CompanyName { get; set; }
    public string Name { get; set; }
    public bool AllowConcurrentBookings { get; set; }
    public bool IsActive { get; set; }
    public string Note { get; set; }
    public int SortOrder { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public Guid? CreatedBy { get; set; }
    public DateTimeOffset? UpdatedAt { get; set; }
    public Guid? UpdatedBy { get; set; }
}

public class RoomCreateRequest
{
    [Required]
    public Guid CompanyId { get; set; }

    [Required]
    [MaxLength(255)]
    public string Name { get; set; }

    public bool AllowConcurrentBookings { get; set; }

    public string Note { get; set; }

    public int SortOrder { get; set; }
}

/// <summary>CompanyId je namjerno izostavljen — prostorija se ne smije premjestiti u drugu poslovnicu
/// nakon kreiranja (povijesni termini bi inače djelovali kao da su se dogodili na drugoj lokaciji).
/// Za premještaj: deaktivirati staru prostoriju i kreirati novu u ciljnoj poslovnici.</summary>
public class RoomUpdateRequest
{
    [Required]
    [MaxLength(255)]
    public string Name { get; set; }

    public bool AllowConcurrentBookings { get; set; }

    public string Note { get; set; }

    public int SortOrder { get; set; }
}
