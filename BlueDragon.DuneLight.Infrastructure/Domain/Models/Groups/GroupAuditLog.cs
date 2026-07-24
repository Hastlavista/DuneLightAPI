using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace BlueDragon.DuneLight.Infrastructure.Domain.Models.Groups;

/// <summary>
/// Audit log za promjene na grupi i članstvu — tko, kada, prethodna/nova vrijednost. Isti obrazac
/// kao EmployeeAuditLog/AppointmentAuditLog. ChangeType: "Capacity", "DefaultTrainer", "Active",
/// "MemberAdded", "MemberRemoved".
/// </summary>
[Table("group_audit_log")]
public class GroupAuditLog
{
    [Key]
    [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
    [Column("id")]
    public Guid? Id { get; set; }

    [Column("group_id")]
    public Guid GroupId { get; set; }

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
