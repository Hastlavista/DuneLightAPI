using System;
using BlueDragon.DuneLight.Core.Enums;
using BlueDragon.DuneLight.Infrastructure.Domain.Models.Catalog;

namespace BlueDragon.DuneLight.Infrastructure.Utils;

/// <summary>
/// Izračun zadnjeg dana valjanosti paketa (ClientPackage.ValidUntilDate). Čista, testabilna metoda. "Neograničeno" se
/// odnosi na broj ulazaka, ne na valjanost: sva tri načina valjanosti uvijek daju konkretan datum. Paket vrijedi do
/// KRAJA dana isteka (uključivo), bez obzira na vrijeme kupnje — isto pravilo kao prije, sada izraženo kalendarskim
/// datumom (Phase D3B3A.1):
/// - DayCount: ValidityDays kalendarskih dana uključujući dan kupnje (T1-11) → datum kupnje + ValidityDays − 1;
/// - EndOfMonth: zadnji dan mjeseca kupnje;
/// - FixedDate: zadani datum.
///
/// Datum kupnje je POSLOVNI datum (lokalni datum trenutka kupnje u kalendaru poslovnice prodaje, organizacije kad je
/// nema) — vidi <see cref="ForSale"/>; nikad zona hosta, UTC datum ni proizvoljan offset ulazne DateTimeOffset vrijednosti.
/// </summary>
public static class PackageExpiryCalculator
{
    public static DateOnly CalculateValidUntilDate(
        PackageValidityType validityType,
        DateOnly purchaseDate,
        int? validityDays,
        DateOnly? validityFixedDate)
    {
        switch (validityType)
        {
            case PackageValidityType.DayCount:
                if (validityDays is null or <= 0)
                    throw new ArgumentException("ValidityDays is required for DayCount validity type.", nameof(validityDays));
                // CHANGED in T1 (T1-11): "N dana" = točno N kalendarskih dana računajući dan kupnje kao 1. dan (prije: + N, tj.
                // N + 1 dan uz uključiv kraj). Kupljen 1.10., "30 dana" → vrijedi do 30.10. uključivo.
                return purchaseDate.AddDays(validityDays.Value - 1);

            case PackageValidityType.EndOfMonth:
                return new DateOnly(purchaseDate.Year, purchaseDate.Month, DateTime.DaysInMonth(purchaseDate.Year, purchaseDate.Month));

            case PackageValidityType.FixedDate:
                if (validityFixedDate is null)
                    throw new ArgumentException("ValidityFixedDate is required for FixedDate validity type.", nameof(validityFixedDate));
                return validityFixedDate.Value;

            default:
                throw new ArgumentOutOfRangeException(nameof(validityType));
        }
    }

    /// <summary>ValidUntilDate paketa prodanog na POSLOVNI dan <paramref name="purchaseDate"/> (T1-7: ClientPackage.PurchaseDate,
    /// lokalni dan kupnje u zoni poslovnice prodaje, organizacije kad je nema — pozivatelj ga određuje kroz kalendar).
    /// Package.ValidityFixedDate je već kalendarski datum (Phase D3B3A.2) i koristi se izravno, bez ikakve pretvorbe.</summary>
    public static DateOnly ForSale(Package package, DateOnly purchaseDate)
    {
        ArgumentNullException.ThrowIfNull(package);
        return CalculateValidUntilDate(package.ValidityType, purchaseDate, package.ValidityDays, package.ValidityFixedDate);
    }
}
