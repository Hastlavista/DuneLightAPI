namespace BlueDragon.DuneLight.Core.Enums;

/// <summary>
/// Statusi na razini termina (okvir/resurs), NE po klijentu — vidi BookingStatus za klijent-specifično
/// stanje (Confirmed/Completed/Cancelled/NoShow po Booking retku). NoShow namjerno ne postoji ovdje:
/// termin kao takav ne može "izostati", samo pojedini Booking na njemu (vidi Booking.cs).
/// </summary>
public enum AppointmentStatus
{
    Scheduled,
    Completed,
    Cancelled
}
