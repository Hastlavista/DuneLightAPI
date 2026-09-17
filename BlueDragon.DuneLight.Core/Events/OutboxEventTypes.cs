namespace BlueDragon.DuneLight.Core.Events;

/// <summary>
/// Stabilni, eksplicitni Outbox event-tip stringovi (perzistiraju u outbox_messages.type) — NIKAD CLR
/// assembly-qualified imena (vidi spec section 6). Versioned sufiksom (".v1") umjesto generičkog
/// event-versioning frameworka — nova nekompatibilna verzija istog logičkog događaja dobiva novi ".v2" tip i
/// novi handler, stari ostaje razumljiv povijesnim redcima (vidi spec section 8). Samo dodavati, ne mijenjati
/// postojeće vrijednosti (već persistirani redci ih referenciraju).
/// </summary>
public static class OutboxEventTypes
{
    public const string BookingCancelledV1 = "booking.cancelled.v1";
    public const string WaitlistPromotedV1 = "waitlist.promoted.v1";
    public const string BookingNoShowV1 = "booking.no-show.v1";
}
