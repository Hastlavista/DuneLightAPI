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
    public ResolvedPrice Resolve(IEnumerable<PriceCandidate> candidates, decimal defaultPrice, Guid? companyId, Guid? employeeId, DateTimeOffset date)
    {
        List<PriceCandidate> validOnDate = candidates
            .Where(c => c.IsActive && c.ValidFrom <= date && (c.ValidTo == null || c.ValidTo >= date))
            .OrderByDescending(c => c.ValidFrom)
            .ToList();

        if (employeeId.HasValue)
        {
            List<PriceCandidate> employeeTier = validOnDate.Where(c => c.EmployeeId == employeeId.Value).ToList();
            PriceCandidate employeeCompany = companyId.HasValue ? employeeTier.FirstOrDefault(c => c.CompanyId == companyId.Value) : null;
            if (employeeCompany != null)
                return new ResolvedPrice { Price = employeeCompany.Price, Source = PriceSource.EmployeeCompanySpecific };

            PriceCandidate employeeAllCompanies = employeeTier.FirstOrDefault(c => c.CompanyId == null);
            if (employeeAllCompanies != null)
                return new ResolvedPrice { Price = employeeAllCompanies.Price, Source = PriceSource.EmployeeAllCompanies };
        }

        // Nezaposleničke razine: stavke BILO kojeg zaposlenika se ovdje nikad ne koriste (Standard ih preskače u cijelosti).
        List<PriceCandidate> standardTier = validOnDate.Where(c => c.EmployeeId == null).ToList();
        PriceCandidate companySpecific = companyId.HasValue
            ? standardTier.FirstOrDefault(c => c.CompanyId == companyId.Value)
            : null;

        if (companySpecific != null)
            return new ResolvedPrice { Price = companySpecific.Price, Source = PriceSource.CompanySpecific };

        PriceCandidate allCompanies = standardTier.FirstOrDefault(c => c.CompanyId == null);
        if (allCompanies != null)
            return new ResolvedPrice { Price = allCompanies.Price, Source = PriceSource.AllCompanies };

        return new ResolvedPrice { Price = defaultPrice, Source = PriceSource.Default };
    }
}
