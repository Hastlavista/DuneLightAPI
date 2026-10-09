using System;
using System.ComponentModel.DataAnnotations;

namespace BlueDragon.DuneLight.Core.DTOs.Catalog;

/// <summary>K1-4 (12.3) — šifra razloga otkazivanja/izostanka organizacije. Vrijedi za odabrane događaje; deaktivirana šifra se
/// više ne nudi, a sudjelovanja zadržavaju naziv iz trenutka događaja (snapshot). Bez utjecaja na politiku naplate.</summary>
public class CancellationReasonDto
{
    public Guid Id { get; set; }
    public string Name { get; set; }
    public bool IsActive { get; set; }
    public int SortOrder { get; set; }
    public bool AppliesToClientCancellation { get; set; }
    public bool AppliesToBusinessCancellation { get; set; }
    public bool AppliesToNoShow { get; set; }
}

/// <summary>K1-4 — kreiranje i izmjena šifre (izmjena je potpuna zamjena). Barem jedan događaj mora biti odabran.</summary>
public class CancellationReasonUpsertRequest
{
    [Required]
    [MaxLength(255)]
    public string Name { get; set; }

    public int SortOrder { get; set; }
    public bool AppliesToClientCancellation { get; set; }
    public bool AppliesToBusinessCancellation { get; set; }
    public bool AppliesToNoShow { get; set; }
}
