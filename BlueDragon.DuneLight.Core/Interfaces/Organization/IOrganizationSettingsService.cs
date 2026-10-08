using System;
using System.Threading.Tasks;
using BlueDragon.DuneLight.Core.DTOs.Organization;

namespace BlueDragon.DuneLight.Core.Interfaces.Organization;

/// <summary>Poslovne postavke organizacije. P1 (D1): rok otkazivanja više nije ovdje — vidi ICancellationPolicyService.</summary>
public interface IOrganizationSettingsService
{
    Task<OrganizationSettingsDto> GetSettings(Guid organizationId);

    /// <summary>Phase D3B3A — kad se paket troši (default OnCompletion ako organizacija nema redak postavki).</summary>
    Task<Enums.PackageConsumptionTiming> GetPackageConsumptionTiming(Guid organizationId);

    /// <summary>Postavlja IANA vremensku zonu organizacije; nepodržan id baca ValidationAppException.</summary>
    Task<OrganizationSettingsDto> UpdateTimeZone(Guid organizationId, Guid userId, OrganizationTimeZoneUpdateRequest request);

    /// <summary>P2 (Q14) — rok najave izmjene plana za postojeća članstva (default 30 dana).</summary>
    Task<int> GetMembershipChangeNoticeDays(Guid organizationId);

    Task<OrganizationSettingsDto> UpdateMembershipDebtRules(Guid organizationId, Guid userId, OrganizationMembershipDebtUpdateRequest request);

    /// <summary>P2 (Q4, 2D) — ponašanje kad je limit članarine iskorišten.</summary>
    Task<OrganizationSettingsDto> UpdateMembershipCoverageRules(Guid organizationId, Guid userId, OrganizationMembershipCoverageUpdateRequest request);

    /// <summary>P2 (2F) — postavke provizija organizacije (osnovica, Q38); API pod commissions.manage.</summary>
    Task<OrganizationCommissionSettingsDto> GetCommissionSettings(Guid organizationId);
    Task<OrganizationCommissionSettingsDto> UpdateCommissionSettings(Guid organizationId, Guid userId, OrganizationCommissionSettingsDto request);

    Task<OrganizationSettingsDto> UpdateMembershipChangeNoticeDays(Guid organizationId, Guid userId, OrganizationMembershipChangeNoticeUpdateRequest request);
}
