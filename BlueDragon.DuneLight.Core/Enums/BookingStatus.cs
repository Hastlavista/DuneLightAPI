namespace BlueDragon.DuneLight.Core.Enums;

/// <summary>
/// Klijent-specifično stanje jednog Booking retka (jedan Klijent na jednom Appointmentu). Confirmed je
/// jedino ne-terminalno stanje — Completed/Cancelled/NoShow su terminalni, nema povratka na Confirmed
/// osim eksplicitnog admin/trener poništenja check-ina (vidi BookingService.SetStatus).
/// </summary>
public enum BookingStatus
{
    Confirmed,
    Completed,
    Cancelled,
    NoShow
}
