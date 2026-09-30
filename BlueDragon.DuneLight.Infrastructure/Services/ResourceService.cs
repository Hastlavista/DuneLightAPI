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

/// <summary>
/// Resource == konačan, višekratno upotrebljiv kapacitet unutar točno jedne Company (vidi Resource). Isti obrazac kao
/// RoomService: Company mora pripadati organizaciji i biti aktivna, CompanyId/OrganizationId se nakon kreiranja ne
/// mijenjaju, normalizirani naziv je jedinstven među aktivnim resursima iste poslovnice, active lifecycle bez pravila
/// "barem jedan aktivan". Zakazivanje resurse još ne koristi.
/// </summary>
public class ResourceService : IResourceService
{
    private readonly IResourceHandler _resourceHandler;
    private readonly ICompanyHandler _companyHandler;

    public ResourceService(IResourceHandler resourceHandler, ICompanyHandler companyHandler)
    {
        _resourceHandler = resourceHandler;
        _companyHandler = companyHandler;
    }

    public async Task<PagedResult<ResourceDto>> GetPaged(Guid organizationId, Guid? companyId, PagedRequest request)
    {
        (List<Resource> items, int totalCount) = await _resourceHandler.GetPaged(organizationId, companyId, request);
        return PagedResult<ResourceDto>.Create(items.Select(ToDto).ToList(), totalCount, request.Page, request.PageSize);
    }

    public async Task<ResourceDto> GetById(Guid organizationId, Guid id)
    {
        Resource resource = await _resourceHandler.GetById(organizationId, id);
        if (resource == null)
            throw new NotFoundAppException("Resource", id);

        return ToDto(resource);
    }

    public async Task<ResourceDto> Create(Guid organizationId, Guid userId, ResourceCreateRequest request)
    {
        string name = request.Name?.Trim();
        EnsureCapacityIsValid(request.Capacity);

        // Poslovnica se traži unutar organizacije pozivatelja — poslovnica druge organizacije je "nije pronađena".
        Company company = await _companyHandler.GetById(organizationId, request.CompanyId);
        if (company == null)
            throw new NotFoundAppException("Company", request.CompanyId);
        if (!company.IsActive)
            throw new ValidationAppException($"Poslovnica '{company.Name}' nije aktivna — novi resurs se ne može kreirati.");

        await EnsureNameIsUnique(organizationId, request.CompanyId, name, excludeId: null);

        Resource resource = new Resource
        {
            Id = Guid.NewGuid(),
            OrganizationId = organizationId,
            CompanyId = request.CompanyId,
            Name = name,
            Capacity = request.Capacity,
            Note = request.Note,
            SortOrder = request.SortOrder,
            IsActive = true,
            CreatedAt = DateTimeOffset.UtcNow,
            CreatedBy = userId
        };

        await _resourceHandler.Add(resource);
        return await GetById(organizationId, resource.Id.GetValueOrDefault());
    }

    public async Task<ResourceDto> Update(Guid organizationId, Guid userId, Guid id, ResourceUpdateRequest request)
    {
        Resource resource = await _resourceHandler.GetById(organizationId, id);
        if (resource == null)
            throw new NotFoundAppException("Resource", id);

        string name = request.Name?.Trim();
        EnsureCapacityIsValid(request.Capacity);
        await EnsureNameIsUnique(organizationId, resource.CompanyId, name, excludeId: id);

        // Id, OrganizationId i CompanyId se namjerno ne diraju — resurs nikad ne mijenja poslovnicu ni organizaciju.
        resource.Name = name;
        resource.Capacity = request.Capacity;
        resource.Note = request.Note;
        resource.SortOrder = request.SortOrder;
        resource.UpdatedAt = DateTimeOffset.UtcNow;
        resource.UpdatedBy = userId;

        await _resourceHandler.Update(resource);
        return await GetById(organizationId, id);
    }

    public async Task<ResourceDto> SetActive(Guid organizationId, Guid userId, Guid id, bool isActive)
    {
        Resource resource = await _resourceHandler.GetById(organizationId, id);
        if (resource == null)
            throw new NotFoundAppException("Resource", id);

        if (resource.IsActive == isActive)
            return ToDto(resource);

        if (isActive)
        {
            Company company = await _companyHandler.GetById(organizationId, resource.CompanyId);
            if (company == null || !company.IsActive)
                throw new ValidationAppException($"Poslovnica resursa '{resource.Name}' nije aktivna — resurs se ne može ponovno aktivirati.");

            // Naziv je mogao u međuvremenu "procuriti" na drugi aktivni resurs iste poslovnice (djelomični unique
            // indeks vrijedi samo WHERE is_active = true) — isti obrazac kao RoomService.Reactivate.
            await EnsureNameIsUnique(organizationId, resource.CompanyId, resource.Name, excludeId: id);
        }

        resource.IsActive = isActive;
        resource.UpdatedAt = DateTimeOffset.UtcNow;
        resource.UpdatedBy = userId;
        await _resourceHandler.Update(resource);

        return ToDto(resource);
    }

    /// <summary>Trajno brisanje, kao Room.Delete. Resurs trenutno nitko ne referencira; kad rezervacije resursa
    /// (segmenti termina) budu uvedene, ovdje dolazi provjera referenci (REFERENCED_CANNOT_DELETE) kao kod Room.</summary>
    public async Task Delete(Guid organizationId, Guid id)
    {
        Resource resource = await _resourceHandler.GetById(organizationId, id);
        if (resource == null)
            throw new NotFoundAppException("Resource", id);

        await _resourceHandler.Delete(resource);
    }

    private static void EnsureCapacityIsValid(int capacity)
    {
        if (!CatalogCapacity.IsValid(capacity))
            throw new ValidationAppException("Kapacitet resursa mora biti najmanje 1.");
    }

    private async Task EnsureNameIsUnique(Guid organizationId, Guid companyId, string name, Guid? excludeId)
    {
        bool exists = await _resourceHandler.NameExistsAmongActive(organizationId, companyId, name, excludeId);
        if (exists)
            throw new BusinessRuleException(ErrorCodes.DuplicateName, $"Aktivni resurs s nazivom '{name}' već postoji u ovoj poslovnici.");
    }

    private static ResourceDto ToDto(Resource resource)
    {
        return new ResourceDto
        {
            Id = resource.Id.GetValueOrDefault(),
            CompanyId = resource.CompanyId,
            CompanyName = resource.Company?.Name,
            Name = resource.Name,
            Capacity = resource.Capacity,
            IsActive = resource.IsActive,
            Note = resource.Note,
            SortOrder = resource.SortOrder,
            CreatedAt = resource.CreatedAt,
            CreatedBy = resource.CreatedBy,
            UpdatedAt = resource.UpdatedAt,
            UpdatedBy = resource.UpdatedBy
        };
    }
}
