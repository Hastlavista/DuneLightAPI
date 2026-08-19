using System;
using System.Threading.Tasks;
using BlueDragon.DuneLight.API.Authorization;
using BlueDragon.DuneLight.API.Extensions;
using BlueDragon.DuneLight.Core.DTOs.Clients;
using BlueDragon.DuneLight.Core.Interfaces.Clients;
using BlueDragon.DuneLight.Core.Shared;
using Microsoft.AspNetCore.Mvc;

namespace BlueDragon.DuneLight.API.Controllers.Clients;

[ApiController]
[Route("api/clients/tags")]
[Produces("application/json")]
public class ClientTagsController : ControllerBase
{
    private readonly IClientTagService _clientTagService;

    public ClientTagsController(IClientTagService clientTagService)
    {
        _clientTagService = clientTagService;
    }

    [HttpGet]
    [RequireGrant(Grants.ClientsTagsView)]
    public async Task<ActionResult<PagedResult<ClientTagDto>>> GetPaged([FromQuery] PagedRequest request)
    {
        return Ok(await _clientTagService.GetPaged(this.CurrentOrganizationId(), request));
    }

    [HttpGet("{id:guid}")]
    [RequireGrant(Grants.ClientsTagsView)]
    public async Task<ActionResult<ClientTagDto>> GetById(Guid id)
    {
        return Ok(await _clientTagService.GetById(this.CurrentOrganizationId(), id));
    }

    [HttpPost]
    [RequireGrant(Grants.ClientsTagsManage)]
    public async Task<ActionResult<ClientTagDto>> Create([FromBody] ClientTagCreateRequest request)
    {
        ClientTagDto created = await _clientTagService.Create(this.CurrentOrganizationId(), this.CurrentUserId(), request);
        return CreatedAtAction(nameof(GetById), new { id = created.Id }, created);
    }

    [HttpPut("{id:guid}")]
    [RequireGrant(Grants.ClientsTagsManage)]
    public async Task<ActionResult<ClientTagDto>> Update(Guid id, [FromBody] ClientTagUpdateRequest request)
    {
        return Ok(await _clientTagService.Update(this.CurrentOrganizationId(), this.CurrentUserId(), id, request));
    }

    [HttpPatch("{id:guid}/activate")]
    [RequireGrant(Grants.ClientsTagsManage)]
    public async Task<ActionResult<ClientTagDto>> Activate(Guid id)
    {
        return Ok(await _clientTagService.SetActive(this.CurrentOrganizationId(), this.CurrentUserId(), id, true));
    }

    [HttpPatch("{id:guid}/deactivate")]
    [RequireGrant(Grants.ClientsTagsManage)]
    public async Task<ActionResult<ClientTagDto>> Deactivate(Guid id)
    {
        return Ok(await _clientTagService.SetActive(this.CurrentOrganizationId(), this.CurrentUserId(), id, false));
    }

    [HttpDelete("{id:guid}")]
    [RequireGrant(Grants.ClientsTagsManage)]
    public async Task<IActionResult> Delete(Guid id)
    {
        await _clientTagService.Delete(this.CurrentOrganizationId(), id);
        return NoContent();
    }
}
