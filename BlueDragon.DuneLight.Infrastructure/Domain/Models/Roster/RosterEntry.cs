using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using BlueDragon.DuneLight.Infrastructure.Domain.Models.Employees;

namespace BlueDragon.DuneLight.Infrastructure.Domain.Models.Roster;

/// <summary>
/// Jedna evidencija za oba oblika (rad/odsutnost) — oblik je određen RosterType.IsAbsence, ne posebnim poljem.
/// Rad: DateFrom = jedini dan, DateTo uvijek null, StartTime/EndTime obavezni. Dvokratni rad su dva zasebna retka.
/// Odsutnost: DateFrom/DateTo = raspon (DateTo null = otvorena odsutnost, još traje), StartTime/EndTime uvijek null.
/// </summary>
[Table("roster_entries")]
public class RosterEntry
{
    [Key]
    [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
    [Column("id")]
    public Guid? Id { get; set; }

    [Column("organization_id")]
    public Guid OrganizationId { get; set; }

    [Column("employee_id")]
    public Guid EmployeeId { get; set; }

    [Column("roster_type_id")]
    public Guid RosterTypeId { get; set; }

    [Column("date_from")]
    public DateTimeOffset DateFrom { get; set; }

    [Column("date_to")]
    public DateTimeOffset? DateTo { get; set; }

    [Column("start_time")]
    public TimeSpan? StartTime { get; set; }

    [Column("end_time")]
    public TimeSpan? EndTime { get; set; }

    [Column("duration_hours")]
    public decimal? DurationHours { get; set; }

    [Column("note")]
    public string Note { get; set; }

    [Column("created_at")]
    public DateTimeOffset CreatedAt { get; set; }

    [Column("created_by")]
    public Guid? CreatedBy { get; set; }

    [Column("updated_at")]
    public DateTimeOffset? UpdatedAt { get; set; }

    [Column("updated_by")]
    public Guid? UpdatedBy { get; set; }

    public Employee Employee { get; set; }
    public RosterType RosterType { get; set; }
}
