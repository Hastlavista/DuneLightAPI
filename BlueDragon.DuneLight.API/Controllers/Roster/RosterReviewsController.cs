using System;
using System.Threading.Tasks;
using BlueDragon.DuneLight.API.Extensions;
using BlueDragon.DuneLight.Core.DTOs.Roster;
using BlueDragon.DuneLight.Core.Interfaces.Roster;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace BlueDragon.DuneLight.API.Controllers.Roster;

/// <summary>Pregledi rostera — timski mjesečni (kao Excel "R S") i osobni po zaposleniku.</summary>
[ApiController]
[Route("api/roster")]
[Produces("application/json")]
public class RosterReviewsController : ControllerBase
{
    private readonly IRosterEntryService _rosterEntryService;

    public RosterReviewsController(IRosterEntryService rosterEntryService)
    {
        _rosterEntryService = rosterEntryService;
    }

    /// <summary>Matrica dani × zaposlenici za jedan mjesec, sa zbrojevima po vrsti. `locationId` filtrira po dodijeljenoj lokaciji zaposlenika.</summary>
    [HttpGet("team-monthly")]
    [Authorize(Roles = "Admin,Member")]
    public async Task<ActionResult<RosterTeamMonthlyDto>> GetTeamMonthly(
        [FromQuery] int year, [FromQuery] int month, [FromQuery] Guid? locationId)
    {
        return Ok(await _rosterEntryService.GetTeamMonthly(this.CurrentOrganizationId(), year, month, locationId));
    }

    /// <summary>Osobni pregled jednog zaposlenika za proizvoljno razdoblje. Member smije dohvatiti samo svoj `employeeId`.</summary>
    [HttpGet("personal")]
    [Authorize(Roles = "Admin,Member")]
    public async Task<ActionResult<RosterPersonalReviewDto>> GetPersonal(
        [FromQuery] Guid employeeId, [FromQuery] DateTimeOffset from, [FromQuery] DateTimeOffset to)
    {
        return Ok(await _rosterEntryService.GetPersonal(
            this.CurrentOrganizationId(), this.CurrentUserId(), this.CurrentIsAdmin(), employeeId, from, to));
    }
}
