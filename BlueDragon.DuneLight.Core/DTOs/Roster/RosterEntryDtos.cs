using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;

namespace BlueDragon.DuneLight.Core.DTOs.Roster;

public class RosterEntryDto
{
    public Guid Id { get; set; }
    public Guid EmployeeId { get; set; }
    public string EmployeeName { get; set; }
    public Guid RosterTypeId { get; set; }
    public string RosterTypeName { get; set; }
    public string RosterTypeColorHex { get; set; }
    public bool IsAbsence { get; set; }
    public bool CountsAsWork { get; set; }

    /// <summary>Oblik rad: jedini datum. Oblik odsutnost: "datum od".</summary>
    public DateTimeOffset DateFrom { get; set; }

    /// <summary>Uvijek null za oblik rad. Za odsutnost: null = otvorena (još traje).</summary>
    public DateTimeOffset? DateTo { get; set; }

    public TimeSpan? StartTime { get; set; }
    public TimeSpan? EndTime { get; set; }
    public decimal? DurationHours { get; set; }
    public string Note { get; set; }

    /// <summary>Samo za work-formu: zamjenjuje WorkingHoursTemplate za ovaj EmployeeId+DateFrom kod izračuna dostupnosti. Uvijek false za odsutnost.</summary>
    public bool IsOverride { get; set; }

    /// <summary>Popunjeno samo kao odgovor na create/update (preklapanje s drugim zapisom istog zaposlenika) — inače prazno.</summary>
    public List<string> Warnings { get; set; } = new();

    public DateTimeOffset CreatedAt { get; set; }
    public Guid? CreatedBy { get; set; }
    public DateTimeOffset? UpdatedAt { get; set; }
    public Guid? UpdatedBy { get; set; }
}

/// <summary>
/// Jedan zahtjev za oba oblika zapisa — koji su podaci obavezni/dopušteni ovisi o RosterType.IsAbsence
/// (validira se u servisu). Rad: DateTo mora biti prazan, StartTime/EndTime obavezni.
/// Odsutnost: StartTime/EndTime moraju biti prazni, DateTo opcionalan (otvorena odsutnost).
/// </summary>
public class RosterEntryCreateRequest
{
    [Required]
    public Guid EmployeeId { get; set; }

    [Required]
    public Guid RosterTypeId { get; set; }

    [Required]
    public DateTimeOffset DateFrom { get; set; }

    public DateTimeOffset? DateTo { get; set; }
    public TimeSpan? StartTime { get; set; }
    public TimeSpan? EndTime { get; set; }
    public string Note { get; set; }

    /// <summary>Samo za work-formu — ignorira se za odsutnost. Svi work-redovi istog EmployeeId+DateFrom moraju dijeliti istu vrijednost.</summary>
    public bool IsOverride { get; set; }
}

public class RosterEntryUpdateRequest
{
    [Required]
    public Guid EmployeeId { get; set; }

    [Required]
    public Guid RosterTypeId { get; set; }

    [Required]
    public DateTimeOffset DateFrom { get; set; }

    public DateTimeOffset? DateTo { get; set; }
    public TimeSpan? StartTime { get; set; }
    public TimeSpan? EndTime { get; set; }
    public string Note { get; set; }

    /// <summary>Samo za work-formu — ignorira se za odsutnost. Svi work-redovi istog EmployeeId+DateFrom moraju dijeliti istu vrijednost.</summary>
    public bool IsOverride { get; set; }
}
