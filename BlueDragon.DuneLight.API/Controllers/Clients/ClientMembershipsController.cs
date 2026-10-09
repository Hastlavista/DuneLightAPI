using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using BlueDragon.DuneLight.API.Authorization;
using BlueDragon.DuneLight.API.Extensions;
using BlueDragon.DuneLight.Core.DTOs.Clients;
using BlueDragon.DuneLight.Core.Interfaces.Clients;
using BlueDragon.DuneLight.Core.Shared;
using Microsoft.AspNetCore.Mvc;

namespace BlueDragon.DuneLight.API.Controllers.Clients;

/// <summary>
/// P2 (faza 2B, docs/p2) — članstva klijenata. Uske naredbe (ADR-0014), svaka radnja zasebnim grantom (princip granularnih
/// grantova po radnji, P2 dnevnik 2026-10-07): view, sell, cancel (i povlačenje), pause (i otkaz buduće pauze), plan-change,
/// end-override, void-sale.
/// </summary>
[ApiController]
[Route("api")]
[Produces("application/json")]
public class ClientMembershipsController : ControllerBase
{
    private readonly IClientMembershipService _membershipService;

    public ClientMembershipsController(IClientMembershipService membershipService)
    {
        _membershipService = membershipService;
    }

    [HttpGet("clients/{clientId:guid}/memberships")]
    [RequireGrant(Grants.ClientsMembershipsView)]
    public async Task<ActionResult<List<ClientMembershipDto>>> GetByClient(Guid clientId)
    {
        return Ok(await _membershipService.GetByClient(this.CurrentOrganizationId(), clientId));
    }

    /// <summary>Pregled 2B — članstva kojima izmjena plana nije primijenjena (trajna oznaka i razlog), opcionalno za jedan plan.</summary>
    [HttpGet("memberships/plan-update-not-applied")]
    [RequireGrant(Grants.ClientsMembershipsView)]
    public async Task<ActionResult<List<ClientMembershipDto>>> GetPlanUpdateNotApplied([FromQuery] Guid? membershipPlanId)
    {
        return Ok(await _membershipService.GetPlanUpdateNotApplied(this.CurrentOrganizationId(), membershipPlanId));
    }

    /// <summary>K1-8 — članstva koja stoje jer su sve poslovnice opsega plana neaktivne (obnova ne otvara periode ni zaduženja),
    /// s datumom od kad stoje — za ručni otkaz ako je zatvaranje trajno.</summary>
    [HttpGet("memberships/standing-still")]
    [RequireGrant(Grants.ClientsMembershipsView)]
    public async Task<ActionResult<List<ClientMembershipDto>>> GetStandingStill()
    {
        return Ok(await _membershipService.GetStandingStill(this.CurrentOrganizationId()));
    }

    [HttpGet("memberships/{id:guid}")]
    [RequireGrant(Grants.ClientsMembershipsView)]
    public async Task<ActionResult<ClientMembershipDto>> GetById(Guid id)
    {
        return Ok(await _membershipService.GetById(this.CurrentOrganizationId(), id));
    }

    /// <summary>Prodaja članarine (Q45 datum početka, Q10 preklapanje, kapacitet plana). Upozorenje ako plan ne vrijedi u
    /// poslovnici prodaje.</summary>
    [HttpPost("clients/{clientId:guid}/memberships")]
    [RequireGrant(Grants.ClientsMembershipsSell)]
    public async Task<ActionResult<ClientMembershipDto>> Sell(Guid clientId, [FromBody] ClientMembershipSellRequest request)
    {
        return Ok(await _membershipService.Sell(this.CurrentOrganizationId(), this.CurrentUserId(), clientId, request));
    }

    /// <summary>Pregled datuma od kad bi otkaz zatražen danas djelovao i zašto (otkazni rok / minimalna obveza / kraj perioda).</summary>
    [HttpGet("memberships/{id:guid}/cancellation-preview")]
    [RequireGrant(Grants.ClientsMembershipsCancel)]
    public async Task<ActionResult<MembershipCancellationPreviewDto>> PreviewCancellation(Guid id)
    {
        return Ok(await _membershipService.PreviewCancellation(this.CurrentOrganizationId(), id));
    }

    [HttpPost("memberships/{id:guid}/cancellation")]
    [RequireGrant(Grants.ClientsMembershipsCancel)]
    public async Task<ActionResult<ClientMembershipDto>> RequestCancellation(Guid id, [FromBody] ClientMembershipCancelRequest request)
    {
        return Ok(await _membershipService.RequestCancellation(this.CurrentOrganizationId(), this.CurrentUserId(), id, request));
    }

    [HttpDelete("memberships/{id:guid}/cancellation")]
    [RequireGrant(Grants.ClientsMembershipsCancel)]
    public async Task<ActionResult<ClientMembershipDto>> WithdrawCancellation(Guid id)
    {
        return Ok(await _membershipService.WithdrawCancellation(this.CurrentOrganizationId(), this.CurrentUserId(), id));
    }

    [HttpPost("memberships/{id:guid}/pauses")]
    [RequireGrant(Grants.ClientsMembershipsPause)]
    public async Task<ActionResult<ClientMembershipDto>> Pause(Guid id, [FromBody] ClientMembershipPauseRequest request)
    {
        return Ok(await _membershipService.Pause(this.CurrentOrganizationId(), this.CurrentUserId(), id, request));
    }

