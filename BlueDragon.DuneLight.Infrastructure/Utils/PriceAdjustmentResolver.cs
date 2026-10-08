using System;
using System.Collections.Generic;
using System.Linq;
using BlueDragon.DuneLight.Core.DTOs.Appointments;
using BlueDragon.DuneLight.Core.Enums;
using BlueDragon.DuneLight.Infrastructure.Domain.Models.Catalog;

namespace BlueDragon.DuneLight.Infrastructure.Utils;

/// <summary>Jedan kandidat prilagodbe cijene (izvor + pravilo + cijena koju bi dao, ili razlog neprimjene).</summary>
public sealed record PriceAdjustmentCandidate(
    PriceAdjustmentType Type, Guid SourceId, int SourceOrder, decimal? ResultingPrice, PriceAdjustmentReason? Reason,
    MembershipPlanPriceBenefit Rule);

/// <summary>Ishod: predložena cijena, primijenjeni kandidat (null = cjenik) i evaluacija svih kandidata.</summary>
public sealed record PriceAdjustmentResult(decimal SuggestedAmount, PriceAdjustmentCandidate Applied, List<PriceAdjustmentCandidateDto> Evaluation);

/// <summary>
/// P2 (Q1, P2_PLAN §10.4) — čisti izbor JEDNE prilagodbe cijene: najniža cijena ispod osnovne pobjeđuje, bez slaganja; kod iste
/// cijene odlučuje fiksni redoslijed tipova (<see cref="PriceAdjustmentType"/>: Membership → ClientTag → ClientGroup → Promo), pa
/// redoslijed izvora. Svaki kandidat dobiva ishod (Applied | LostToBetterPrice | LostOnTie | NoReduction | NotApplicable) — zapis za
/// izvještaje, objašnjenje klijentu i kasniji fiksni prioritet bez migracije. Ručni iznos nije dio izbora (ima zadnju riječ).
/// </summary>
public static class PriceAdjustmentResolver
{
    public static PriceAdjustmentResult Resolve(decimal basePrice, IReadOnlyCollection<PriceAdjustmentCandidate> candidates)
    {
        PriceAdjustmentCandidate best = candidates
            .Where(c => c.Reason == null && c.ResultingPrice.HasValue && c.ResultingPrice.Value < basePrice)
            .OrderBy(c => c.ResultingPrice.Value)
            .ThenBy(c => c.Type)
            .ThenBy(c => c.SourceOrder)
            .FirstOrDefault();

        List<PriceAdjustmentCandidateDto> evaluation = candidates
            .OrderBy(c => c.Type).ThenBy(c => c.SourceOrder)
            .Select(c => new PriceAdjustmentCandidateDto
            {
                Type = c.Type,
                SourceId = c.SourceId,
                Outcome = OutcomeOf(c, basePrice, best),
                Reason = c.Reason,
                ResultingPrice = c.Reason == null ? c.ResultingPrice : null,
                RuleScope = c.Rule?.Scope,
                RuleType = c.Rule?.Type,
                RuleValue = c.Rule?.Value
            })
            .ToList();

        return new PriceAdjustmentResult(best?.ResultingPrice ?? basePrice, best, evaluation);
    }

    private static PriceAdjustmentOutcome OutcomeOf(PriceAdjustmentCandidate candidate, decimal basePrice, PriceAdjustmentCandidate best)
    {
        if (candidate.Reason != null || !candidate.ResultingPrice.HasValue)
            return PriceAdjustmentOutcome.NotApplicable;
        if (candidate.ResultingPrice.Value >= basePrice)
            return PriceAdjustmentOutcome.NoReduction;
        if (ReferenceEquals(candidate, best))
            return PriceAdjustmentOutcome.Applied;
        return candidate.ResultingPrice.Value == best.ResultingPrice.Value
            ? PriceAdjustmentOutcome.LostOnTie
            : PriceAdjustmentOutcome.LostToBetterPrice;
    }
}

