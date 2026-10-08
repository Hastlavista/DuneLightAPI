using System;
using System.Collections.Generic;
using System.Linq;
using BlueDragon.DuneLight.Core.Enums;
using BlueDragon.DuneLight.Infrastructure.Domain.Models.Catalog;

namespace BlueDragon.DuneLight.Infrastructure.Utils;

/// <summary>
/// P2 (Q48) — klasifikacija izmjene uvjeta plana za postojeća članstva: Favorable samo ako NIJEDAN uvjet koji klijent osjeti
/// nije lošiji; sve lošije ili nejasno je Mixed (rok najave). Svaki stupac verzije mora biti ili uspoređen ili izričito
/// izuzet (test refleksijom nad MembershipPlanVersion), pa novi uvjet ne može tiho proći kao "povoljan".
/// </summary>
public static class MembershipPlanChangeClassifier
{
    /// <summary>Svojstva verzije koja se uspoređuju (nazivi dimenzija u rezultatu).</summary>
    public static readonly IReadOnlyCollection<string> ComparedProperties = new[]
    {
        nameof(MembershipPlanVersion.Price),
        nameof(MembershipPlanVersion.BillingInterval),
        nameof(MembershipPlanVersion.RenewalAnchor),
        nameof(MembershipPlanVersion.CompanyScope),
        nameof(MembershipPlanVersion.Companies),
        nameof(MembershipPlanVersion.Services),
        nameof(MembershipPlanVersion.UsageLimits),
        nameof(MembershipPlanVersion.PriceBenefits),
        nameof(MembershipPlanVersion.MinimumCommitmentPeriods),
        nameof(MembershipPlanVersion.CancellationNoticeDays),
        nameof(MembershipPlanVersion.PauseAllowed),
        nameof(MembershipPlanVersion.MaxPauseDays),
        nameof(MembershipPlanVersion.MaxPausePeriods),
        nameof(MembershipPlanVersion.MaxPausesPer12Months),
        nameof(MembershipPlanVersion.PauseExtendsPeriod)
    };

    /// <summary>Svojstva koja nisu uvjet koji postojeći član osjeti: identitet/audit, i početna naknada (plaća se samo pri
    /// prodaji, postojeći članovi je ne plaćaju ponovno).</summary>
    public static readonly IReadOnlyCollection<string> IgnoredProperties = new[]
    {
        nameof(MembershipPlanVersion.Id),
        nameof(MembershipPlanVersion.OrganizationId),
        nameof(MembershipPlanVersion.MembershipPlanId),
        nameof(MembershipPlanVersion.Version),
        nameof(MembershipPlanVersion.CreatedAt),
        nameof(MembershipPlanVersion.CreatedBy),
        nameof(MembershipPlanVersion.Plan),
        nameof(MembershipPlanVersion.StartFee)
    };

