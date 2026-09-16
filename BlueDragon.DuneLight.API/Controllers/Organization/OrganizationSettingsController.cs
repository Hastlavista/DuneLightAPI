using System.Threading.Tasks;
using BlueDragon.DuneLight.API.Authorization;
using BlueDragon.DuneLight.API.Extensions;
using BlueDragon.DuneLight.Core.DTOs.Organization;
using BlueDragon.DuneLight.Core.Interfaces.Organization;
using BlueDragon.DuneLight.Core.Shared;
using Microsoft.AspNetCore.Mvc;

namespace BlueDragon.DuneLight.API.Controllers.Organization;

/// <summary>
/// Poslovne postavke organizacije — odvojeno od OrganizationBrandingController (vizualni identitet). Trenutno
/// samo CancellationCutoffMinutes (rok za "normalno" vs "kasno" otkazivanje Bookinga, vidi BookingCancellationPolicy).
/// </summary>
[ApiController]
[Route("api/organization/settings")]
[Produces("application/json")]
public class OrganizationSettingsController : ControllerBase
{
    private readonly IOrganizationSettingsService _organizationSettingsService;

    public OrganizationSettingsController(IOrganizationSettingsService organizationSettingsService)
    {
        _organizationSettingsService = organizationSettingsService;
    }

    [HttpGet]
    [RequireGrant(Grants.OrganizationSettingsManage)]
    public async Task<ActionResult<OrganizationSettingsDto>> GetSettings()
    {
        return Ok(await _organizationSettingsService.GetSettings(this.CurrentOrganizationId()));
    }

    [HttpPut("cancellation-cutoff")]
    [RequireGrant(Grants.OrganizationSettingsManage)]
    public async Task<ActionResult<OrganizationSettingsDto>> UpdateCancellationCutoff([FromBody] OrganizationSettingsUpdateRequest request)
    {
        return Ok(await _organizationSettingsService.UpdateCancellationCutoff(
            this.CurrentOrganizationId(), this.CurrentUserId(), request));
    }
}
