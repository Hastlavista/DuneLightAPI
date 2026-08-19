using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using BlueDragon.DuneLight.API.Authorization;
using BlueDragon.DuneLight.API.Extensions;
using BlueDragon.DuneLight.Core.DTOs.Groups;
using BlueDragon.DuneLight.Core.Interfaces.Groups;
using BlueDragon.DuneLight.Core.Shared;
using Microsoft.AspNetCore.Mvc;

namespace BlueDragon.DuneLight.API.Controllers.Clients;

[ApiController]
[Route("api/clients/{clientId:guid}/groups")]
[Produces("application/json")]
public class ClientGroupsController : ControllerBase
{
    private readonly IGroupService _groupService;

    public ClientGroupsController(IGroupService groupService)
    {
        _groupService = groupService;
    }

    /// <summary>Grupe čiji je klijent član — aktivno i povijesno (za dopunu Klijent detalja).</summary>
    [HttpGet]
    [RequireGrant(Grants.GroupsView)]
    public async Task<ActionResult<List<ClientGroupMembershipDto>>> GetByClient(Guid clientId)
    {
        return Ok(await _groupService.GetMembershipsByClient(this.CurrentOrganizationId(), clientId));
    }
}
