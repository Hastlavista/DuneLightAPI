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
using BlueDragon.DuneLight.Infrastructure.Utils;
using ServiceEntity = BlueDragon.DuneLight.Infrastructure.Domain.Models.Catalog.Service;

namespace BlueDragon.DuneLight.Infrastructure.Services;

public class PricingService : IPricingService
{
    private readonly IPriceListItemHandler _priceListItemHandler;
    private readonly IServiceHandler _serviceHandler;
    private readonly IPackageHandler _packageHandler;
    private readonly ICompanyHandler _companyHandler;
    private readonly IPriceResolutionService _priceResolutionService;

    public PricingService(
        IPriceListItemHandler priceListItemHandler,
        IServiceHandler serviceHandler,
        IPackageHandler packageHandler,
        ICompanyHandler companyHandler,
        IPriceResolutionService priceResolutionService)
    {
        _priceListItemHandler = priceListItemHandler;
        _serviceHandler = serviceHandler;
        _packageHandler = packageHandler;
        _companyHandler = companyHandler;
        _priceResolutionService = priceResolutionService;
    }

    public async Task<PagedResult<PriceListItemDto>> GetPaged(
        Guid organizationId, PagedRequest request, Guid? companyId, PricingSubjectType? subjectType)
    {
        (List<PriceListItem> items, int totalCount) = await _priceListItemHandler.GetPaged(organizationId, request, companyId, subjectType);
        return PagedResult<PriceListItemDto>.Create(items.Select(ToDto).ToList(), totalCount, request.Page, request.PageSize);
    }

    public async Task<PriceListItemDto> GetById(Guid organizationId, Guid id)
    {
        PriceListItem item = await _priceListItemHandler.GetById(organizationId, id);
        if (item == null)
            throw new NotFoundAppException("PriceListItem", id);

        return ToDto(item);
    }

    public async Task<PriceListItemDto> Create(Guid organizationId, Guid userId, PriceListItemCreateRequest request)
    {
        Guid subjectId = ValidateSubject(request.SubjectType, request.ServiceId, request.PackageId);
        await EnsureSubjectExists(organizationId, request.SubjectType, subjectId);
        await EnsureCompanyExists(organizationId, request.CompanyId);
        ValidateDateRange(request.ValidFrom, request.ValidTo);

        await EnsureNoOverlap(organizationId, request.SubjectType, subjectId, request.CompanyId, request.ValidFrom, request.ValidTo, excludeId: null);

        PriceListItem item = new PriceListItem
        {
            Id = Guid.NewGuid(),
            OrganizationId = organizationId,
            ServiceId = request.SubjectType == PricingSubjectType.Service ? subjectId : null,
            PackageId = request.SubjectType == PricingSubjectType.Package ? subjectId : null,
            CompanyId = request.CompanyId,
            Price = request.Price,
            ValidFrom = request.ValidFrom,
            ValidTo = request.ValidTo,
            IsActive = true,
            CreatedAt = DateTimeOffset.UtcNow,
            CreatedBy = userId
        };

        await _priceListItemHandler.Add(item);
        return await GetById(organizationId, item.Id.GetValueOrDefault());
    }

    public async Task<PriceListItemDto> Update(Guid organizationId, Guid userId, Guid id, PriceListItemUpdateRequest request)
    {
        PriceListItem item = await _priceListItemHandler.GetById(organizationId, id);
        if (item == null)
            throw new NotFoundAppException("PriceListItem", id);

        ValidateDateRange(request.ValidFrom, request.ValidTo);

        PricingSubjectType subjectType = item.ServiceId != null ? PricingSubjectType.Service : PricingSubjectType.Package;
        Guid subjectId = item.ServiceId ?? item.PackageId!.Value;

        if (item.IsActive)
            await EnsureNoOverlap(organizationId, subjectType, subjectId, item.CompanyId, request.ValidFrom, request.ValidTo, excludeId: id);

        if (item.Price != request.Price)
        {
            await _priceListItemHandler.AddHistory(new PriceListItemHistory
            {
                Id = Guid.NewGuid(),
                PriceListItemId = id,
                OldPrice = item.Price,
                NewPrice = request.Price,
                ChangedAt = DateTimeOffset.UtcNow,
                ChangedBy = userId
            });
        }

        item.Price = request.Price;
        item.ValidFrom = request.ValidFrom;
        item.ValidTo = request.ValidTo;
        item.UpdatedAt = DateTimeOffset.UtcNow;
        item.UpdatedBy = userId;

        await _priceListItemHandler.Update(item);
        return await GetById(organizationId, id);
    }

