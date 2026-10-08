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
/// Phase D3B3B / P1 (D5, D7) — JEDINA, status-aware derivacija novčanog namirenja jednog sudjelovanja
/// (BookingSegmentParticipation je granica namirenja: CheckoutItem -&gt; sudjelovanje). Sve je IZVEDENO, ništa se ne sprema;
/// svi potrošači (Booking/Participation read modeli, dashboard, checkout, plaćanja, prisutnost) koriste ovo:
/// - FinalPrice = Participation.Amount (D3B2) — nikad se ne zamjenjuje naknadom;
/// - EntitlementCovered = aktivna potrošnja IZVRŠENJA USLUGE (D3B3A) — paket pokriva cijelu uslugu;
/// - MonetaryDue po statusu (D5): Confirmed/Completed → 0 kad je cijena 0 ili usluga pokrivena paketom, inače FinalPrice;
///   Cancelled/NoShow s AKTIVNOM posljedicom politike → 0 ako je kazna podmirena aktivnom povezanom potrošnjom paketa, inače
///   CalculatedFeeAmount; Cancelled/NoShow bez aktivne posljedice (na vrijeme, Business, System, Waived, Reversed) → 0.
///   Klasifikacija se ovdje NE ponavlja — čita se samo aktivna posljedica;
/// - SettledAmount = zbroj aktivnih (Payment.Status = Completed) PaymentAllocation preko SVIH CheckoutItem stavki
///   sudjelovanja (svih checkouta kroz vrijeme) — svaka alokacija se broji točno jednom;
/// - OutstandingAmount = MonetaryDue − SettledAmount, NE klampa se (negativno = preplata); FullySettled = Outstanding &lt;= 0;
/// - SurplusAmount (D7) = max(SettledAmount − MonetaryDue, 0) — informativno, NIJE kredit klijenta ni povratni saldo.
/// Životni ciklus je neovisan: Confirmed smije biti plaćen unaprijed, Completed smije imati dug. P1 nikad ne pomiče novac.
/// Zahtijeva učitano: Participation.CheckoutItems.Allocations.Payment, PackageConsumptions i PolicyConsequences.
/// </summary>
public readonly record struct ParticipationSettlement(
    decimal FinalPrice, bool EntitlementCovered, decimal MonetaryDue, decimal SettledAmount, decimal OutstandingAmount)
{
    public bool FullySettled => OutstandingAmount <= 0m;

    /// <summary>D7 — preplata (informativno).</summary>
    public decimal SurplusAmount => OutstandingAmount < 0m ? -OutstandingAmount : 0m;

    public static ParticipationSettlement Of(BookingSegmentParticipation participation) =>
        Of(participation, participation.CheckoutItems);

    /// <summary>Isto, sa stavkama sudjelovanja učitanim izravno (npr. svježe pod lockom unutar transakcije).</summary>
    public static ParticipationSettlement Of(BookingSegmentParticipation participation, IEnumerable<CheckoutItem> checkoutItems)
    {
        ArgumentNullException.ThrowIfNull(participation);
        decimal price = participation.Amount;
        bool covered = PackageConsumptions.IsSettledByPackage(participation) || MembershipCoverages.CoversService(participation);
        decimal monetaryDue = MonetaryDueOf(participation, price, covered);
        decimal settled = SettledAmountOf(checkoutItems);
        return new ParticipationSettlement(price, covered, monetaryDue, settled, monetaryDue - settled);
    }

    /// <remarks>P2 (2D): usluga pokrivena članarinom (aktivan claim) ili pokriće koje čeka evaluaciju (Q27.2) nema novčanog duga;
    /// kasni otkaz / izostanak uz zadržani kredit perioda (Q26) također nema duga — kredit je kazna umjesto naknade. Bez
    /// projekcije pokrića (klijent bez članarine) izračun je nepromijenjen.</remarks>
    private static decimal MonetaryDueOf(BookingSegmentParticipation participation, decimal price, bool serviceCovered)
    {
        if (ParticipationOccupancy.Occupies(participation.Status))
            return price <= 0m || serviceCovered || MembershipCoverages.IsPending(participation) ? 0m : price;

        ParticipationPolicyConsequence consequence = PolicyConsequences.ActiveOf(participation);
        if (consequence == null)
            return 0m;
        if (consequence.MembershipCreditForfeited)
            return 0m;
        return PolicyConsequences.ActiveConsumptionOf(participation, consequence) != null ? 0m : consequence.CalculatedFeeAmount;
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
        EnsureNotMembershipCovered(participation);
    }

    /// <summary>P2 (2D) — treći izvor: aktivno sudjelovanje pokriveno članarinom ne prima novac ni paket (članarina ima
    /// prednost); dok pokriće čeka evaluaciju (Q27.2), naplata nije moguća. Otkazano/izostalo sudjelovanje se ne blokira
    /// (naknada politike uz zadržano mjesto u prozorima je normalan dug).</summary>
    public static void EnsureNotMembershipCovered(BookingSegmentParticipation participation)
    {
        if (!ParticipationOccupancy.Occupies(participation.Status))
            return;
        if (MembershipCoverages.CoversService(participation))
            throw new BusinessRuleException(ErrorCodes.ParticipationCoveredByMembership,
                "Sudjelovanje je pokriveno članarinom — novčana naplata i paket nisu dopušteni.",
                new { participationId = participation.Id, clientMembershipId = participation.MembershipCoverage.ClientMembershipId });
        if (MembershipCoverages.IsPending(participation))
            throw new BusinessRuleException(ErrorCodes.MembershipCoveragePending,
                "Pokriće članarinom čeka evaluaciju (termin je iza horizonta) — naplata nije moguća dok se ne evaluira.",
                new { participationId = participation.Id, expectedPeriodStartsOn = participation.MembershipCoverage.ExpectedPeriodStartsOn });
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
