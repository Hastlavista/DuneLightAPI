using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using BlueDragon.DuneLight.API.Authorization;
using BlueDragon.DuneLight.API.Extensions;
using BlueDragon.DuneLight.Core.DTOs.Capabilities;
using BlueDragon.DuneLight.Core.DTOs.Permissions;
using BlueDragon.DuneLight.Core.Interfaces.Capabilities;
using BlueDragon.DuneLight.Core.Interfaces.Permissions;
using BlueDragon.DuneLight.Core.Shared;
using Microsoft.AspNetCore.Mvc;

namespace BlueDragon.DuneLight.API.Controllers.Permissions;

/// <summary>
/// Upravljanje GrantGroup-ama — Grant-only Tenant Authorization Refactor: više NEMA Owner bypass-a
/// ([RequireOwner] je uklonjen). Čitanje ide preko permissions.view ILI permissions.manage; mutacija
/// definicije grupe (Create/Update/Delete/capability-based/template-upgrade) ide preko permissions.manage;
/// dodjela grupa korisnicima (assignments/*) ide preko permissions.assignments.manage — vidi Grants.cs.
/// GrantGroup IME nema nikakvo autorizacijsko značenje; bilo koja grupa s ovim raw grantovima ima ovu ovlast.
/// </summary>
[ApiController]
[Route("api/permissions/grant-groups")]
[Produces("application/json")]
public class GrantGroupsController : ControllerBase
{
    private readonly IGrantGroupService _grantGroupService;
    private readonly ICapabilityReadService _capabilityReadService;
    private readonly IGrantGroupCapabilityAuthoringService _authoringService;
    private readonly IGrantGroupTemplateUpgradeService _templateUpgradeService;

    public GrantGroupsController(
        IGrantGroupService grantGroupService,
        ICapabilityReadService capabilityReadService,
        IGrantGroupCapabilityAuthoringService authoringService,
        IGrantGroupTemplateUpgradeService templateUpgradeService)
    {
        _grantGroupService = grantGroupService;
        _capabilityReadService = capabilityReadService;
        _authoringService = authoringService;
        _templateUpgradeService = templateUpgradeService;
    }

    [HttpGet]
    [RequireGrant(Grants.PermissionsView, Grants.PermissionsManage)]
    public async Task<ActionResult<List<GrantGroupDto>>> GetAll()
    {
        return Ok(await _grantGroupService.GetAll(this.CurrentOrganizationId()));
    }

    [HttpGet("{id:guid}")]
    [RequireGrant(Grants.PermissionsView, Grants.PermissionsManage)]
    public async Task<ActionResult<GrantGroupDto>> GetById(Guid id)
    {
        return Ok(await _grantGroupService.GetById(this.CurrentOrganizationId(), id));
    }

    [HttpPost]
    [RequireGrant(Grants.PermissionsManage)]
    public async Task<ActionResult<GrantGroupDto>> Create([FromBody] GrantGroupCreateRequest request)
    {
        GrantGroupDto created = await _grantGroupService.Create(this.CurrentOrganizationId(), this.CurrentUserId(), request);
        return CreatedAtAction(nameof(GetById), new { id = created.Id }, created);
    }

    [HttpPut("{id:guid}")]
    [RequireGrant(Grants.PermissionsManage)]
    public async Task<ActionResult<GrantGroupDto>> Update(Guid id, [FromBody] GrantGroupUpdateRequest request)
    {
        return Ok(await _grantGroupService.Update(this.CurrentOrganizationId(), this.CurrentUserId(), id, request));
    }

    [HttpDelete("{id:guid}")]
    [RequireGrant(Grants.PermissionsManage)]
    public async Task<IActionResult> Delete(Guid id)
    {
        await _grantGroupService.Delete(this.CurrentOrganizationId(), id);
        return NoContent();
    }

    [HttpGet("assignments/{userId:guid}")]
    [RequireGrant(Grants.PermissionsView, Grants.PermissionsManage, Grants.PermissionsAssignmentsManage)]
    public async Task<ActionResult<List<Guid>>> GetAssignments(Guid userId)
    {
        return Ok(await _grantGroupService.GetAssignedGrantGroupIds(this.CurrentOrganizationId(), userId));
    }

    /// <summary>Zamjenjuje CIJELI skup GrantGroup dodjela za korisnika.</summary>
    [HttpPut("assignments/{userId:guid}")]
    [RequireGrant(Grants.PermissionsManage, Grants.PermissionsAssignmentsManage)]
    public async Task<IActionResult> SetAssignments(Guid userId, [FromBody] AssignUserGrantGroupsRequest request)
    {
        await _grantGroupService.SetUserGrantGroups(this.CurrentOrganizationId(), userId, request);
        return NoContent();
    }

    /// <summary>FAZA 1 Part R — je li ova GrantGroup nastala iz predloška (i s kojim capability odabirima),
    /// isključivo iz stabilnih snapshot metapodataka (vidi GrantGroupCapabilitySnapshot). Prazan rezultat
    /// (HasSnapshotMetadata=false) znači da grupa nema capability provenance (custom, ili predviđena prije ove faze).</summary>
    [HttpGet("{id:guid}/template-match")]
    [RequireGrant(Grants.PermissionsView, Grants.PermissionsManage)]
    public async Task<ActionResult<GrantGroupTemplateMatchDto>> GetTemplateMatch(Guid id)
    {
        return Ok(await _capabilityReadService.GetGrantGroupTemplateMatch(this.CurrentOrganizationId(), id));
    }

