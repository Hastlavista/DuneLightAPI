using System;

namespace BlueDragon.DuneLight.Core.Events;

/// <summary>
/// Payload za OutboxEventTypes.BookingNoShowV1 — vidi BookingCancelledEvent za obrazloženje oblika. Emitira se
/// samo za stvaran persistiran prijelaz Confirmed -&gt; NoShow (BookingService.SetParticipationStatus/AppointmentService
/// appointment-wide), nikad izveden iz proteka vremena (vidi spec section 36).
/// </summary>
public class BookingNoShowEvent
{
    public Guid OrganizationId { get; set; }
    public Guid BookingId { get; set; }

    /// <summary>Phase M0: sudjelovanje čiji je prijelaz ovo — identitet pojave je (ParticipationId, StatusVersion).</summary>
    public Guid ParticipationId { get; set; }
    public Guid AppointmentId { get; set; }
    public Guid ClientId { get; set; }
    public Guid CompanyId { get; set; }

    /// <summary>StatusVersion SUDJELOVANJA (ParticipationId) NAKON ovog prijelaza — identitet ove KONKRETNE NoShow
    /// pojave, ne samog Bookinga (koji na grupnim terminima može ponovno postati Confirmed i kasnije opet
    /// NoShow). Nosi ga i Outbox idempotency-key i Notification.SourceVersion.</summary>
    public int StatusVersion { get; set; }

    public DateTimeOffset OccurredAt { get; set; }
}
