using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace BlueDragon.DuneLight.Infrastructure.Domain.Models.Checkouts;

/// <summary>
/// Audit log za Checkout — isti obrazac kao AppointmentAuditLog/EmployeeAuditLog/GroupAuditLog, zaseban jer
/// Checkout je vlastiti korijenski agregat (nije uvijek vezan uz jedan Appointment — Package-only checkout
/// nema Appointment, vidi spec section 42). ChangeType: "CheckoutCreated", "ItemAdded", "ItemRemoved",
/// "PaymentRecorded", "PaymentVoided", "CheckoutCompleted", "CheckoutCancelled", "ClientPackageIssued"
/// (vidi spec section 51).
/// </summary>
[Table("checkout_audit_log")]
public class CheckoutAuditLog
{
    [Key]
    [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
    [Column("id")]
    public Guid? Id { get; set; }

    [Column("checkout_id")]
    public Guid CheckoutId { get; set; }

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
