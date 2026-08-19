using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using BlueDragon.DuneLight.Core.Enums;
using BlueDragon.DuneLight.Infrastructure.Domain.Models.Catalog;
using BlueDragon.DuneLight.Infrastructure.Domain.Models.Employees;
using BlueDragon.DuneLight.Infrastructure.Domain.Models.Groups;

namespace BlueDragon.DuneLight.Infrastructure.Domain.Models.Appointments;

/// <summary>
/// Termin. Form=Individual: jedan ili više klijenata, naplata po terminu. Form=Group: veza na
/// Group/GroupSlot koji ga je generirao; termin nema vlastiti iznos (Amount=0, IsPaid=false) —
/// naplata ide kroz pakete članova na razini AppointmentAttendance.
/// </summary>
[Table("appointments")]
public class Appointment
{
    [Key]
    [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
    [Column("id")]
    public Guid? Id { get; set; }

    [Column("organization_id")]
    public Guid OrganizationId { get; set; }

    [Column("form")]
    public AppointmentForm Form { get; set; }

    /// <summary>Pohranjeno u UTC, proizvoljno vrijeme (ne samo puni sat).</summary>
    [Column("starts_at")]
    public DateTimeOffset StartsAt { get; set; }

    /// <summary>Snapshot Service.DefaultDurationMinutes u trenutku kreiranja termina.</summary>
    [Column("duration_minutes")]
    public int DurationMinutes { get; set; }

    [Column("service_id")]
    public Guid ServiceId { get; set; }

    /// <summary>Null samo za grupne termine čija grupa nema zadanog trenera — dodjeljuje se ručno naknadno.
    /// Individualni termin uvijek ima trenera.</summary>
    [Column("employee_id")]
    public Guid? EmployeeId { get; set; }

    [Column("company_id")]
    public Guid CompanyId { get; set; }

    [Column("amount")]
    public decimal Amount { get; set; }

    /// <summary>Snapshot predložene cijene iz IPriceResolutionService u trenutku kreiranja/naplate.</summary>
    [Column("suggested_amount")]
    public decimal SuggestedAmount { get; set; }

    [Column("is_amount_manually_overridden")]
    public bool IsAmountManuallyOverridden { get; set; }

    [Column("payment_method")]
    public PaymentMethod? PaymentMethod { get; set; }

    [Column("is_paid")]
    public bool IsPaid { get; set; }

    [Column("status")]
    public AppointmentStatus Status { get; set; }

    [Column("note")]
    public string Note { get; set; }

    [Column("group_id")]
    public Guid? GroupId { get; set; }

    /// <summary>Koji slot grupe (dan+vrijeme) je generirao ovaj termin — koristi se za idempotentnu
    /// provjeru kod ponovnog generiranja. Izmjena/uklanjanje slota ne dira već generirane termine.</summary>
    [Column("group_slot_id")]
    public Guid? GroupSlotId { get; set; }

    /// <summary>Zajednička oznaka svih termina generiranih iz istog zahtjeva za ponavljajući termin.</summary>
    [Column("recurrence_group_id")]
    public Guid? RecurrenceGroupId { get; set; }

    [Column("created_at")]
    public DateTimeOffset CreatedAt { get; set; }

    [Column("created_by")]
    public Guid? CreatedBy { get; set; }

    [Column("updated_at")]
    public DateTimeOffset? UpdatedAt { get; set; }

    [Column("updated_by")]
    public Guid? UpdatedBy { get; set; }

    public Service Service { get; set; }
    public Employee Employee { get; set; }
    public Company Company { get; set; }
    public Group Group { get; set; }
    public GroupSlot GroupSlot { get; set; }
    public List<AppointmentClient> Clients { get; set; } = new();

    /// <summary>Evidencija prisutnosti — relevantno samo kad je Form=Group.</summary>
    public List<AppointmentAttendance> Attendances { get; set; } = new();
}
