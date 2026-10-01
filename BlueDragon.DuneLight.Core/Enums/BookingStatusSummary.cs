namespace BlueDragon.DuneLight.Core.Enums;

/// <summary>
/// Phase M0 — IZVEDENI (nikad spremljeni) sažetak statusa Bookinga iz statusa njegovih sudjelovanja: kad su sva
/// sudjelovanja u istom statusu, sažetak je taj status; inače <see cref="Mixed"/>. Booking NEMA vlastiti životni ciklus —
/// ovo je samo read-model i NIKAD nije ciljni status prijelaza (naredbe primaju <see cref="BookingStatus"/>, koji nema
/// Mixed). Imena i redoslijed prve četiri vrijednosti jednaki su <see cref="BookingStatus"/> (isti JSON za jedno
/// sudjelovanje).
/// </summary>
public enum BookingStatusSummary
{
    Confirmed,
    Completed,
    Cancelled,
    NoShow,
    Mixed
}
