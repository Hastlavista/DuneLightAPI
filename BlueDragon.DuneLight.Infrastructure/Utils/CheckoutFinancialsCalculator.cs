using System;
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
    /// za paket-pokriveno sudjelovanje — vidi spec section 22).</summary>
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
/// servisima/kontrolerima (vidi spec section 57-58, isti princip kao ParticipationSettlement). Namjerno
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
    /// <summary>
    /// Izračun jedne stavke. Stavka USLUGE (Type=Booking) od Phase D3B3B računa se na granici SUDJELOVANJA
    /// (item.Participation, vidi ParticipationSettlement):
    /// - MonetaryDue = 0 kad je sudjelovanje pokriveno paketom (aktivna PackageConsumption), inače snapshot Amount stavke;
    /// - OutstandingAmount = min(MonetaryDue - aktivne alokacije OVE stavke, preostali dug SUDJELOVANJA preko svih njegovih
    ///   stavki) — isto sudjelovanje se ne može preplatiti kroz više stavki/checkouta, a stavka čije je sudjelovanje već
    ///   namireno drugdje nema dug.
    /// Kad Participation nije učitan, MonetaryDue pada na puni Amount (konzervativno). Paket/proizvod: kao prije.
    /// </summary>
    public static CheckoutItemFinancials CalculateItem(CheckoutItem item)
    {
        decimal retail = item.Amount;

        decimal paid = item.Allocations
            .Where(a => a.Payment != null && a.Payment.Status == PaymentStatus.Completed)
            .Sum(a => a.Amount);

        decimal monetaryDue = retail;
        decimal outstanding = monetaryDue - paid;

        if (item.Type == CheckoutItemType.Booking && item.Participation != null)
        {
            ParticipationSettlement settlement = ParticipationSettlement.Of(item.Participation);
            if (settlement.EntitlementCovered)
                monetaryDue = 0m;
            outstanding = Math.Min(monetaryDue - paid, settlement.OutstandingAmount);
        }

        if (outstanding < 0m)
            outstanding = 0m;

        return new CheckoutItemFinancials(retail, monetaryDue, paid, outstanding);
    }

    /// <summary>Sažetak cijelog Checkouta — zbroj po-stavačnih izračuna (vidi CalculateItem). `checkout.Items`
    /// i svaka stavka `.Participation`/`.Allocations.Payment` moraju biti učitani unaprijed (vidi ICheckoutHandler).</summary>
    public static CheckoutFinancials Calculate(Checkout checkout)
    {
        decimal retailTotal = 0m, monetaryDue = 0m, paidAmount = 0m, outstandingAmount = 0m;

        foreach (CheckoutItem item in checkout.Items)
        {
            CheckoutItemFinancials itemFinancials = CalculateItem(item);
            retailTotal += itemFinancials.RetailAmount;
            monetaryDue += itemFinancials.MonetaryDue;
            paidAmount += itemFinancials.PaidAmount;
            outstandingAmount += itemFinancials.OutstandingAmount;
        }

        return new CheckoutFinancials(retailTotal, monetaryDue, paidAmount, outstandingAmount);
    }

    public static IReadOnlyList<CheckoutItemFinancials> CalculateItems(Checkout checkout)
    {
        return checkout.Items.Select(item => CalculateItem(item)).ToList();
    }
}
