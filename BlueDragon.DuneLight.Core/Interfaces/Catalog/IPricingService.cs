using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using BlueDragon.DuneLight.Core.DTOs.Catalog;
using BlueDragon.DuneLight.Core.Enums;
using BlueDragon.DuneLight.Core.Shared;

namespace BlueDragon.DuneLight.Core.Interfaces.Catalog;

public interface IPricingService
{
    Task<PagedResult<PriceListItemDto>> GetPaged(Guid organizationId, PagedRequest request, Guid? locationId, PricingSubjectType? subjectType);
    Task<PriceListItemDto> GetById(Guid organizationId, Guid id);
    Task<PriceListItemDto> Create(Guid organizationId, Guid userId, PriceListItemCreateRequest request);
    Task<PriceListItemDto> Update(Guid organizationId, Guid userId, Guid id, PriceListItemUpdateRequest request);
    Task<PriceListItemDto> SetActive(Guid organizationId, Guid userId, Guid id, bool isActive);
    Task Delete(Guid organizationId, Guid id);

    /// <summary>Trenutno važeći cjenik za lokaciju (pregledni prikaz).</summary>
    Task<List<EffectivePriceDto>> GetEffectivePriceList(Guid organizationId, Guid? locationId, DateTimeOffset date);

    /// <summary>Razriješena cijena za (usluga/paket, lokacija, datum).</summary>
    Task<ResolvePriceResponse> ResolvePrice(Guid organizationId, ResolvePriceRequest request);
}
