namespace BlueDragon.DuneLight.Core.Enums;

public enum NotificationSourceType
{
    /// <summary>Više se ne proizvodi (Phase M0) — booking.cancelled/no-show pojave su po SUDJELOVANJU.</summary>
    Booking,
    WaitlistEntry,

    /// <summary>Phase M0: izvor je BookingSegmentParticipation — SourceVersion je StatusVersion tog sudjelovanja (Booking
    /// nema vlastitu verziju; Booking-wide otkazivanje daje po jednu pojavu za svako otkazano sudjelovanje).</summary>
    Participation
}
