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
/// definicije grupe (Create/Update/Delete/capability-based) ide preko permissions.manage;
/// dodjela grupa korisnicima (assignments/*) ide preko permissions.assignments.manage — vidi Grants.cs.
/// GrantGroup IME nema nikakvo autorizacijsko značenje; bilo koja grupa s ovim raw grantovima ima ovu ovlast.
/// </summary>
[ApiController]
[Route("api/permissions/grant-groups")]
[Produces("application/json")]
public class GrantGroupsController : ControllerBase
{
    private readonly IGrantGroupService _grantGroupService;
    private readonly IGrantGroupCapabilityAuthoringService _authoringService;

    public GrantGroupsController(
        IGrantGroupService grantGroupService,
        IGrantGroupCapabilityAuthoringService authoringService)
    {
        _grantGroupService = grantGroupService;
        _authoringService = authoringService;
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

    /// <summary>Capability-aware create. Klijent šalje capability odabire + dodatne ručne grantove; backend iz
    /// CapabilityCatalog-a računa i sprema samo konačni raw grant skup (vidi IGrantGroupCapabilityAuthoringService).</summary>
    [HttpPost("capability-based")]
    [RequireGrant(Grants.PermissionsManage)]
    public async Task<ActionResult<GrantGroupAuthoringDto>> CreateCapabilityBased([FromBody] GrantGroupCapabilityWriteRequest request)
    {
        GrantGroupAuthoringDto created = await _authoringService.Create(this.CurrentOrganizationId(), this.CurrentUserId(), request);
        return CreatedAtAction(nameof(GetAuthoringState), new { id = created.GrantGroup.Id }, created);
    }

    /// <summary>Capability-aware edit — puni save iz role-editora: odabiri + ManualGrantKeys zamjenjuju cijeli grant
    /// skup grupe.</summary>
    [HttpPut("{id:guid}/capability-based")]
    [RequireGrant(Grants.PermissionsManage)]
    public async Task<ActionResult<GrantGroupAuthoringDto>> UpdateCapabilityBased(Guid id, [FromBody] GrantGroupCapabilityWriteRequest request)
    {
        return Ok(await _authoringService.Update(this.CurrentOrganizationId(), this.CurrentUserId(), id, request));
    }

    /// <summary>Authoring stanje (capability odabiri, ručni grantovi) izvedeno iz trenutnih grantova grupe.</summary>
    [HttpGet("{id:guid}/authoring-state")]
    [RequireGrant(Grants.PermissionsView, Grants.PermissionsManage)]
    public async Task<ActionResult<GrantGroupAuthoringDto>> GetAuthoringState(Guid id)
    {
        return Ok(await _authoringService.GetAuthoringState(this.CurrentOrganizationId(), id));
    }
}
