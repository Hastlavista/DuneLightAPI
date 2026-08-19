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
[Route("api/clients/{clientId:guid}/packages")]
[Produces("application/json")]
public class ClientPackagesController : ControllerBase
{
    private readonly IClientPackageService _clientPackageService;

    public ClientPackagesController(IClientPackageService clientPackageService)
    {
        _clientPackageService = clientPackageService;
    }

    [HttpGet]
    [RequireGrant(Grants.ClientsPackagesView)]
    public async Task<ActionResult<List<ClientPackageDto>>> GetByClient(Guid clientId)
    {
        return Ok(await _clientPackageService.GetByClient(this.CurrentOrganizationId(), clientId));
    }

    [HttpGet("{id:guid}")]
    [RequireGrant(Grants.ClientsPackagesView)]
    public async Task<ActionResult<ClientPackageDto>> GetById(Guid clientId, Guid id)
    {
        return Ok(await _clientPackageService.GetById(this.CurrentOrganizationId(), clientId, id));
    }

    /// <summary>Aktivni paketi klijenta koji pokrivaju uslugu i imaju preostalih ulazaka — za odabir kod plaćanja termina.</summary>
    [HttpGet("eligible")]
    [RequireGrant(Grants.ClientsPackagesView)]
    public async Task<ActionResult<List<ClientPackageDto>>> GetEligible(Guid clientId, [FromQuery] Guid serviceId, [FromQuery] DateTimeOffset? date)
    {
        return Ok(await _clientPackageService.GetEligibleForService(
            this.CurrentOrganizationId(), clientId, serviceId, date ?? DateTimeOffset.UtcNow));
    }

    [HttpPost]
    [RequireGrant(Grants.ClientsPackagesManage)]
    public async Task<ActionResult<ClientPackageDto>> Create(Guid clientId, [FromBody] ClientPackageCreateRequest request)
    {
        ClientPackageDto created = await _clientPackageService.Create(this.CurrentOrganizationId(), this.CurrentUserId(), clientId, request);
        return CreatedAtAction(nameof(GetById), new { clientId, id = created.Id }, created);
    }
}
