using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using BlueDragon.DuneLight.API.Authorization;
using BlueDragon.DuneLight.API.Extensions;
using BlueDragon.DuneLight.Core.DTOs.Catalog;
using BlueDragon.DuneLight.Core.Interfaces.Catalog;
using BlueDragon.DuneLight.Core.Shared;
using Microsoft.AspNetCore.Mvc;

namespace BlueDragon.DuneLight.API.Controllers.Catalog;

/// <summary>
/// P2 (faza 2A, docs/p2) — planovi članarina: profil s nepromjenjivim verzijama uvjeta (objava nove verzije = POST versions).
/// Uske naredbe (ADR-0014); plan se ne briše, samo deaktivira. Autorizacija isključivo catalog.memberships.view / .manage.
/// </summary>
[ApiController]
[Route("api/membership-plans")]
[Produces("application/json")]
public class MembershipPlansController : ControllerBase
{
    private readonly IMembershipPlanService _membershipPlanService;

    public MembershipPlansController(IMembershipPlanService membershipPlanService)
    {
        _membershipPlanService = membershipPlanService;
    }

    [HttpGet]
    [RequireGrant(Grants.CatalogMembershipsView)]
    public async Task<ActionResult<List<MembershipPlanDto>>> GetAll()
    {
        return Ok(await _membershipPlanService.GetAll(this.CurrentOrganizationId()));
    }

    [HttpGet("{id:guid}")]
    [RequireGrant(Grants.CatalogMembershipsView)]
    public async Task<ActionResult<MembershipPlanDto>> GetById(Guid id)
    {
        return Ok(await _membershipPlanService.GetById(this.CurrentOrganizationId(), id));
    }

    /// <summary>Novi plan s prvom (objavljenom) verzijom uvjeta.</summary>
    [HttpPost]
    [RequireGrant(Grants.CatalogMembershipsManage)]
    public async Task<ActionResult<MembershipPlanDto>> Create([FromBody] MembershipPlanCreateRequest request)
    {
        return Ok(await _membershipPlanService.Create(this.CurrentOrganizationId(), this.CurrentUserId(), request));
    }

    [HttpPatch("{id:guid}/details")]
    [RequireGrant(Grants.CatalogMembershipsManage)]
    public async Task<ActionResult<MembershipPlanDto>> UpdateDetails(Guid id, [FromBody] MembershipPlanDetailsRequest request)
    {
        return Ok(await _membershipPlanService.UpdateDetails(this.CurrentOrganizationId(), this.CurrentUserId(), id, request));
    }

    /// <summary>Najveći broj aktivnih članstava plana (null = bez ograničenja); vrijedi za buduće prodaje.</summary>
    [HttpPut("{id:guid}/capacity")]
    [RequireGrant(Grants.CatalogMembershipsManage)]
    public async Task<ActionResult<MembershipPlanDto>> UpdateCapacity(Guid id, [FromBody] MembershipPlanCapacityRequest request)
    {
        return Ok(await _membershipPlanService.UpdateCapacity(this.CurrentOrganizationId(), this.CurrentUserId(), id, request));
    }

    /// <summary>Objavljuje novu verziju uvjeta (Version + 1); starije verzije se ne mijenjaju. ApplyTo (Q14): samo nove prodaje
    /// ili i postojeća članstva (od prve obnove nakon roka najave; strogo povoljna izmjena od sljedeće, Q48). Odgovor nosi
    /// klasifikaciju izmjene i pogođena članstva.</summary>
    [HttpPost("{id:guid}/versions")]
    [RequireGrant(Grants.CatalogMembershipsManage)]
    public async Task<ActionResult<MembershipPlanVersionPublishResultDto>> PublishVersion(Guid id, [FromBody] MembershipPlanVersionPublishRequest request)
    {
        return Ok(await _membershipPlanService.PublishVersion(this.CurrentOrganizationId(), this.CurrentUserId(), id, request));
    }

    [HttpPost("{id:guid}/activate")]
    [RequireGrant(Grants.CatalogMembershipsDeactivate)]
    public async Task<ActionResult<MembershipPlanDto>> Activate(Guid id)
    {
        return Ok(await _membershipPlanService.Activate(this.CurrentOrganizationId(), this.CurrentUserId(), id));
    }

    /// <summary>Neaktivan plan se ne prodaje; postojeća članstva traju do kraja tekućeg perioda i ne obnavljaju se.</summary>
    [HttpPost("{id:guid}/deactivate")]
    [RequireGrant(Grants.CatalogMembershipsDeactivate)]
    public async Task<ActionResult<MembershipPlanDto>> Deactivate(Guid id)
    {
        return Ok(await _membershipPlanService.Deactivate(this.CurrentOrganizationId(), this.CurrentUserId(), id));
    }
}
