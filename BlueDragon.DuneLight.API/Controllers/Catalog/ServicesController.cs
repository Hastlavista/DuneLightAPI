using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using BlueDragon.DuneLight.API.Authorization;
using BlueDragon.DuneLight.API.Extensions;
using BlueDragon.DuneLight.Core.DTOs.Catalog;
using BlueDragon.DuneLight.Core.Enums;
using BlueDragon.DuneLight.Core.Interfaces.Catalog;
using BlueDragon.DuneLight.Core.Shared;
using Microsoft.AspNetCore.Mvc;

namespace BlueDragon.DuneLight.API.Controllers.Catalog;

[ApiController]
[Route("api/catalog/services")]
[Produces("application/json")]
public class ServicesController : ControllerBase
{
    private readonly IServiceCatalogService _serviceCatalogService;
    private readonly IServiceAvailabilityService _serviceAvailabilityService;

    public ServicesController(IServiceCatalogService serviceCatalogService, IServiceAvailabilityService serviceAvailabilityService)
    {
        _serviceCatalogService = serviceCatalogService;
        _serviceAvailabilityService = serviceAvailabilityService;
    }

    [HttpGet]
    [RequireGrant(Grants.CatalogServicesView)]
    public async Task<ActionResult<PagedResult<ServiceDto>>> GetPaged([FromQuery] PagedRequest request, [FromQuery] ServiceExecutionMode? executionMode)
    {
        return Ok(await _serviceCatalogService.GetPaged(this.CurrentOrganizationId(), request, executionMode));
    }

    [HttpGet("{id:guid}")]
    [RequireGrant(Grants.CatalogServicesView)]
    public async Task<ActionResult<ServiceDto>> GetById(Guid id)
    {
        return Ok(await _serviceCatalogService.GetById(this.CurrentOrganizationId(), id));
    }

    [HttpPost]
    [RequireGrant(Grants.CatalogServicesManage)]
    public async Task<ActionResult<ServiceDto>> Create([FromBody] ServiceCreateRequest request)
    {
        ServiceDto created = await _serviceCatalogService.Create(this.CurrentOrganizationId(), this.CurrentUserId(), request);
        return CreatedAtAction(nameof(GetById), new { id = created.Id }, created);
    }

    [HttpPut("{id:guid}")]
    [RequireGrant(Grants.CatalogServicesManage)]
    public async Task<ActionResult<ServiceDto>> Update(Guid id, [FromBody] ServiceUpdateRequest request)
    {
        return Ok(await _serviceCatalogService.Update(this.CurrentOrganizationId(), this.CurrentUserId(), id, request));
    }

    [HttpPatch("{id:guid}/activate")]
    [RequireGrant(Grants.CatalogServicesManage)]
    public async Task<ActionResult<ServiceDto>> Activate(Guid id)
    {
        return Ok(await _serviceCatalogService.SetActive(this.CurrentOrganizationId(), this.CurrentUserId(), id, true));
    }

    [HttpPatch("{id:guid}/deactivate")]
    [RequireGrant(Grants.CatalogServicesManage)]
    public async Task<ActionResult<ServiceDto>> Deactivate(Guid id)
    {
        return Ok(await _serviceCatalogService.SetActive(this.CurrentOrganizationId(), this.CurrentUserId(), id, false));
    }

    [HttpDelete("{id:guid}")]
    [RequireGrant(Grants.CatalogServicesManage)]
    public async Task<IActionResult> Delete(Guid id)
    {
        await _serviceCatalogService.Delete(this.CurrentOrganizationId(), id);
        return NoContent();
    }

    /// <summary>Poslovnice u kojima je usluga trenutno dostupna — uključuje i neaktivne (grandfathered) dodjele,
    /// admin ih vidi radi čišćenja konfiguracije. Prazan popis znači "dostupna nigdje" (namjerno, vidi ServiceCompany).</summary>
    [HttpGet("{serviceId:guid}/companies")]
    [RequireGrant(Grants.CatalogServicesView)]
    public async Task<ActionResult<List<CompanyDto>>> GetAssignedCompanies(Guid serviceId)
    {
        return Ok(await _serviceAvailabilityService.GetAssignedCompanies(this.CurrentOrganizationId(), serviceId));
    }

    /// <summary>Atomarno zamjenjuje cijeli popis poslovnica u kojima je usluga dostupna. Nove dodjele zahtijevaju
    /// aktivnu uslugu i aktivnu ciljanu poslovnicu; postojeće (i eventualno neaktivne) dodjele smiju ostati.</summary>
    [HttpPut("{serviceId:guid}/companies")]
    [RequireGrant(Grants.CatalogServicesManage)]
    public async Task<ActionResult<List<CompanyDto>>> ReplaceAssignedCompanies(Guid serviceId, [FromBody] ReplaceServiceCompaniesRequest request)
    {
        return Ok(await _serviceAvailabilityService.ReplaceAssignedCompanies(this.CurrentOrganizationId(), this.CurrentUserId(), serviceId, request));
    }
}
