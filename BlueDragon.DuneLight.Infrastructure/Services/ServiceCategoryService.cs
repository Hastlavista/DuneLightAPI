using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using BlueDragon.DuneLight.Core.DTOs.Catalog;
using BlueDragon.DuneLight.Core.Interfaces.Catalog;
using BlueDragon.DuneLight.Core.Shared;
using BlueDragon.DuneLight.Core.Shared.Exceptions;
using BlueDragon.DuneLight.Infrastructure.Domain.Models.Catalog;
using BlueDragon.DuneLight.Infrastructure.Handlers.Interfaces;

namespace BlueDragon.DuneLight.Infrastructure.Services;

public class ServiceCategoryService : IServiceCategoryService
{
    private readonly IServiceCategoryHandler _categoryHandler;
    private readonly IServiceHandler _serviceHandler;

    public ServiceCategoryService(IServiceCategoryHandler categoryHandler, IServiceHandler serviceHandler)
    {
        _categoryHandler = categoryHandler;
        _serviceHandler = serviceHandler;
    }

    public async Task<PagedResult<ServiceCategoryDto>> GetPaged(Guid organizationId, PagedRequest request)
    {
        (List<ServiceCategory> items, int totalCount) = await _categoryHandler.GetPaged(organizationId, request);
        return PagedResult<ServiceCategoryDto>.Create(items.Select(ToDto).ToList(), totalCount, request.Page, request.PageSize);
    }

    public async Task<ServiceCategoryDto> GetById(Guid organizationId, Guid id)
    {
        ServiceCategory category = await _categoryHandler.GetById(organizationId, id);
        if (category == null)
            throw new NotFoundAppException("ServiceCategory", id);

        return ToDto(category);
    }

    public async Task<ServiceCategoryDto> Create(Guid organizationId, Guid userId, ServiceCategoryCreateRequest request)
    {
        await EnsureNameIsUnique(organizationId, request.Name, excludeId: null);

        ServiceCategory category = new ServiceCategory
        {
            Id = Guid.NewGuid(),
            OrganizationId = organizationId,
            Name = request.Name,
            ExecutionMode = request.ExecutionMode,
            ColorHex = request.ColorHex,
            RequiresClient = request.RequiresClient,
            CountsTowardRevenue = request.CountsTowardRevenue,
            Description = request.Description,
            SortOrder = request.SortOrder,
            IsActive = true,
            CreatedAt = DateTimeOffset.UtcNow,
            CreatedBy = userId
        };

        await _categoryHandler.Add(category);
        return ToDto(category);
    }

    public async Task<ServiceCategoryDto> Update(Guid organizationId, Guid userId, Guid id, ServiceCategoryUpdateRequest request)
    {
        ServiceCategory category = await _categoryHandler.GetById(organizationId, id);
        if (category == null)
            throw new NotFoundAppException("ServiceCategory", id);

        await EnsureNameIsUnique(organizationId, request.Name, excludeId: id);

        category.Name = request.Name;
        category.ExecutionMode = request.ExecutionMode;
        category.ColorHex = request.ColorHex;
        category.RequiresClient = request.RequiresClient;
        category.CountsTowardRevenue = request.CountsTowardRevenue;
        category.Description = request.Description;
        category.SortOrder = request.SortOrder;
        category.UpdatedAt = DateTimeOffset.UtcNow;
        category.UpdatedBy = userId;

        await _categoryHandler.Update(category);
        return ToDto(category);
    }

    public async Task<ServiceCategoryDto> SetActive(Guid organizationId, Guid userId, Guid id, bool isActive)
    {
        ServiceCategory category = await _categoryHandler.GetById(organizationId, id);
        if (category == null)
            throw new NotFoundAppException("ServiceCategory", id);

        if (!isActive && category.IsActive)
        {
            bool hasActiveServices = await _serviceHandler.HasActiveServicesInCategory(organizationId, id);
            if (hasActiveServices)
                throw new BusinessRuleException(ErrorCodes.CategoryInUse, "Kategorija se koristi na aktivnim uslugama i ne može se deaktivirati.");
        }

        if (isActive)
            await EnsureNameIsUnique(organizationId, category.Name, excludeId: id);

        category.IsActive = isActive;
        category.UpdatedAt = DateTimeOffset.UtcNow;
        category.UpdatedBy = userId;

        await _categoryHandler.Update(category);
        return ToDto(category);
    }

    public async Task Delete(Guid organizationId, Guid id)
    {
        ServiceCategory category = await _categoryHandler.GetById(organizationId, id);
        if (category == null)
            throw new NotFoundAppException("ServiceCategory", id);

        bool isReferenced = await _serviceHandler.IsCategoryReferenced(organizationId, id);
        if (isReferenced)
            throw new BusinessRuleException(ErrorCodes.ReferencedCannotDelete, "Kategoriju koristi barem jedna usluga i ne može se trajno obrisati — deaktivirajte je umjesto toga.");

        await _categoryHandler.Delete(category);
    }

    private async Task EnsureNameIsUnique(Guid organizationId, string name, Guid? excludeId)
    {
        bool exists = await _categoryHandler.NameExistsAmongActive(organizationId, name, excludeId);
        if (exists)
            throw new BusinessRuleException(ErrorCodes.DuplicateName, $"Aktivna kategorija s nazivom '{name}' već postoji.");
    }

    private static ServiceCategoryDto ToDto(ServiceCategory category)
    {
        return new ServiceCategoryDto
        {
            Id = category.Id.GetValueOrDefault(),
            Name = category.Name,
            ExecutionMode = category.ExecutionMode,
            ColorHex = category.ColorHex,
            RequiresClient = category.RequiresClient,
            CountsTowardRevenue = category.CountsTowardRevenue,
            Description = category.Description,
            IsActive = category.IsActive,
            SortOrder = category.SortOrder,
            CreatedAt = category.CreatedAt,
            CreatedBy = category.CreatedBy,
            UpdatedAt = category.UpdatedAt,
            UpdatedBy = category.UpdatedBy
        };
    }
}
