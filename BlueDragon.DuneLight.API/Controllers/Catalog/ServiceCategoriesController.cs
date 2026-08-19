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
[Route("api/catalog/service-categories")]
[Produces("application/json")]
public class ServiceCategoriesController : ControllerBase
{
    private readonly IServiceCategoryService _categoryService;

    public ServiceCategoriesController(IServiceCategoryService categoryService)
    {
        _categoryService = categoryService;
    }

    [HttpGet]
    [RequireGrant(Grants.CatalogServiceCategoriesView)]
    public async Task<ActionResult<PagedResult<ServiceCategoryDto>>> GetPaged([FromQuery] PagedRequest request)
    {
        return Ok(await _categoryService.GetPaged(this.CurrentOrganizationId(), request));
    }

    [HttpGet("{id:guid}")]
    [RequireGrant(Grants.CatalogServiceCategoriesView)]
    public async Task<ActionResult<ServiceCategoryDto>> GetById(Guid id)
    {
        return Ok(await _categoryService.GetById(this.CurrentOrganizationId(), id));
    }

    [HttpPost]
    [RequireGrant(Grants.CatalogServiceCategoriesManage)]
    public async Task<ActionResult<ServiceCategoryDto>> Create([FromBody] ServiceCategoryCreateRequest request)
    {
        ServiceCategoryDto created = await _categoryService.Create(this.CurrentOrganizationId(), this.CurrentUserId(), request);
        return CreatedAtAction(nameof(GetById), new { id = created.Id }, created);
    }

    [HttpPut("{id:guid}")]
    [RequireGrant(Grants.CatalogServiceCategoriesManage)]
    public async Task<ActionResult<ServiceCategoryDto>> Update(Guid id, [FromBody] ServiceCategoryUpdateRequest request)
    {
        return Ok(await _categoryService.Update(this.CurrentOrganizationId(), this.CurrentUserId(), id, request));
    }

    [HttpPatch("{id:guid}/activate")]
    [RequireGrant(Grants.CatalogServiceCategoriesManage)]
    public async Task<ActionResult<ServiceCategoryDto>> Activate(Guid id)
    {
        return Ok(await _categoryService.SetActive(this.CurrentOrganizationId(), this.CurrentUserId(), id, true));
    }

    [HttpPatch("{id:guid}/deactivate")]
    [RequireGrant(Grants.CatalogServiceCategoriesManage)]
    public async Task<ActionResult<ServiceCategoryDto>> Deactivate(Guid id)
    {
        return Ok(await _categoryService.SetActive(this.CurrentOrganizationId(), this.CurrentUserId(), id, false));
    }

    [HttpDelete("{id:guid}")]
    [RequireGrant(Grants.CatalogServiceCategoriesManage)]
    public async Task<IActionResult> Delete(Guid id)
    {
        await _categoryService.Delete(this.CurrentOrganizationId(), id);
        return NoContent();
    }
}
