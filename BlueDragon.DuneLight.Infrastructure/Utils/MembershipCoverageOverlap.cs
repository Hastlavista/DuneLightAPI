using System;
using System.Collections.Generic;
using System.Linq;

namespace BlueDragon.DuneLight.Infrastructure.Utils;

/// <summary>Opseg pokrića jednog članstva u vremenu: razdoblje važenja [From, To] (To = null: bez kraja), usluge i poslovnice.</summary>
public sealed record MembershipCoverageScope(
    Guid MembershipId, string PlanName, DateOnly From, DateOnly? To,
    IReadOnlyCollection<Guid> ServiceIds, bool AllCompanies, IReadOnlyCollection<Guid> CompanyIds);

public sealed record MembershipCoverageConflict(
    MembershipCoverageScope Existing, List<Guid> SharedServiceIds, List<Guid> SharedCompanyIds, bool SharedAllCompanies);

/// <summary>
/// P2 (Q10/Q46) — dva članstva se preklapaju samo ako im se razdoblja važenja preklapaju I dijele barem jednu uslugu u barem
/// jednoj poslovnici. Članstvo sa zakazanom promjenom uvjeta daje dva opsega (trenutni uvjeti do promjene, novi od promjene),
/// pa provjera uzima u obzir i zakazane promjene (Q46.2). Čista funkcija.
/// </summary>
public static class MembershipCoverageOverlap
{
    public static MembershipCoverageConflict FindConflict(MembershipCoverageScope candidate, IEnumerable<MembershipCoverageScope> existing)
    {
        foreach (MembershipCoverageScope other in existing)
        {
            if (other.MembershipId == candidate.MembershipId)
                continue;
            bool timeOverlaps = (other.To == null || other.To.Value >= candidate.From) && (candidate.To == null || candidate.To.Value >= other.From);
            if (!timeOverlaps)
                continue;

            List<Guid> sharedServices = candidate.ServiceIds.Intersect(other.ServiceIds).ToList();
            if (sharedServices.Count == 0)
                continue;

            bool sharedAll = candidate.AllCompanies && other.AllCompanies;
            List<Guid> sharedCompanies = sharedAll ? new List<Guid>()
                : candidate.AllCompanies ? other.CompanyIds.ToList()
                : other.AllCompanies ? candidate.CompanyIds.ToList()
                : candidate.CompanyIds.Intersect(other.CompanyIds).ToList();
            if (sharedAll || sharedCompanies.Count > 0)
                return new MembershipCoverageConflict(other, sharedServices, sharedCompanies, sharedAll);
        }

        return null;
    }
}
