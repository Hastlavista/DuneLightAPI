using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using BlueDragon.DuneLight.API.Extensions;
using BlueDragon.DuneLight.Core.DTOs.Appointments;
using BlueDragon.DuneLight.Core.Interfaces.Appointments;
using BlueDragon.DuneLight.Core.Shared;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace BlueDragon.DuneLight.API.Controllers.Appointments;

[ApiController]
[Route("api/appointments")]
[Produces("application/json")]
public class AppointmentsController : ControllerBase
{
    private readonly IAppointmentService _appointmentService;

    public AppointmentsController(IAppointmentService appointmentService)
    {
        _appointmentService = appointmentService;
    }

    /// <summary>Raspored za razdoblje — filtri: lokacija (zadano sve), trener, usluga/kategorija, status. Uključuje otkazane/no-show.</summary>
    [HttpGet("schedule")]
    [Authorize(Roles = "Admin,Member")]
    public async Task<ActionResult<List<AppointmentScheduleCellDto>>> GetSchedule([FromQuery] AppointmentScheduleQuery query)
    {
        return Ok(await _appointmentService.GetSchedule(this.CurrentOrganizationId(), query));
    }

    [HttpGet("{id:guid}")]
    [Authorize(Roles = "Admin,Member")]
    public async Task<ActionResult<AppointmentDto>> GetById(Guid id)
    {
        return Ok(await _appointmentService.GetById(this.CurrentOrganizationId(), id));
    }

    /// <summary>Povijest termina po klijentu, najnoviji prvi.</summary>
    [HttpGet("by-client/{clientId:guid}")]
    [Authorize(Roles = "Admin,Member")]
    public async Task<ActionResult<PagedResult<AppointmentDto>>> GetByClient(Guid clientId, [FromQuery] PagedRequest request)
    {
        return Ok(await _appointmentService.GetByClient(this.CurrentOrganizationId(), clientId, request));
    }

    /// <summary>"Zakaži" — status Scheduled, bez naplate.</summary>
    [HttpPost("schedule")]
    [Authorize(Roles = "Admin,Member")]
    public async Task<ActionResult<AppointmentDto>> Create([FromBody] AppointmentCreateRequest request)
    {
        AppointmentDto created = await _appointmentService.Create(
            this.CurrentOrganizationId(), this.CurrentUserId(), this.CurrentIsAdmin(), request);
        return CreatedAtAction(nameof(GetById), new { id = created.Id }, created);
    }

    /// <summary>"Upiši odrađeno" — novi termin odmah u statusu Completed, naplata odmah.</summary>
    [HttpPost("complete")]
    [Authorize(Roles = "Admin,Member")]
    public async Task<ActionResult<AppointmentDto>> CompleteNew([FromBody] AppointmentCompleteRequest request)
    {
        AppointmentDto created = await _appointmentService.CompleteNew(
            this.CurrentOrganizationId(), this.CurrentUserId(), this.CurrentIsAdmin(), request);
        return CreatedAtAction(nameof(GetById), new { id = created.Id }, created);
    }

    /// <summary>Prijelaz postojećeg (obično Scheduled) termina u Completed, naplata odmah.</summary>
    [HttpPatch("{id:guid}/complete")]
    [Authorize(Roles = "Admin,Member")]
    public async Task<ActionResult<AppointmentDto>> CompleteExisting(Guid id, [FromBody] AppointmentCompleteRequest request)
    {
        return Ok(await _appointmentService.CompleteExisting(
            this.CurrentOrganizationId(), this.CurrentUserId(), this.CurrentIsAdmin(), id, request));
    }

    [HttpPut("{id:guid}")]
    [Authorize(Roles = "Admin,Member")]
    public async Task<ActionResult<AppointmentDto>> Update(Guid id, [FromBody] AppointmentUpdateRequest request)
    {
        return Ok(await _appointmentService.Update(
            this.CurrentOrganizationId(), this.CurrentUserId(), this.CurrentIsAdmin(), id, request));
    }

    /// <summary>Brzo pomicanje termina (drag-and-drop na rasporedu) — samo StartsAt/trener/lokacija, ostalo netaknuto.</summary>
    [HttpPatch("{id:guid}/move")]
    [Authorize(Roles = "Admin,Member")]
    public async Task<ActionResult<AppointmentDto>> Move(Guid id, [FromBody] AppointmentMoveRequest request)
    {
        return Ok(await _appointmentService.Move(
            this.CurrentOrganizationId(), this.CurrentUserId(), this.CurrentIsAdmin(), id, request));
    }

    [HttpPost("{id:guid}/cancel")]
    [Authorize(Roles = "Admin,Member")]
    public async Task<ActionResult<AppointmentDto>> Cancel(Guid id, [FromBody] AppointmentCancelRequest request)
    {
        return Ok(await _appointmentService.Cancel(
            this.CurrentOrganizationId(), this.CurrentUserId(), this.CurrentIsAdmin(), id, request));
    }

    [HttpPost("{id:guid}/no-show")]
    [Authorize(Roles = "Admin,Member")]
    public async Task<ActionResult<AppointmentDto>> MarkNoShow(Guid id, [FromBody] AppointmentCancelRequest request)
    {
        return Ok(await _appointmentService.MarkNoShow(
            this.CurrentOrganizationId(), this.CurrentUserId(), this.CurrentIsAdmin(), id, request));
    }

    /// <summary>Trajno brisanje — samo istog dana kad je termin unesen (pogrešan unos).</summary>
    [HttpDelete("{id:guid}")]
    [Authorize(Roles = "Admin")]
    public async Task<IActionResult> Delete(Guid id)
    {
        await _appointmentService.Delete(this.CurrentOrganizationId(), this.CurrentUserId(), id);
        return NoContent();
    }

    /// <summary>Generira niz individualnih termina (isti klijent(i)/usluga/trener/lokacija/dan-u-tjednu/vrijeme) do datuma kraja.</summary>
    [HttpPost("recurring")]
    [Authorize(Roles = "Admin,Member")]
    public async Task<ActionResult<List<AppointmentDto>>> CreateRecurring([FromBody] RecurringAppointmentCreateRequest request)
    {
        return Ok(await _appointmentService.CreateRecurring(
            this.CurrentOrganizationId(), this.CurrentUserId(), this.CurrentIsAdmin(), request));
    }
}
