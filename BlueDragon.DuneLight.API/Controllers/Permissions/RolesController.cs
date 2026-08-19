using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using BlueDragon.DuneLight.API.Authorization;
using BlueDragon.DuneLight.API.Extensions;
using BlueDragon.DuneLight.Core.DTOs.Permissions;
using BlueDragon.DuneLight.Core.Interfaces.Permissions;
using Microsoft.AspNetCore.Mvc;

namespace BlueDragon.DuneLight.API.Controllers.Permissions;

/// <summary>Poslovne oznake (Role) — samo naziv za prikaz/filtriranje, NE utječu na autorizaciju. Owner-only iz istog razloga kao GrantGroups (jedna administrativna površina).</summary>
[ApiController]
[Route("api/permissions/roles")]
[Produces("application/json")]
[RequireOwner]
public class RolesController : ControllerBase
{
    private readonly IRoleService _roleService;

    public RolesController(IRoleService roleService)
    {
        _roleService = roleService;
    }

    [HttpGet]
    public async Task<ActionResult<List<RoleDto>>> GetAll()
    {
        return Ok(await _roleService.GetAll(this.CurrentOrganizationId()));
    }

    [HttpGet("{id:guid}")]
    public async Task<ActionResult<RoleDto>> GetById(Guid id)
    {
        return Ok(await _roleService.GetById(this.CurrentOrganizationId(), id));
    }

    [HttpPost]
    public async Task<ActionResult<RoleDto>> Create([FromBody] RoleCreateRequest request)
    {
        RoleDto created = await _roleService.Create(this.CurrentOrganizationId(), this.CurrentUserId(), request);
        return CreatedAtAction(nameof(GetById), new { id = created.Id }, created);
    }

    [HttpPut("{id:guid}")]
    public async Task<ActionResult<RoleDto>> Update(Guid id, [FromBody] RoleUpdateRequest request)
    {
        return Ok(await _roleService.Update(this.CurrentOrganizationId(), this.CurrentUserId(), id, request));
    }

    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> Delete(Guid id)
    {
        await _roleService.Delete(this.CurrentOrganizationId(), id);
        return NoContent();
    }

    [HttpGet("assignments/{userId:guid}")]
    public async Task<ActionResult<List<Guid>>> GetAssignments(Guid userId)
    {
        return Ok(await _roleService.GetAssignedRoleIds(this.CurrentOrganizationId(), userId));
    }

    /// <summary>Zamjenjuje CIJELI skup Role dodjela za korisnika.</summary>
    [HttpPut("assignments/{userId:guid}")]
    public async Task<IActionResult> SetAssignments(Guid userId, [FromBody] AssignUserRolesRequest request)
    {
        await _roleService.SetUserRoles(this.CurrentOrganizationId(), userId, request);
        return NoContent();
    }
}