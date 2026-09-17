using System;

namespace BlueDragon.DuneLight.Core.Events;

/// <summary>
/// Payload za OutboxEventTypes.BookingCancelledV1 — namjerno samo ID-jevi i minimalan neizmjeniv kontekst, bez
/// PII i bez serijalizirane pune EF entitete (vidi spec section 7). Emitira se za SVAKI Booking koji stvarno
/// prijeđe Confirmed -&gt; Cancelled, bez obzira na izvor prijelaza (BookingService.SetStatus izravno,
/// AppointmentService appointment-wide otkazivanje, ili GroupService.RemoveMember napuštanje grupe — vidi spec
/// section 32-34), uvijek isti event-tip i isti handler.
/// </summary>
public class BookingCancelledEvent
{
    public Guid OrganizationId { get; set; }
    public Guid BookingId { get; set; }
    public Guid AppointmentId { get; set; }
    public Guid ClientId { get; set; }
    public Guid CompanyId { get; set; }

    /// <summary>Booking.StatusVersion NAKON ovog prijelaza (vidi Booking.cs) — identitet ove KONKRETNE Cancelled
    /// pojave, ne samog Bookinga (koji na grupnim terminima može ponovno postati Confirmed i kasnije opet
    /// Cancelled). Nosi ga i Outbox idempotency-key i Notification.SourceVersion.</summary>
    public int StatusVersion { get; set; }

    public DateTimeOffset OccurredAt { get; set; }
}
