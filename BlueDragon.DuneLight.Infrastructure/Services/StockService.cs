using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using BlueDragon.DuneLight.Core.DTOs.Products;
using BlueDragon.DuneLight.Core.Enums;
using BlueDragon.DuneLight.Core.Interfaces.Products;
using BlueDragon.DuneLight.Core.Shared;
using BlueDragon.DuneLight.Core.Shared.Exceptions;
using BlueDragon.DuneLight.Infrastructure.Domain.Models.Catalog;
using BlueDragon.DuneLight.Infrastructure.Domain.Models.Checkouts;
using BlueDragon.DuneLight.Infrastructure.Domain.Models.Products;
using BlueDragon.DuneLight.Infrastructure.Handlers.Interfaces;
using BlueDragon.DuneLight.Infrastructure.UnitOfWork;
using Microsoft.EntityFrameworkCore;
using ProductEntity = BlueDragon.DuneLight.Infrastructure.Domain.Models.Products.Product;

namespace BlueDragon.DuneLight.Infrastructure.Services;

/// <summary>
/// Vidi IStockService/IStockLedgerService za domensku napomenu. Svaka mutacija zaključava ProductStock redak(e)
/// preko IProductStockHandler.GetOrCreateForUpdate unutar transakcije PRIJE validacije/mutacije — isti obrazac
/// kao CheckoutService (concurrency, vidi spec section 26/34).
/// </summary>
public class StockService : IStockService, IStockLedgerService
{
    private readonly IProductHandler _productHandler;
    private readonly IProductStockHandler _productStockHandler;
    private readonly IStockMovementHandler _stockMovementHandler;
    private readonly ICompanyHandler _companyHandler;
    private readonly IUnitOfWorkFactory _unitOfWorkFactory;

    public StockService(
        IProductHandler productHandler,
        IProductStockHandler productStockHandler,
        IStockMovementHandler stockMovementHandler,
        ICompanyHandler companyHandler,
        IUnitOfWorkFactory unitOfWorkFactory)
    {
        _productHandler = productHandler;
        _productStockHandler = productStockHandler;
        _stockMovementHandler = stockMovementHandler;
        _companyHandler = companyHandler;
        _unitOfWorkFactory = unitOfWorkFactory;
    }

    public async Task<List<ProductStockDto>> GetByCompany(Guid organizationId, Guid companyId)
    {
        // Tenant-scoped parent validacija PRIJE čitanja djece — bez ovoga nepostojeća/tuđa Company i valjana
        // prazna Company obje vraćaju [] što curi postojanje/nepostojanje resursa kroz sporedni kanal (vidi
        // spec fix section 6). Ponovno koristi postojeći ICompanyHandler, ne duplicira tenant provjeru.
        Company company = await _companyHandler.GetById(organizationId, companyId);
        if (company == null)
            throw new NotFoundAppException("Company", companyId);

        List<ProductStock> stock = await _productStockHandler.GetByCompany(organizationId, companyId);
        return stock.Select(ToDto).ToList();
    }

    public async Task<List<ProductStockDto>> GetByProduct(Guid organizationId, Guid productId)
    {
        ProductEntity product = await _productHandler.GetById(organizationId, productId);
        if (product == null)
            throw new NotFoundAppException("Product", productId);

        List<ProductStock> stock = await _productStockHandler.GetByProduct(organizationId, productId);
        return stock.Select(ToDto).ToList();
    }

    public async Task<List<StockMovementDto>> GetMovements(Guid organizationId, Guid productId)
    {
        ProductEntity product = await _productHandler.GetById(organizationId, productId);
        if (product == null)
            throw new NotFoundAppException("Product", productId);

        List<StockMovement> movements = await _stockMovementHandler.GetByProduct(organizationId, productId);
        return movements.Select(ToDto).ToList();
    }

