using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using BlueDragon.DuneLight.Infrastructure.Domain.Models.Catalog;
using BlueDragon.DuneLight.Infrastructure.Domain.Models.Employees;

namespace BlueDragon.DuneLight.Infrastructure.Domain.Models.Appointments;

/// <summary>
/// Blok na rasporedu koji zauzima slot kod konkretnog trenera bez usluge/klijenta/naplate — sprječava
/// zakazivanje termina preko tog vremena. Namjerno odvojeno od Appointment (vidi EnsureNoOverlap u
/// AppointmentService i ScheduleBreakService za tvrdu blokadu u oba smjera).
/// </summary>
[Table("schedule_breaks")]
public class ScheduleBreak
{
    [Key]
    [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
    [Column("id")]
    public Guid? Id { get; set; }

    [Column("organization_id")]
    public Guid OrganizationId { get; set; }

    [Column("employee_id")]
    public Guid EmployeeId { get; set; }

    [Column("company_id")]
    public Guid CompanyId { get; set; }

    /// <summary>Pohranjeno u UTC, proizvoljno vrijeme (ne samo puni sat).</summary>
    [Column("starts_at")]
    public DateTimeOffset StartsAt { get; set; }

    [Column("duration_minutes")]
    public int DurationMinutes { get; set; }

    [Column("note")]
    public string Note { get; set; }

    /// <summary>Zajednička oznaka svih pauza generiranih iz istog zahtjeva za ponavljajuću pauzu.</summary>
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

    public Employee Employee { get; set; }
    public Company Company { get; set; }
}
