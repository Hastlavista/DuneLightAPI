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
using ServiceEntity = BlueDragon.DuneLight.Infrastructure.Domain.Models.Catalog.Service;

namespace BlueDragon.DuneLight.Infrastructure.Services;

public class ServiceCatalogService : IServiceCatalogService
{
    private readonly IServiceHandler _serviceHandler;
    private readonly IServiceCategoryHandler _categoryHandler;

    public ServiceCatalogService(IServiceHandler serviceHandler, IServiceCategoryHandler categoryHandler)
    {
        _serviceHandler = serviceHandler;
        _categoryHandler = categoryHandler;
    }

    public async Task<PagedResult<ServiceDto>> GetPaged(Guid organizationId, PagedRequest request, Guid? serviceCategoryId)
    {
        (List<ServiceEntity> items, int totalCount) = await _serviceHandler.GetPaged(organizationId, request, serviceCategoryId);
        return PagedResult<ServiceDto>.Create(items.Select(ToDto).ToList(), totalCount, request.Page, request.PageSize);
    }

    public async Task<ServiceDto> GetById(Guid organizationId, Guid id)
    {
        ServiceEntity service = await _serviceHandler.GetById(organizationId, id);
        if (service == null)
            throw new NotFoundAppException("Service", id);

        return ToDto(service);
    }

    public async Task<ServiceDto> Create(Guid organizationId, Guid userId, ServiceCreateRequest request)
    {
        await EnsureNameIsUnique(organizationId, request.Name, excludeId: null);
        await EnsureCategoryIsUsable(organizationId, request.ServiceCategoryId);

        ServiceEntity service = new ServiceEntity
        {
            Id = Guid.NewGuid(),
            OrganizationId = organizationId,
            Name = request.Name,
            ServiceCategoryId = request.ServiceCategoryId,
            DefaultDurationMinutes = request.DefaultDurationMinutes,
            DefaultPrice = request.DefaultPrice,
            Description = request.Description,
            SortOrder = request.SortOrder,
            IsActive = true,
            CreatedAt = DateTimeOffset.UtcNow,
            CreatedBy = userId
        };

        await _serviceHandler.Add(service);
        return await GetById(organizationId, service.Id.GetValueOrDefault());
    }

    public async Task<ServiceDto> Update(Guid organizationId, Guid userId, Guid id, ServiceUpdateRequest request)
    {
        ServiceEntity service = await _serviceHandler.GetById(organizationId, id);
        if (service == null)
            throw new NotFoundAppException("Service", id);

        await EnsureNameIsUnique(organizationId, request.Name, excludeId: id);
        await EnsureCategoryIsUsable(organizationId, request.ServiceCategoryId);

        service.Name = request.Name;
        service.ServiceCategoryId = request.ServiceCategoryId;
        service.DefaultDurationMinutes = request.DefaultDurationMinutes;
        service.DefaultPrice = request.DefaultPrice;
        service.Description = request.Description;
        service.SortOrder = request.SortOrder;
        service.UpdatedAt = DateTimeOffset.UtcNow;
        service.UpdatedBy = userId;

        await _serviceHandler.Update(service);
        return await GetById(organizationId, id);
    }

    public async Task<ServiceDto> SetActive(Guid organizationId, Guid userId, Guid id, bool isActive)
    {
        ServiceEntity service = await _serviceHandler.GetById(organizationId, id);
        if (service == null)
            throw new NotFoundAppException("Service", id);

        if (isActive)
            await EnsureNameIsUnique(organizationId, service.Name, excludeId: id);

        service.IsActive = isActive;
        service.UpdatedAt = DateTimeOffset.UtcNow;
        service.UpdatedBy = userId;

        await _serviceHandler.Update(service);
        return await GetById(organizationId, id);
    }

    public async Task Delete(Guid organizationId, Guid id)
    {
        ServiceEntity service = await _serviceHandler.GetById(organizationId, id);
        if (service == null)
            throw new NotFoundAppException("Service", id);

        bool isReferenced = await _serviceHandler.IsReferenced(organizationId, id);
        if (isReferenced)
            throw new BusinessRuleException(ErrorCodes.ReferencedCannotDelete, "Usluga je korištena u cjeniku ili paketu i ne može se trajno obrisati — deaktivirajte je umjesto toga.");

        await _serviceHandler.Delete(service);
    }

    private async Task EnsureNameIsUnique(Guid organizationId, string name, Guid? excludeId)
    {
        bool exists = await _serviceHandler.NameExistsAmongActive(organizationId, name, excludeId);
        if (exists)
            throw new BusinessRuleException(ErrorCodes.DuplicateName, $"Aktivna usluga s nazivom '{name}' već postoji.");
    }

    private async Task EnsureCategoryIsUsable(Guid organizationId, Guid serviceCategoryId)
    {
        ServiceCategory category = await _categoryHandler.GetById(organizationId, serviceCategoryId);
        if (category == null)
            throw new NotFoundAppException("ServiceCategory", serviceCategoryId);

        if (!category.IsActive)
            throw new ValidationAppException("Odabrana kategorija usluge nije aktivna.");
    }

    private static ServiceDto ToDto(ServiceEntity service)
    {
        return new ServiceDto
        {
            Id = service.Id.GetValueOrDefault(),
            Name = service.Name,
            ServiceCategoryId = service.ServiceCategoryId,
            ServiceCategoryName = service.ServiceCategory?.Name,
            DefaultDurationMinutes = service.DefaultDurationMinutes,
            DefaultPrice = service.DefaultPrice,
            Description = service.Description,
            IsActive = service.IsActive,
            SortOrder = service.SortOrder,
            CreatedAt = service.CreatedAt,
            CreatedBy = service.CreatedBy,
            UpdatedAt = service.UpdatedAt,
            UpdatedBy = service.UpdatedBy
        };
    }
}
