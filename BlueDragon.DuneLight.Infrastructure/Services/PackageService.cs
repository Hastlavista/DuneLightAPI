using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using BlueDragon.DuneLight.Core.DTOs.Catalog;
using BlueDragon.DuneLight.Core.Enums;
using BlueDragon.DuneLight.Core.Interfaces.Catalog;
using BlueDragon.DuneLight.Core.Shared;
using BlueDragon.DuneLight.Core.Shared.Exceptions;
using BlueDragon.DuneLight.Infrastructure.Domain.Models.Catalog;
using BlueDragon.DuneLight.Infrastructure.Handlers.Interfaces;

namespace BlueDragon.DuneLight.Infrastructure.Services;

public class PackageService : IPackageService
{
    private readonly IPackageHandler _packageHandler;
    private readonly IServiceHandler _serviceHandler;

    public PackageService(IPackageHandler packageHandler, IServiceHandler serviceHandler)
    {
        _packageHandler = packageHandler;
        _serviceHandler = serviceHandler;
    }

    public async Task<PagedResult<PackageDto>> GetPaged(Guid organizationId, PagedRequest request)
    {
        (List<Package> items, int totalCount) = await _packageHandler.GetPaged(organizationId, request);
        return PagedResult<PackageDto>.Create(items.Select(ToDto).ToList(), totalCount, request.Page, request.PageSize);
    }

    public async Task<PackageDto> GetById(Guid organizationId, Guid id)
    {
        Package package = await _packageHandler.GetById(organizationId, id);
        if (package == null)
            throw new NotFoundAppException("Package", id);

        return ToDto(package);
    }

    public async Task<PackageDto> Create(Guid organizationId, Guid userId, PackageCreateRequest request)
    {
        await ValidateServiceItems(organizationId, request.EntryMode, request.Services, grandfatheredServiceIds: null);
        ValidateValidity(request.ValidityType, request.ValidityDays, request.ValidityFixedDate);

        Package package = new Package
        {
            Id = Guid.NewGuid(),
            OrganizationId = organizationId,
            Name = request.Name,
            Description = request.Description,
            EntryMode = request.EntryMode,
            TotalEntryCount = request.EntryMode == PackageEntryMode.SharedPool ? request.TotalEntryCount : null,
            ValidityType = request.ValidityType,
            ValidityDays = request.ValidityType == PackageValidityType.DayCount ? request.ValidityDays : null,
            ValidityFixedDate = request.ValidityType == PackageValidityType.FixedDate ? request.ValidityFixedDate : null,
            DefaultPrice = request.DefaultPrice,
            SortOrder = request.SortOrder,
            IsActive = true,
            CreatedAt = DateTimeOffset.UtcNow,
            CreatedBy = userId
        };

        List<PackageServiceItem> serviceItems = BuildServiceItems(request.EntryMode, request.Services);

        package.Services = serviceItems;
        await _packageHandler.Add(package);
        return await GetById(organizationId, package.Id.GetValueOrDefault());
    }

    public async Task<PackageDto> Update(Guid organizationId, Guid userId, Guid id, PackageUpdateRequest request)
    {
        Package package = await _packageHandler.GetById(organizationId, id);
        if (package == null)
            throw new NotFoundAppException("Package", id);

        HashSet<Guid> grandfatheredServiceIds = package.Services.Select(s => s.ServiceId).ToHashSet();
        await ValidateServiceItems(organizationId, request.EntryMode, request.Services, grandfatheredServiceIds);
        ValidateValidity(request.ValidityType, request.ValidityDays, request.ValidityFixedDate);

        package.Name = request.Name;
        package.Description = request.Description;
        package.EntryMode = request.EntryMode;
        package.TotalEntryCount = request.EntryMode == PackageEntryMode.SharedPool ? request.TotalEntryCount : null;
        package.ValidityType = request.ValidityType;
        package.ValidityDays = request.ValidityType == PackageValidityType.DayCount ? request.ValidityDays : null;
        package.ValidityFixedDate = request.ValidityType == PackageValidityType.FixedDate ? request.ValidityFixedDate : null;
        package.DefaultPrice = request.DefaultPrice;
        package.SortOrder = request.SortOrder;
        package.UpdatedAt = DateTimeOffset.UtcNow;
        package.UpdatedBy = userId;

        List<PackageServiceItem> serviceItems = BuildServiceItems(request.EntryMode, request.Services);

        await _packageHandler.Update(package, serviceItems);
        return await GetById(organizationId, id);
    }

