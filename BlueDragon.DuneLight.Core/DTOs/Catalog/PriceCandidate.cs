using System;

namespace BlueDragon.DuneLight.Core.DTOs.Catalog;

/// <summary>
/// Minimalan, EF-neovisan prikaz stavke cjenika koji koristi čisti (testabilni)
/// algoritam razrješavanja cijene u <see cref="Interfaces.Catalog.IPriceResolutionService"/>.
/// </summary>
public class PriceCandidate
{
    public Guid? LocationId { get; set; }
    public decimal Price { get; set; }
    public DateTimeOffset ValidFrom { get; set; }
    public DateTimeOffset? ValidTo { get; set; }
    public bool IsActive { get; set; }
}
