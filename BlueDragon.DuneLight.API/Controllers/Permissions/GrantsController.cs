using System.Collections.Generic;
using BlueDragon.DuneLight.API.Authorization;
using BlueDragon.DuneLight.Core.Shared;
using Microsoft.AspNetCore.Mvc;

namespace BlueDragon.DuneLight.API.Controllers.Permissions;

/// <summary>Statični katalog svih grant-ključeva u sustavu — UI ga koristi za slaganje GrantGroup-a. Owner-only, isto obrazloženje kao GrantGroupsController.</summary>
[ApiController]
[Route("api/grants")]
[Produces("application/json")]
[RequireOwner]
public class GrantsController : ControllerBase
{
    [HttpGet]
    public ActionResult<IReadOnlyList<GrantDefinition>> GetAll()
    {
        return Ok(Grants.Catalog);
    }
}