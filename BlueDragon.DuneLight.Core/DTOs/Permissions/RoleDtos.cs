using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;

namespace BlueDragon.DuneLight.Core.DTOs.Permissions;

public class RoleDto
{
    public Guid Id { get; set; }
    public string Name { get; set; }
    public DateTimeOffset? CreatedAt { get; set; }
}

public class RoleCreateRequest
{
    [Required]
    [MaxLength(255)]
    public string Name { get; set; }
}

public class RoleUpdateRequest
{
    [Required]
    [MaxLength(255)]
    public string Name { get; set; }
}

/// <summary>Zamjenjuje CIJELI skup Role dodjela za korisnika (ne dodaje, nego postavlja). Prazan popis je dopušten.</summary>
public class AssignUserRolesRequest
{
    [Required]
    public List<Guid> RoleIds { get; set; } = new();
}