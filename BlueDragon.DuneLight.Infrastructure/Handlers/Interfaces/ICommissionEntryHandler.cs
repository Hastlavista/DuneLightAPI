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

    /// <summary>Trenutno AKTIVAN (Status=Earned) zapis za ovaj Booking, ili null — deterministična identifikacija
    /// izvora za reverziju (ApplyIndividualCompletionCorrection), nikad po iznosu/datumu/zaposleniku (vidi spec
    /// section 14). Najviše jedan takav redak može postojati u danom trenutku (svaki Earned zapis se reverzira
    /// PRIJE nego Booking uopće može ponovno zaraditi novi, vidi CommissionEntry.cs SourceVersion napomenu).</summary>
    Task<CommissionEntry> GetActiveForBooking(IUnitOfWork uow, Guid organizationId, Guid bookingId);

    /// <summary>Sprema promjene na postojećem zapisu (isključivo Status/ReversedAt/ReversedBy — sve ostalo je
    /// nepromjenjiv snapshot, vidi CommissionEntry.cs) unutar pozivateljeve transakcije.</summary>
    Task Update(IUnitOfWork uow, CommissionEntry entry);

    Task<(List<CommissionEntry> Items, int TotalCount)> GetPaged(Guid organizationId, CommissionEntryQuery query);

    /// <summary>Agregat po Employeeu (Sum CommissionAmount po Status) unutar raspona — jedan SQL upit (GROUP BY),
    /// izbjegava učitavanje svih redaka u memoriju za sažetak (vidi spec section 58/61 N+1 upozorenje).</summary>
    Task<List<EmployeeCommissionSummaryDto>> GetSummaryByEmployee(Guid organizationId, CommissionSummaryQuery query);
}
