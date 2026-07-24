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
    public ResolvedPrice Resolve(IEnumerable<PriceCandidate> candidates, decimal defaultPrice, Guid? locationId, DateTimeOffset date)
    {
        List<PriceCandidate> validOnDate = candidates
            .Where(c => c.IsActive && c.ValidFrom <= date && (c.ValidTo == null || c.ValidTo >= date))
            .OrderByDescending(c => c.ValidFrom)
            .ToList();

        PriceCandidate locationSpecific = locationId.HasValue
            ? validOnDate.FirstOrDefault(c => c.LocationId == locationId.Value)
            : null;

        if (locationSpecific != null)
            return new ResolvedPrice { Price = locationSpecific.Price, Source = PriceSource.LocationSpecific };

        PriceCandidate allLocations = validOnDate.FirstOrDefault(c => c.LocationId == null);
        if (allLocations != null)
            return new ResolvedPrice { Price = allLocations.Price, Source = PriceSource.AllLocations };

        return new ResolvedPrice { Price = defaultPrice, Source = PriceSource.Default };
    }
}
