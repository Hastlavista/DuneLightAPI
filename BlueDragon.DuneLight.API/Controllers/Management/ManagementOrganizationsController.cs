using System;
using System.Threading.Tasks;
using BlueDragon.DuneLight.API.Authentication;
using BlueDragon.DuneLight.Core.DTOs.Management;
using BlueDragon.DuneLight.Core.Interfaces.Management;
using BlueDragon.DuneLight.Core.Shared;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace BlueDragon.DuneLight.API.Controllers.Management;

/// <summary>Cross-tenant Organization direktorij — NAMJERNO presijeca organization_id (vidi
/// IManagementHandler klasnu napomenu). Zaštićeno PlatformBearer JWT shemom (PlatformAccount identitet),
/// potpuno odvojeno od tenant RequireGrant lanca. Read-only u ovoj fazi — bez edit/delete/deactivate (vidi
/// zahtjev).</summary>
[ApiController]
[Route("api/management/organizations")]
[Produces("application/json")]
[Authorize(AuthenticationSchemes = PlatformAuthenticationDefaults.Scheme)]
public class ManagementOrganizationsController : ControllerBase
{
    private readonly IManagementReadService _managementReadService;

    public ManagementOrganizationsController(IManagementReadService managementReadService)
    {
        _managementReadService = managementReadService;
    }

    [HttpGet]
    public async Task<ActionResult<PagedResult<ManagementOrganizationListItemDto>>> GetPaged([FromQuery] PagedRequest request)
    {
        return Ok(await _managementReadService.GetOrganizations(request));
    }

    [HttpGet("{id:guid}")]
    public async Task<ActionResult<ManagementOrganizationDetailDto>> GetById(Guid id)
    {
        return Ok(await _managementReadService.GetOrganizationDetail(id));
    }
}