    public async Task<ProductStockDto> Adjust(Guid organizationId, Guid userId, Guid productId, Guid companyId, StockAdjustRequest request)
    {
        ProductEntity product = await _productHandler.GetById(organizationId, productId);
        if (product == null)
            throw new NotFoundAppException("Product", productId);

        Company company = await _companyHandler.GetById(organizationId, companyId);
        if (company == null)
            throw new NotFoundAppException("Company", companyId);
        if (!company.IsActive)
            throw new BusinessRuleException(ErrorCodes.InactiveCompany, $"Tvrtka '{company.Name}' nije aktivna.");

        DateTimeOffset now = DateTimeOffset.UtcNow;
        await using IUnitOfWork uow = await _unitOfWorkFactory.Begin();

        ProductStock stock = await _productStockHandler.GetOrCreateForUpdate(uow, organizationId, productId, companyId, now);

        // Prvi ikad zabilježen movement za ovaj par znači da je redak upravo (unutar GetOrCreateForUpdate)
        // prvi put stvoren s Quantity=0 — takva korekcija je Initial, ne Adjustment (vidi spec section 61).
        bool hasExistingMovement = await uow.Context.StockMovements
            .AnyAsync(m => m.OrganizationId == organizationId && m.ProductId == productId && m.CompanyId == companyId);

        int delta = request.Quantity - stock.Quantity;
        if (delta != 0)
        {
            stock.Quantity = request.Quantity;
            stock.UpdatedAt = now;
            await _productStockHandler.Update(uow, stock);

            await _stockMovementHandler.Add(uow, new StockMovement
            {
                Id = Guid.NewGuid(),
                OrganizationId = organizationId,
                ProductId = productId,
                CompanyId = companyId,
                Type = hasExistingMovement ? StockMovementType.Adjustment : StockMovementType.Initial,
                QuantityDelta = delta,
                Reason = request.Reason,
                CreatedAt = now,
                CreatedBy = userId
            });
        }

        await uow.CommitAsync();
        return ToDto(stock, product.Name, company.Name);
    }

    public async Task<StockTransferResultDto> Transfer(Guid organizationId, Guid userId, StockTransferRequest request)
    {
        if (request.FromCompanyId == request.ToCompanyId)
            throw new BusinessRuleException(ErrorCodes.TransferSameCompany, "Izvorišna i odredišna poslovnica ne smiju biti iste.");

        ProductEntity product = await _productHandler.GetById(organizationId, request.ProductId);
        if (product == null)
            throw new NotFoundAppException("Product", request.ProductId);
        if (!product.IsActive)
            throw new BusinessRuleException(ErrorCodes.InactiveProduct, $"Proizvod '{product.Name}' nije aktivan.");

        Company fromCompany = await _companyHandler.GetById(organizationId, request.FromCompanyId);
        if (fromCompany == null)
            throw new NotFoundAppException("Company", request.FromCompanyId);
        if (!fromCompany.IsActive)
            throw new BusinessRuleException(ErrorCodes.InactiveCompany, $"Tvrtka '{fromCompany.Name}' nije aktivna.");

        Company toCompany = await _companyHandler.GetById(organizationId, request.ToCompanyId);
        if (toCompany == null)
            throw new NotFoundAppException("Company", request.ToCompanyId);
        if (!toCompany.IsActive)
            throw new BusinessRuleException(ErrorCodes.InactiveCompany, $"Tvrtka '{toCompany.Name}' nije aktivna.");

        DateTimeOffset now = DateTimeOffset.UtcNow;
        await using IUnitOfWork uow = await _unitOfWorkFactory.Begin();

        // Deterministički redoslijed zaključavanja po CompanyId (vidi spec section 34) — smanjuje rizik
        // deadlocka kad dva usporedna transfera ciljaju iste dvije Company u suprotnom smjeru.
        List<Guid> orderedCompanyIds = new List<Guid> { request.FromCompanyId, request.ToCompanyId }.OrderBy(id => id).ToList();
        Dictionary<Guid, ProductStock> locked = new Dictionary<Guid, ProductStock>();
        foreach (Guid companyId in orderedCompanyIds)
            locked[companyId] = await _productStockHandler.GetOrCreateForUpdate(uow, organizationId, request.ProductId, companyId, now);

        ProductStock source = locked[request.FromCompanyId];
        ProductStock destination = locked[request.ToCompanyId];

        if (source.Quantity < request.Quantity)
            throw new BusinessRuleException(
                ErrorCodes.InsufficientStock, "Izvorišna poslovnica nema dovoljno zalihe za transfer.",
                new { available = source.Quantity, requested = request.Quantity });

        Guid correlationId = Guid.NewGuid();

        source.Quantity -= request.Quantity;
        source.UpdatedAt = now;
        destination.Quantity += request.Quantity;
        destination.UpdatedAt = now;

        await _productStockHandler.Update(uow, source);
        await _productStockHandler.Update(uow, destination);

        await _stockMovementHandler.Add(uow, new StockMovement
        {
            Id = Guid.NewGuid(),
            OrganizationId = organizationId,
            ProductId = request.ProductId,
            CompanyId = request.FromCompanyId,
            Type = StockMovementType.TransferOut,
            QuantityDelta = -request.Quantity,
            RelatedCompanyId = request.ToCompanyId,
            TransferCorrelationId = correlationId,
            CreatedAt = now,
            CreatedBy = userId
        });
        await _stockMovementHandler.Add(uow, new StockMovement
        {
            Id = Guid.NewGuid(),
            OrganizationId = organizationId,
            ProductId = request.ProductId,
            CompanyId = request.ToCompanyId,
            Type = StockMovementType.TransferIn,
            QuantityDelta = request.Quantity,
            RelatedCompanyId = request.FromCompanyId,
            TransferCorrelationId = correlationId,
            CreatedAt = now,
            CreatedBy = userId
        });

        await uow.CommitAsync();

        return new StockTransferResultDto
        {
            Source = ToDto(source, product.Name, fromCompany.Name),
            Destination = ToDto(destination, product.Name, toCompany.Name)
        };
    }

