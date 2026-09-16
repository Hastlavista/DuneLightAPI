using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using BlueDragon.DuneLight.Core.DTOs.Commissions;
using BlueDragon.DuneLight.Infrastructure.Domain.Models.Commissions;
using BlueDragon.DuneLight.Infrastructure.UnitOfWork;

namespace BlueDragon.DuneLight.Infrastructure.Handlers.Interfaces;

public interface ICommissionEntryHandler
{
    /// <summary>Umeće novi zapis unutar pozivateljeve transakcije — može baciti DbUpdateException na povredu
    /// jednog od unique indeksa (vidi CommissionEntry.cs), koju CommissionService hvata i tiho preskače.</summary>
    Task Add(IUnitOfWork uow, CommissionEntry entry);

    Task<(List<CommissionEntry> Items, int TotalCount)> GetPaged(Guid organizationId, CommissionEntryQuery query);

    /// <summary>Agregat po Employeeu (Sum CommissionAmount po Status) unutar raspona — jedan SQL upit (GROUP BY),
    /// izbjegava učitavanje svih redaka u memoriju za sažetak (vidi spec section 58/61 N+1 upozorenje).</summary>
    Task<List<EmployeeCommissionSummaryDto>> GetSummaryByEmployee(Guid organizationId, CommissionSummaryQuery query);
}
