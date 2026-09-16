using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using BlueDragon.DuneLight.Infrastructure.Domain.Models.Products;
using BlueDragon.DuneLight.Infrastructure.UnitOfWork;

namespace BlueDragon.DuneLight.Infrastructure.Handlers.Interfaces;

public interface IProductStockHandler
{
    /// <summary>Zaključava ProductStock redak za (productId, companyId) unutar pozivateljeve transakcije — ako
    /// redak još ne postoji, atomski ga stvara s Quantity=0 (INSERT ... ON CONFLICT DO NOTHING + SELECT ... FOR
    /// UPDATE u istom pozivu, vidi ProductStock.cs klasnu napomenu) umjesto retry-petlje. Pozivatelj (StockService)
    /// mora pozivati ovu metodu jednom po (productId, companyId) paru u DETERMINISTIČKOM redoslijedu (npr. sortirano
    /// po ProductId za više proizvoda iste Company, po CompanyId za transfer) da izbjegne deadlock (vidi spec
    /// section 26/34).</summary>
    Task<ProductStock> GetOrCreateForUpdate(IUnitOfWork uow, Guid organizationId, Guid productId, Guid companyId, DateTimeOffset now);

    /// <summary>Sprema skalarne promjene (Quantity/UpdatedAt) na već zaključanom retku iz GetOrCreateForUpdate.</summary>
    Task Update(IUnitOfWork uow, ProductStock stock);

    Task<ProductStock> Get(Guid organizationId, Guid productId, Guid companyId);
    Task<List<ProductStock>> GetByCompany(Guid organizationId, Guid companyId);
    Task<List<ProductStock>> GetByProduct(Guid organizationId, Guid productId);
}