    /// <summary>Raniji povratak iz pauze u tijeku (Q5.6, Q47: kalendarski plan traži Confirm, inače vraća pregled).</summary>
    [HttpPost("memberships/{id:guid}/pauses/{pauseId:guid}/end-early")]
    [RequireGrant(Grants.ClientsMembershipsPause)]
    public async Task<ActionResult<ClientMembershipPauseEndEarlyResultDto>> EndPauseEarly(
        Guid id, Guid pauseId, [FromBody] ClientMembershipPauseEndEarlyRequest request)
    {
        return Ok(await _membershipService.EndPauseEarly(this.CurrentOrganizationId(), this.CurrentUserId(), id, pauseId, request));
    }

    [HttpPost("memberships/{id:guid}/pauses/{pauseId:guid}/cancel")]
    [RequireGrant(Grants.ClientsMembershipsPause)]
    public async Task<ActionResult<ClientMembershipDto>> CancelPause(Guid id, Guid pauseId)
    {
        return Ok(await _membershipService.CancelPause(this.CurrentOrganizationId(), this.CurrentUserId(), id, pauseId));
    }

    [HttpPost("memberships/{id:guid}/plan-change")]
    [RequireGrant(Grants.ClientsMembershipsPlanChange)]
    public async Task<ActionResult<ClientMembershipDto>> ChangePlan(Guid id, [FromBody] ClientMembershipPlanChangeRequest request)
    {
        return Ok(await _membershipService.ChangePlan(this.CurrentOrganizationId(), this.CurrentUserId(), id, request));
    }

    [HttpDelete("memberships/{id:guid}/plan-change")]
    [RequireGrant(Grants.ClientsMembershipsPlanChange)]
    public async Task<ActionResult<ClientMembershipDto>> WithdrawPlanChange(Guid id)
    {
        return Ok(await _membershipService.WithdrawPlanChange(this.CurrentOrganizationId(), this.CurrentUserId(), id));
    }

    /// <summary>Ručni raniji izlazak: datum od danas do izračunatog datuma otkaza, uz obavezan razlog.</summary>
    [HttpPost("memberships/{id:guid}/end")]
    [RequireGrant(Grants.ClientsMembershipsEndOverride)]
    public async Task<ActionResult<ClientMembershipDto>> EndEarly(Guid id, [FromBody] ClientMembershipEndRequest request)
    {
        return Ok(await _membershipService.EndEarly(this.CurrentOrganizationId(), this.CurrentUserId(), id, request));
    }

    [HttpGet("memberships/{id:guid}/periods")]
    [RequireGrant(Grants.ClientsMembershipsView)]
    public async Task<ActionResult<List<MembershipPeriodDto>>> GetPeriods(Guid id)
    {
        return Ok(await _membershipService.GetPeriods(this.CurrentOrganizationId(), id));
    }

    [HttpGet("memberships/{id:guid}/charges")]
    [RequireGrant(Grants.ClientsMembershipsView)]
    public async Task<ActionResult<List<MembershipChargeDto>>> GetCharges(Guid id)
    {
        return Ok(await _membershipService.GetCharges(this.CurrentOrganizationId(), id));
    }

    /// <summary>2C (Q16) — otpis preostalog duga zaduženja (uz razlog); otpisano zaduženje je konačno.</summary>
    [HttpPost("membership-charges/{chargeId:guid}/write-off")]
    [RequireGrant(Grants.MembershipsChargesWriteOff)]
    public async Task<ActionResult<MembershipChargeDto>> WriteOffCharge(Guid chargeId, [FromBody] MembershipChargeWriteOffRequest request)
    {
        return Ok(await _membershipService.WriteOffCharge(this.CurrentOrganizationId(), this.CurrentUserId(), chargeId, request));
    }

    /// <summary>2C — članstva deaktiviranog plana koja će završiti na sljedećoj obnovi ako plan tada još nije aktivan.</summary>
    [HttpGet("membership-plans/{planId:guid}/memberships-ending")]
    [RequireGrant(Grants.ClientsMembershipsView)]
    public async Task<ActionResult<List<ClientMembershipDto>>> GetEndingDueToPlanDeactivation(Guid planId)
    {
        return Ok(await _membershipService.GetEndingDueToPlanDeactivation(this.CurrentOrganizationId(), planId));
    }

    [HttpPost("memberships/{id:guid}/void-sale")]
    [RequireGrant(Grants.ClientsMembershipsVoidSale)]
    public async Task<ActionResult<ClientMembershipDto>> VoidSale(Guid id, [FromBody] ClientMembershipVoidRequest request)
    {
        return Ok(await _membershipService.VoidSale(this.CurrentOrganizationId(), this.CurrentUserId(), id, request));
    }

    /// <summary>2F (§16.3) — kome ide provizija na prvu prodaju članarine (null = nikome), dok provizija nije nastala; nakon
    /// nastanka COMMISSION_SALE_ALREADY_EARNED (korekcija: POST /api/commissions/entries/{id}/reassign uz commissions.manage).</summary>
    [HttpPatch("memberships/{id:guid}/sale-commission-employee")]
    [RequireGrant(Grants.ClientsMembershipsSell)]
    public async Task<ActionResult<ClientMembershipDto>> SetSaleCommissionEmployee(
        Guid id, [FromBody] Core.DTOs.Commissions.SaleCommissionEmployeeRequest request)
    {
        return Ok(await _membershipService.SetSaleCommissionEmployee(this.CurrentOrganizationId(), this.CurrentUserId(), id, request));
    }
}
