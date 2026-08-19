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
[Route("api/catalog/companies")]
[Produces("application/json")]
public class CompaniesController : ControllerBase
{
    private readonly ICompanyService _companyService;

    public CompaniesController(ICompanyService companyService)
    {
        _companyService = companyService;
    }

    [HttpGet]
    [RequireGrant(Grants.CatalogCompaniesView)]
    public async Task<ActionResult<PagedResult<CompanyDto>>> GetPaged([FromQuery] PagedRequest request)
    {
        return Ok(await _companyService.GetPaged(this.CurrentOrganizationId(), request));
    }

    [HttpGet("{id:guid}")]
    [RequireGrant(Grants.CatalogCompaniesView)]
    public async Task<ActionResult<CompanyDto>> GetById(Guid id)
    {
        return Ok(await _companyService.GetById(this.CurrentOrganizationId(), id));
    }

    [HttpPost]
    [RequireGrant(Grants.CatalogCompaniesManage)]
    public async Task<ActionResult<CompanyDto>> Create([FromBody] CompanyCreateRequest request)
    {
        CompanyDto created = await _companyService.Create(this.CurrentOrganizationId(), this.CurrentUserId(), request);
        return CreatedAtAction(nameof(GetById), new { id = created.Id }, created);
    }

    [HttpPut("{id:guid}")]
    [RequireGrant(Grants.CatalogCompaniesManage)]
    public async Task<ActionResult<CompanyDto>> Update(Guid id, [FromBody] CompanyUpdateRequest request)
    {
        return Ok(await _companyService.Update(this.CurrentOrganizationId(), this.CurrentUserId(), id, request));
    }

    [HttpPatch("{id:guid}/activate")]
    [RequireGrant(Grants.CatalogCompaniesManage)]
    public async Task<ActionResult<CompanyDto>> Activate(Guid id)
    {
        return Ok(await _companyService.SetActive(this.CurrentOrganizationId(), this.CurrentUserId(), id, true));
    }

    [HttpPatch("{id:guid}/deactivate")]
    [RequireGrant(Grants.CatalogCompaniesManage)]
    public async Task<ActionResult<CompanyDto>> Deactivate(Guid id)
    {
        return Ok(await _companyService.SetActive(this.CurrentOrganizationId(), this.CurrentUserId(), id, false));
    }

    [HttpDelete("{id:guid}")]
    [RequireGrant(Grants.CatalogCompaniesManage)]
    public async Task<IActionResult> Delete(Guid id)
    {
        await _companyService.Delete(this.CurrentOrganizationId(), id);
        return NoContent();
    }
}
