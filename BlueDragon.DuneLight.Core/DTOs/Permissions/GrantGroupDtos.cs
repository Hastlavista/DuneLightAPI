using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;

namespace BlueDragon.DuneLight.Core.DTOs.Permissions;

public class GrantGroupDto
{
    public Guid Id { get; set; }
    public string Name { get; set; }
    public List<string> Grants { get; set; } = new();
    public int AssignedUserCount { get; set; }
    public DateTimeOffset? CreatedAt { get; set; }
    public DateTimeOffset? UpdatedAt { get; set; }
}

public class GrantGroupCreateRequest
{
    [Required]
    [MaxLength(255)]
    public string Name { get; set; }

    /// <summary>Svaki ključ mora postojati u Grants katalogu (validira se u servisu) — prazan popis je dopušten (grupa bez ovlasti).</summary>
    [Required]
    public List<string> Grants { get; set; } = new();
}

public class GrantGroupUpdateRequest
{
    [Required]
    [MaxLength(255)]
    public string Name { get; set; }

    [Required]
    public List<string> Grants { get; set; } = new();
}

/// <summary>Zamjenjuje CIJELI skup GrantGroup dodjela za korisnika (ne dodaje, nego postavlja).</summary>
public class AssignUserGrantGroupsRequest
{
    [Required]
    public List<Guid> GrantGroupIds { get; set; } = new();
}