/// <summary>
/// P2 (faza 2E) — čista pravila cjenovne pogodnosti članarine: efektivno pravilo za uslugu (pravilo usluge ima prednost pred "Sve
/// usluge"), izračun (zaokruženo na 0,01, nikad ispod 0) i uvjeti (kao pokriće bez limita: članstvo vrijedi na datum sesije,
/// poslovnica u opsegu plana, nije u pauzi, nije u dugu uz "ne pokrivaj"/"blokiraj"; samo nepokrivene sesije, Q2).
/// </summary>
public static class MembershipPriceBenefitRules
{
    public static MembershipPlanPriceBenefit BenefitFor(MembershipPlanVersion terms, Guid serviceId) =>
        terms.PriceBenefits.FirstOrDefault(b => b.Scope == MembershipPriceBenefitScope.Service && b.ServiceId == serviceId)
        ?? terms.PriceBenefits.FirstOrDefault(b => b.Scope == MembershipPriceBenefitScope.AllServices);

    public static decimal Apply(MembershipPlanPriceBenefit rule, decimal basePrice)
    {
        decimal price = rule.Type switch
        {
            MembershipPriceBenefitType.PercentOff => basePrice * (100m - rule.Value) / 100m,
            MembershipPriceBenefitType.AmountOff => basePrice - rule.Value,
            MembershipPriceBenefitType.FixedPrice => rule.Value,
            _ => basePrice
        };
        return Math.Max(0m, Math.Round(price, 2, MidpointRounding.AwayFromZero));
    }

    /// <summary>Kandidat pogodnosti jednog članstva za sesiju. <paramref name="covered"/>/<paramref name="packageCovered"/> = sesija
    /// je pokrivena (Q2: pogodnost samo na nepokrivene).</summary>
    public static PriceAdjustmentCandidate Candidate(
        ClientMembershipView membership, MembershipCoverageSubject subject, decimal basePrice, bool covered, bool packageCovered)
    {
        PriceAdjustmentCandidate NotApplicable(PriceAdjustmentReason reason, MembershipPlanPriceBenefit rule = null) =>
            new(PriceAdjustmentType.Membership, membership.Membership.Id, membership.Order, null, reason, rule);

        Domain.Models.Clients.ClientMembership m = membership.Membership;
        if (!MembershipCoverageRules.IsValidOn(m, subject.PeriodDate))
            return NotApplicable(PriceAdjustmentReason.OutsideMembershipPeriod);
        MembershipPlanVersion terms = MembershipCoverageRules.TermsOn(m, subject.PeriodDate);
        MembershipPlanPriceBenefit rule = BenefitFor(terms, subject.ServiceId);
        if (rule == null)
            return NotApplicable(PriceAdjustmentReason.NoBenefitForService);
        if (covered)
            return NotApplicable(PriceAdjustmentReason.CoveredByMembership, rule);
        if (packageCovered)
            return NotApplicable(PriceAdjustmentReason.CoveredByPackage, rule);
        if (!MembershipCoverageRules.CoversCompany(terms, subject.CompanyId))
            return NotApplicable(PriceAdjustmentReason.CompanyNotCovered, rule);
        if (MembershipTimelines.PauseSpans(m).Any(p => p.StartsOn <= subject.PeriodDate && p.EndsOn >= subject.PeriodDate)
            || MembershipCoverageRules.PeriodOf(m, subject.PeriodDate).Skipped)
            return NotApplicable(PriceAdjustmentReason.Paused, rule);
        if (membership.Standing == MembershipStanding.Delinquent && membership.DebtBehavior != MembershipDebtBehavior.KeepCovering)
            return NotApplicable(PriceAdjustmentReason.DebtNotCovered, rule);
        return new PriceAdjustmentCandidate(PriceAdjustmentType.Membership, m.Id, membership.Order, Apply(rule, basePrice), null, rule);
    }
}

/// <summary>Članstvo klijenta kako ga vidi pogodnost: redoslijed izvora (početak, Id) i stanje duga na "danas".</summary>
public sealed record ClientMembershipView(
    Domain.Models.Clients.ClientMembership Membership, int Order, MembershipStanding Standing, MembershipDebtBehavior DebtBehavior);