    /// <summary>FAZA 2 Part D — capability-aware create. Klijent šalje capability odabire + legitimne ručne
    /// grantove; backend materijalizira i piše autoritativni raw grant skup (vidi IGrantGroupCapabilityAuthoringService).</summary>
    [HttpPost("capability-based")]
    [RequireGrant(Grants.PermissionsManage)]
    public async Task<ActionResult<GrantGroupAuthoringDto>> CreateCapabilityBased([FromBody] GrantGroupCapabilityWriteRequest request)
    {
        GrantGroupAuthoringDto created = await _authoringService.Create(this.CurrentOrganizationId(), this.CurrentUserId(), request);
        return CreatedAtAction(nameof(GetAuthoringState), new { id = created.GrantGroup.Id }, created);
    }

    /// <summary>FAZA 2 Part E — capability-aware ordinary edit. Eksplicitni full-state save iz role-editora:
    /// ManualGrantKeys predstavlja NOVI željeni ručni skup (ne automatski očuvan stari), vidi
    /// GrantGroupHandler.ApplyCapabilitySelections napomenu zašto se razlikuje od ApplyTemplate.</summary>
    [HttpPut("{id:guid}/capability-based")]
    [RequireGrant(Grants.PermissionsManage)]
    public async Task<ActionResult<GrantGroupAuthoringDto>> UpdateCapabilityBased(Guid id, [FromBody] GrantGroupCapabilityWriteRequest request)
    {
        return Ok(await _authoringService.Update(this.CurrentOrganizationId(), this.CurrentUserId(), id, request));
    }

    /// <summary>FAZA 2 Part I/J — trenutno autoritativno authoring stanje (capability odabiri, ručni grantovi,
    /// template provenance, customized flag). Za legacy/drifted grupu (bez snapshot metapodataka) NIKAD ne
    /// izmišlja capability odabire niti nešto piše u bazu — vidi GrantGroupAuthoringDto.HasCapabilityMetadata.</summary>
    [HttpGet("{id:guid}/authoring-state")]
    [RequireGrant(Grants.PermissionsView, Grants.PermissionsManage)]
    public async Task<ActionResult<GrantGroupAuthoringDto>> GetAuthoringState(Guid id)
    {
        return Ok(await _authoringService.GetAuthoringState(this.CurrentOrganizationId(), id));
    }

    /// <summary>FAZA 3 — je li dostupan noviji predložak (i je li grupa "customized") za banner na role-editoru.
    /// HasUpgrade je NEOVISAN o IsCustomized (vidi IGrantGroupTemplateUpgradeService).</summary>
    [HttpGet("{id:guid}/template-upgrade-status")]
    [RequireGrant(Grants.PermissionsView, Grants.PermissionsManage)]
    public async Task<ActionResult<GrantGroupTemplateUpgradeStatusDto>> GetTemplateUpgradeStatus(Guid id)
    {
        return Ok(await _templateUpgradeService.GetUpgradeStatus(this.CurrentOrganizationId(), id));
    }

    /// <summary>FAZA 3 — čisti read-only diff prema ciljnoj predložak-verziji, bez upisa u bazu.</summary>
    [HttpGet("{id:guid}/template-upgrade-diff")]
    [RequireGrant(Grants.PermissionsView, Grants.PermissionsManage)]
    public async Task<ActionResult<GrantGroupTemplateDiffDto>> GetTemplateUpgradeDiff(Guid id, [FromQuery] int targetVersion)
    {
        return Ok(await _templateUpgradeService.GetDiff(this.CurrentOrganizationId(), id, targetVersion));
    }

    /// <summary>FAZA 3 — pokazuje TOČNO što bi Apply napravio (iste resolutions/stateToken provjere), bez ikakvog upisa.</summary>
    [HttpPost("{id:guid}/template-upgrade/preview")]
    [RequireGrant(Grants.PermissionsManage)]
    public async Task<ActionResult<GrantGroupTemplateUpgradePlanDto>> PreviewTemplateUpgrade(Guid id, [FromBody] GrantGroupTemplateUpgradePlanRequest request)
    {
        return Ok(await _templateUpgradeService.Preview(this.CurrentOrganizationId(), id, request));
    }

    /// <summary>FAZA 3 Part K — strict gating: backend NEOVISNO provjerava stateToken svježinu i potpunu razriješenost
    /// konflikata (GRANT_GROUP_UPGRADE_STATE_CHANGED / GRANT_GROUP_UPGRADE_CONFLICT_RESOLUTION_REQUIRED), frontend
    /// gating je samo UX. Primjenjuje se atomski (jedna transakcija, uključujući audit log).</summary>
    [HttpPost("{id:guid}/template-upgrade/apply")]
    [RequireGrant(Grants.PermissionsManage)]
    public async Task<ActionResult<GrantGroupAuthoringDto>> ApplyTemplateUpgrade(Guid id, [FromBody] GrantGroupTemplateUpgradePlanRequest request)
    {
        return Ok(await _templateUpgradeService.Apply(this.CurrentOrganizationId(), this.CurrentUserId(), id, request));
    }
}