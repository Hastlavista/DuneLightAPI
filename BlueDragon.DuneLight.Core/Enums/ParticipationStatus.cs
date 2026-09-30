namespace BlueDragon.DuneLight.Core.Enums;

/// <summary>
/// Životni ciklus jednog sudjelovanja (BookingSegmentParticipation) — isti skup stanja kao BookingStatus, zaseban tip jer
/// je sudjelovanje ciljna izvršna/komercijalna jedinica, a Booking status ostaje nepromijenjen. Namjerno NEMA:
/// Arrived (dolazak je metapodatak ArrivedAt/ArrivedBy), LateCancelled (kasno otkazivanje je klasifikacija
/// IsLateCancellation uz Status=Cancelled), Closed ni Mixed (Mixed je budući izvedeni sažetak Bookinga).
/// </summary>
public enum ParticipationStatus
{
    Confirmed,
    Completed,
    Cancelled,
    NoShow
}
