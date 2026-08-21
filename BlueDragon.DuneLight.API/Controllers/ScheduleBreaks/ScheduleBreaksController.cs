using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using BlueDragon.DuneLight.API.Authorization;
using BlueDragon.DuneLight.API.Extensions;
using BlueDragon.DuneLight.Core.DTOs.ScheduleBreaks;
using BlueDragon.DuneLight.Core.Interfaces.ScheduleBreaks;
using BlueDragon.DuneLight.Core.Shared;
using Microsoft.AspNetCore.Mvc;

namespace BlueDragon.DuneLight.API.Controllers.ScheduleBreaks;

[ApiController]
[Route("api/schedule-breaks")]
[Produces("application/json")]
public class ScheduleBreaksController : ControllerBase
{
    private readonly IScheduleBreakService _scheduleBreakService;

    public ScheduleBreaksController(IScheduleBreakService scheduleBreakService)
    {
        _scheduleBreakService = scheduleBreakService;
    }

    /// <summary>Pauze za razdoblje — filtri: trener, tvrtka (zadano sve).</summary>
    [HttpGet]
    [RequireGrant(Grants.ScheduleBreaksView)]
    public async Task<ActionResult<List<ScheduleBreakDto>>> GetList([FromQuery] ScheduleBreakQuery query)
    {
        return Ok(await _scheduleBreakService.GetList(this.CurrentOrganizationId(), query));
    }

    [HttpGet("{id:guid}")]
    [RequireGrant(Grants.ScheduleBreaksView)]
    public async Task<ActionResult<ScheduleBreakDto>> GetById(Guid id)
    {
        return Ok(await _scheduleBreakService.GetById(this.CurrentOrganizationId(), id));
    }

    /// <summary>Jednokratna pauza.</summary>
    [HttpPost]
    [RequireGrant(Grants.ScheduleBreaksWriteOwn, Grants.ScheduleBreaksWriteAll)]
    public async Task<ActionResult<ScheduleBreakDto>> Create([FromBody] ScheduleBreakCreateRequest request)
    {
        ScheduleBreakDto created = await _scheduleBreakService.Create(
            this.CurrentOrganizationId(), this.CurrentUserId(), this.HasGrant(Grants.ScheduleBreaksWriteAll), request);
        return CreatedAtAction(nameof(GetById), new { id = created.Id }, created);
    }

    /// <summary>Generira niz pauza (isti trener/tvrtka/dan-u-tjednu/vrijeme/trajanje) do datuma kraja.</summary>
    [HttpPost("recurring")]
    [RequireGrant(Grants.ScheduleBreaksWriteOwn, Grants.ScheduleBreaksWriteAll)]
    public async Task<ActionResult<List<ScheduleBreakDto>>> CreateRecurring([FromBody] RecurringScheduleBreakCreateRequest request)
    {
        return Ok(await _scheduleBreakService.CreateRecurring(
            this.CurrentOrganizationId(), this.CurrentUserId(), this.HasGrant(Grants.ScheduleBreaksWriteAll), request));
    }

    [HttpPut("{id:guid}")]
    [RequireGrant(Grants.ScheduleBreaksWriteOwn, Grants.ScheduleBreaksWriteAll)]
    public async Task<ActionResult<ScheduleBreakDto>> Update(Guid id, [FromBody] ScheduleBreakUpdateRequest request)
    {
        return Ok(await _scheduleBreakService.Update(
            this.CurrentOrganizationId(), this.CurrentUserId(), this.HasGrant(Grants.ScheduleBreaksWriteAll), id, request));
    }

    /// <summary>Pravo brisanje — nema statusa "otkazano" za pauzu. Kod ponavljajuće pauze dira samo taj occurrence.</summary>
    [HttpDelete("{id:guid}")]
    [RequireGrant(Grants.ScheduleBreaksWriteOwn, Grants.ScheduleBreaksWriteAll)]
    public async Task<IActionResult> Delete(Guid id)
    {
        await _scheduleBreakService.Delete(
            this.CurrentOrganizationId(), this.CurrentUserId(), this.HasGrant(Grants.ScheduleBreaksWriteAll), id);
        return NoContent();
    }
}
