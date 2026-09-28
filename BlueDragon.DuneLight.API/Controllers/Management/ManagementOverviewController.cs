using System.Threading.Tasks;
using BlueDragon.DuneLight.API.Authentication;
using BlueDragon.DuneLight.Core.DTOs.Management;
using BlueDragon.DuneLight.Core.Interfaces.Management;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace BlueDragon.DuneLight.API.Controllers.Management;

/// <summary>DuneLight Platform Management — internal control plane, potpuno odvojena autorizacija od tenant
/// grantova. Zaštićeno PlatformBearer JWT shemom (PlatformAccount identitet, vidi PlatformAuthenticationDefaults).
/// Samo metrike koje domena danas pouzdano podržava — vidi ManagementOverviewDto napomenu.</summary>
[ApiController]
[Route("api/management/overview")]
[Produces("application/json")]
[Authorize(AuthenticationSchemes = PlatformAuthenticationDefaults.Scheme)]
public class ManagementOverviewController : ControllerBase
{
    private readonly IManagementReadService _managementReadService;

    public ManagementOverviewController(IManagementReadService managementReadService)
    {
        _managementReadService = managementReadService;
    }

    [HttpGet]
    public async Task<ActionResult<ManagementOverviewDto>> Get()
    {
        return Ok(await _managementReadService.GetOverview());
    }
}
