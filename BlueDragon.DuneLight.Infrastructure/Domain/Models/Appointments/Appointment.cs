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
/// Termin — okvir/resurs (usluga, vrijeme, trener, prostorija). Naplata (Amount/SuggestedAmount, i
/// monetarni Payment ledger) živi isključivo na/preko Booking, ne ovdje — omogućuje mješovito plaćanje po
/// klijentu na istom terminu (npr. duo: jedan klijent iz paketa, drugi karticom). Vidi Booking.cs za punu
/// domensku napomenu. Form=Group: veza na Group/GroupSlot koji ga je generirao.
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

    /// <summary>Opcionalno. Za grupne termine snapshot Group.DefaultRoomId u trenutku generiranja
    /// (može se naknadno promijeniti po pojedinom terminu bez diranja grupe).</summary>
    [Column("room_id")]
    public Guid? RoomId { get; set; }

    [Column("status")]
    public AppointmentStatus Status { get; set; }

    [Column("note")]
    public string Note { get; set; }

    /// <summary>Popunjeno samo kad je Status Cancelled ili NoShow (trener/recepcija upisuje razlog kod ChangeToTerminalStatus).</summary>
    [Column("cancellation_reason")]
    public string CancellationReason { get; set; }

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
    public Room Room { get; set; }
    public Group Group { get; set; }
    public GroupSlot GroupSlot { get; set; }
    public List<Booking> Bookings { get; set; } = new();
}
