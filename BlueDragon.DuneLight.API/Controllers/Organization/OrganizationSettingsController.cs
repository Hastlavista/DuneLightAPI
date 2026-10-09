using System.Threading.Tasks;
using BlueDragon.DuneLight.API.Authorization;
using BlueDragon.DuneLight.API.Extensions;
using BlueDragon.DuneLight.Core.DTOs.Organization;
using BlueDragon.DuneLight.Core.Interfaces.Organization;
using BlueDragon.DuneLight.Core.Shared;
using Microsoft.AspNetCore.Mvc;

namespace BlueDragon.DuneLight.API.Controllers.Organization;

/// <summary>
/// Poslovne postavke organizacije — odvojeno od OrganizationBrandingController (vizualni identitet): potrošnja paketa i
/// vremenska zona. P1 (D1): rok otkazivanja je uklonjen — vidi CancellationPoliciesController.
/// </summary>
[ApiController]
[Route("api/organization/settings")]
[Produces("application/json")]
public class OrganizationSettingsController : ControllerBase
{
    private readonly IOrganizationSettingsService _organizationSettingsService;

    public OrganizationSettingsController(IOrganizationSettingsService organizationSettingsService)
    {
        _organizationSettingsService = organizationSettingsService;
    }

    [HttpGet]
    [RequireGrant(Grants.OrganizationSettingsManage)]
    public async Task<ActionResult<OrganizationSettingsDto>> GetSettings()
    {
        return Ok(await _organizationSettingsService.GetSettings(this.CurrentOrganizationId()));
    }

    /// <summary>IANA vremenska zona poslovnog kalendara (radno vrijeme, odsutnosti, praznici, termini).</summary>
    [HttpPut("time-zone")]
    [RequireGrant(Grants.OrganizationSettingsManage)]
    public async Task<ActionResult<OrganizationSettingsDto>> UpdateTimeZone([FromBody] OrganizationTimeZoneUpdateRequest request)
    {
        return Ok(await _organizationSettingsService.UpdateTimeZone(
            this.CurrentOrganizationId(), this.CurrentUserId(), request));
    }

    /// <summary>P2 (Q15, 2C) — pravila duga članarina: grace period, ponašanje nakon grace perioda i automatski završetak nakon
    /// N neplaćenih perioda (prazno = isključeno).</summary>
    [HttpPut("membership-debt")]
    [RequireGrant(Grants.OrganizationSettingsManage)]
    public async Task<ActionResult<OrganizationSettingsDto>> UpdateMembershipDebtRules([FromBody] OrganizationMembershipDebtUpdateRequest request)
    {
        return Ok(await _organizationSettingsService.UpdateMembershipDebtRules(this.CurrentOrganizationId(), this.CurrentUserId(), request));
    }

    /// <summary>K1-4 — obaveznost odabira šifre razloga po događaju (otkaz klijenta / otkaz studija / izostanak); obavezno vrijedi
    /// samo kad za događaj postoji aktivna šifra.</summary>
    [HttpPut("cancellation-reasons")]
    [RequireGrant(Grants.OrganizationSettingsManage)]
    public async Task<ActionResult<OrganizationSettingsDto>> UpdateCancellationReasonRules([FromBody] OrganizationCancellationReasonRulesUpdateRequest request)
    {
        return Ok(await _organizationSettingsService.UpdateCancellationReasonRules(this.CurrentOrganizationId(), this.CurrentUserId(), request));
    }

    /// <summary>P2 (Q4, 2D) — ponašanje kad je limit članarine iskorišten: FallbackToNextSource (default, rezervacija prolazi
    /// i ide na sljedeći izvor) ili Reject (MEMBERSHIP_LIMIT_EXCEEDED).</summary>
    [HttpPut("membership-coverage")]
    [RequireGrant(Grants.OrganizationSettingsManage)]
    public async Task<ActionResult<OrganizationSettingsDto>> UpdateMembershipCoverageRules([FromBody] OrganizationMembershipCoverageUpdateRequest request)
    {
        return Ok(await _organizationSettingsService.UpdateMembershipCoverageRules(this.CurrentOrganizationId(), this.CurrentUserId(), request));
    }


    /// <summary>P2 (Q14) — rok najave (dani) za nepovoljnu izmjenu plana članarine prenesenu na postojeća članstva.</summary>
    [HttpPut("membership-change-notice")]
    [RequireGrant(Grants.OrganizationSettingsManage)]
    public async Task<ActionResult<OrganizationSettingsDto>> UpdateMembershipChangeNoticeDays(
        [FromBody] OrganizationMembershipChangeNoticeUpdateRequest request)
    {
        return Ok(await _organizationSettingsService.UpdateMembershipChangeNoticeDays(
            this.CurrentOrganizationId(), this.CurrentUserId(), request));
    }
}
