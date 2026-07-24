using System;
using System.Threading.Tasks;
using BlueDragon.DuneLight.API.Extensions;
using BlueDragon.DuneLight.Core.DTOs.Roster;
using BlueDragon.DuneLight.Core.Interfaces.Roster;
using BlueDragon.DuneLight.Core.Shared;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace BlueDragon.DuneLight.API.Controllers.Roster;

/// <summary>Evidencija radnog vremena i odsutnosti. Svi vide sve; Member smije mijenjati/brisati samo svoje zapise (vlasništvo se provjerava u servisu).</summary>
[ApiController]
[Route("api/roster/entries")]
[Produces("application/json")]
public class RosterEntriesController : ControllerBase
{
    private readonly IRosterEntryService _rosterEntryService;

    public RosterEntriesController(IRosterEntryService rosterEntryService)
    {
        _rosterEntryService = rosterEntryService;
    }

    [HttpGet]
    [Authorize(Roles = "Admin,Member")]
    public async Task<ActionResult<PagedResult<RosterEntryDto>>> GetPaged(
        [FromQuery] PagedRequest request, [FromQuery] Guid? employeeId, [FromQuery] Guid? rosterTypeId,
        [FromQuery] DateTimeOffset? from, [FromQuery] DateTimeOffset? to)
    {
        return Ok(await _rosterEntryService.GetPaged(this.CurrentOrganizationId(), request, employeeId, rosterTypeId, from, to));
    }

    [HttpGet("{id:guid}")]
    [Authorize(Roles = "Admin,Member")]
    public async Task<ActionResult<RosterEntryDto>> GetById(Guid id)
    {
        return Ok(await _rosterEntryService.GetById(this.CurrentOrganizationId(), id));
    }

    [HttpPost]
    [Authorize(Roles = "Admin,Member")]
    public async Task<ActionResult<RosterEntryDto>> Create([FromBody] RosterEntryCreateRequest request)
    {
        RosterEntryDto created = await _rosterEntryService.Create(
            this.CurrentOrganizationId(), this.CurrentUserId(), this.CurrentIsAdmin(), request);
        return CreatedAtAction(nameof(GetById), new { id = created.Id }, created);
    }

    [HttpPut("{id:guid}")]
    [Authorize(Roles = "Admin,Member")]
    public async Task<ActionResult<RosterEntryDto>> Update(Guid id, [FromBody] RosterEntryUpdateRequest request)
    {
        return Ok(await _rosterEntryService.Update(
            this.CurrentOrganizationId(), this.CurrentUserId(), this.CurrentIsAdmin(), id, request));
    }

    [HttpDelete("{id:guid}")]
    [Authorize(Roles = "Admin,Member")]
    public async Task<IActionResult> Delete(Guid id)
    {
        await _rosterEntryService.Delete(this.CurrentOrganizationId(), this.CurrentUserId(), this.CurrentIsAdmin(), id);
        return NoContent();
    }
}
