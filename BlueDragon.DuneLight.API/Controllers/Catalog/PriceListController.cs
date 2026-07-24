using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using BlueDragon.DuneLight.API.Extensions;
using BlueDragon.DuneLight.Core.DTOs.Catalog;
using BlueDragon.DuneLight.Core.Enums;
using BlueDragon.DuneLight.Core.Interfaces.Catalog;
using BlueDragon.DuneLight.Core.Shared;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace BlueDragon.DuneLight.API.Controllers.Catalog;

[ApiController]
[Route("api/catalog/price-list")]
[Produces("application/json")]
public class PriceListController : ControllerBase
{
    private readonly IPricingService _pricingService;

    public PriceListController(IPricingService pricingService)
    {
        _pricingService = pricingService;
    }

    [HttpGet]
    [Authorize(Roles = "Admin,Member")]
    public async Task<ActionResult<PagedResult<PriceListItemDto>>> GetPaged(
        [FromQuery] PagedRequest request, [FromQuery] Guid? locationId, [FromQuery] PricingSubjectType? subjectType)
    {
        return Ok(await _pricingService.GetPaged(this.CurrentOrganizationId(), request, locationId, subjectType));
    }

    [HttpGet("{id:guid}")]
    [Authorize(Roles = "Admin,Member")]
    public async Task<ActionResult<PriceListItemDto>> GetById(Guid id)
    {
        return Ok(await _pricingService.GetById(this.CurrentOrganizationId(), id));
    }

    /// <summary>Trenutno važeći cjenik za lokaciju (pregledni prikaz).</summary>
    [HttpGet("effective")]
    [Authorize(Roles = "Admin,Member")]
    public async Task<ActionResult<List<EffectivePriceDto>>> GetEffective([FromQuery] Guid? locationId, [FromQuery] DateTimeOffset? date)
    {
        DateTimeOffset effectiveDate = date ?? DateTimeOffset.UtcNow;
        return Ok(await _pricingService.GetEffectivePriceList(this.CurrentOrganizationId(), locationId, effectiveDate));
    }

    /// <summary>Razriješena cijena za (usluga/paket, lokacija, datum).</summary>
    [HttpGet("resolve")]
    [Authorize(Roles = "Admin,Member")]
    public async Task<ActionResult<ResolvePriceResponse>> Resolve([FromQuery] ResolvePriceRequest request)
    {
        return Ok(await _pricingService.ResolvePrice(this.CurrentOrganizationId(), request));
    }

    [HttpPost]
    [Authorize(Roles = "Admin")]
    public async Task<ActionResult<PriceListItemDto>> Create([FromBody] PriceListItemCreateRequest request)
    {
        PriceListItemDto created = await _pricingService.Create(this.CurrentOrganizationId(), this.CurrentUserId(), request);
        return CreatedAtAction(nameof(GetById), new { id = created.Id }, created);
    }

    [HttpPut("{id:guid}")]
    [Authorize(Roles = "Admin")]
    public async Task<ActionResult<PriceListItemDto>> Update(Guid id, [FromBody] PriceListItemUpdateRequest request)
    {
        return Ok(await _pricingService.Update(this.CurrentOrganizationId(), this.CurrentUserId(), id, request));
    }

    [HttpPatch("{id:guid}/activate")]
    [Authorize(Roles = "Admin")]
    public async Task<ActionResult<PriceListItemDto>> Activate(Guid id)
    {
        return Ok(await _pricingService.SetActive(this.CurrentOrganizationId(), this.CurrentUserId(), id, true));
    }

    [HttpPatch("{id:guid}/deactivate")]
    [Authorize(Roles = "Admin")]
    public async Task<ActionResult<PriceListItemDto>> Deactivate(Guid id)
    {
        return Ok(await _pricingService.SetActive(this.CurrentOrganizationId(), this.CurrentUserId(), id, false));
    }

    [HttpDelete("{id:guid}")]
    [Authorize(Roles = "Admin")]
    public async Task<IActionResult> Delete(Guid id)
    {
        await _pricingService.Delete(this.CurrentOrganizationId(), id);
        return NoContent();
    }
}
