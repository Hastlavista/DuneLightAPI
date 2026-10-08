using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using BlueDragon.DuneLight.Core.DTOs.Commissions;
using BlueDragon.DuneLight.Core.Enums;
using BlueDragon.DuneLight.Infrastructure.Domain.Models.Commissions;
using BlueDragon.DuneLight.Infrastructure.UnitOfWork;

namespace BlueDragon.DuneLight.Infrastructure.Handlers.Interfaces;

public interface ICommissionEntryHandler
{
    /// <summary>Umeće novi zapis unutar pozivateljeve transakcije — povreda unique indeksa (vidi CommissionEntry.cs) prekida
    /// cijelu pozivateljevu transakciju (CommissionService.TryAdd namjerno ne hvata).</summary>
    Task Add(IUnitOfWork uow, CommissionEntry entry);

    /// <summary>Phase M1G — SVI aktivni (Earned) IndividualService zapisi sudjelovanja (po jedan za svakog zaposlenika segmenta);
    /// deterministična identifikacija izvora za reverziju korekcije completiona. P2 (2F): PolicyFee zapisi istog sudjelovanja se
    /// ovdje NE vraćaju (zaseban izvor).</summary>
    Task<List<CommissionEntry>> GetActiveForParticipation(IUnitOfWork uow, Guid organizationId, Guid participationId);

    /// <summary>P2 (2F) — SVI zapisi (Earned i Reversed) izvora, unutar transakcije: PolicyFee po sudjelovanju, MembershipSale po
    /// članstvu, Product/PackageSale po stavci checkouta. Koristi se za "postoji li aktivna" i sljedeći SourceVersion.</summary>
    Task<List<CommissionEntry>> GetForSource(
        IUnitOfWork uow, Guid organizationId, CommissionSourceType sourceType, Guid sourceId);

    Task<CommissionEntry> GetForUpdate(IUnitOfWork uow, Guid organizationId, Guid id);

    /// <summary>Sprema promjene na postojećem zapisu (isključivo Status/ReversedAt/ReversedBy/ReversalReason — sve ostalo je
    /// nepromjenjiv snapshot, vidi CommissionEntry.cs) unutar pozivateljeve transakcije.</summary>
    Task Update(IUnitOfWork uow, CommissionEntry entry);

    Task<CommissionEntry> GetById(Guid organizationId, Guid id);

    /// <summary>P2 (2F, brojanje po događajima) — zapisi zarađeni ILI stornirani u razdoblju [From, To).</summary>
    Task<(List<CommissionEntry> Items, int TotalCount)> GetPaged(Guid organizationId, CommissionEntryQuery query);

    /// <summary>Agregat po zaposleniku po događajima: zarada u razdoblju EarnedAt, storno u razdoblju ReversedAt.</summary>
    Task<List<EmployeeCommissionSummaryDto>> GetSummaryByEmployee(Guid organizationId, CommissionSummaryQuery query);
}
