using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using BlueDragon.DuneLight.Core.Enums;

namespace BlueDragon.DuneLight.Core.DTOs.Appointments;

public class AppointmentClientDto
{
    public Guid ClientId { get; set; }
    public string ClientName { get; set; }
    public Guid? ClientPackageId { get; set; }
    public bool PackageEntryDeducted { get; set; }
    public bool PackageEntryReturned { get; set; }
}

public class AppointmentDto
{
    public Guid Id { get; set; }
    public AppointmentForm Form { get; set; }
    public DateTimeOffset StartsAt { get; set; }
    public int DurationMinutes { get; set; }
    public Guid ServiceId { get; set; }
    public string ServiceName { get; set; }
    public string ServiceCategoryColorHex { get; set; }
    public Guid? EmployeeId { get; set; }
    public string EmployeeName { get; set; }
    public Guid LocationId { get; set; }
    public string LocationName { get; set; }
    public decimal Amount { get; set; }
    public decimal SuggestedAmount { get; set; }
    public bool IsAmountManuallyOverridden { get; set; }
    public PaymentMethod? PaymentMethod { get; set; }
    public bool IsPaid { get; set; }
    public AppointmentStatus Status { get; set; }
    public string Note { get; set; }
    public Guid? GroupId { get; set; }
    public Guid? RecurrenceGroupId { get; set; }
    public List<AppointmentClientDto> Clients { get; set; } = new();

    /// <summary>Popunjeno samo kao odgovor na create/update (preklapanje trenera/klijenata) — inače prazno.</summary>
    public List<string> Warnings { get; set; } = new();

    public DateTimeOffset CreatedAt { get; set; }
    public Guid? CreatedBy { get; set; }
    public DateTimeOffset? UpdatedAt { get; set; }
    public Guid? UpdatedBy { get; set; }
}

/// <summary>Lagani DTO za ćelije rasporeda — puni detalj dolazi preko GetById na klik.</summary>
public class AppointmentScheduleCellDto
{
    public Guid Id { get; set; }
    public DateTimeOffset StartsAt { get; set; }
    public int DurationMinutes { get; set; }
    public Guid ServiceId { get; set; }
    public string ServiceName { get; set; }
    public string ServiceCategoryColorHex { get; set; }
    public Guid? EmployeeId { get; set; }
    public string EmployeeName { get; set; }
    public Guid LocationId { get; set; }
    public string LocationName { get; set; }
    public List<string> ClientNames { get; set; } = new();
    public AppointmentStatus Status { get; set; }
    public bool IsCancelled { get; set; }

    public AppointmentForm Form { get; set; }

    /// <summary>Popunjeno samo za Form=Group.</summary>
    public Guid? GroupId { get; set; }

    /// <summary>Naziv grupe — za prikaz u ćeliji umjesto imena klijenta (ClientNames je prazan za grupne termine).</summary>
    public string GroupName { get; set; }

    /// <summary>Broj članova s Attended=true. Null za individualne termine.</summary>
    public int? AttendanceCount { get; set; }

    /// <summary>Broj aktivnih članova grupe (roster kapacitet za prikaz, npr. "6/8"). Null za individualne termine.</summary>
    public int? ExpectedCount { get; set; }
}

public class AppointmentScheduleQuery
{
    [Required]
    public DateTimeOffset From { get; set; }

    [Required]
    public DateTimeOffset To { get; set; }

    public Guid? LocationId { get; set; }
    public Guid? EmployeeId { get; set; }
    public Guid? ServiceId { get; set; }
    public Guid? ServiceCategoryId { get; set; }
    public AppointmentStatus? Status { get; set; }
}

public class AppointmentClientPackageSelection
{
    [Required]
    public Guid ClientId { get; set; }

    [Required]
    public Guid ClientPackageId { get; set; }
}

/// <summary>"Zakaži" — kreira termin u statusu Scheduled, bez naplate.</summary>
public class AppointmentCreateRequest
{
    [Required]
    public DateTimeOffset StartsAt { get; set; }

    [Required]
    public Guid ServiceId { get; set; }

    [Required]
    public Guid EmployeeId { get; set; }

    [Required]
    public Guid LocationId { get; set; }

    [Required]
    [MinLength(1, ErrorMessage = "Termin mora imati barem jednog klijenta.")]
    public List<Guid> ClientIds { get; set; } = new();

    /// <summary>Ručni override predložene cijene. Null = koristi se predložena cijena iz cjenika.</summary>
    [Range(0, double.MaxValue, ErrorMessage = "Iznos ne smije biti negativan.")]
    public decimal? Amount { get; set; }

    public string Note { get; set; }
}

/// <summary>"Upiši odrađeno" — kreira/prevodi termin u Completed, naplata odmah.</summary>
public class AppointmentCompleteRequest : AppointmentCreateRequest
{
    [Required]
    public PaymentMethod PaymentMethod { get; set; }

    /// <summary>Obavezno i mora pokrivati sve ClientIds kad je PaymentMethod = Package.</summary>
    public List<AppointmentClientPackageSelection> PackageSelections { get; set; } = new();
}

/// <summary>Izmjena vremena/usluge/trenera/lokacije/klijenata/napomene/iznosa. Ne dira plaćanje/paket — za to postoje complete/cancel/no-show.</summary>
public class AppointmentUpdateRequest : AppointmentCreateRequest
{
}

/// <summary>Brzo pomicanje termina (drag-and-drop) — mijenja samo StartsAt i po potrebi trenera/lokaciju.
/// Ne dira uslugu/klijente/iznos/napomenu/plaćanje/paket.</summary>
public class AppointmentMoveRequest
{
    [Required]
    public DateTimeOffset StartsAt { get; set; }

    /// <summary>Null = trener se ne mijenja.</summary>
    public Guid? EmployeeId { get; set; }

    /// <summary>Null = lokacija se ne mijenja.</summary>
    public Guid? LocationId { get; set; }
}

public class AppointmentCancelRequest
{
    /// <summary>Klijenti kojima se eksplicitno vraća skinuti ulazak iz paketa. Prazna lista = ništa se ne vraća.</summary>
    public List<Guid> ReturnEntryForClientIds { get; set; } = new();
}

public class RecurringAppointmentCreateRequest
{
    /// <summary>Daily = svaki kalendarski dan uključivo vikend; Weekly = +7 dana (postojeće ponašanje).</summary>
    [Required]
    public RecurrenceType RecurrenceType { get; set; }

    [Required]
    public Guid ServiceId { get; set; }

    [Required]
    public Guid EmployeeId { get; set; }

    [Required]
    public Guid LocationId { get; set; }

    [Required]
    [MinLength(1, ErrorMessage = "Termin mora imati barem jednog klijenta.")]
    public List<Guid> ClientIds { get; set; } = new();

    /// <summary>Datum/vrijeme prvog termina — dan u tjednu i vrijeme se ponavljaju iz ovoga.</summary>
    [Required]
    public DateTimeOffset FirstOccurrenceStartsAt { get; set; }

    [Required]
    public DateTimeOffset EndDate { get; set; }

    public string Note { get; set; }
}

/// <summary>Jedan sudarajući datum u nizu — dio { conflicts: [...] } priloga uz 409 RECURRING_CONFLICT.</summary>
public class RecurringConflictDetail
{
    public DateTimeOffset Date { get; set; }

    /// <summary>ErrorCodes.RecurringConflictReasonAppointment ili ErrorCodes.RecurringConflictReasonRosterAbsence.</summary>
    public string Reason { get; set; }
}
