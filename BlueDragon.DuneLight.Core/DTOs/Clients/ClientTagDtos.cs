using System;
using System.ComponentModel.DataAnnotations;

namespace BlueDragon.DuneLight.Core.DTOs.Clients;

public class ClientTagDto
{
    public Guid Id { get; set; }
    public string Name { get; set; }
    public string ColorHex { get; set; }
    public bool IsActive { get; set; }
    public int SortOrder { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public Guid? CreatedBy { get; set; }
    public DateTimeOffset? UpdatedAt { get; set; }
    public Guid? UpdatedBy { get; set; }
}

public class ClientTagCreateRequest
{
    [Required]
    [MaxLength(255)]
    public string Name { get; set; }

    [MaxLength(7)]
    public string ColorHex { get; set; }

    public int SortOrder { get; set; }
}

public class ClientTagUpdateRequest
{
    [Required]
    [MaxLength(255)]
    public string Name { get; set; }

    [MaxLength(7)]
    public string ColorHex { get; set; }

    public int SortOrder { get; set; }
}
