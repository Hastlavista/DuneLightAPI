using System;
using System.Collections.Generic;
using System.Linq;
using BlueDragon.DuneLight.Core.Enums;
using BlueDragon.DuneLight.Infrastructure.Domain.Models.Clients;

namespace BlueDragon.DuneLight.Infrastructure.Utils;

/// <summary>
/// P2 (2F, Q42) — zaduženja PRVE PRODAJE članarine: zaduženje prvog perioda (dospijeće = datum početka članstva, Q45.1) i
/// početna naknada (nastaje samo pri prodaji). Provizija na prvu prodaju nastaje kad su sva (neponištena) zaduženja prve prodaje
/// konačna (Paid ili WrittenOff); osnovica je stvarno plaćeno na njima (otpisani dio = 0). Čisto, bez pristupa bazi.
/// </summary>
public static class MembershipFirstSale
{
    public static bool IsFirstSaleCharge(MembershipCharge charge, DateOnly membershipStartsOn) =>
        charge.Kind == MembershipChargeKind.StartFee
        || (charge.Kind == MembershipChargeKind.Period && charge.DueOn == membershipStartsOn);

    /// <summary>Neponištena zaduženja prve prodaje (prazno = nema ih, npr. poništena prodaja).</summary>
    public static List<MembershipCharge> ChargesOf(ClientMembership membership) => membership.Charges
        .Where(c => c.Lifecycle != MembershipChargeLifecycle.Voided && IsFirstSaleCharge(c, membership.StartsOn))
        .OrderBy(c => c.Kind)
        .ToList();

    /// <summary>Zaduženja prve prodaje postoje (besplatan plan ima samo početnu naknadu, 2C) i sva su konačna.</summary>
    public static bool IsSettled(IReadOnlyCollection<MembershipCharge> charges) =>
        charges.Count > 0 && charges.All(MembershipChargeSettlement.IsFinal);

    /// <summary>Q42 — stvarno plaćeno (aktivne alokacije) na zaduženjima prve prodaje; otpisani dio se ne broji.</summary>
    public static decimal PaidAmount(IEnumerable<MembershipCharge> charges) => charges.Sum(MembershipChargeSettlement.Settled);
}
