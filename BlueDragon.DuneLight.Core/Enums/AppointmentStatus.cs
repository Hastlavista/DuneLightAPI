namespace BlueDragon.DuneLight.Core.Enums;

/// <summary>
/// Phase M1A — AGREGATNI operativni životni ciklus termina, IZVEDEN iz statusa njegovih sudjelovanja
/// (BookingSegmentParticipation je izvršna istina; vidi Infrastructure.Utils.AppointmentLifecycle):
/// - <see cref="Scheduled"/>: barem jedno sudjelovanje je još Confirmed (posao nije razriješen);
/// - <see cref="Cancelled"/>: SVA sudjelovanja su Cancelled (sav posao otkazan, ništa izvršeno/razriješeno drukčije);
/// - <see cref="Closed"/>: nijedno Confirmed, a ishod nije "sve otkazano" (Completed/NoShow, bilo koja kombinacija).
/// Closed znači operativno razriješeno — NE financijski namireno. Termin nema "Completed" (izvršenje je po sudjelovanju)
/// ni NoShow; status se nikad ne postavlja ručno mimo izvođenja.
/// </summary>
public enum AppointmentStatus
{
    Scheduled,
    Cancelled,
    Closed
}
