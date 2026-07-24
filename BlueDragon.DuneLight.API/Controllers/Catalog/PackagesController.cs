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
    [Authorize(Roles = "Admin,Member")]
    public async Task<ActionResult<PagedResult<PackageDto>>> GetPaged([FromQuery] PagedRequest request)
    {
        return Ok(await _packageService.GetPaged(this.CurrentOrganizationId(), request));
    }

    [HttpGet("{id:guid}")]
    [Authorize(Roles = "Admin,Member")]
    public async Task<ActionResult<PackageDto>> GetById(Guid id)
    {
        return Ok(await _packageService.GetById(this.CurrentOrganizationId(), id));
    }

    [HttpPost]
    [Authorize(Roles = "Admin")]
    public async Task<ActionResult<PackageDto>> Create([FromBody] PackageCreateRequest request)
    {
        PackageDto created = await _packageService.Create(this.CurrentOrganizationId(), this.CurrentUserId(), request);
        return CreatedAtAction(nameof(GetById), new { id = created.Id }, created);
    }

    [HttpPut("{id:guid}")]
    [Authorize(Roles = "Admin")]
    public async Task<ActionResult<PackageDto>> Update(Guid id, [FromBody] PackageUpdateRequest request)
    {
        return Ok(await _packageService.Update(this.CurrentOrganizationId(), this.CurrentUserId(), id, request));
    }

    [HttpPatch("{id:guid}/activate")]
    [Authorize(Roles = "Admin")]
    public async Task<ActionResult<PackageDto>> Activate(Guid id)
    {
        return Ok(await _packageService.SetActive(this.CurrentOrganizationId(), this.CurrentUserId(), id, true));
    }

    [HttpPatch("{id:guid}/deactivate")]
    [Authorize(Roles = "Admin")]
    public async Task<ActionResult<PackageDto>> Deactivate(Guid id)
    {
        return Ok(await _packageService.SetActive(this.CurrentOrganizationId(), this.CurrentUserId(), id, false));
    }

    [HttpDelete("{id:guid}")]
    [Authorize(Roles = "Admin")]
    public async Task<IActionResult> Delete(Guid id)
    {
        await _packageService.Delete(this.CurrentOrganizationId(), id);
        return NoContent();
    }
}
