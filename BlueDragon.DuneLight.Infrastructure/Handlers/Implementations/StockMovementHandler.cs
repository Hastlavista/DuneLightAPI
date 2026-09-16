using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using BlueDragon.DuneLight.Infrastructure.Domain.Contexts;
using BlueDragon.DuneLight.Infrastructure.Domain.Models.Products;
using BlueDragon.DuneLight.Infrastructure.Domain.Settings;
using BlueDragon.DuneLight.Infrastructure.Handlers.Interfaces;
using BlueDragon.DuneLight.Infrastructure.UnitOfWork;
using Microsoft.EntityFrameworkCore;

namespace BlueDragon.DuneLight.Infrastructure.Handlers.Implementations;

public class StockMovementHandler : IStockMovementHandler
{
    private readonly DatabaseSettings _databaseSettings;

    public StockMovementHandler(DatabaseSettings databaseSettings)
    {
        _databaseSettings = databaseSettings;
    }

    public async Task Add(IUnitOfWork uow, StockMovement movement)
    {
        uow.Context.StockMovements.Add(movement);
        await uow.Context.SaveChangesAsync();
    }

    public async Task<List<StockMovement>> GetByProduct(Guid organizationId, Guid productId)
    {
        await using DatabaseContext context = DatabaseContext.GenerateContext(_databaseSettings.ConnectionString);
        return await context.StockMovements
            .Include(m => m.Company)
            .Include(m => m.RelatedCompany)
            .Where(m => m.OrganizationId == organizationId && m.ProductId == productId)
            .OrderByDescending(m => m.CreatedAt)
            .ToListAsync();
    }
}
