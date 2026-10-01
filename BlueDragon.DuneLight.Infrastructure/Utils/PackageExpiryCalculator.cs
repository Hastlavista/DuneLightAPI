using System;
using BlueDragon.DuneLight.Core.Enums;
using BlueDragon.DuneLight.Infrastructure.Domain.Models.Catalog;

namespace BlueDragon.DuneLight.Infrastructure.Utils;

/// <summary>
/// Izračun zadnjeg dana valjanosti paketa (ClientPackage.ValidUntilDate). Čista, testabilna metoda. "Neograničeno" se
/// odnosi na broj ulazaka, ne na valjanost: sva tri načina valjanosti uvijek daju konkretan datum. Paket vrijedi do
/// KRAJA dana isteka (uključivo), bez obzira na vrijeme kupnje — isto pravilo kao prije, sada izraženo kalendarskim
/// datumom (Phase D3B3A.1):
/// - DayCount: datum kupnje + ValidityDays dana;
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
                return purchaseDate.AddDays(validityDays.Value);

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

    /// <summary>ValidUntilDate paketa prodanog u trenutku <paramref name="purchasedAt"/> (UTC instant): poslovni datum
    /// kupnje u kalendaru prodaje (<paramref name="saleCalendar"/> = poslovnica prodaje, inače organizacija).
    /// Package.ValidityFixedDate je katalogni podatak organizacije (bez poslovnice) — njegov datum se čita u kalendaru
    /// organizacije (<paramref name="organizationCalendar"/>).</summary>
    public static DateOnly ForSale(
        Package package, DateTimeOffset purchasedAt, OrganizationCalendar saleCalendar, OrganizationCalendar organizationCalendar)
    {
        ArgumentNullException.ThrowIfNull(package);
        DateOnly? fixedDate = package.ValidityFixedDate.HasValue
            ? organizationCalendar.LocalDate(package.ValidityFixedDate.Value)
            : null;
        return CalculateValidUntilDate(package.ValidityType, saleCalendar.LocalDate(purchasedAt), package.ValidityDays, fixedDate);
    }
}