    public async Task<PriceListItemDto> SetActive(Guid organizationId, Guid userId, Guid id, bool isActive)
    {
        PriceListItem item = await _priceListItemHandler.GetById(organizationId, id);
        if (item == null)
            throw new NotFoundAppException("PriceListItem", id);

        if (isActive && !item.IsActive)
        {
            PricingSubjectType subjectType = item.ServiceId != null ? PricingSubjectType.Service : PricingSubjectType.Package;
            Guid subjectId = item.ServiceId ?? item.PackageId!.Value;
            await EnsureNoOverlap(organizationId, subjectType, subjectId, item.CompanyId, item.ValidFrom, item.ValidTo, excludeId: id);
        }

        item.IsActive = isActive;
        item.UpdatedAt = DateTimeOffset.UtcNow;
        item.UpdatedBy = userId;

        await _priceListItemHandler.Update(item);
        return await GetById(organizationId, id);
    }

    public async Task Delete(Guid organizationId, Guid id)
    {
        PriceListItem item = await _priceListItemHandler.GetById(organizationId, id);
        if (item == null)
            throw new NotFoundAppException("PriceListItem", id);

        bool hasHistory = await _priceListItemHandler.HasHistory(id);
        if (hasHistory)
            throw new BusinessRuleException(ErrorCodes.ReferencedCannotDelete, "Stavka cjenika ima zabilježenu povijest promjena cijene i ne može se trajno obrisati — deaktivirajte je umjesto toga.");

        await _priceListItemHandler.Delete(item);
    }

    public async Task<List<EffectivePriceDto>> GetEffectivePriceList(Guid organizationId, Guid? companyId, DateTimeOffset date)
    {
        List<ServiceEntity> services = await _serviceHandler.GetAllActive(organizationId);
        List<Package> packages = await _packageHandler.GetAllActive(organizationId);
        List<PriceListItem> priceItems = await _priceListItemHandler.GetActiveForCompany(organizationId, companyId, date);

        List<EffectivePriceDto> result = new List<EffectivePriceDto>();

        foreach (ServiceEntity service in services)
        {
            List<PriceCandidate> candidates = priceItems
                .Where(p => p.ServiceId == service.Id)
                .Select(ToCandidate)
                .ToList();
            ResolvedPrice resolved = _priceResolutionService.Resolve(candidates, service.DefaultPrice, companyId, date);

            result.Add(new EffectivePriceDto
            {
                SubjectType = PricingSubjectType.Service,
                SubjectId = service.Id.GetValueOrDefault(),
                SubjectName = service.Name,
                Price = resolved.Price,
                Source = resolved.Source
            });
        }

        foreach (Package package in packages)
        {
            List<PriceCandidate> candidates = priceItems
                .Where(p => p.PackageId == package.Id)
                .Select(ToCandidate)
                .ToList();
            ResolvedPrice resolved = _priceResolutionService.Resolve(candidates, package.DefaultPrice, companyId, date);

            result.Add(new EffectivePriceDto
            {
                SubjectType = PricingSubjectType.Package,
                SubjectId = package.Id.GetValueOrDefault(),
                SubjectName = package.Name,
                Price = resolved.Price,
                Source = resolved.Source
            });
        }

        return result.OrderBy(r => r.SubjectType).ThenBy(r => r.SubjectName).ToList();
    }

    public async Task<ResolvePriceResponse> ResolvePrice(Guid organizationId, ResolvePriceRequest request)
    {
        DateTimeOffset date = request.Date ?? DateTimeOffset.UtcNow;
        decimal defaultPrice = await GetDefaultPrice(organizationId, request.SubjectType, request.SubjectId);

        List<PriceListItem> candidates = await _priceListItemHandler.GetActiveCandidates(
            organizationId, request.SubjectType, request.SubjectId, request.CompanyId);

        ResolvedPrice resolved = _priceResolutionService.Resolve(
            candidates.Select(ToCandidate), defaultPrice, request.CompanyId, date);

        return new ResolvePriceResponse
        {
            SubjectType = request.SubjectType,
            SubjectId = request.SubjectId,
            CompanyId = request.CompanyId,
            Date = date,
            Price = resolved.Price,
            Source = resolved.Source
        };
    }

