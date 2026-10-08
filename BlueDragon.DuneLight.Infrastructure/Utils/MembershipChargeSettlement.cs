using System;
using System.Collections.Generic;
using System.Linq;
using BlueDragon.DuneLight.Core.Enums;
using BlueDragon.DuneLight.Infrastructure.Domain.Models.Clients;

namespace BlueDragon.DuneLight.Infrastructure.Utils;

/// <summary>
/// P2 (faza 2C, Q15/Q16) — JEDINA derivacija plaćenosti zaduženja članarine i stanja duga članstva (čisto, bez I/O).
/// Plaćeno = zbroj aktivnih (Payment.Status = Completed) alokacija preko svih stavki checkouta zaduženja (svaka alokacija
/// jednom). Konačno = Paid ili WrittenOff; PartiallyPaid je za pravila duga neplaćeno (Q16.1). Odluke unutar transakcije se
/// donose odavde (iz alokacija), nikad iz projekcije SettledAmount/SettlementStatus. Zahtijeva učitano
/// Charge.CheckoutItems.Allocations.Payment.
/// </summary>
public static class MembershipChargeSettlement
{
    public static decimal Settled(MembershipCharge charge) => ParticipationSettlement.SettledAmountOf(charge.CheckoutItems);

    public static MembershipChargeSettlementStatus SettlementStatusOf(decimal amount, decimal settled) =>
        settled <= 0m ? MembershipChargeSettlementStatus.Unpaid
        : settled >= amount ? MembershipChargeSettlementStatus.Paid
        : MembershipChargeSettlementStatus.PartiallyPaid;

    public static MembershipChargeStatus StatusOf(MembershipCharge charge)
    {
        if (charge.Lifecycle == MembershipChargeLifecycle.Voided)
            return MembershipChargeStatus.Voided;
        if (charge.Lifecycle == MembershipChargeLifecycle.WrittenOff)
            return MembershipChargeStatus.WrittenOff;
        return SettlementStatusOf(charge.Amount, Settled(charge)) switch
        {
            MembershipChargeSettlementStatus.Paid => MembershipChargeStatus.Paid,
            MembershipChargeSettlementStatus.PartiallyPaid => MembershipChargeStatus.PartiallyPaid,
            _ => MembershipChargeStatus.Pending
        };
    }

    /// <summary>Preostali dug: samo otvorena zaduženja; otpisano i poništeno ne duguju ništa.</summary>
    public static decimal Outstanding(MembershipCharge charge) =>
        charge.Lifecycle == MembershipChargeLifecycle.Open ? Math.Max(charge.Amount - Settled(charge), 0m) : 0m;

    /// <summary>Q16.2 — konačno zaduženje: plaćeno u cijelosti ili otpisano.</summary>
    public static bool IsFinal(MembershipCharge charge) =>
        charge.Lifecycle == MembershipChargeLifecycle.WrittenOff
        || (charge.Lifecycle == MembershipChargeLifecycle.Open && Settled(charge) >= charge.Amount);

    /// <summary>Nekonačno otvoreno zaduženje kojem je grace istekao (dospijeće + grace &lt; danas).</summary>
    public static bool IsOverdueAfterGrace(MembershipCharge charge, DateOnly today, int graceDays) =>
        charge.Lifecycle == MembershipChargeLifecycle.Open && !IsFinal(charge) && charge.DueOn.AddDays(graceDays) < today;

    /// <summary>Q15.3 — stanje duga iz najstarijeg nekonačnog dospjelog zaduženja.</summary>
    public static MembershipStanding Standing(IEnumerable<MembershipCharge> charges, DateOnly today, int graceDays)
    {
        MembershipCharge oldestDue = charges
            .Where(c => c.Lifecycle == MembershipChargeLifecycle.Open && !IsFinal(c) && c.DueOn <= today)
            .OrderBy(c => c.DueOn)
            .FirstOrDefault();
        if (oldestDue == null)
            return MembershipStanding.Current;
        return oldestDue.DueOn.AddDays(graceDays) < today ? MembershipStanding.Delinquent : MembershipStanding.InGrace;
    }

    /// <summary>2C — broj neplaćenih perioda za automatski završetak: zaduženja perioda koja nisu konačna nakon grace perioda.</summary>
    public static int UnpaidPeriodsAfterGrace(IEnumerable<MembershipCharge> charges, DateOnly today, int graceDays) =>
        charges.Count(c => c.Kind == MembershipChargeKind.Period && IsOverdueAfterGrace(c, today, graceDays));
}
