using System;
using System.Collections.Generic;
using System.Linq;
using BlueDragon.DuneLight.Core.DTOs.Catalog;
using BlueDragon.DuneLight.Core.Interfaces.Catalog;

namespace BlueDragon.DuneLight.Infrastructure.Services;

/// <summary>
/// Čisti algoritam razrješavanja cijene, bez pristupa bazi — vidi <see cref="IPriceResolutionService"/>.
/// </summary>
public class PriceResolutionService : IPriceResolutionService
{
    public ResolvedPrice Resolve(IEnumerable<PriceCandidate> candidates, decimal defaultPrice, Guid? companyId, DateTimeOffset date)
    {
        List<PriceCandidate> validOnDate = candidates
            .Where(c => c.IsActive && c.ValidFrom <= date && (c.ValidTo == null || c.ValidTo >= date))
            .OrderByDescending(c => c.ValidFrom)
            .ToList();

        PriceCandidate companySpecific = companyId.HasValue
            ? validOnDate.FirstOrDefault(c => c.CompanyId == companyId.Value)
            : null;

        if (companySpecific != null)
            return new ResolvedPrice { Price = companySpecific.Price, Source = PriceSource.CompanySpecific };

        PriceCandidate allCompanies = validOnDate.FirstOrDefault(c => c.CompanyId == null);
        if (allCompanies != null)
            return new ResolvedPrice { Price = allCompanies.Price, Source = PriceSource.AllCompanies };

        return new ResolvedPrice { Price = defaultPrice, Source = PriceSource.Default };
    }
}