    private async Task<decimal> GetDefaultPrice(Guid organizationId, PricingSubjectType subjectType, Guid subjectId)
    {
        if (subjectType == PricingSubjectType.Service)
        {
            ServiceEntity service = await _serviceHandler.GetById(organizationId, subjectId);
            if (service == null)
                throw new NotFoundAppException("Service", subjectId);

            return service.DefaultPrice;
        }

        Package package = await _packageHandler.GetById(organizationId, subjectId);
        if (package == null)
            throw new NotFoundAppException("Package", subjectId);

        return package.DefaultPrice;
    }

    private async Task EnsureSubjectExists(Guid organizationId, PricingSubjectType subjectType, Guid subjectId)
    {
        await GetDefaultPrice(organizationId, subjectType, subjectId);
    }

    private async Task EnsureCompanyExists(Guid organizationId, Guid? companyId)
    {
        if (!companyId.HasValue)
            return;

        Company company = await _companyHandler.GetById(organizationId, companyId.Value);
        if (company == null)
            throw new NotFoundAppException("Company", companyId.Value);
    }

    private async Task EnsureNoOverlap(
        Guid organizationId, PricingSubjectType subjectType, Guid subjectId, Guid? companyId,
        DateTimeOffset validFrom, DateTimeOffset? validTo, Guid? excludeId)
    {
        List<PriceListItem> existing = await _priceListItemHandler.GetActiveForExactCompany(
            organizationId, subjectType, subjectId, companyId, excludeId);

        bool overlaps = existing.Any(e => DateRangeOverlap.Overlaps(validFrom, validTo, e.ValidFrom, e.ValidTo));
        if (overlaps)
            throw new BusinessRuleException(ErrorCodes.PriceOverlap, "Već postoji aktivna stavka cjenika za istu kombinaciju usluge/paketa i tvrtke koja se preklapa s odabranim razdobljem.");
    }

    private static void ValidateDateRange(DateTimeOffset validFrom, DateTimeOffset? validTo)
    {
        if (validTo.HasValue && validTo.Value < validFrom)
            throw new ValidationAppException("Datum 'vrijedi do' ne smije biti prije datuma 'vrijedi od'.");
    }

    private static Guid ValidateSubject(PricingSubjectType subjectType, Guid? serviceId, Guid? packageId)
    {
        if (subjectType == PricingSubjectType.Service)
        {
            if (!serviceId.HasValue || packageId.HasValue)
                throw new ValidationAppException("Kad je SubjectType = Service, ServiceId je obavezan, a PackageId mora biti prazan.");

            return serviceId.Value;
        }

        if (!packageId.HasValue || serviceId.HasValue)
            throw new ValidationAppException("Kad je SubjectType = Package, PackageId je obavezan, a ServiceId mora biti prazan.");

        return packageId.Value;
    }

    private static PriceCandidate ToCandidate(PriceListItem item)
    {
        return new PriceCandidate
        {
            CompanyId = item.CompanyId,
            Price = item.Price,
            ValidFrom = item.ValidFrom,
            ValidTo = item.ValidTo,
            IsActive = item.IsActive
        };
    }

    private static PriceListItemDto ToDto(PriceListItem item)
    {
        return new PriceListItemDto
        {
            Id = item.Id.GetValueOrDefault(),
            SubjectType = item.ServiceId != null ? PricingSubjectType.Service : PricingSubjectType.Package,
            ServiceId = item.ServiceId,
            ServiceName = item.Service?.Name,
            PackageId = item.PackageId,
            PackageName = item.Package?.Name,
            CompanyId = item.CompanyId,
            CompanyName = item.Company?.Name,
            Price = item.Price,
            ValidFrom = item.ValidFrom,
            ValidTo = item.ValidTo,
            IsActive = item.IsActive,
            CreatedAt = item.CreatedAt,
            CreatedBy = item.CreatedBy,
            UpdatedAt = item.UpdatedAt,
            UpdatedBy = item.UpdatedBy
        };
    }
}
