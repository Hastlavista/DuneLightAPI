using System;

namespace BlueDragon.DuneLight.Core.DTOs.Catalog;

/// <summary>
/// Minimalan, EF-neovisan prikaz stavke cjenika koji koristi čisti (testabilni)
/// algoritam razrješavanja cijene u <see cref="Interfaces.Catalog.IPriceResolutionService"/>.
/// </summary>
public class PriceCandidate
{
    public Guid? CompanyId { get; set; }

    /// <summary>Phase M1G — razina zaposlenika (samo stavke usluge); null = stavka bez zaposlenika.</summary>
    public Guid? EmployeeId { get; set; }
    public decimal Price { get; set; }
    public DateOnly ValidFrom { get; set; }
    public DateOnly? ValidTo { get; set; }
    public bool IsActive { get; set; }
}
