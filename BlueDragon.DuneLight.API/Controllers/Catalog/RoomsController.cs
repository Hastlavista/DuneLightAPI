using System;
using System.Threading.Tasks;
using BlueDragon.DuneLight.API.Authorization;
using BlueDragon.DuneLight.API.Extensions;
using BlueDragon.DuneLight.Core.DTOs.Catalog;
using BlueDragon.DuneLight.Core.Interfaces.Catalog;
using BlueDragon.DuneLight.Core.Shared;
using Microsoft.AspNetCore.Mvc;

namespace BlueDragon.DuneLight.API.Controllers.Catalog;

[ApiController]
[Route("api/catalog/rooms")]
[Produces("application/json")]
public class RoomsController : ControllerBase
{
    private readonly IRoomService _roomService;

    public RoomsController(IRoomService roomService)
    {
        _roomService = roomService;
    }

    [HttpGet]
    [RequireGrant(Grants.CatalogRoomsView)]
    public async Task<ActionResult<PagedResult<RoomDto>>> GetPaged([FromQuery] PagedRequest request, [FromQuery] Guid? companyId)
    {
        return Ok(await _roomService.GetPaged(this.CurrentOrganizationId(), companyId, request));
    }

    [HttpGet("{id:guid}")]
    [RequireGrant(Grants.CatalogRoomsView)]
    public async Task<ActionResult<RoomDto>> GetById(Guid id)
    {
        return Ok(await _roomService.GetById(this.CurrentOrganizationId(), id));
    }

    [HttpPost]
    [RequireGrant(Grants.CatalogRoomsManage)]
    public async Task<ActionResult<RoomDto>> Create([FromBody] RoomCreateRequest request)
    {
        RoomDto created = await _roomService.Create(this.CurrentOrganizationId(), this.CurrentUserId(), request);
        return CreatedAtAction(nameof(GetById), new { id = created.Id }, created);
    }

    [HttpPut("{id:guid}")]
    [RequireGrant(Grants.CatalogRoomsManage)]
    public async Task<ActionResult<RoomDto>> Update(Guid id, [FromBody] RoomUpdateRequest request)
    {
        return Ok(await _roomService.Update(this.CurrentOrganizationId(), this.CurrentUserId(), id, request));
    }

    [HttpPatch("{id:guid}/activate")]
    [RequireGrant(Grants.CatalogRoomsManage)]
    public async Task<ActionResult<RoomDto>> Activate(Guid id)
    {
        return Ok(await _roomService.SetActive(this.CurrentOrganizationId(), this.CurrentUserId(), id, true));
    }

    [HttpPatch("{id:guid}/deactivate")]
    [RequireGrant(Grants.CatalogRoomsManage)]
    public async Task<ActionResult<RoomDto>> Deactivate(Guid id)
    {
        return Ok(await _roomService.SetActive(this.CurrentOrganizationId(), this.CurrentUserId(), id, false));
    }

    [HttpDelete("{id:guid}")]
    [RequireGrant(Grants.CatalogRoomsManage)]
    public async Task<IActionResult> Delete(Guid id)
    {
        await _roomService.Delete(this.CurrentOrganizationId(), id);
        return NoContent();
    }
}
