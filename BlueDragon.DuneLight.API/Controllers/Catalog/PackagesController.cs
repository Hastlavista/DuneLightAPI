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
[Route("api/catalog/packages")]
[Produces("application/json")]
public class PackagesController : ControllerBase
{
    private readonly IPackageService _packageService;

    public PackagesController(IPackageService packageService)
    {
        _packageService = packageService;
    }

    [HttpGet]
    [RequireGrant(Grants.CatalogPackagesView)]
    public async Task<ActionResult<PagedResult<PackageDto>>> GetPaged([FromQuery] PagedRequest request)
    {
        return Ok(await _packageService.GetPaged(this.CurrentOrganizationId(), request));
    }

    [HttpGet("{id:guid}")]
    [RequireGrant(Grants.CatalogPackagesView)]
    public async Task<ActionResult<PackageDto>> GetById(Guid id)
    {
        return Ok(await _packageService.GetById(this.CurrentOrganizationId(), id));
    }

    [HttpPost]
    [RequireGrant(Grants.CatalogPackagesManage)]
    public async Task<ActionResult<PackageDto>> Create([FromBody] PackageCreateRequest request)
    {
        PackageDto created = await _packageService.Create(this.CurrentOrganizationId(), this.CurrentUserId(), request);
        return CreatedAtAction(nameof(GetById), new { id = created.Id }, created);
    }

    [HttpPut("{id:guid}")]
    [RequireGrant(Grants.CatalogPackagesManage)]
    public async Task<ActionResult<PackageDto>> Update(Guid id, [FromBody] PackageUpdateRequest request)
    {
        return Ok(await _packageService.Update(this.CurrentOrganizationId(), this.CurrentUserId(), id, request));
    }

    [HttpPatch("{id:guid}/activate")]
    [RequireGrant(Grants.CatalogPackagesManage)]
    public async Task<ActionResult<PackageDto>> Activate(Guid id)
    {
        return Ok(await _packageService.SetActive(this.CurrentOrganizationId(), this.CurrentUserId(), id, true));
    }

    [HttpPatch("{id:guid}/deactivate")]
    [RequireGrant(Grants.CatalogPackagesManage)]
    public async Task<ActionResult<PackageDto>> Deactivate(Guid id)
    {
        return Ok(await _packageService.SetActive(this.CurrentOrganizationId(), this.CurrentUserId(), id, false));
    }

    [HttpDelete("{id:guid}")]
    [RequireGrant(Grants.CatalogPackagesManage)]
    public async Task<IActionResult> Delete(Guid id)
    {
        await _packageService.Delete(this.CurrentOrganizationId(), id);
        return NoContent();
    }
}
