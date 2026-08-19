using System;
using System.Threading.Tasks;
using BlueDragon.DuneLight.API.Authorization;
using BlueDragon.DuneLight.API.Extensions;
using BlueDragon.DuneLight.Core.DTOs.Roster;
using BlueDragon.DuneLight.Core.Interfaces.Roster;
using BlueDragon.DuneLight.Core.Shared;
using Microsoft.AspNetCore.Mvc;

namespace BlueDragon.DuneLight.API.Controllers.Roster;

/// <summary>Šifrarnik vrsta rostera — zajednički za firmu, svima dostupan za čitanje, uređuje ga samo Admin.</summary>
[ApiController]
[Route("api/roster/types")]
[Produces("application/json")]
public class RosterTypesController : ControllerBase
{
    private readonly IRosterTypeService _rosterTypeService;

    public RosterTypesController(IRosterTypeService rosterTypeService)
    {
        _rosterTypeService = rosterTypeService;
    }

    [HttpGet]
    [RequireGrant(Grants.RosterTypesView)]
    public async Task<ActionResult<PagedResult<RosterTypeDto>>> GetPaged([FromQuery] PagedRequest request)
    {
        return Ok(await _rosterTypeService.GetPaged(this.CurrentOrganizationId(), request));
    }

    [HttpGet("{id:guid}")]
    [RequireGrant(Grants.RosterTypesView)]
    public async Task<ActionResult<RosterTypeDto>> GetById(Guid id)
    {
        return Ok(await _rosterTypeService.GetById(this.CurrentOrganizationId(), id));
    }

    [HttpPost]
    [RequireGrant(Grants.RosterTypesManage)]
    public async Task<ActionResult<RosterTypeDto>> Create([FromBody] RosterTypeCreateRequest request)
    {
        RosterTypeDto created = await _rosterTypeService.Create(this.CurrentOrganizationId(), this.CurrentUserId(), request);
        return CreatedAtAction(nameof(GetById), new { id = created.Id }, created);
    }

    [HttpPut("{id:guid}")]
    [RequireGrant(Grants.RosterTypesManage)]
    public async Task<ActionResult<RosterTypeDto>> Update(Guid id, [FromBody] RosterTypeUpdateRequest request)
    {
        return Ok(await _rosterTypeService.Update(this.CurrentOrganizationId(), this.CurrentUserId(), id, request));
    }

    [HttpPatch("{id:guid}/activate")]
    [RequireGrant(Grants.RosterTypesManage)]
    public async Task<ActionResult<RosterTypeDto>> Activate(Guid id)
    {
        return Ok(await _rosterTypeService.SetActive(this.CurrentOrganizationId(), this.CurrentUserId(), id, true));
    }

    [HttpPatch("{id:guid}/deactivate")]
    [RequireGrant(Grants.RosterTypesManage)]
    public async Task<ActionResult<RosterTypeDto>> Deactivate(Guid id)
    {
        return Ok(await _rosterTypeService.SetActive(this.CurrentOrganizationId(), this.CurrentUserId(), id, false));
    }

    [HttpDelete("{id:guid}")]
    [RequireGrant(Grants.RosterTypesManage)]
    public async Task<IActionResult> Delete(Guid id)
    {
        await _rosterTypeService.Delete(this.CurrentOrganizationId(), id);
        return NoContent();
    }
}
