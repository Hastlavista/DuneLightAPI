using System.Collections.Generic;
using System.Linq;
using BlueDragon.DuneLight.Core.Enums;
using BlueDragon.DuneLight.Infrastructure.Domain.Models.Appointments;
using BlueDragon.DuneLight.Infrastructure.Domain.Models.Checkouts;

namespace BlueDragon.DuneLight.Infrastructure.Utils;

/// <summary>Izvedeni financijski sažetak jedne CheckoutItem stavke — vidi spec section 20-23.</summary>
public readonly struct CheckoutItemFinancials
{
    public CheckoutItemFinancials(decimal retailAmount, decimal monetaryDue, decimal paidAmount, decimal outstandingAmount)
    {
        RetailAmount = retailAmount;
        MonetaryDue = monetaryDue;
        PaidAmount = paidAmount;
        OutstandingAmount = outstandingAmount;
    }

    /// <summary>Puna komercijalna vrijednost stavke bez obzira na način podmirenja (nikad se ne mijenja na 0
    /// za paket-pokriven Booking — vidi spec section 22).</summary>
    public decimal RetailAmount { get; }

    /// <summary>Koliko se OVE stavke stvarno duguje u novcu — 0 za paket-pokriven Booking ili Amount=0 Booking,
    /// inače jednako RetailAmount (Package-purchase stavka je UVIJEK monetarno dužna, nikad paket-pokrivena
    /// samim sobom — vidi spec section 40).</summary>
    public decimal MonetaryDue { get; }

    public decimal PaidAmount { get; }
    public decimal OutstandingAmount { get; }
}

/// <summary>Izvedeni financijski sažetak jednog Checkouta — vidi spec section 13/57-58. Čist izračun nad već
/// učitanim grafom (Checkout.Items.Allocations.Payment) — ne izvodi upite.</summary>
public readonly struct CheckoutFinancials
{
    public CheckoutFinancials(decimal retailTotal, decimal monetaryDue, decimal paidAmount, decimal outstandingAmount)
    {
        RetailTotal = retailTotal;
        MonetaryDue = monetaryDue;
        PaidAmount = paidAmount;
        OutstandingAmount = outstandingAmount;
    }

    public decimal RetailTotal { get; }
    public decimal MonetaryDue { get; }
    public decimal PaidAmount { get; }
    public decimal OutstandingAmount { get; }
    public bool IsFullyPaid => OutstandingAmount <= 0m;
}

/// <summary>
/// Jedini izvor istine za Checkout/CheckoutItem financijske izračune — ne duplicirati ovu aritmetiku po
/// servisima/kontrolerima (vidi spec section 57-58, isti princip kao BookingFinancialsCalculator). Namjerno
/// decimal svugdje, bez perzistiranog "IsPaid" (uvijek izvedeno, vidi spec section 58).
///
/// Voided Checkout (vidi CheckoutStatus.Voided): ovaj izračun ne grana posebno po Checkout.Status — namjerno.
/// Kad se Checkout Completed -&gt; Voided (uvijek zajedno s njegovim jedinim Paymentom Completed -&gt; Voided,
/// vidi PaymentService.TryVoidSoleAutoCheckout), PaidAmount/OutstandingAmount ovdje ispravno padaju natrag na
/// "nenaplaćeno" SAMO zato što Payment.Status više nije Completed (filtrirano u CalculateItem) — isti mehanizam
/// koji već isključuje bilo koji drugi voidan Payment. Buduća agregatna izvještaja o prihodu NE smiju brojati
/// Voided (ni Cancelled) checkoute kao namirene; budući da PaidAmount ovdje već ispravno pada na 0 za njih,
/// zbrajanje po PaidAmount (a ne po pretpostavci "Completed = naplaćeno") je ispravan i dovoljan pristup.
/// </summary>
public static class CheckoutFinancialsCalculator
{
    /// <summary>Booking stavka je paket-pokrivena (MonetaryDue=0) kad je njezin Booking.PackageCoverageApplied
    /// I ne PackageCoverageReturned — isto pravilo kao BookingFinancialsCalculator.IsPackageSettled, ponovljeno
    /// ovdje jer CheckoutItem ne nosi Booking uvijek učitan istim putem; pozivatelj mora proslijediti Booking
    /// entitet (vidi CalculateItem). Javno (ne privatno) jer je ovo i centralna provjera koju CheckoutService
    /// koristi da eksplicitno odbije eksplicitnu PaymentAllocation prema paket-namirenoj Booking stavci (vidi
    /// CheckoutService.BuildExplicitAllocations) — ista provjera, dva mjesta upotrebe, jedan izvor istine.</summary>
    public static bool IsBookingPackageSettled(Booking booking)
    {
        return booking != null && booking.ClientPackageId.HasValue && booking.PackageCoverageApplied && !booking.PackageCoverageReturned;
    }

    /// <summary>
    /// Izračun jedne stavke. `booking` se prosljeđuje eksplicitno (može biti null za Type=Package, ili kad
    /// CheckoutItem.Booking nije učitan) — kad je null za Type=Booking, MonetaryDue pada natrag na puni Amount
    /// (konzervativno: ne pretpostavlja paket-pokriće bez podataka).
    /// </summary>
    public static CheckoutItemFinancials CalculateItem(CheckoutItem item, Booking booking = null)
    {
        decimal retail = item.Amount;

        decimal monetaryDue = item.Type == CheckoutItemType.Booking && IsBookingPackageSettled(booking)
            ? 0m
            : retail;

        decimal paid = item.Allocations
            .Where(a => a.Payment != null && a.Payment.Status == PaymentStatus.Completed)
            .Sum(a => a.Amount);

        decimal outstanding = monetaryDue - paid;
        if (outstanding < 0m)
            outstanding = 0m;

        return new CheckoutItemFinancials(retail, monetaryDue, paid, outstanding);
    }

    /// <summary>Sažetak cijelog Checkouta — zbroj po-stavačnih izračuna (vidi CalculateItem). `checkout.Items`
    /// i svaka stavka `.Booking`/`.Allocations.Payment` moraju biti učitani unaprijed (vidi ICheckoutHandler).</summary>
    public static CheckoutFinancials Calculate(Checkout checkout)
    {
        decimal retailTotal = 0m, monetaryDue = 0m, paidAmount = 0m, outstandingAmount = 0m;

        foreach (CheckoutItem item in checkout.Items)
        {
            CheckoutItemFinancials itemFinancials = CalculateItem(item, item.Booking);
            retailTotal += itemFinancials.RetailAmount;
            monetaryDue += itemFinancials.MonetaryDue;
            paidAmount += itemFinancials.PaidAmount;
            outstandingAmount += itemFinancials.OutstandingAmount;
        }

        return new CheckoutFinancials(retailTotal, monetaryDue, paidAmount, outstandingAmount);
    }

    public static IReadOnlyList<CheckoutItemFinancials> CalculateItems(Checkout checkout)
    {
        return checkout.Items.Select(item => CalculateItem(item, item.Booking)).ToList();
    }
}
