using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using BlueDragon.DuneLight.Core.DTOs.Catalog;
using BlueDragon.DuneLight.Core.Enums;
using BlueDragon.DuneLight.Core.Shared;

namespace BlueDragon.DuneLight.Core.Interfaces.Catalog;

public interface IPricingService
{
    Task<PagedResult<PriceListItemDto>> GetPaged(Guid organizationId, PagedRequest request, Guid? companyId, PricingSubjectType? subjectType);
    Task<PriceListItemDto> GetById(Guid organizationId, Guid id);
    Task<PriceListItemDto> Create(Guid organizationId, Guid userId, PriceListItemCreateRequest request);
    Task<PriceListItemDto> Update(Guid organizationId, Guid userId, Guid id, PriceListItemUpdateRequest request);
    Task<PriceListItemDto> SetActive(Guid organizationId, Guid userId, Guid id, bool isActive);
    Task Delete(Guid organizationId, Guid id);

    /// <summary>Trenutno važeći cjenik za tvrtku (pregledni prikaz).</summary>
    Task<List<EffectivePriceDto>> GetEffectivePriceList(Guid organizationId, Guid? companyId, DateOnly? date);

    /// <summary>Razriješena cijena za (usluga/paket, tvrtka, datum).</summary>
    Task<ResolvePriceResponse> ResolvePrice(Guid organizationId, ResolvePriceRequest request);

    /// <summary>T1-7: cijena usluge za termin/segment koji počinje u <paramref name="serviceStartsAt"/> — JEDINO mjesto koje
    /// određuje dan cjenika termina: lokalni datum početka u efektivnoj zoni poslovnice termina (termin preko ponoći pripada
    /// danu početka; promjena cijene unutar dana namjerno nije podržana).</summary>
    Task<ResolvePriceResponse> ResolveForServiceStart(
        Guid organizationId, Guid serviceId, Guid companyId, Guid? pricingEmployeeId, DateTimeOffset serviceStartsAt);

    /// <summary>T1-8: dan cjenika termina/segmenta koji počinje u <paramref name="serviceStartsAt"/> (isto pravilo kao
    /// <see cref="ResolveForServiceStart"/>) — koristi ga promjena vremena segmenta da odluči čita li se cjenik ponovno.</summary>
    Task<DateOnly> PriceListDay(Guid organizationId, Guid companyId, DateTimeOffset serviceStartsAt);
}
