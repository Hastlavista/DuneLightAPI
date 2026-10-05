using System;
using System.Collections.Generic;
using System.Linq;
using BlueDragon.DuneLight.Core.Enums;
using BlueDragon.DuneLight.Core.Shared;
using BlueDragon.DuneLight.Core.Shared.Exceptions;
using BlueDragon.DuneLight.Infrastructure.Domain.Models.Appointments;
using BlueDragon.DuneLight.Infrastructure.Domain.Models.Checkouts;

namespace BlueDragon.DuneLight.Infrastructure.Utils;

/// <summary>
/// Phase D3B3B — JEDINI izvor istine za novčano namirenje jednog sudjelovanja (BookingSegmentParticipation je granica
/// namirenja: CheckoutItem -&gt; sudjelovanje). Sve je IZVEDENO, ništa se ne sprema:
/// - FinalPrice = Participation.Amount (D3B2);
/// - EntitlementCovered = aktivna PackageConsumption (D3B3A) — trenutno pravilo je SVE-ILI-NIŠTA: paket pokriva cijelu
///   uslugu, nema novčane vrijednosti jedinice paketa (paket NIJE Payment/PaymentMethod/PaymentAllocation);
/// - MonetaryDue = 0 kad je cijena 0 ili pokriveno paketom, inače FinalPrice (cijena se NIKAD ne mijenja zbog paketa);
/// - SettledAmount = zbroj aktivnih (Payment.Status = Completed) PaymentAllocation preko SVIH CheckoutItem stavki
///   sudjelovanja (svih checkouta kroz vrijeme) — svaka alokacija se broji točno jednom;
/// - OutstandingAmount = max(MonetaryDue - SettledAmount, 0); FullySettled = OutstandingAmount &lt;= 0.
/// Životni ciklus je neovisan: Confirmed smije biti plaćen unaprijed, Completed smije imati dug.
///
/// Buduće mješovito namirenje (dio paketom, ostatak novcem) dodaje se kao zasebna komponenta pokrića u MonetaryDue
/// (npr. vrijednost pokrića ulaskom) — granica ostaje sudjelovanje, alokacije ostaju neovisne o PackageConsumption.
/// Zahtijeva učitano: Participation.CheckoutItems.Allocations.Payment i PackageConsumptions.
/// </summary>
public readonly record struct ParticipationSettlement(
    decimal FinalPrice, bool EntitlementCovered, decimal MonetaryDue, decimal SettledAmount, decimal OutstandingAmount)
{
    public bool FullySettled => OutstandingAmount <= 0m;

    public static ParticipationSettlement Of(BookingSegmentParticipation participation) =>
        Of(participation, participation.CheckoutItems);

    /// <summary>Isto, sa stavkama sudjelovanja učitanim izravno (npr. svježe pod lockom unutar transakcije).</summary>
    public static ParticipationSettlement Of(BookingSegmentParticipation participation, IEnumerable<CheckoutItem> checkoutItems)
    {
        ArgumentNullException.ThrowIfNull(participation);
        decimal price = participation.Amount;
        bool covered = participation.PackageConsumptions.Any(c => c.Status == PackageConsumptionStatus.Consumed);
        decimal monetaryDue = price <= 0m || covered ? 0m : price;
        decimal settled = SettledAmountOf(checkoutItems);
        decimal outstanding = monetaryDue - settled;
        return new ParticipationSettlement(price, covered, monetaryDue, settled, outstanding < 0m ? 0m : outstanding);
    }

    /// <summary>Zbroj aktivnih alokacija (voidan Payment se ne broji) preko zadanih stavki, bez dvostrukog brojanja.</summary>
    public static decimal SettledAmountOf(IEnumerable<CheckoutItem> items) => items
        .SelectMany(i => i.Allocations)
        .Where(a => a.Payment != null && a.Payment.Status == PaymentStatus.Completed)
        .GroupBy(a => a.Id)
        .Sum(g => g.First().Amount);

    /// <summary>Puna povijest Paymenta sudjelovanja (uklj. voidane), distinct, najnoviji prvi.</summary>
    public static List<Payment> PaymentsOf(BookingSegmentParticipation participation) => PaymentsOf(new[] { participation });

    /// <summary>Phase M0: ista povijest preko više sudjelovanja (npr. Booking read-model), distinct, najnoviji prvi.</summary>
    public static List<Payment> PaymentsOf(IEnumerable<BookingSegmentParticipation> participations) => participations
        .SelectMany(p => p.CheckoutItems)
        .SelectMany(i => i.Allocations)
        .Select(a => a.Payment)
        .Where(p => p != null)
        .GroupBy(p => p.Id)
        .Select(g => g.First())
        .OrderByDescending(p => p.CreatedAt)
        .ToList();
}

/// <summary>
/// Phase D3B3B — JEDINO pravilo međusobne isključivosti paketa i novca za jedno sudjelovanje (prije raspršeno po
/// ResolveCoverage, completionu i PaymentService). Trenutno pravilo (sve-ili-ništa pokriće paketom):
/// - sudjelovanje s AKTIVNOM potrošnjom paketa ne smije primiti novčano namirenje;
/// - sudjelovanje s AKTIVNIM novčanim namirenjem ne smije potrošiti paket dok se to namirenje ne poništi.
/// </summary>
public static class SettlementExclusivityPolicy
{
    public static void EnsureMoneyAllowed(BookingSegmentParticipation participation)
    {
        if (participation.PackageConsumptions.Any(c => c.Status == PackageConsumptionStatus.Consumed))
            throw new BusinessRuleException(
                ErrorCodes.PaymentNotAllowed, "Booking je pokriven paketom — dodatna novčana naplata nije dopuštena.");
    }

    public static void EnsurePackageAllowed(BookingSegmentParticipation participation, decimal activeMonetarySettlement)
    {
        if (activeMonetarySettlement > 0m)
            throw new BusinessRuleException(
                ErrorCodes.BookingAlreadyHasMonetaryPayment,
                "Booking već ima aktivnu novčanu uplatu — pokriće paketom se ne može primijeniti dok se ne poništi ta uplata.",
                new { bookingId = participation.BookingId, existingMonetaryPaid = activeMonetarySettlement });
    }
}
