using System.Collections.Generic;
using System.Threading.Tasks;
using BlueDragon.DuneLight.API.Authorization;
using BlueDragon.DuneLight.Core.DTOs.Capabilities;
using BlueDragon.DuneLight.Core.Interfaces.Capabilities;
using BlueDragon.DuneLight.Core.Shared;
using Microsoft.AspNetCore.Mvc;

namespace BlueDragon.DuneLight.API.Controllers.Permissions;

/// <summary>FAZA 1 Part R — read-only capability/predložak metapodaci za role-editor UI. Zaštićeno
/// permissions.view/permissions.manage (bilo koji); NEMA mutacijskih endpointa u ovoj fazi (vidi
/// CapabilityVersionGuard — autoritativna izmjena capability-ja/predloška je platform-only, ne izlaže se
/// tenant korisniku).</summary>
[ApiController]
[Route("api/permissions/capabilities")]
[Produces("application/json")]
[RequireGrant(Grants.PermissionsView, Grants.PermissionsManage)]
public class CapabilitiesController : ControllerBase
{
    private readonly ICapabilityReadService _capabilityReadService;

    public CapabilitiesController(ICapabilityReadService capabilityReadService)
    {
        _capabilityReadService = capabilityReadService;
    }

    [HttpGet]
    public async Task<ActionResult<List<CapabilityDefinitionDto>>> GetLatestActiveDefinitions()
    {
        return Ok(await _capabilityReadService.GetLatestActiveDefinitions());
    }

    [HttpGet("{key}")]
    public async Task<ActionResult<CapabilityDefinitionDto>> GetDefinitionDetails(string key, [FromQuery] int? version)
    {
        return Ok(await _capabilityReadService.GetDefinitionDetails(key, version));
    }

    [HttpGet("~/api/permissions/role-templates")]
    public async Task<ActionResult<List<DefaultRoleTemplateDto>>> GetLatestActiveTemplates()
    {
        return Ok(await _capabilityReadService.GetLatestActiveTemplates());
    }

    [HttpGet("~/api/permissions/role-templates/{key}")]
    public async Task<ActionResult<DefaultRoleTemplateDto>> GetTemplateDetails(string key, [FromQuery] int? version)
    {
        return Ok(await _capabilityReadService.GetTemplateDetails(key, version));
    }
}
