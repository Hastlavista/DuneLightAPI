using System;
using System.Threading.Tasks;
using BlueDragon.DuneLight.API.Authentication;
using BlueDragon.DuneLight.Core.DTOs.Management;
using BlueDragon.DuneLight.Core.Interfaces.Management;
using BlueDragon.DuneLight.Core.Shared;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace BlueDragon.DuneLight.API.Controllers.Management;

/// <summary>Cross-tenant User direktorij — NAMJERNO presijeca organization_id (vidi IManagementHandler klasnu
/// napomenu). PagedRequest.IsActive se ovdje odnosi na User.IsActive (aktivan/deaktiviran korisnik), ne na
/// nepostojeći Organization lifecycle. organizationId je zaseban query filter (paralelno EmployeesController
/// GetPaged obrascu s dodatnim [FromQuery] parametrima uz PagedRequest). Zaštićeno PlatformBearer JWT shemom.</summary>
[ApiController]
[Route("api/management/users")]
[Produces("application/json")]
[Authorize(AuthenticationSchemes = PlatformAuthenticationDefaults.Scheme)]
public class ManagementUsersController : ControllerBase
{
    private readonly IManagementReadService _managementReadService;

    public ManagementUsersController(IManagementReadService managementReadService)
    {
        _managementReadService = managementReadService;
    }

    [HttpGet]
    public async Task<ActionResult<PagedResult<ManagementUserListItemDto>>> GetPaged([FromQuery] PagedRequest request, [FromQuery] Guid? organizationId)
    {
        return Ok(await _managementReadService.GetUsers(request, organizationId));
    }
}
