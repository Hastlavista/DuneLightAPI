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

    /// <summary>Update stavke i upis povijesnog zapisa u istom kontekstu/SaveChanges (atomično).</summary>
    Task UpdateWithHistory(PriceListItem item, PriceListItemHistory history);

    Task Delete(PriceListItem item);
    Task<bool> HasHistory(Guid priceListItemId);

    /// <summary>Aktivne stavke za TOČNO isti opseg (tvrtka i zaposlenik, uklj. null) — koristi se za provjeru preklapanja.</summary>
    Task<List<PriceListItem>> GetActiveForExactScope(
        Guid organizationId, PricingSubjectType subjectType, Guid subjectId, Guid? companyId, Guid? employeeId, Guid? excludeId);

    /// <summary>Aktivne stavke za tvrtku ILI "sve tvrtke", bez zaposlenika ILI za zadanog zaposlenika — kandidati za
    /// razrješavanje cijene (stavke drugih zaposlenika se nikad ne učitavaju).</summary>
    Task<List<PriceListItem>> GetActiveCandidates(
        Guid organizationId, PricingSubjectType subjectType, Guid subjectId, Guid? companyId, Guid? employeeId);

    /// <summary>Sve aktivne stavke BEZ zaposlenika važeće na dani datum za tvrtku ILI "sve tvrtke" — za pregledni cjenik.</summary>
    Task<List<PriceListItem>> GetActiveForCompany(Guid organizationId, Guid? companyId, DateOnly date);

    /// <summary>T1-8 — zakazana (Confirmed) sudjelovanja usluge s početkom segmenta u [from, to) (to null = bez gornje granice),
    /// opcionalno samo poslovnice termina i zaposlenika izvora cijene: poslovnica termina i početak segmenta (dan cjenika računa
    /// pozivatelj u zoni poslovnice).</summary>
    Task<List<(Guid CompanyId, DateTimeOffset PlannedStart)>> GetScheduledServiceStarts(
        Guid organizationId, Guid serviceId, Guid? companyId, Guid? pricingEmployeeId, DateTimeOffset from, DateTimeOffset? to);
}
