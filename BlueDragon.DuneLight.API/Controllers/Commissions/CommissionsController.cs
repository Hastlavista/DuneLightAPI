using System.Threading.Tasks;
using BlueDragon.DuneLight.API.Authorization;
using BlueDragon.DuneLight.API.Extensions;
using BlueDragon.DuneLight.Core.DTOs.Commissions;
using BlueDragon.DuneLight.Core.Interfaces.Commissions;
using BlueDragon.DuneLight.Core.Shared;
using Microsoft.AspNetCore.Mvc;

namespace BlueDragon.DuneLight.API.Controllers.Commissions;

/// <summary>Read-only ledger — koliko je provizije zarađeno po zaposleniku/razdoblju (vidi ICommissionService).
/// Konfiguracija pravila ide kroz CommissionRulesController.</summary>
[ApiController]
[Route("api/commissions")]
[Produces("application/json")]
public class CommissionsController : ControllerBase
{
    private readonly ICommissionService _commissionService;

    public CommissionsController(ICommissionService commissionService)
    {
        _commissionService = commissionService;
    }

    [HttpGet("entries")]
    [RequireGrant(Grants.CommissionsView, Grants.CommissionsManage)]
    public async Task<ActionResult<PagedResult<CommissionEntryDto>>> GetEntries([FromQuery] CommissionEntryQuery query)
    {
        return Ok(await _commissionService.GetEntries(this.CurrentOrganizationId(), query));
    }

    [HttpGet("summary")]
    [RequireGrant(Grants.CommissionsView, Grants.CommissionsManage)]
    public async Task<ActionResult<CommissionSummaryResultDto>> GetSummary([FromQuery] CommissionSummaryQuery query)
    {
        return Ok(await _commissionService.GetSummary(this.CurrentOrganizationId(), query));
    }
}
