using System.Threading.Tasks;
using BlueDragon.DuneLight.API.Authorization;
using BlueDragon.DuneLight.API.Extensions;
using BlueDragon.DuneLight.Core.DTOs.Commissions;
using BlueDragon.DuneLight.Core.Interfaces.Commissions;
using BlueDragon.DuneLight.Core.DTOs.Organization;
using BlueDragon.DuneLight.Core.Interfaces.Organization;
using BlueDragon.DuneLight.Core.Shared;
using Microsoft.AspNetCore.Mvc;

namespace BlueDragon.DuneLight.API.Controllers.Commissions;

/// <summary>Ledger provizija (povijest, sažetak po događajima, korekcija i naknadna dodjela korisnika provizije na prodaju) i
/// postavke provizija organizacije (P2 2F, commissions.manage jer izravno mijenjaju zaradu zaposlenika).
/// Konfiguracija pravila ide kroz CommissionRulesController.</summary>
[ApiController]
[Route("api/commissions")]
[Produces("application/json")]
public class CommissionsController : ControllerBase
{
    private readonly ICommissionService _commissionService;
    private readonly IOrganizationSettingsService _settingsService;

    public CommissionsController(ICommissionService commissionService, IOrganizationSettingsService settingsService)
    {
        _commissionService = commissionService;
        _settingsService = settingsService;
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

    /// <summary>P2 (2F, Q50) — korekcija korisnika provizije na prodaju nakon nastanka: storno postojeće provizije i nova
    /// provizija za novog korisnika (po njegovom pravilu važećem na datum nastanka), obavezan razlog.</summary>
    [HttpPost("entries/{id:guid}/reassign")]
    [RequireGrant(Grants.CommissionsManage)]
    public async Task<ActionResult<CommissionEntryReassignResultDto>> Reassign(System.Guid id, [FromBody] CommissionEntryReassignRequest request)
    {
        return Ok(await _commissionService.Reassign(this.CurrentOrganizationId(), this.CurrentUserId(), id, request));
    }

    /// <summary>P2 (2F, pregled — izbor 8) — naknadna dodjela korisnika provizije na prodaju kad provizija nije nastala jer korisnika
    /// nije bilo (proizvod/paket zatvorenog checkouta ili prva prodaja članarine); provizija nastaje po pravilu važećem na izvorni
    /// datum nastanka. "Nema pravila" i "osnovica 0" su konačni (COMMISSION_SALE_NOT_ASSIGNABLE).</summary>
    [HttpPost("sale-assignments")]
    [RequireGrant(Grants.CommissionsManage)]
    public async Task<ActionResult<CommissionSaleAssignmentResultDto>> AssignSale([FromBody] CommissionSaleAssignmentRequest request)
    {
        return Ok(await _commissionService.AssignSale(this.CurrentOrganizationId(), this.CurrentUserId(), request));
    }

    /// <summary>P2 (2F, Vagaro) — postavke provizija: "oduzmi popuste", "oduzmi popuste članstva" (sve isključeno = cijena sesije) i
    /// provizija kod kasnog otkaza / izostanka (Never | WhenFeePaid).</summary>
    [HttpGet("settings")]
    [RequireGrant(Grants.CommissionsRulesView, Grants.CommissionsManage)]
    public async Task<ActionResult<OrganizationCommissionSettingsDto>> GetSettings()
    {
        return Ok(await _settingsService.GetCommissionSettings(this.CurrentOrganizationId()));
    }

    [HttpPut("settings")]
    [RequireGrant(Grants.CommissionsManage)]
    public async Task<ActionResult<OrganizationCommissionSettingsDto>> UpdateSettings([FromBody] OrganizationCommissionSettingsDto request)
    {
        return Ok(await _settingsService.UpdateCommissionSettings(this.CurrentOrganizationId(), this.CurrentUserId(), request));
    }
}
