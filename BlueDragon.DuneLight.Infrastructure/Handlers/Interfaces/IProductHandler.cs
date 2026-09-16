using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using BlueDragon.DuneLight.Core.Shared;
using BlueDragon.DuneLight.Infrastructure.Domain.Models.Products;

namespace BlueDragon.DuneLight.Infrastructure.Handlers.Interfaces;

public interface IProductHandler
{
    Task<(List<Product> Items, int TotalCount)> GetPaged(Guid organizationId, PagedRequest request);
    Task<Product> GetById(Guid organizationId, Guid id);

    /// <summary>Svi AKTIVNI Product retci u organizaciji, jedan bounded upit (bez paginacije) — za
    /// OperationalDashboardService.Alerts (out-of-stock), gdje Product bez ijednog ProductStock retka za
    /// Company mora i dalje biti obuhvaćen (vidi ProductStockHandler.GetByCompany — samo POSTOJEĆI retci).</summary>
    Task<List<Product>> GetActive(Guid organizationId);

    Task<bool> NameExistsAmongActive(Guid organizationId, string name, Guid? excludeId);

    /// <summary>Normalizirano (trim + case-insensitive) postoji li SKU u Organization — SKU nije vezan uz
    /// IsActive (za razliku od naziva), vidi spec section 5.</summary>
    Task<bool> SkuExists(Guid organizationId, string sku, Guid? excludeId);

    Task Add(Product product);
    Task Update(Product product);
    Task Delete(Product product);

    /// <summary>Bilo kakva zaliha/prodajna povijest (CheckoutItem, StockMovement ili ProductStock redak) —
    /// vidi spec section 7. Ako je true, hard delete se blokira (REFERENCED_CANNOT_DELETE).</summary>
    Task<bool> IsReferenced(Guid organizationId, Guid id);
}
