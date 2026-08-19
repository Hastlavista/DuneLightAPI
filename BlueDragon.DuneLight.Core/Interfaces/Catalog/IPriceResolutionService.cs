using System;
using System.Collections.Generic;
using BlueDragon.DuneLight.Core.DTOs.Catalog;

namespace BlueDragon.DuneLight.Core.Interfaces.Catalog;

public class ResolvedPrice
{
    public decimal Price { get; set; }
    public PriceSource Source { get; set; }
}

/// <summary>
/// Zaseban, čisti servis za razrješavanje cijene (bez pristupa bazi) — koristi ga
/// pregledni cjenik i, kasnije, moduli termina i prodaje paketa.
///
/// Redoslijed: 1) aktivna stavka za konkretnu tvrtku koja vrijedi na dani datum,
/// 2) aktivna stavka za "sve tvrtke" (CompanyId == null) koja vrijedi na dani datum,
/// 3) zadana (default) cijena usluge/paketa.
/// </summary>
public interface IPriceResolutionService
{
    ResolvedPrice Resolve(IEnumerable<PriceCandidate> candidates, decimal defaultPrice, Guid? companyId, DateTimeOffset date);
}
