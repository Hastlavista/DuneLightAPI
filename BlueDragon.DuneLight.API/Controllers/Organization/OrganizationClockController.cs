using System.Threading.Tasks;
using BlueDragon.DuneLight.API.Extensions;
using BlueDragon.DuneLight.Core.DTOs.Organization;
using BlueDragon.DuneLight.Core.Interfaces.Organization;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace BlueDragon.DuneLight.API.Controllers.Organization;

/// <summary>
/// T1 — trenutno vrijeme organizacije. Frontend iz njega uzima "sada" i "danas" i nikad ne koristi sat preglednika za poslovnu
/// logiku (FE-ADR-0005). Dostupno svakom prijavljenom korisniku organizacije, bez granta.
/// </summary>
[ApiController]
[Route("api/organization/clock")]
[Produces("application/json")]
public class OrganizationClockController : ControllerBase
{
    private readonly IOrganizationClockService _organizationClockService;

    public OrganizationClockController(IOrganizationClockService organizationClockService)
    {
        _organizationClockService = organizationClockService;
    }

    [HttpGet]
    [Authorize]
    public async Task<ActionResult<OrganizationClockDto>> Get()
    {
        return Ok(await _organizationClockService.GetClock(this.CurrentOrganizationId()));
    }
}
