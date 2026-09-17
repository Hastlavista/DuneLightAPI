using BlueDragon.DuneLight.Core.Enums;
using BlueDragon.DuneLight.Infrastructure.Domain.Models.Appointments;

namespace BlueDragon.DuneLight.Infrastructure.Utils;

/// <summary>Jedina dozvoljena putanja koja mijenja Booking.Status — uz status uvijek inkrementira StatusVersion,
/// TOČNO kad se status stvarno promijeni (nikad za idempotentan poziv sa istim statusom), tako da StatusVersion
/// ostaje stabilan identitet JEDNE konkretne pojave prijelaza (vidi Booking.cs domensku napomenu).</summary>
public static class BookingStatusVersioning
{
    public static bool TrySetStatus(Booking booking, BookingStatus newStatus)
    {
        if (booking.Status == newStatus)
            return false;

        booking.Status = newStatus;
        booking.StatusVersion++;
        return true;
    }
}
