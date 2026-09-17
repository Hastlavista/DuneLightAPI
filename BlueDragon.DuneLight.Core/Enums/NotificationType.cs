namespace BlueDragon.DuneLight.Core.Enums;

/// <summary>Namjerno malen, zatvoren skup — jedan po svakom trenutno oživljenom Outbox event tipu
/// (vidi OutboxEventTypes). Proširuje se tek kad se ožiči novi notification-producing event.</summary>
public enum NotificationType
{
    BookingCancelled,
    WaitlistPromoted,
    BookingNoShow
}