    public async Task ConsumeForSale(
        IUnitOfWork uow, Guid organizationId, Guid userId, Guid companyId, List<CheckoutItem> productItems, DateTimeOffset now)
    {
        if (productItems == null || productItems.Count == 0)
            return;

        List<Guid> orderedProductIds = productItems
            .Select(i => i.ProductId.GetValueOrDefault())
            .Distinct()
            .OrderBy(id => id)
            .ToList();

        Dictionary<Guid, ProductStock> locked = new Dictionary<Guid, ProductStock>();
        foreach (Guid productId in orderedProductIds)
            locked[productId] = await _productStockHandler.GetOrCreateForUpdate(uow, organizationId, productId, companyId, now);

        // Zbroj po Productu (obrana ako bi ikad postojalo više stavki istog Producta u jednom checkoutu, vidi
        // spec section 27) — validacija SVIH prije bilo kakvog decrementa (atomarnost, vidi spec section 25/26).
        Dictionary<Guid, int> requiredByProduct = productItems
            .GroupBy(i => i.ProductId.GetValueOrDefault())
            .ToDictionary(g => g.Key, g => g.Sum(i => i.Quantity));

        foreach (KeyValuePair<Guid, int> required in requiredByProduct)
        {
            ProductStock stock = locked[required.Key];
            if (stock.Quantity < required.Value)
                throw new BusinessRuleException(
                    ErrorCodes.InsufficientStock, "Nedovoljna zaliha za jednu ili više Product stavki checkouta.",
                    new { productId = required.Key, available = stock.Quantity, requested = required.Value });
        }

        foreach (CheckoutItem item in productItems)
        {
            ProductStock stock = locked[item.ProductId.GetValueOrDefault()];
            stock.Quantity -= item.Quantity;
            stock.UpdatedAt = now;
            await _productStockHandler.Update(uow, stock);

            await _stockMovementHandler.Add(uow, new StockMovement
            {
                Id = Guid.NewGuid(),
                OrganizationId = organizationId,
                ProductId = item.ProductId.GetValueOrDefault(),
                CompanyId = companyId,
                Type = StockMovementType.Sale,
                QuantityDelta = -item.Quantity,
                CheckoutItemId = item.Id,
                CreatedAt = now,
                CreatedBy = userId
            });
        }
    }

    private static ProductStockDto ToDto(ProductStock stock)
    {
        return ToDto(stock, stock.Product?.Name, stock.Company?.Name);
    }

    private static ProductStockDto ToDto(ProductStock stock, string productName, string companyName)
    {
        return new ProductStockDto
        {
            ProductId = stock.ProductId,
            ProductName = productName,
            CompanyId = stock.CompanyId,
            CompanyName = companyName,
            Quantity = stock.Quantity,
            UpdatedAt = stock.UpdatedAt
        };
    }

    private static StockMovementDto ToDto(StockMovement movement)
    {
        return new StockMovementDto
        {
            Id = movement.Id.GetValueOrDefault(),
            ProductId = movement.ProductId,
            CompanyId = movement.CompanyId,
            CompanyName = movement.Company?.Name,
            Type = movement.Type,
            QuantityDelta = movement.QuantityDelta,
            Reason = movement.Reason,
            CheckoutItemId = movement.CheckoutItemId,
            RelatedCompanyId = movement.RelatedCompanyId,
            RelatedCompanyName = movement.RelatedCompany?.Name,
            TransferCorrelationId = movement.TransferCorrelationId,
            CreatedAt = movement.CreatedAt,
            CreatedBy = movement.CreatedBy
        };
    }
}
