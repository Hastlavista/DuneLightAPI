using System;

namespace BlueDragon.DuneLight.Core.Events;

/// <summary>
/// Payload za OutboxEventTypes.BookingNoShowV1 — vidi BookingCancelledEvent za obrazloženje oblika. Emitira se
/// samo za stvaran persistiran prijelaz Confirmed -&gt; NoShow (BookingService.SetStatus/AppointmentService
/// appointment-wide), nikad izveden iz proteka vremena (vidi spec section 36).
/// </summary>
public class BookingNoShowEvent
{
    public Guid OrganizationId { get; set; }
    public Guid BookingId { get; set; }
    public Guid AppointmentId { get; set; }
    public Guid ClientId { get; set; }
    public Guid CompanyId { get; set; }

    /// <summary>Booking.StatusVersion NAKON ovog prijelaza (vidi Booking.cs) — identitet ove KONKRETNE NoShow
    /// pojave, ne samog Bookinga (koji na grupnim terminima može ponovno postati Confirmed i kasnije opet
    /// NoShow). Nosi ga i Outbox idempotency-key i Notification.SourceVersion.</summary>
    public int StatusVersion { get; set; }

    public DateTimeOffset OccurredAt { get; set; }
}
