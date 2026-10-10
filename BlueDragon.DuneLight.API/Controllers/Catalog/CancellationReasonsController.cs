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

/// <summary>K1-4 (12.3) — šifrarnik razloga otkazivanja i izostanka. Popis vide svi koji otkazuju ili evidentiraju izostanak
/// (forma otkaza); upravljanje traži catalog.cancellation-reasons.manage. Šifra se ne briše, samo deaktivira.</summary>
[ApiController]
[Route("api/catalog/cancellation-reasons")]
[Produces("application/json")]
public class CancellationReasonsController : ControllerBase
{
    private readonly ICancellationReasonService _service;

    public CancellationReasonsController(ICancellationReasonService service)
    {
        _service = service;
    }

    [HttpGet]
    [RequireGrant(Grants.CatalogCancellationReasonsView, Grants.CatalogCancellationReasonsManage, Grants.AppointmentsWriteOwn, Grants.AppointmentsWriteAll,
        Grants.GroupsAttendanceOwn, Grants.GroupsAttendanceAll)]
    public async Task<ActionResult<List<CancellationReasonDto>>> GetAll([FromQuery] bool? isActive, [FromQuery] CancellationReasonEvent? appliesTo)
    {
        return Ok(await _service.GetAll(this.CurrentOrganizationId(), isActive, appliesTo));
    }

    [HttpPost]
    [RequireGrant(Grants.CatalogCancellationReasonsManage)]
    public async Task<ActionResult<CancellationReasonDto>> Create([FromBody] CancellationReasonUpsertRequest request)
    {
        return Ok(await _service.Create(this.CurrentOrganizationId(), this.CurrentUserId(), request));
    }

    [HttpPut("{id:guid}")]
    [RequireGrant(Grants.CatalogCancellationReasonsManage)]
    public async Task<ActionResult<CancellationReasonDto>> Update(Guid id, [FromBody] CancellationReasonUpsertRequest request)
    {
        return Ok(await _service.Update(this.CurrentOrganizationId(), this.CurrentUserId(), id, request));
    }

    [HttpPatch("{id:guid}/activate")]
    [RequireGrant(Grants.CatalogCancellationReasonsManage)]
    public async Task<ActionResult<CancellationReasonDto>> Activate(Guid id)
    {
        return Ok(await _service.SetActive(this.CurrentOrganizationId(), this.CurrentUserId(), id, true));
    }

    [HttpPatch("{id:guid}/deactivate")]
    [RequireGrant(Grants.CatalogCancellationReasonsManage)]
    public async Task<ActionResult<CancellationReasonDto>> Deactivate(Guid id)
    {
        return Ok(await _service.SetActive(this.CurrentOrganizationId(), this.CurrentUserId(), id, false));
    }
}
