using System;

namespace BlueDragon.DuneLight.Core.Events;

/// <summary>
/// Payload za OutboxEventTypes.BookingCancelledV1 — namjerno samo ID-jevi i minimalan neizmjeniv kontekst, bez
/// PII i bez serijalizirane pune EF entitete (vidi spec section 7). Emitira se za SVAKI Booking koji stvarno
/// prijeđe Confirmed -&gt; Cancelled, bez obzira na izvor prijelaza (BookingService.SetParticipationStatus izravno,
/// AppointmentService appointment-wide otkazivanje, ili GroupService.RemoveMember napuštanje grupe — vidi spec
/// section 32-34), uvijek isti event-tip i isti handler.
/// </summary>
public class BookingCancelledEvent
{
    public Guid OrganizationId { get; set; }
    public Guid BookingId { get; set; }

    /// <summary>Phase M0: sudjelovanje čiji je prijelaz ovo — identitet pojave je (ParticipationId, StatusVersion).</summary>
    public Guid ParticipationId { get; set; }
    public Guid AppointmentId { get; set; }
    public Guid ClientId { get; set; }
    public Guid CompanyId { get; set; }

    /// <summary>StatusVersion SUDJELOVANJA (ParticipationId) NAKON ovog prijelaza — identitet ove KONKRETNE Cancelled
    /// pojave, ne samog Bookinga (koji na grupnim terminima može ponovno postati Confirmed i kasnije opet
    /// Cancelled). Nosi ga i Outbox idempotency-key i Notification.SourceVersion.</summary>
    public int StatusVersion { get; set; }

    public DateTimeOffset OccurredAt { get; set; }
}
