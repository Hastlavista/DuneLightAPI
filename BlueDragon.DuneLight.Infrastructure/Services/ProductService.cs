using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using BlueDragon.DuneLight.Core.DTOs.Products;
using BlueDragon.DuneLight.Core.Interfaces.Products;
using BlueDragon.DuneLight.Core.Shared;
using BlueDragon.DuneLight.Core.Shared.Exceptions;
using BlueDragon.DuneLight.Infrastructure.Handlers.Interfaces;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using ProductEntity = BlueDragon.DuneLight.Infrastructure.Domain.Models.Products.Product;

namespace BlueDragon.DuneLight.Infrastructure.Services;

/// <summary>Vidi IProductService za domensku napomenu.</summary>
public class ProductService : IProductService
{
    private readonly IProductHandler _productHandler;

    public ProductService(IProductHandler productHandler)
    {
        _productHandler = productHandler;
    }

    public async Task<PagedResult<ProductDto>> GetPaged(Guid organizationId, PagedRequest request)
    {
        (List<ProductEntity> items, int totalCount) = await _productHandler.GetPaged(organizationId, request);
        return PagedResult<ProductDto>.Create(items.Select(ToDto).ToList(), totalCount, request.Page, request.PageSize);
    }

    public async Task<ProductDto> GetById(Guid organizationId, Guid id)
    {
        ProductEntity product = await _productHandler.GetById(organizationId, id);
        if (product == null)
            throw new NotFoundAppException("Product", id);

        return ToDto(product);
    }

    public async Task<ProductDto> Create(Guid organizationId, Guid userId, ProductCreateRequest request)
    {
        string name = request.Name?.Trim();
        string sku = NormalizeSku(request.Sku);

        await EnsureNameIsUnique(organizationId, name, excludeId: null);
        await EnsureSkuIsUnique(organizationId, sku, excludeId: null);

        ProductEntity product = new ProductEntity
        {
            Id = Guid.NewGuid(),
            OrganizationId = organizationId,
            Name = name,
            Description = request.Description,
            Sku = sku,
            DefaultPrice = request.DefaultPrice,
            IsActive = true,
            CreatedAt = DateTimeOffset.UtcNow,
            CreatedBy = userId
        };

        await _productHandler.Add(product);
        return await GetById(organizationId, product.Id.GetValueOrDefault());
    }

    public async Task<ProductDto> Update(Guid organizationId, Guid userId, Guid id, ProductUpdateRequest request)
    {
        ProductEntity product = await _productHandler.GetById(organizationId, id);
        if (product == null)
            throw new NotFoundAppException("Product", id);

        string name = request.Name?.Trim();
        string sku = NormalizeSku(request.Sku);

        await EnsureNameIsUnique(organizationId, name, excludeId: id);
        await EnsureSkuIsUnique(organizationId, sku, excludeId: id);

        // Id i OrganizationId se namjerno ne diraju — Product nikad ne mijenja vlasničku organizaciju.
        product.Name = name;
        product.Description = request.Description;
        product.Sku = sku;
        product.DefaultPrice = request.DefaultPrice;
        product.UpdatedAt = DateTimeOffset.UtcNow;
        product.UpdatedBy = userId;

        await _productHandler.Update(product);
        return await GetById(organizationId, id);
    }

    public async Task<ProductDto> SetActive(Guid organizationId, Guid userId, Guid id, bool isActive)
    {
        ProductEntity product = await _productHandler.GetById(organizationId, id);
        if (product == null)
            throw new NotFoundAppException("Product", id);

        if (isActive)
            await EnsureNameIsUnique(organizationId, product.Name, excludeId: id);

        product.IsActive = isActive;
        product.UpdatedAt = DateTimeOffset.UtcNow;
        product.UpdatedBy = userId;

        await _productHandler.Update(product);
        return await GetById(organizationId, id);
    }

    public async Task Delete(Guid organizationId, Guid id)
    {
        ProductEntity product = await _productHandler.GetById(organizationId, id);
        if (product == null)
            throw new NotFoundAppException("Product", id);

        bool isReferenced = await _productHandler.IsReferenced(organizationId, id);
        if (isReferenced)
            throw new BusinessRuleException(
                ErrorCodes.ReferencedCannotDelete,
                "Proizvod ima zalihu ili prodajnu povijest i ne može se trajno obrisati — deaktivirajte ga umjesto toga.");

        try
        {
            await _productHandler.Delete(product);
        }
        catch (DbUpdateException ex) when (IsForeignKeyViolation(ex))
        {
            // IsReferenced je provjeren gore, ali dopušta uski vremenski prozor (TOCTOU) u kojem se referenca
            // može stvoriti između provjere i stvarnog DELETE-a (npr. istovremeni StockMovement/CheckoutItem).
            // FK constraint je konačna linija obrane — ovdje se hvata SAMO Postgres foreign_key_violation
            // (SqlState 23503), ne bilo koji DbUpdateException, da nepovezani DB kvarovi i dalje isplivaju
            // normalno (vidi spec fix section 5).
            throw new BusinessRuleException(
                ErrorCodes.ReferencedCannotDelete,
                "Proizvod ima zalihu ili prodajnu povijest i ne može se trajno obrisati — deaktivirajte ga umjesto toga.");
        }
    }

    private static bool IsForeignKeyViolation(DbUpdateException ex)
    {
        return ex.InnerException is PostgresException pgEx && pgEx.SqlState == PostgresErrorCodes.ForeignKeyViolation;
    }

    private async Task EnsureNameIsUnique(Guid organizationId, string name, Guid? excludeId)
    {
        bool exists = await _productHandler.NameExistsAmongActive(organizationId, name, excludeId);
        if (exists)
            throw new BusinessRuleException(ErrorCodes.DuplicateName, $"Aktivan proizvod s nazivom '{name}' već postoji.");
    }

    private async Task EnsureSkuIsUnique(Guid organizationId, string sku, Guid? excludeId)
    {
        if (sku == null)
            return;

        bool exists = await _productHandler.SkuExists(organizationId, sku, excludeId);
        if (exists)
            throw new BusinessRuleException(ErrorCodes.DuplicateSku, $"Proizvod sa SKU '{sku}' već postoji.");
    }

    private static string NormalizeSku(string sku)
    {
        return string.IsNullOrWhiteSpace(sku) ? null : sku.Trim();
    }

    private static ProductDto ToDto(ProductEntity product)
    {
        return new ProductDto
        {
            Id = product.Id.GetValueOrDefault(),
            Name = product.Name,
            Description = product.Description,
            Sku = product.Sku,
            DefaultPrice = product.DefaultPrice,
            IsActive = product.IsActive,
            CreatedAt = product.CreatedAt,
            CreatedBy = product.CreatedBy,
            UpdatedAt = product.UpdatedAt,
            UpdatedBy = product.UpdatedBy
        };
    }
}
