using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using BlueDragon.DuneLight.API.Authorization;
using BlueDragon.DuneLight.API.Extensions;
using BlueDragon.DuneLight.Core.DTOs.Clients;
using BlueDragon.DuneLight.Core.Interfaces.Clients;
using BlueDragon.DuneLight.Core.Shared;
using Microsoft.AspNetCore.Mvc;

namespace BlueDragon.DuneLight.API.Controllers.Clients;

[ApiController]
[Route("api/clients")]
[Produces("application/json")]
public class ClientsController : ControllerBase
{
    private readonly IClientService _clientService;

    public ClientsController(IClientService clientService)
    {
        _clientService = clientService;
    }

    /// <summary>Popis klijenata. mineFirst=true stavlja klijente prijavljenog trenera prve (redoslijed, ne filter).</summary>
    [HttpGet]
    [RequireGrant(Grants.ClientsView)]
    public async Task<ActionResult<PagedResult<ClientDto>>> GetPaged(
        [FromQuery] PagedRequest request, [FromQuery] Guid? tagId, [FromQuery] Guid? homeTrainerId,
        [FromQuery] Guid? homeCompanyId, [FromQuery] bool mineFirst = false)
    {
        return Ok(await _clientService.GetPaged(
            this.CurrentOrganizationId(), request, tagId, homeTrainerId, homeCompanyId, mineFirst, this.CurrentUserId()));
    }

    /// <summary>Klijenti kojima je rođendan u zadanom razdoblju (godina se zanemaruje), sortirano po datumu.</summary>
    [HttpGet("birthdays")]
    [RequireGrant(Grants.ClientsView)]
    public async Task<ActionResult<List<ClientBirthdayDto>>> GetBirthdays([FromQuery] DateTimeOffset from, [FromQuery] DateTimeOffset to)
    {
        return Ok(await _clientService.GetBirthdays(this.CurrentOrganizationId(), from, to));
    }

    /// <summary>Prijedlog sljedećeg slobodnog broja člana (admin/trener ga može promijeniti pri kreiranju).</summary>
    [HttpGet("next-member-number")]
    [RequireGrant(Grants.ClientsView)]
    public async Task<ActionResult<int>> GetNextMemberNumber()
    {
        return Ok(await _clientService.GetNextMemberNumberSuggestion(this.CurrentOrganizationId()));
    }

    [HttpGet("{id:guid}")]
    [RequireGrant(Grants.ClientsView)]
    public async Task<ActionResult<ClientDto>> GetById(Guid id)
    {
        return Ok(await _clientService.GetById(this.CurrentOrganizationId(), id));
    }

    [HttpPost]
    [RequireGrant(Grants.ClientsManage)]
    public async Task<ActionResult<ClientDto>> Create([FromBody] ClientCreateRequest request)
    {
        ClientDto created = await _clientService.Create(this.CurrentOrganizationId(), this.CurrentUserId(), request);
        return CreatedAtAction(nameof(GetById), new { id = created.Id }, created);
    }

    [HttpPut("{id:guid}")]
    [RequireGrant(Grants.ClientsManage)]
    public async Task<ActionResult<ClientDto>> Update(Guid id, [FromBody] ClientUpdateRequest request)
    {
        return Ok(await _clientService.Update(this.CurrentOrganizationId(), this.CurrentUserId(), id, request));
    }

    [HttpPatch("{id:guid}/activate")]
    [RequireGrant(Grants.ClientsStatusManage)]
    public async Task<ActionResult<ClientDto>> Activate(Guid id)
    {
        return Ok(await _clientService.SetActive(this.CurrentOrganizationId(), this.CurrentUserId(), id, true));
    }

    [HttpPatch("{id:guid}/deactivate")]
    [RequireGrant(Grants.ClientsStatusManage)]
    public async Task<ActionResult<ClientDto>> Deactivate(Guid id)
    {
        return Ok(await _clientService.SetActive(this.CurrentOrganizationId(), this.CurrentUserId(), id, false));
    }

    [HttpDelete("{id:guid}")]
    [RequireGrant(Grants.ClientsStatusManage)]
    public async Task<IActionResult> Delete(Guid id)
    {
        await _clientService.Delete(this.CurrentOrganizationId(), id);
        return NoContent();
    }

    /// <summary>GDPR pravo na zaborav — nepovratno uklanja osobne/zdravstvene podatke, čuva Id/broj člana.</summary>
    [HttpPost("{id:guid}/anonymize")]
    [RequireGrant(Grants.ClientsAnonymize)]
    public async Task<ActionResult<ClientDto>> Anonymize(Guid id)
    {
        return Ok(await _clientService.Anonymize(this.CurrentOrganizationId(), this.CurrentUserId(), id));
    }
}
