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
/// Redoslijed (Phase M1G): uz <c>employeeId</c> (izvor cijene Employee) najprije 1) stavka zaposlenika za konkretnu tvrtku,
/// 2) stavka zaposlenika za "sve tvrtke"; zatim (i jedino za <c>employeeId</c> = null, izvor Standard)
/// 3) aktivna stavka za konkretnu tvrtku, 4) aktivna stavka za "sve tvrtke" (CompanyId == null), 5) zadana (default)
/// cijena usluge/paketa. Stavke DRUGIH zaposlenika se nikad ne razmatraju; Standard preskače sve razine zaposlenika.
/// </summary>
public interface IPriceResolutionService
{
    ResolvedPrice Resolve(IEnumerable<PriceCandidate> candidates, decimal defaultPrice, Guid? companyId, Guid? employeeId, DateTimeOffset date);
}
