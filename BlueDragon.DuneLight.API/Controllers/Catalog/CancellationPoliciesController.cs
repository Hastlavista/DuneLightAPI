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
/// P1 (ADR-0015) — politike otkazivanja: imenovani profili s nepromjenjivim verzijama (objava nove verzije = POST versions),
/// dodjele po scopeu (Company+Service, Service, Company) i zadana politika organizacije. Autorizacija isključivo
/// catalog.cancellation-policies.view / .manage (ne organization.settings.manage).
/// </summary>
[ApiController]
[Route("api/cancellation-policies")]
[Produces("application/json")]
public class CancellationPoliciesController : ControllerBase
{
    private readonly ICancellationPolicyService _cancellationPolicyService;

    public CancellationPoliciesController(ICancellationPolicyService cancellationPolicyService)
    {
        _cancellationPolicyService = cancellationPolicyService;
    }

    [HttpGet]
    [RequireGrant(Grants.CatalogCancellationPoliciesView)]
    public async Task<ActionResult<List<CancellationPolicyDto>>> GetAll()
    {
        return Ok(await _cancellationPolicyService.GetAll(this.CurrentOrganizationId()));
    }

    [HttpGet("{id:guid}")]
    [RequireGrant(Grants.CatalogCancellationPoliciesView)]
    public async Task<ActionResult<CancellationPolicyDto>> GetById(Guid id)
    {
        return Ok(await _cancellationPolicyService.GetById(this.CurrentOrganizationId(), id));
    }

    /// <summary>Novi profil s prvom (objavljenom) verzijom.</summary>
    [HttpPost]
    [RequireGrant(Grants.CatalogCancellationPoliciesManage)]
    public async Task<ActionResult<CancellationPolicyDto>> Create([FromBody] CancellationPolicyCreateRequest request)
    {
        return Ok(await _cancellationPolicyService.Create(this.CurrentOrganizationId(), this.CurrentUserId(), request));
    }

    [HttpPatch("{id:guid}/name")]
    [RequireGrant(Grants.CatalogCancellationPoliciesManage)]
    public async Task<ActionResult<CancellationPolicyDto>> Rename(Guid id, [FromBody] CancellationPolicyRenameRequest request)
    {
        return Ok(await _cancellationPolicyService.Rename(this.CurrentOrganizationId(), this.CurrentUserId(), id, request));
    }

    /// <summary>Objavljuje novu verziju pravila (Version + 1); primjenjuje se odmah na buduće događaje (nema Drafta ni
    /// EffectiveFrom). Starije verzije se ne mijenjaju.</summary>
    [HttpPost("{id:guid}/versions")]
    [RequireGrant(Grants.CatalogCancellationPoliciesManage)]
    public async Task<ActionResult<CancellationPolicyDto>> PublishVersion(Guid id, [FromBody] CancellationPolicyRulesRequest request)
    {
        return Ok(await _cancellationPolicyService.PublishVersion(this.CurrentOrganizationId(), this.CurrentUserId(), id, request));
    }

    [HttpPost("{id:guid}/activate")]
    [RequireGrant(Grants.CatalogCancellationPoliciesManage)]
    public async Task<ActionResult<CancellationPolicyDto>> Activate(Guid id)
    {
        return Ok(await _cancellationPolicyService.Activate(this.CurrentOrganizationId(), this.CurrentUserId(), id));
    }

    /// <summary>Zadana ili dodijeljena politika se ne može deaktivirati (CANCELLATION_POLICY_IN_USE).</summary>
    [HttpPost("{id:guid}/deactivate")]
    [RequireGrant(Grants.CatalogCancellationPoliciesManage)]
    public async Task<ActionResult<CancellationPolicyDto>> Deactivate(Guid id)
    {
        return Ok(await _cancellationPolicyService.Deactivate(this.CurrentOrganizationId(), this.CurrentUserId(), id));
    }

    /// <summary>Postavlja zadanu politiku organizacije (zadnja razina razrješavanja).</summary>
    [HttpPut("default")]
    [RequireGrant(Grants.CatalogCancellationPoliciesManage)]
    public async Task<ActionResult<CancellationPolicyDto>> SetOrganizationDefault([FromBody] CancellationPolicyDefaultRequest request)
    {
        return Ok(await _cancellationPolicyService.SetOrganizationDefault(this.CurrentOrganizationId(), this.CurrentUserId(), request));
    }

    [HttpGet("assignments")]
    [RequireGrant(Grants.CatalogCancellationPoliciesView)]
    public async Task<ActionResult<List<CancellationPolicyAssignmentDto>>> GetAssignments()
    {
        return Ok(await _cancellationPolicyService.GetAssignments(this.CurrentOrganizationId()));
    }

    /// <summary>Dodjela profila scopeu (upsert po scopeu): Company+Service, samo Service ili samo Company.</summary>
    [HttpPut("assignments")]
    [RequireGrant(Grants.CatalogCancellationPoliciesManage)]
    public async Task<ActionResult<CancellationPolicyAssignmentDto>> Assign([FromBody] CancellationPolicyAssignmentRequest request)
    {
        return Ok(await _cancellationPolicyService.Assign(this.CurrentOrganizationId(), this.CurrentUserId(), request));
    }

    [HttpDelete("assignments/{assignmentId:guid}")]
    [RequireGrant(Grants.CatalogCancellationPoliciesManage)]
    public async Task<IActionResult> RemoveAssignment(Guid assignmentId)
    {
        await _cancellationPolicyService.RemoveAssignment(this.CurrentOrganizationId(), assignmentId);
        return NoContent();
    }

    /// <summary>Pregled: koja bi politika (profil + najnovija verzija) sada vrijedila za poslovnicu i uslugu.</summary>
    [HttpGet("resolve")]
    [RequireGrant(Grants.CatalogCancellationPoliciesView)]
    public async Task<ActionResult<CancellationPolicyResolutionDto>> Resolve([FromQuery] Guid companyId, [FromQuery] Guid serviceId)
    {
        return Ok(await _cancellationPolicyService.Resolve(this.CurrentOrganizationId(), companyId, serviceId));
    }
}
