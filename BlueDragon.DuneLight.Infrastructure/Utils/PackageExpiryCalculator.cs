using System;
using BlueDragon.DuneLight.Core.Enums;

namespace BlueDragon.DuneLight.Infrastructure.Utils;

/// <summary>
/// Izračun datuma isteka paketa. Čista, testabilna metoda — koristit će je i buduća
/// prodaja paketa klijentu. "Neograničeno" se odnosi na broj ulazaka, ne na valjanost:
/// sva tri načina valjanosti uvijek daju konkretan datum isteka. Paket vrijedi do kraja
/// dana isteka (uključivo), bez obzira na vrijeme kupnje.
/// </summary>
public static class PackageExpiryCalculator
{
    public static DateTimeOffset CalculateExpiryDate(
        PackageValidityType validityType,
        DateTimeOffset purchaseDate,
        int? validityDays,
        DateTimeOffset? validityFixedDate)
    {
        DateTimeOffset expiryDay;

        switch (validityType)
        {
            case PackageValidityType.DayCount:
                if (validityDays is null or <= 0)
                    throw new ArgumentException("ValidityDays is required for DayCount validity type.", nameof(validityDays));
                expiryDay = purchaseDate.AddDays(validityDays.Value);
                break;

            case PackageValidityType.EndOfMonth:
                int daysInMonth = DateTime.DaysInMonth(purchaseDate.Year, purchaseDate.Month);
                expiryDay = new DateTimeOffset(purchaseDate.Year, purchaseDate.Month, daysInMonth, 0, 0, 0, purchaseDate.Offset);
                break;

            case PackageValidityType.FixedDate:
                if (validityFixedDate is null)
                    throw new ArgumentException("ValidityFixedDate is required for FixedDate validity type.", nameof(validityFixedDate));
                expiryDay = validityFixedDate.Value;
                break;

            default:
                throw new ArgumentOutOfRangeException(nameof(validityType));
        }

        DateTimeOffset startOfExpiryDay = new DateTimeOffset(
            expiryDay.Year, expiryDay.Month, expiryDay.Day, 0, 0, 0, expiryDay.Offset);
        return startOfExpiryDay.AddDays(1).AddTicks(-1);
    }
}
