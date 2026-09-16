using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using BlueDragon.DuneLight.Infrastructure.Domain.Models.Products;
using BlueDragon.DuneLight.Infrastructure.UnitOfWork;

namespace BlueDragon.DuneLight.Infrastructure.Handlers.Interfaces;

public interface IStockMovementHandler
{
    /// <summary>Umeće novi (nepromjenjiv) povijesni redak — StockMovement se nikad ne update-a/briše, vidi
    /// StockMovement.cs klasnu napomenu.</summary>
    Task Add(IUnitOfWork uow, StockMovement movement);

    /// <summary>Puna povijest kretanja zalihe za jedan Product (svih Company), najnoviji prvi.</summary>
    Task<List<StockMovement>> GetByProduct(Guid organizationId, Guid productId);
}
