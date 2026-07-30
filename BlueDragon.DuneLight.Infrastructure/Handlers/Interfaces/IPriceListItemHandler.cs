using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using BlueDragon.DuneLight.Core.Enums;
using BlueDragon.DuneLight.Core.Shared;
using BlueDragon.DuneLight.Infrastructure.Domain.Models.Catalog;

namespace BlueDragon.DuneLight.Infrastructure.Handlers.Interfaces;

public interface IPriceListItemHandler
{
    Task<(List<PriceListItem> Items, int TotalCount)> GetPaged(
        Guid organizationId, PagedRequest request, Guid? locationId, PricingSubjectType? subjectType);

    Task<PriceListItem> GetById(Guid organizationId, Guid id);
    Task Add(PriceListItem item);
    Task Update(PriceListItem item);
    Task Delete(PriceListItem item);
    Task AddHistory(PriceListItemHistory history);
    Task<bool> HasHistory(Guid priceListItemId);

    /// <summary>Aktivne stavke za TOČNO istu lokaciju (uklj. null) — koristi se za provjeru preklapanja.</summary>
    Task<List<PriceListItem>> GetActiveForExactLocation(
        Guid organizationId, PricingSubjectType subjectType, Guid subjectId, Guid? locationId, Guid? excludeId);

    /// <summary>Aktivne stavke za lokaciju ILI "sve lokacije" — kandidati za razrješavanje cijene.</summary>
    Task<List<PriceListItem>> GetActiveCandidates(
        Guid organizationId, PricingSubjectType subjectType, Guid subjectId, Guid? locationId);

    /// <summary>Sve aktivne stavke važeće na dani datum za lokaciju ILI "sve lokacije" — za pregledni cjenik.</summary>
    Task<List<PriceListItem>> GetActiveForLocation(Guid organizationId, Guid? locationId, DateTimeOffset date);
}
