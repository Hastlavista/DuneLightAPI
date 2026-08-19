using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using BlueDragon.DuneLight.API.Authorization;
using BlueDragon.DuneLight.API.Extensions;
using BlueDragon.DuneLight.Core.DTOs.Permissions;
using BlueDragon.DuneLight.Core.Interfaces.Permissions;
using Microsoft.AspNetCore.Mvc;

namespace BlueDragon.DuneLight.API.Controllers.Permissions;

/// <summary>Upravljanje GrantGroup-ama — Owner-only (vidi RequireOwnerAttribute), dok se ne pokaže potreba da i ne-Owner Admin upravlja dozvolama.</summary>
[ApiController]
[Route("api/permissions/grant-groups")]
[Produces("application/json")]
[RequireOwner]
public class GrantGroupsController : ControllerBase
{
    private readonly IGrantGroupService _grantGroupService;

    public GrantGroupsController(IGrantGroupService grantGroupService)
    {
        _grantGroupService = grantGroupService;
    }

    [HttpGet]
    public async Task<ActionResult<List<GrantGroupDto>>> GetAll()
    {
        return Ok(await _grantGroupService.GetAll(this.CurrentOrganizationId()));
    }

    [HttpGet("{id:guid}")]
    public async Task<ActionResult<GrantGroupDto>> GetById(Guid id)
    {
        return Ok(await _grantGroupService.GetById(this.CurrentOrganizationId(), id));
    }

    [HttpPost]
    public async Task<ActionResult<GrantGroupDto>> Create([FromBody] GrantGroupCreateRequest request)
    {
        GrantGroupDto created = await _grantGroupService.Create(this.CurrentOrganizationId(), this.CurrentUserId(), request);
        return CreatedAtAction(nameof(GetById), new { id = created.Id }, created);
    }

    [HttpPut("{id:guid}")]
    public async Task<ActionResult<GrantGroupDto>> Update(Guid id, [FromBody] GrantGroupUpdateRequest request)
    {
        return Ok(await _grantGroupService.Update(this.CurrentOrganizationId(), this.CurrentUserId(), id, request));
    }

    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> Delete(Guid id)
    {
        await _grantGroupService.Delete(this.CurrentOrganizationId(), id);
        return NoContent();
    }

    [HttpGet("assignments/{userId:guid}")]
    public async Task<ActionResult<List<Guid>>> GetAssignments(Guid userId)
    {
        return Ok(await _grantGroupService.GetAssignedGrantGroupIds(this.CurrentOrganizationId(), userId));
    }

    /// <summary>Zamjenjuje CIJELI skup GrantGroup dodjela za korisnika.</summary>
    [HttpPut("assignments/{userId:guid}")]
    public async Task<IActionResult> SetAssignments(Guid userId, [FromBody] AssignUserGrantGroupsRequest request)
    {
        await _grantGroupService.SetUserGrantGroups(this.CurrentOrganizationId(), userId, request);
        return NoContent();
    }
}