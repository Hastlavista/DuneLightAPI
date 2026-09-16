using System;
using System.Threading.Tasks;
using BlueDragon.DuneLight.API.Authorization;
using BlueDragon.DuneLight.API.Extensions;
using BlueDragon.DuneLight.Core.DTOs.Dashboard;
using BlueDragon.DuneLight.Core.Interfaces.Dashboard;
using BlueDragon.DuneLight.Core.Shared;
using Microsoft.AspNetCore.Mvc;

namespace BlueDragon.DuneLight.API.Controllers.Dashboard;

/// <summary>Operativna nadzorna ploča — vidi IOperationalDashboardService za domensku napomenu. Read-only.</summary>
[ApiController]
[Route("api/dashboard")]
[Produces("application/json")]
public class DashboardController : ControllerBase
{
    private readonly IOperationalDashboardService _dashboardService;

    public DashboardController(IOperationalDashboardService dashboardService)
    {
        _dashboardService = dashboardService;
    }

    /// <summary>Datum je opcionalan — izostavljen znači tekući UTC kalendarski dan (vidi
    /// IOperationalDashboardService.GetDashboard).</summary>
    [HttpGet("operational")]
    [RequireGrant(Grants.DashboardView)]
    public async Task<ActionResult<OperationalDashboardDto>> GetOperational([FromQuery] Guid companyId, [FromQuery] DateTimeOffset? date)
    {
        return Ok(await _dashboardService.GetDashboard(this.CurrentOrganizationId(), companyId, date));
    }
}
