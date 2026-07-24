using System;
using System.Threading.Tasks;
using BlueDragon.DuneLight.API.Extensions;
using BlueDragon.DuneLight.Core.DTOs.Catalog;
using BlueDragon.DuneLight.Core.Interfaces.Catalog;
using BlueDragon.DuneLight.Core.Shared;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace BlueDragon.DuneLight.API.Controllers.Catalog;

[ApiController]
[Route("api/catalog/locations")]
[Produces("application/json")]
public class LocationsController : ControllerBase
{
    private readonly ILocationService _locationService;

    public LocationsController(ILocationService locationService)
    {
        _locationService = locationService;
    }

    [HttpGet]
    [Authorize(Roles = "Admin,Member")]
    public async Task<ActionResult<PagedResult<LocationDto>>> GetPaged([FromQuery] PagedRequest request)
    {
        return Ok(await _locationService.GetPaged(this.CurrentOrganizationId(), request));
    }

    [HttpGet("{id:guid}")]
    [Authorize(Roles = "Admin,Member")]
    public async Task<ActionResult<LocationDto>> GetById(Guid id)
    {
        return Ok(await _locationService.GetById(this.CurrentOrganizationId(), id));
    }

    [HttpPost]
    [Authorize(Roles = "Admin")]
    public async Task<ActionResult<LocationDto>> Create([FromBody] LocationCreateRequest request)
    {
        LocationDto created = await _locationService.Create(this.CurrentOrganizationId(), this.CurrentUserId(), request);
        return CreatedAtAction(nameof(GetById), new { id = created.Id }, created);
    }

    [HttpPut("{id:guid}")]
    [Authorize(Roles = "Admin")]
    public async Task<ActionResult<LocationDto>> Update(Guid id, [FromBody] LocationUpdateRequest request)
    {
        return Ok(await _locationService.Update(this.CurrentOrganizationId(), this.CurrentUserId(), id, request));
    }

    [HttpPatch("{id:guid}/activate")]
    [Authorize(Roles = "Admin")]
    public async Task<ActionResult<LocationDto>> Activate(Guid id)
    {
        return Ok(await _locationService.SetActive(this.CurrentOrganizationId(), this.CurrentUserId(), id, true));
    }

    [HttpPatch("{id:guid}/deactivate")]
    [Authorize(Roles = "Admin")]
    public async Task<ActionResult<LocationDto>> Deactivate(Guid id)
    {
        return Ok(await _locationService.SetActive(this.CurrentOrganizationId(), this.CurrentUserId(), id, false));
    }

    [HttpDelete("{id:guid}")]
    [Authorize(Roles = "Admin")]
    public async Task<IActionResult> Delete(Guid id)
    {
        await _locationService.Delete(this.CurrentOrganizationId(), id);
        return NoContent();
    }
}
