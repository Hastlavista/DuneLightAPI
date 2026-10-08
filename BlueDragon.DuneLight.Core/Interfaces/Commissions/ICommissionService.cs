using System;
using System.Threading.Tasks;
using BlueDragon.DuneLight.Core.DTOs.Commissions;
using BlueDragon.DuneLight.Core.Shared;

namespace BlueDragon.DuneLight.Core.Interfaces.Commissions;

/// <summary>Čitanje CommissionEntry ledgera (povijest/sažetak) — čita SAMO persistirane snapshotove, nikad ne
/// preračunava po trenutnom CommissionRule (vidi CommissionEntry.cs/spec section 50). P2 (2F): brojanje po događajima.</summary>
public interface ICommissionService
{
    Task<PagedResult<CommissionEntryDto>> GetEntries(Guid organizationId, CommissionEntryQuery query);

    Task<CommissionSummaryResultDto> GetSummary(Guid organizationId, CommissionSummaryQuery query);

    /// <summary>P2 (2F, Q50) — korekcija korisnika provizije na prodaju nakon nastanka: storno + nova provizija za novog korisnika
    /// u istoj transakciji, obavezan razlog; izvor (stavka checkouta ili članstvo) dobiva novog korisnika i zapis promjene.</summary>
    Task<CommissionEntryReassignResultDto> Reassign(Guid organizationId, Guid userId, Guid entryId, CommissionEntryReassignRequest request);

    /// <summary>P2 (2F, pregled — izbor 8) — naknadna dodjela korisnika provizije na prodaju kad provizija nije nastala jer korisnika
    /// nije bilo (stavka proizvoda/paketa zatvorenog checkouta ili prva prodaja članarine); commissions.manage + razlog.</summary>
    Task<CommissionSaleAssignmentResultDto> AssignSale(Guid organizationId, Guid userId, CommissionSaleAssignmentRequest request);
}
