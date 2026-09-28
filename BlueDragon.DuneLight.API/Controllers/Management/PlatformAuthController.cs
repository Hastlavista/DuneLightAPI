using System.Net;
using System.Threading.Tasks;
using BlueDragon.DuneLight.Core.DTOs.Management;
using BlueDragon.DuneLight.Core.Interfaces.Management;
using BlueDragon.DuneLight.Core.Shared;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace BlueDragon.DuneLight.API.Controllers.Management;

/// <summary>PlatformAccount authentication — completely separate from tenant api/public/Auth/* (see
/// PlatformAccount's class doc). [AllowAnonymous] on Login only; there is no register/self-service endpoint in
/// Phase 1, accounts only come from PlatformAccountBootstrapper.</summary>
[ApiController]
[Route("api/management/auth")]
[Produces("application/json")]
public class PlatformAuthController : ControllerBase
{
    private readonly IPlatformAuthService _platformAuthService;

    public PlatformAuthController(IPlatformAuthService platformAuthService)
    {
        _platformAuthService = platformAuthService;
    }

    [HttpPost("login")]
    [AllowAnonymous]
    [ProducesResponseType(typeof(PlatformAuthResponse), (int)HttpStatusCode.OK)]
    [ProducesResponseType(typeof(ErrorResponse), (int)HttpStatusCode.Unauthorized)]
    [ProducesResponseType(typeof(ErrorResponse), (int)HttpStatusCode.BadRequest)]
    public async Task<ActionResult<PlatformAuthResponse>> Login([FromBody] PlatformLoginRequest request)
    {
        return Ok(await _platformAuthService.Login(request));
    }
}
