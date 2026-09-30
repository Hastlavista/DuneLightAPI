using System;
using System.Threading.Tasks;
using BlueDragon.DuneLight.API.Authorization;
using BlueDragon.DuneLight.API.Extensions;
using BlueDragon.DuneLight.Core.DTOs.Catalog;
using BlueDragon.DuneLight.Core.Interfaces.Catalog;
using BlueDragon.DuneLight.Core.Shared;
using Microsoft.AspNetCore.Mvc;

namespace BlueDragon.DuneLight.API.Controllers.Catalog;

[ApiController]
[Route("api/catalog/resources")]
[Produces("application/json")]
public class ResourcesController : ControllerBase
{
    private readonly IResourceService _resourceService;

    public ResourcesController(IResourceService resourceService)
    {
        _resourceService = resourceService;
    }

    [HttpGet]
    [RequireGrant(Grants.CatalogResourcesView)]
    public async Task<ActionResult<PagedResult<ResourceDto>>> GetPaged([FromQuery] PagedRequest request, [FromQuery] Guid? companyId)
    {
        return Ok(await _resourceService.GetPaged(this.CurrentOrganizationId(), companyId, request));
    }

    [HttpGet("{id:guid}")]
    [RequireGrant(Grants.CatalogResourcesView)]
    public async Task<ActionResult<ResourceDto>> GetById(Guid id)
    {
        return Ok(await _resourceService.GetById(this.CurrentOrganizationId(), id));
    }

    [HttpPost]
    [RequireGrant(Grants.CatalogResourcesManage)]
    public async Task<ActionResult<ResourceDto>> Create([FromBody] ResourceCreateRequest request)
    {
        ResourceDto created = await _resourceService.Create(this.CurrentOrganizationId(), this.CurrentUserId(), request);
        return CreatedAtAction(nameof(GetById), new { id = created.Id }, created);
    }

    [HttpPut("{id:guid}")]
    [RequireGrant(Grants.CatalogResourcesManage)]
    public async Task<ActionResult<ResourceDto>> Update(Guid id, [FromBody] ResourceUpdateRequest request)
    {
        return Ok(await _resourceService.Update(this.CurrentOrganizationId(), this.CurrentUserId(), id, request));
    }

    [HttpPatch("{id:guid}/activate")]
    [RequireGrant(Grants.CatalogResourcesManage)]
    public async Task<ActionResult<ResourceDto>> Activate(Guid id)
    {
        return Ok(await _resourceService.SetActive(this.CurrentOrganizationId(), this.CurrentUserId(), id, true));
    }

    [HttpPatch("{id:guid}/deactivate")]
    [RequireGrant(Grants.CatalogResourcesManage)]
    public async Task<ActionResult<ResourceDto>> Deactivate(Guid id)
    {
        return Ok(await _resourceService.SetActive(this.CurrentOrganizationId(), this.CurrentUserId(), id, false));
    }

    [HttpDelete("{id:guid}")]
    [RequireGrant(Grants.CatalogResourcesManage)]
    public async Task<IActionResult> Delete(Guid id)
    {
        await _resourceService.Delete(this.CurrentOrganizationId(), id);
        return NoContent();
    }
}
