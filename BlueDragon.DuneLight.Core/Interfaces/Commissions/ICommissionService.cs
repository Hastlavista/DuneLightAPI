using System;
using System.Threading.Tasks;
using BlueDragon.DuneLight.Core.DTOs.Commissions;
using BlueDragon.DuneLight.Core.Shared;

namespace BlueDragon.DuneLight.Core.Interfaces.Commissions;

/// <summary>Čitanje CommissionEntry ledgera (povijest/sažetak) — čita SAMO persistirane snapshotove, nikad ne
/// preračunava po trenutnom CommissionRule (vidi CommissionEntry.cs/spec section 50).</summary>
public interface ICommissionService
{
    Task<PagedResult<CommissionEntryDto>> GetEntries(Guid organizationId, CommissionEntryQuery query);

    Task<CommissionSummaryResultDto> GetSummary(Guid organizationId, CommissionSummaryQuery query);
}