    public static (MembershipPlanChangeClassification Classification, List<string> WorsenedDimensions) Classify(
        MembershipPlanVersion previous, MembershipPlanVersion next)
    {
        ArgumentNullException.ThrowIfNull(previous);
        ArgumentNullException.ThrowIfNull(next);
        List<string> worse = new();

        if (next.Price > previous.Price)
            worse.Add(nameof(MembershipPlanVersion.Price));
        if (next.BillingInterval != previous.BillingInterval)
            worse.Add(nameof(MembershipPlanVersion.BillingInterval));
        if (next.RenewalAnchor != previous.RenewalAnchor)
            worse.Add(nameof(MembershipPlanVersion.RenewalAnchor));

        if (!CompaniesCovered(previous, next))
            worse.Add(nameof(MembershipPlanVersion.Companies));

        HashSet<Guid> nextServices = next.Services.Select(s => s.ServiceId).ToHashSet();
        if (previous.Services.Any(s => !nextServices.Contains(s.ServiceId)))
            worse.Add(nameof(MembershipPlanVersion.Services));

        // Svaki limit nove verzije mora postojati i u staroj (isti opseg i prozor) s manjim ili jednakim brojem; novi ili
        // stroži limit je pogoršanje, uklonjen limit je poboljšanje.
        bool limitsWorse = next.UsageLimits.Any(l =>
        {
            MembershipPlanUsageLimit old = previous.UsageLimits.FirstOrDefault(o => o.ServiceId == l.ServiceId && o.Window == l.Window);
            return old == null || l.MaxUses < old.MaxUses;
        });
        if (limitsWorse)
            worse.Add(nameof(MembershipPlanVersion.UsageLimits));

        if (BenefitsWorse(previous, next))
            worse.Add(nameof(MembershipPlanVersion.PriceBenefits));

        if ((next.MinimumCommitmentPeriods ?? 0) > (previous.MinimumCommitmentPeriods ?? 0))
            worse.Add(nameof(MembershipPlanVersion.MinimumCommitmentPeriods));
        if ((next.CancellationNoticeDays ?? 0) > (previous.CancellationNoticeDays ?? 0))
            worse.Add(nameof(MembershipPlanVersion.CancellationNoticeDays));

        if (previous.PauseAllowed && !next.PauseAllowed)
            worse.Add(nameof(MembershipPlanVersion.PauseAllowed));
        if (previous.PauseAllowed && next.PauseAllowed)
        {
            if (LessOrUnlimited(next.MaxPauseDays, previous.MaxPauseDays))
                worse.Add(nameof(MembershipPlanVersion.MaxPauseDays));
            if (LessOrUnlimited(next.MaxPausePeriods, previous.MaxPausePeriods))
                worse.Add(nameof(MembershipPlanVersion.MaxPausePeriods));
            if (LessOrUnlimited(next.MaxPausesPer12Months, previous.MaxPausesPer12Months))
                worse.Add(nameof(MembershipPlanVersion.MaxPausesPer12Months));
            if (previous.PauseExtendsPeriod && !next.PauseExtendsPeriod)
                worse.Add(nameof(MembershipPlanVersion.PauseExtendsPeriod));
        }

        return (worse.Count == 0 ? MembershipPlanChangeClassification.Favorable : MembershipPlanChangeClassification.Mixed, worse);
    }

    private static bool CompaniesCovered(MembershipPlanVersion previous, MembershipPlanVersion next)
    {
        if (next.CompanyScope == MembershipCompanyScope.AllCompanies)
            return true;
        if (previous.CompanyScope == MembershipCompanyScope.AllCompanies)
            return false;
        HashSet<Guid> nextCompanies = next.Companies.Select(c => c.CompanyId).ToHashSet();
        return previous.Companies.All(c => nextCompanies.Contains(c.CompanyId));
    }

    /// <summary>2E — cjenovna pogodnost (Q48 t.7): uspoređuje se EFEKTIVNO pravilo za svaku uslugu iz obje verzije i za "ostale
    /// usluge" (pravilo usluge, inače "Sve usluge"). Lošije: pogodnost nestala, manji postotak ili iznos popusta, viša cijena za
    /// člana; promjena vrste pravila se ne može jednoznačno usporediti (bez cijene usluge) pa je mješovita.</summary>
    private static bool BenefitsWorse(MembershipPlanVersion previous, MembershipPlanVersion next)
    {
        IEnumerable<Guid?> keys = previous.PriceBenefits.Concat(next.PriceBenefits)
            .Where(b => b.ServiceId.HasValue).Select(b => b.ServiceId).Distinct()
            .Append(null);
        return keys.Any(serviceId => BenefitWorse(Effective(previous, serviceId), Effective(next, serviceId)));
    }

    private static MembershipPlanPriceBenefit Effective(MembershipPlanVersion version, Guid? serviceId) =>
        (serviceId.HasValue ? version.PriceBenefits.FirstOrDefault(b => b.ServiceId == serviceId) : null)
        ?? version.PriceBenefits.FirstOrDefault(b => b.Scope == MembershipPriceBenefitScope.AllServices);

    private static bool BenefitWorse(MembershipPlanPriceBenefit old, MembershipPlanPriceBenefit current)
    {
        if (old == null)
            return false;
        if (current == null || current.Type != old.Type)
            return true;
        return old.Type == MembershipPriceBenefitType.FixedPrice ? current.Value > old.Value : current.Value < old.Value;
    }

    /// <summary>Je li nova granica stroža od stare; null znači bez ograničenja.</summary>
    private static bool LessOrUnlimited(int? next, int? previous) =>
        next.HasValue && (!previous.HasValue || next.Value < previous.Value);
}
