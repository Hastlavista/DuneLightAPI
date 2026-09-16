using System.Collections.Generic;
using System.Linq;
using BlueDragon.DuneLight.Core.Enums;
using BlueDragon.DuneLight.Infrastructure.Domain.Models.Appointments;
using BlueDragon.DuneLight.Infrastructure.Domain.Models.Checkouts;

namespace BlueDragon.DuneLight.Infrastructure.Utils;

/// <summary>
/// Jedini izvor istine za "koliko je plaćeno / koliko se duguje" na jednom Bookingu — ne duplicirati ovu
/// aritmetiku po servisima/kontrolerima (vidi spec section 9/41/56). Namjerno decimal (ne double) svugdje.
///
/// Otkad Payment pripada Checkoutu (ne izravno Bookingu — vidi Payment.cs), "koliko je plaćeno" se izvodi iz
/// PaymentAllocation redaka preko SVIH CheckoutItem stavki koje referenciraju ovaj Booking (obično jedna,
/// vidi Booking.CheckoutItems), brojeći samo alokacije čiji roditeljski Payment.Status je Completed
/// (voidan Payment se ne broji, isto ponašanje kao prije uvođenja Checkouta).
/// </summary>
public static class BookingFinancialsCalculator
{
    /// <summary>Zbroj PaymentAllocation.Amount preko svih CheckoutItems ovog Bookinga, samo za alokacije čiji
    /// roditeljski Payment.Status je Completed (voidan Payment se isključuje).</summary>
    public static decimal CalculatePaidAmount(IEnumerable<CheckoutItem> checkoutItems)
    {
        return checkoutItems
            .SelectMany(i => i.Allocations)
            .Where(a => a.Payment != null && a.Payment.Status == PaymentStatus.Completed)
            .Sum(a => a.Amount);
    }

    /// <summary>
    /// Jedini izvor istine za "je li booking namiren paketom" — namjerno NEOVISNO o CoverageType (nema grananja
    /// po SessionPackage/MonthlyPackage/SharedPool ovdje, vidi klasnu napomenu na Booking.PackageCoverageApplied).
    /// Sam ClientPackageId NIJE dovoljan (paket može biti tek ODABRAN na budućem/Confirmed bookingu prije stvarnog
    /// check-ina/completiona — vidi Booking.PackageCoverageApplied) — potrebno je da je entitlement STVARNO
    /// primijenjen (PackageCoverageApplied) i da nije naknadno vraćen (PackageCoverageReturned).
    /// </summary>
    private static bool IsPackageSettled(Booking booking)
    {
        return booking.ClientPackageId.HasValue && booking.PackageCoverageApplied && !booking.PackageCoverageReturned;
    }

    /// <summary>
    /// Amount=0 (gratis/promo) i paket-namiren booking (vidi IsPackageSettled) su namireni bez obzira na
    /// Paymente. Inače Amount minus zbroj aktivnih alokacija, nikad negativno.
    /// </summary>
    public static decimal CalculateOutstanding(Booking booking, IEnumerable<CheckoutItem> checkoutItems)
    {
        if (booking.Amount <= 0m || IsPackageSettled(booking))
            return 0m;

        decimal outstanding = booking.Amount - CalculatePaidAmount(checkoutItems);
        return outstanding < 0m ? 0m : outstanding;
    }

    public static bool IsSettled(Booking booking, IEnumerable<CheckoutItem> checkoutItems)
    {
        List<CheckoutItem> materialized = checkoutItems as List<CheckoutItem> ?? checkoutItems.ToList();
        return CalculateOutstanding(booking, materialized) <= 0m;
    }

    /// <summary>Prikladnost kad je booking.CheckoutItems već učitan (Include CheckoutItems.Allocations.Payment) —
    /// vidi AppointmentHandler include lance.</summary>
    public static decimal CalculatePaidAmount(Booking booking) => CalculatePaidAmount(booking.CheckoutItems);

    /// <summary>Prikladnost kad je booking.CheckoutItems već učitan (Include).</summary>
    public static decimal CalculateOutstanding(Booking booking) => CalculateOutstanding(booking, booking.CheckoutItems);

    /// <summary>Puna povijest Paymenta ovog Bookinga (uklj. voidane, distinct preko svih njegovih povijesnih
    /// CheckoutItem stavki — vidi Booking.CheckoutItems domensku napomenu), najnoviji prvi — za BookingDto.Payments
    /// (vidi BookingService/AppointmentService ToDto). Zahtijeva booking.CheckoutItems.Allocations.Payment učitan.</summary>
    public static List<Payment> GetPayments(Booking booking)
    {
        return booking.CheckoutItems
            .SelectMany(i => i.Allocations)
            .Select(a => a.Payment)
            .Where(p => p != null)
            .GroupBy(p => p.Id)
            .Select(g => g.First())
            .OrderByDescending(p => p.CreatedAt)
            .ToList();
    }
}
