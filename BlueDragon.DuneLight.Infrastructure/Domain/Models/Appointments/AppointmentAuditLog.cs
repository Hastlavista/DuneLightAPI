using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace BlueDragon.DuneLight.Infrastructure.Domain.Models.Appointments;

/// <summary>
/// Audit log za osjetljive promjene na terminu ILI na pojedinom Bookingu (ručna izmjena iznosa,
/// odbijanje/vraćanje ulaska iz paketa, promjena statusa, promjena trenera) — bilježi tko, kada i koja
/// je bila prethodna vrijednost. Isti obrazac kao EmployeeAuditLog. BookingId je null za promjene na
/// razini cijelog termina (Amount/EmployeeId/Appointment Status), popunjen za promjene na razini jednog
/// Bookinga (BookingStatus/BookingPackageCoverageApplied/BookingPackageCoverageReturned/PaymentCreated/
/// PaymentVoided) — jedna tablica umjesto zasebnog BookingAuditLog, jer je Booking uvijek dijete točno
/// jednog Appointmenta.
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

    /// <summary>Null = promjena na razini termina. Popunjeno = promjena na razini ovog Bookinga.</summary>
    [Column("booking_id")]
    public Guid? BookingId { get; set; }

    /// <summary>Popunjeno samo za promjene na razini jednog WaitlistEntry retka ("WaitlistJoined"/
    /// "WaitlistCancelled"/"WaitlistPromoted"/"WaitlistExpired") — isti obrazac kao BookingId, jedna
    /// zajednička audit tablica umjesto zasebnog WaitlistAuditLog (vidi klasnu napomenu).</summary>
    [Column("waitlist_entry_id")]
    public Guid? WaitlistEntryId { get; set; }

    /// <summary>"Amount", "Status", "EmployeeId" (razina termina), "BookingStatus", "BookingPackageCoverageApplied",
    /// "BookingPackageCoverageReturned", "PaymentCreated", "PaymentVoided" (razina jednog Bookinga, vidi
    /// BookingId) ili "WaitlistJoined"/"WaitlistCancelled"/"WaitlistPromoted"/"WaitlistExpired" (razina liste
    /// čekanja, vidi WaitlistEntryId).</summary>
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

    /// <summary>Popunjeno SAMO za ChangeType="BookingStatus" — Booking.StatusVersion NAKON ovog prijelaza (vidi
    /// BookingStatusVersioning). Veže ovaj audit redak na ISTU pojavu koju referenciraju Outbox idempotency key
    /// (booking-cancelled/booking-noshow:{id}:{version}) i Notification.SourceVersion, umjesto da se identitet
    /// pojave oslanja samo na ChangedAt redoslijed (vidi audit-cleanup spec section 51/57).</summary>
    [Column("status_version")]
    public int? StatusVersion { get; set; }
}
