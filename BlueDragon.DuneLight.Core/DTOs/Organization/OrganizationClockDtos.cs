using System;
using System.Collections.Generic;

namespace BlueDragon.DuneLight.Core.DTOs.Organization;

/// <summary>
/// T1 — trenutno vrijeme organizacije: jedini izvor "sada" i "danas" za frontend (FE-ADR-0005). Bez simuliranog pomaka
/// (testni alati isključeni ili organizacija nije pomaknuta) EffectiveUtc = RealUtc i Offset = 0.
/// </summary>
public class OrganizationClockDto
{
    /// <summary>Stvarni trenutak (UTC).</summary>
    public DateTimeOffset RealUtc { get; set; }

    /// <summary>Poslovni trenutak organizacije (UTC) = stvarni + pomak. Po njemu sustav računa sva poslovna pravila.</summary>
    public DateTimeOffset EffectiveUtc { get; set; }

    /// <summary>Simulirani pomak (0 bez pomaka). Samo naprijed.</summary>
    public TimeSpan Offset { get; set; }

    public bool IsSimulated { get; set; }

    /// <summary>IANA zona organizacije.</summary>
    public string TimeZone { get; set; }

    /// <summary>Poslovni datum u zoni organizacije.</summary>
    public DateOnly LocalDate { get; set; }

    /// <summary>Lokalno vrijeme u zoni organizacije.</summary>
    public TimeOnly LocalTime { get; set; }

    /// <summary>Poslovnice čija se efektivna zona razlikuje od zone organizacije.</summary>
    public List<CompanyClockDto> Companies { get; set; } = new();
}

public class CompanyClockDto
{
    public Guid CompanyId { get; set; }
    public string TimeZone { get; set; }
    public DateOnly LocalDate { get; set; }
    public TimeOnly LocalTime { get; set; }
}
