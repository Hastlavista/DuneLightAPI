using System;
using System.Threading.Tasks;
using BlueDragon.DuneLight.API.Extensions;
using BlueDragon.DuneLight.Core.DTOs.Catalog;
using BlueDragon.DuneLight.Core.Interfaces.Catalog;
using BlueDragon.DuneLight.Core.Shared;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace BlueDragon.DuneLight.API.Controllers.Catalog;

[ApiController]
[Route("api/catalog/services")]
[Produces("application/json")]
public class ServicesController : ControllerBase
{
    private readonly IServiceCatalogService _serviceCatalogService;

    public ServicesController(IServiceCatalogService serviceCatalogService)
    {
        _serviceCatalogService = serviceCatalogService;
    }

    [HttpGet]
    [Authorize(Roles = "Admin,Member")]
    public async Task<ActionResult<PagedResult<ServiceDto>>> GetPaged([FromQuery] PagedRequest request, [FromQuery] Guid? serviceCategoryId)
    {
        return Ok(await _serviceCatalogService.GetPaged(this.CurrentOrganizationId(), request, serviceCategoryId));
    }

    [HttpGet("{id:guid}")]
    [Authorize(Roles = "Admin,Member")]
    public async Task<ActionResult<ServiceDto>> GetById(Guid id)
    {
        return Ok(await _serviceCatalogService.GetById(this.CurrentOrganizationId(), id));
    }

    [HttpPost]
    [Authorize(Roles = "Admin")]
    public async Task<ActionResult<ServiceDto>> Create([FromBody] ServiceCreateRequest request)
    {
        ServiceDto created = await _serviceCatalogService.Create(this.CurrentOrganizationId(), this.CurrentUserId(), request);
        return CreatedAtAction(nameof(GetById), new { id = created.Id }, created);
    }

    [HttpPut("{id:guid}")]
    [Authorize(Roles = "Admin")]
    public async Task<ActionResult<ServiceDto>> Update(Guid id, [FromBody] ServiceUpdateRequest request)
    {
        return Ok(await _serviceCatalogService.Update(this.CurrentOrganizationId(), this.CurrentUserId(), id, request));
    }

    [HttpPatch("{id:guid}/activate")]
    [Authorize(Roles = "Admin")]
    public async Task<ActionResult<ServiceDto>> Activate(Guid id)
    {
        return Ok(await _serviceCatalogService.SetActive(this.CurrentOrganizationId(), this.CurrentUserId(), id, true));
    }

    [HttpPatch("{id:guid}/deactivate")]
    [Authorize(Roles = "Admin")]
    public async Task<ActionResult<ServiceDto>> Deactivate(Guid id)
    {
        return Ok(await _serviceCatalogService.SetActive(this.CurrentOrganizationId(), this.CurrentUserId(), id, false));
    }

    [HttpDelete("{id:guid}")]
    [Authorize(Roles = "Admin")]
    public async Task<IActionResult> Delete(Guid id)
    {
        await _serviceCatalogService.Delete(this.CurrentOrganizationId(), id);
        return NoContent();
    }
}
