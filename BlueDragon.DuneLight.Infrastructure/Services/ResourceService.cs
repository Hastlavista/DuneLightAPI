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
using BlueDragon.DuneLight.Infrastructure.UnitOfWork;
using BlueDragon.DuneLight.Infrastructure.Utils;

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
    private readonly ISchedulingOccupancyHandler _schedulingOccupancyHandler;
    private readonly IUnitOfWorkFactory _unitOfWorkFactory;
    private readonly TimeProvider _timeProvider;

    public ResourceService(
        IResourceHandler resourceHandler, ICompanyHandler companyHandler, ISchedulingOccupancyHandler schedulingOccupancyHandler,
        IUnitOfWorkFactory unitOfWorkFactory, TimeProvider timeProvider)
    {
        _resourceHandler = resourceHandler;
        _companyHandler = companyHandler;
        _schedulingOccupancyHandler = schedulingOccupancyHandler;
        _unitOfWorkFactory = unitOfWorkFactory;
        _timeProvider = timeProvider;
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
            CreatedAt = _timeProvider.GetUtcNow(),
            CreatedBy = userId
        };

        await _resourceHandler.Add(resource);
        return await GetById(organizationId, resource.Id.GetValueOrDefault());
    }

    public async Task<ResourceDto> Update(Guid organizationId, Guid userId, Guid id, ResourceUpdateRequest request)
    {
        Resource existing = await _resourceHandler.GetById(organizationId, id);
        if (existing == null)
            throw new NotFoundAppException("Resource", id);

        string name = request.Name?.Trim();
        EnsureCapacityIsValid(request.Capacity);
        await EnsureNameIsUnique(organizationId, existing.CompanyId, name, excludeId: id);

        // Phase M1D.1: kapacitet je TVRDA invarijanta zakazivanja — izmjena uzima ISTI lock subjekta rasporeda kao upisi
        // zakazivanja (SchedulingLockOrder; jedini lock ove transakcije), tek zatim čita trenutni kapacitet i (kod smanjenja)
        // vršnu tekuću/buduću zauzetost; commit dok je lock još držan. Povećanje je uvijek dopušteno.
        await using (IUnitOfWork uow = await _unitOfWorkFactory.Begin())
        {
            await _schedulingOccupancyHandler.LockSchedulingSubjects(uow, null, null, null, new[] { id });
            Resource resource = await _resourceHandler.GetForCapacityChange(uow, organizationId, id);
            if (resource == null)
                throw new NotFoundAppException("Resource", id);

            await CapacityChangeGuard.EnsureResourceCapacityChange(
                _schedulingOccupancyHandler, uow, organizationId, id, resource.Name, resource.Capacity, request.Capacity, _timeProvider.GetUtcNow());

            // Id, OrganizationId i CompanyId se namjerno ne diraju — Resource nikad ne mijenja poslovnicu.
            resource.Name = name;
            resource.Capacity = request.Capacity;
            resource.Note = request.Note;
            resource.SortOrder = request.SortOrder;
            resource.UpdatedAt = _timeProvider.GetUtcNow();
            resource.UpdatedBy = userId;
            await uow.CommitAsync();
        }

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
        resource.UpdatedAt = _timeProvider.GetUtcNow();
        resource.UpdatedBy = userId;
        await _resourceHandler.Update(resource);

        return ToDto(resource);
    }

    /// <summary>Trajno brisanje, kao Room.Delete: resurs koji zauzima barem jedan segment termina se ne može obrisati
    /// (REFERENCED_CANNOT_DELETE) — deaktivirati umjesto toga.</summary>
    public async Task Delete(Guid organizationId, Guid id)
    {
        Resource resource = await _resourceHandler.GetById(organizationId, id);
        if (resource == null)
            throw new NotFoundAppException("Resource", id);

        bool isReferenced = await _resourceHandler.IsReferenced(organizationId, id);
        if (isReferenced)
            throw new BusinessRuleException(ErrorCodes.ReferencedCannotDelete, "Resurs je korišten na terminu i ne može se trajno obrisati — deaktivirajte ga umjesto toga.");

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
