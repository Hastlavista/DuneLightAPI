using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace BlueDragon.DuneLight.Infrastructure.Domain.Models.Employees;

/// <summary>
/// Audit log za osjetljive promjene na zaposleniku (uloga, aktivnost) — bilježi tko, kada
/// i koja je bila prethodna vrijednost. Postojanje zapisa blokira tvrdo brisanje zaposlenika.
/// </summary>
[Table("employee_audit_log")]
public class EmployeeAuditLog
{
    [Key]
    [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
    [Column("id")]
    public Guid? Id { get; set; }

    [Column("employee_id")]
    public Guid EmployeeId { get; set; }

    /// <summary>"Role" ili "Status".</summary>
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
