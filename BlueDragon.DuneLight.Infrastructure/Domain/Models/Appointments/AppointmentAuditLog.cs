using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace BlueDragon.DuneLight.Infrastructure.Domain.Models.Appointments;

/// <summary>
/// Audit log za osjetljive promjene na terminu (ručna izmjena iznosa, vraćanje ulaska iz paketa,
/// promjena statusa) — bilježi tko, kada i koja je bila prethodna vrijednost. Isti obrazac kao
/// EmployeeAuditLog.
/// </summary>
[Table("appointment_audit_log")]
public class AppointmentAuditLog
{
    [Key]
    [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
    [Column("id")]
    public Guid? Id { get; set; }

    [Column("appointment_id")]
    public Guid AppointmentId { get; set; }

    /// <summary>"Amount", "Status" ili "PackageEntryReturn".</summary>
    [Column("change_type")]
    public string ChangeType { get; set; }

    [Column("old_value")]
    public string OldValue { get; set; }

    [Column("new_value")]
    public string NewValue { get; set; }

    [Column("changed_at")]
    public DateTimeOffset ChangedAt { get; set; }

    [Column("changed_by")]
    public Guid? ChangedBy { get; set; }
}
