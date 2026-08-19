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
        Guid organizationId, PagedRequest request, Guid? companyId, PricingSubjectType? subjectType);

    Task<PriceListItem> GetById(Guid organizationId, Guid id);
    Task Add(PriceListItem item);
    Task Update(PriceListItem item);
    Task Delete(PriceListItem item);
    Task AddHistory(PriceListItemHistory history);
    Task<bool> HasHistory(Guid priceListItemId);

    /// <summary>Aktivne stavke za TOČNO istu tvrtku (uklj. null) — koristi se za provjeru preklapanja.</summary>
    Task<List<PriceListItem>> GetActiveForExactCompany(
        Guid organizationId, PricingSubjectType subjectType, Guid subjectId, Guid? companyId, Guid? excludeId);

    /// <summary>Aktivne stavke za tvrtku ILI "sve tvrtke" — kandidati za razrješavanje cijene.</summary>
    Task<List<PriceListItem>> GetActiveCandidates(
        Guid organizationId, PricingSubjectType subjectType, Guid subjectId, Guid? companyId);

    /// <summary>Sve aktivne stavke važeće na dani datum za tvrtku ILI "sve tvrtke" — za pregledni cjenik.</summary>
    Task<List<PriceListItem>> GetActiveForCompany(Guid organizationId, Guid? companyId, DateTimeOffset date);
}
