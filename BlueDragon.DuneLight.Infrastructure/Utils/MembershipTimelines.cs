using System;
using System.Collections.Generic;
using System.Linq;
using BlueDragon.DuneLight.Core.Enums;
using BlueDragon.DuneLight.Infrastructure.Domain.Models.Catalog;
using BlueDragon.DuneLight.Infrastructure.Domain.Models.Clients;

namespace BlueDragon.DuneLight.Infrastructure.Utils;

/// <summary>
/// P2 (2B) — prijelaz s entiteta članstva na čistu matematiku (MembershipPeriodCalendar / MembershipCoverageOverlap) i zapis
/// povijesti. Zahtijeva učitane Pauses, PlanVersion (i PendingPlanVersion kad postoji) s uslugama i poslovnicama.
/// </summary>
public static class MembershipTimelines
{
    /// <summary>Neotkazane pauze sa zadnjim stvarnim danom.</summary>
    public static List<MembershipPauseSpan> PauseSpans(ClientMembership membership) => membership.Pauses
        .Where(p => p.CancelledAt == null)
        .OrderBy(p => p.StartsOn)
        .Select(p => new MembershipPauseSpan(p.Kind, p.StartsOn, p.EffectiveEndsOn))
        .ToList();

    /// <summary>Vremenska linija trenutnih uvjeta (od sidra trenutnih uvjeta, 2C), bez zakazane promjene. Pauze koje su
    /// završile prije sidra već su ugrađene u granicu promjene uvjeta, pa se ne računaju ponovno.</summary>
    public static MembershipTimeline Current(ClientMembership membership) => new(
        membership.TermsAnchorOn, MembershipPlanReadModel.PeriodTerms(membership.PlanVersion),
        PauseSpans(membership).Where(p => p.EndsOn >= membership.TermsAnchorOn).ToList())
    {
        MembershipStartsOn = membership.StartsOn
    };

    /// <summary>Vremenska linija sa zakazanom promjenom uvjeta (kad postoji).</summary>
    public static MembershipTimeline WithPending(ClientMembership membership) => membership.PendingPlanVersion == null
        ? Current(membership)
        : Current(membership) with
        {
            PendingTerms = MembershipPlanReadModel.PeriodTerms(membership.PendingPlanVersion),
            PendingNotBefore = membership.PendingEffectiveOn
        };

    /// <summary>Stvarni datum stupanja zakazane promjene na snagu: prva obnova na ili nakon zakazanog datuma (pauze mogu
    /// pomaknuti obnovu).</summary>
    public static DateOnly? PendingEffectiveOn(ClientMembership membership) => membership.PendingEffectiveOn.HasValue
        ? MembershipPeriodCalendar.FirstPeriodStartingOnOrAfter(Current(membership), membership.PendingEffectiveOn.Value).StartsOn
        : null;

    public static MembershipCoverageScope Scope(Guid membershipId, string planName, DateOnly from, DateOnly? to, MembershipPlanVersion version) => new(
        membershipId, planName, from, to,
        version.Services.Select(s => s.ServiceId).ToList(),
        version.CompanyScope == MembershipCompanyScope.AllCompanies,
        version.Companies.Select(c => c.CompanyId).ToList());

    /// <summary>Q46.2 — opseg trenutnih uvjeta do zakazane promjene i opseg novih uvjeta od nje.</summary>
    public static IEnumerable<MembershipCoverageScope> Scopes(ClientMembership membership)
    {
        string planName = membership.Plan?.Name;
        DateOnly? pendingOn = membership.PendingPlanVersion == null ? null : PendingEffectiveOn(membership);
        if (pendingOn == null)
        {
            yield return Scope(membership.Id, planName, membership.StartsOn, membership.EndsOn, membership.PlanVersion);
            yield break;
        }

        yield return Scope(membership.Id, planName, membership.StartsOn, pendingOn.Value.AddDays(-1), membership.PlanVersion);
        yield return Scope(membership.Id, membership.PendingPlanVersion.Plan?.Name ?? planName, pendingOn.Value, membership.EndsOn,
            membership.PendingPlanVersion);
    }

    /// <summary>Pregled 2B — trajna oznaka da izmjena plana (verzija) nije primijenjena na članstvo i zašto.</summary>
    public static void MarkPlanUpdateNotApplied(ClientMembership membership, Guid versionId, string reason, DateTimeOffset at)
    {
        membership.PlanUpdateSkippedVersionId = versionId;
        membership.PlanUpdateSkippedReason = reason;
        membership.PlanUpdateSkippedAt = at;
    }

    /// <summary>Kasnija uspješna izmjena plana rješava oznaku.</summary>
    public static void ClearPlanUpdateNotApplied(ClientMembership membership)
    {
        membership.PlanUpdateSkippedVersionId = null;
        membership.PlanUpdateSkippedVersion = null;
        membership.PlanUpdateSkippedReason = null;
        membership.PlanUpdateSkippedAt = null;
    }

    public static ClientMembershipAuditLog Audit(
        ClientMembership membership, Guid userId, string changeType, string oldValue, string newValue, string reason = null) => new()
    {
        Id = Guid.NewGuid(),
        OrganizationId = membership.OrganizationId,
        ClientMembershipId = membership.Id,
        ChangeType = changeType,
        OldValue = oldValue,
        NewValue = newValue,
        Reason = reason,
        ChangedAt = DateTimeOffset.UtcNow,
        ChangedBy = userId
    };
}
