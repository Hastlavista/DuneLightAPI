using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using BlueDragon.DuneLight.Infrastructure.Domain.Models.Catalog;

namespace BlueDragon.DuneLight.Infrastructure.Domain.Models.Appointments;

/// <summary>
/// Jedno konkretno izvođenje jedne usluge unutar termina (Phase D1 — persistence temelj, NIJE autoritativno).
/// Usluga, planirano/stvarno vrijeme i prostorija pripadaju segmentu; zaposlenici i resursi su na segmentu preko
/// tablica dodjele. Poslovnica (i time efektivna vremenska zona) se izvodi preko Appointment.CompanyId — segment je
/// namjerno ne duplicira. Sva vremena su UTC instanti. Pravila vremena su CHECK ograničenja u bazi:
/// PlannedEnd &gt; PlannedStart; ActualEnd ne postoji bez ActualStart; ActualEnd &gt;= ActualStart.
/// </summary>
[Table("appointment_segments")]
public class AppointmentSegment
{
    [Key]
    [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
    [Column("id")]
    public Guid? Id { get; set; }

    [Column("organization_id")]
    public Guid OrganizationId { get; set; }

    [Column("appointment_id")]
    public Guid AppointmentId { get; set; }

    [Column("service_id")]
    public Guid ServiceId { get; set; }

    [Column("planned_start")]
    public DateTimeOffset PlannedStart { get; set; }

    [Column("planned_end")]
    public DateTimeOffset PlannedEnd { get; set; }

    [Column("actual_start")]
    public DateTimeOffset? ActualStart { get; set; }

    [Column("actual_end")]
    public DateTimeOffset? ActualEnd { get; set; }

    [Column("room_id")]
    public Guid? RoomId { get; set; }

    [Column("created_at")]
    public DateTimeOffset CreatedAt { get; set; }

    [Column("updated_at")]
    public DateTimeOffset? UpdatedAt { get; set; }

    public Appointment Appointment { get; set; }
    public Service Service { get; set; }
    public Room Room { get; set; }
    public List<AppointmentSegmentEmployee> Employees { get; set; } = new();
    public List<AppointmentSegmentResource> Resources { get; set; } = new();
}
