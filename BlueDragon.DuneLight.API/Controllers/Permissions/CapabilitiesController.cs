using System.Collections.Generic;
using System.Linq;
using BlueDragon.DuneLight.API.Authorization;
using BlueDragon.DuneLight.Core.DTOs.Capabilities;
using BlueDragon.DuneLight.Core.Shared;
using BlueDragon.DuneLight.Core.Shared.Exceptions;
using Microsoft.AspNetCore.Mvc;

namespace BlueDragon.DuneLight.API.Controllers.Permissions;

/// <summary>Statični katalog capabilityja (CapabilityCatalog, ADR-0023) za role editor — read-only, isti obrazac kao
/// GrantsController. Zaštićeno permissions.view/permissions.manage (bilo koji).</summary>
[ApiController]
[Route("api/permissions/capabilities")]
[Produces("application/json")]
[RequireGrant(Grants.PermissionsView, Grants.PermissionsManage)]
public class CapabilitiesController : ControllerBase
{
    [HttpGet]
    public ActionResult<List<CapabilityDefinitionDto>> GetAll()
    {
        return Ok(CapabilityCatalog.All.Select(ToDto).ToList());
    }

    [HttpGet("{key}")]
    public ActionResult<CapabilityDefinitionDto> GetByKey(string key)
    {
        CapabilityDefinition capability = CapabilityCatalog.Find(key);
        if (capability == null)
            throw new NotFoundAppException("Capability", key);

        return Ok(ToDto(capability));
    }

    private static CapabilityDefinitionDto ToDto(CapabilityDefinition capability) => new(
        capability.Key,
        capability.CategoryKey,
        capability.ScopeModel,
        capability.Sensitivity,
        capability.Grants.Select(g => new CapabilityDefinitionGrantDto(g.GrantKey, g.Role)).ToList());
}