    public async Task<PackageDto> SetActive(Guid organizationId, Guid userId, Guid id, bool isActive)
    {
        Package package = await _packageHandler.GetById(organizationId, id);
        if (package == null)
            throw new NotFoundAppException("Package", id);

        package.IsActive = isActive;
        package.UpdatedAt = DateTimeOffset.UtcNow;
        package.UpdatedBy = userId;

        await _packageHandler.Update(package, package.Services);
        return await GetById(organizationId, id);
    }

    public async Task Delete(Guid organizationId, Guid id)
    {
        Package package = await _packageHandler.GetById(organizationId, id);
        if (package == null)
            throw new NotFoundAppException("Package", id);

        bool isReferenced = await _packageHandler.IsReferenced(organizationId, id);
        if (isReferenced)
            throw new BusinessRuleException(ErrorCodes.ReferencedCannotDelete, "Paket je korišten u cjeniku i ne može se trajno obrisati — deaktivirajte ga umjesto toga.");

        await _packageHandler.Delete(package);
    }

    private async Task ValidateServiceItems(Guid organizationId, PackageEntryMode entryMode, List<PackageServiceItemRequest> services, HashSet<Guid> grandfatheredServiceIds)
    {
        if (services == null || services.Count == 0)
            throw new ValidationAppException("Paket mora imati barem jednu uslugu.");

        if (services.Select(s => s.ServiceId).Distinct().Count() != services.Count)
            throw new ValidationAppException("Ista usluga ne smije biti navedena dva puta u istom paketu.");

        if (entryMode == PackageEntryMode.PerService && services.Any(s => !s.EntryCount.HasValue || s.EntryCount <= 0))
            throw new ValidationAppException("Kod PerService načina svaka stavka paketa mora imati definiran broj ulazaka veći od nule.");

        List<Guid> serviceIds = services.Select(s => s.ServiceId).ToList();
        Dictionary<Guid, Domain.Models.Catalog.Service> servicesById = (await _serviceHandler.GetByIds(organizationId, serviceIds))
            .ToDictionary(s => s.Id.GetValueOrDefault());

        foreach (PackageServiceItemRequest item in services)
        {
            if (!servicesById.TryGetValue(item.ServiceId, out Domain.Models.Catalog.Service service))
                throw new NotFoundAppException("Service", item.ServiceId);

            bool isGrandfathered = grandfatheredServiceIds != null && grandfatheredServiceIds.Contains(item.ServiceId);
            if (!service.IsActive && !isGrandfathered)
                throw new ValidationAppException($"Odabrana usluga '{service.Name}' nije aktivna.");
        }
    }

    private static void ValidateValidity(PackageValidityType validityType, int? validityDays, DateTimeOffset? validityFixedDate)
    {
        switch (validityType)
        {
            case PackageValidityType.DayCount when !validityDays.HasValue || validityDays <= 0:
                throw new ValidationAppException("Kod DayCount načina valjanosti broj dana je obavezan i mora biti veći od nule.");
            case PackageValidityType.FixedDate when !validityFixedDate.HasValue:
                throw new ValidationAppException("Kod FixedDate načina valjanosti datum isteka je obavezan.");
        }
    }

    private static List<PackageServiceItem> BuildServiceItems(PackageEntryMode entryMode, List<PackageServiceItemRequest> services)
    {
        return services.Select(s => new PackageServiceItem
        {
            Id = Guid.NewGuid(),
            ServiceId = s.ServiceId,
            EntryCount = entryMode == PackageEntryMode.PerService ? s.EntryCount : null
        }).ToList();
    }

    private static PackageDto ToDto(Package package)
    {
        return new PackageDto
        {
            Id = package.Id.GetValueOrDefault(),
            Name = package.Name,
            Description = package.Description,
            EntryMode = package.EntryMode,
            TotalEntryCount = package.TotalEntryCount,
            ValidityType = package.ValidityType,
            ValidityDays = package.ValidityDays,
            ValidityFixedDate = package.ValidityFixedDate,
            DefaultPrice = package.DefaultPrice,
            IsActive = package.IsActive,
            SortOrder = package.SortOrder,
            Services = package.Services.Select(s => new PackageServiceItemDto
            {
                ServiceId = s.ServiceId,
                ServiceName = s.Service?.Name,
                EntryCount = s.EntryCount
            }).ToList(),
            CreatedAt = package.CreatedAt,
            CreatedBy = package.CreatedBy,
            UpdatedAt = package.UpdatedAt,
            UpdatedBy = package.UpdatedBy
        };
    }
}
