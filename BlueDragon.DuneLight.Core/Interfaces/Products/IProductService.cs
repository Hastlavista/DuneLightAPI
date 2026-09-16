using System;
using System.Threading.Tasks;
using BlueDragon.DuneLight.Core.DTOs.Products;
using BlueDragon.DuneLight.Core.Shared;

namespace BlueDragon.DuneLight.Core.Interfaces.Products;

/// <summary>
/// Product == stavka maloprodajnog kataloga na razini Organization (npr. "Proteinska pločica") — ne pripada
/// direktno jednoj Company, dostupnost/zaliha je Company-specifična preko IStockService (vidi ProductStock.cs).
/// </summary>
public interface IProductService
{
    Task<PagedResult<ProductDto>> GetPaged(Guid organizationId, PagedRequest request);
    Task<ProductDto> GetById(Guid organizationId, Guid id);
    Task<ProductDto> Create(Guid organizationId, Guid userId, ProductCreateRequest request);
    Task<ProductDto> Update(Guid organizationId, Guid userId, Guid id, ProductUpdateRequest request);

    /// <summary>Deaktivacija blokira dodavanje u nove Checkoute i nove restock/transfer operacije (ostaje
    /// dopušteno ručno korigirati postojeću zalihu radi usklađenja, vidi spec section 47) — povijesni
    /// CheckoutItem/StockMovement ostaju netaknuti. Reaktivacija zahtijeva da normalizirani naziv trenutno ne
    /// koliduje s drugim aktivnim Productom.</summary>
    Task<ProductDto> SetActive(Guid organizationId, Guid userId, Guid id, bool isActive);

    /// <summary>Trajno briše SAMO ako Product nema nikakvu zalihu/prodajnu povijest (REFERENCED_CANNOT_DELETE
    /// inače, vidi spec section 7) — deaktivacija je redovan put za "ukloni iz prodaje".</summary>
    Task Delete(Guid organizationId, Guid id);
}
