using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using BlueDragon.DuneLight.API.Authorization;
using BlueDragon.DuneLight.API.Extensions;
using BlueDragon.DuneLight.Core.DTOs.Appointments;
using BlueDragon.DuneLight.Core.DTOs.Schedule;
using BlueDragon.DuneLight.Core.DTOs.ScheduleBreaks;
using BlueDragon.DuneLight.Core.Interfaces.Appointments;
using BlueDragon.DuneLight.Core.Interfaces.ScheduleBreaks;
using BlueDragon.DuneLight.Core.Shared;
using Microsoft.AspNetCore.Mvc;

namespace BlueDragon.DuneLight.API.Controllers.Appointments;

[ApiController]
[Route("api/appointments")]
[Produces("application/json")]
public class AppointmentsController : ControllerBase
{
    private readonly IAppointmentService _appointmentService;
    private readonly IScheduleBreakService _scheduleBreakService;

    public AppointmentsController(IAppointmentService appointmentService, IScheduleBreakService scheduleBreakService)
    {
        _appointmentService = appointmentService;
        _scheduleBreakService = scheduleBreakService;
    }

    /// <summary>Raspored za razdoblje — filtri: tvrtka (zadano sve), trener, usluga/kategorija, status. Uključuje
    /// otkazane/no-show termine, te pauze istog trenera (Breaks) kao zaseban niz iste mreže.</summary>
    [HttpGet("schedule")]
    [RequireGrant(Grants.AppointmentsView)]
    public async Task<ActionResult<ScheduleFeedDto>> GetSchedule([FromQuery] AppointmentScheduleQuery query)
    {
        Guid organizationId = this.CurrentOrganizationId();

        List<AppointmentScheduleCellDto> appointments = await _appointmentService.GetSchedule(organizationId, query);

        ScheduleBreakQuery breakQuery = new ScheduleBreakQuery
        {
            From = query.From,
            To = query.To,
            EmployeeId = query.EmployeeId,
            CompanyId = query.CompanyId
        };
        List<ScheduleBreakCellDto> breaks = await _scheduleBreakService.GetScheduleCells(organizationId, breakQuery);

        return Ok(new ScheduleFeedDto { Appointments = appointments, Breaks = breaks });
    }

    /// <summary>Slobodni slotovi točne duljine usluge, za sve zaposlenike poslovnice koji smiju tu uslugu (ili
    /// samo employeeId ako je zadan) — za "Pronađi dostupan termin" u formi novog termina.</summary>
    [HttpGet("available-slots")]
    [RequireGrant(Grants.AppointmentsWriteOwn, Grants.AppointmentsWriteAll)]
    public async Task<ActionResult<List<EmployeeAvailableSlotsDto>>> GetAvailableSlots([FromQuery] AvailableSlotsQuery query)
    {
        return Ok(await _appointmentService.GetAvailableSlots(this.CurrentOrganizationId(), query));
    }

    [HttpGet("{id:guid}")]
    [RequireGrant(Grants.AppointmentsView)]
    public async Task<ActionResult<AppointmentDto>> GetById(Guid id)
    {
        return Ok(await _appointmentService.GetById(this.CurrentOrganizationId(), id));
    }

    /// <summary>Povijest termina po klijentu, najnoviji prvi.</summary>
    [HttpGet("by-client/{clientId:guid}")]
    [RequireGrant(Grants.AppointmentsView)]
    public async Task<ActionResult<PagedResult<AppointmentDto>>> GetByClient(Guid clientId, [FromQuery] PagedRequest request)
    {
        return Ok(await _appointmentService.GetByClient(this.CurrentOrganizationId(), clientId, request));
    }

    /// <summary>"Zakaži" — status Scheduled, bez naplate.</summary>
    [HttpPost("schedule")]
    [RequireGrant(Grants.AppointmentsWriteOwn, Grants.AppointmentsWriteAll)]
    public async Task<ActionResult<AppointmentDto>> Create([FromBody] AppointmentCreateRequest request)
    {
        AppointmentDto created = await _appointmentService.Create(
            this.CurrentOrganizationId(), this.CurrentUserId(), this.HasGrant(Grants.AppointmentsWriteAll), request);
        return CreatedAtAction(nameof(GetById), new { id = created.Id }, created);
    }

    /// <summary>"Upiši odrađeno" — novi termin odmah u statusu Completed, naplata odmah.</summary>
    [HttpPost("complete")]
    [RequireGrant(Grants.AppointmentsWriteOwn, Grants.AppointmentsWriteAll)]
    public async Task<ActionResult<AppointmentDto>> CompleteNew([FromBody] AppointmentCompleteRequest request)
    {
        AppointmentDto created = await _appointmentService.CompleteNew(
            this.CurrentOrganizationId(), this.CurrentUserId(), this.HasGrant(Grants.AppointmentsWriteAll), request);
        return CreatedAtAction(nameof(GetById), new { id = created.Id }, created);
    }

    /// <summary>Prijelaz postojećeg (obično Scheduled) termina u Completed, naplata odmah.</summary>
    [HttpPatch("{id:guid}/complete")]
    [RequireGrant(Grants.AppointmentsWriteOwn, Grants.AppointmentsWriteAll)]
    public async Task<ActionResult<AppointmentDto>> CompleteExisting(Guid id, [FromBody] AppointmentCompleteRequest request)
    {
        return Ok(await _appointmentService.CompleteExisting(
            this.CurrentOrganizationId(), this.CurrentUserId(), this.HasGrant(Grants.AppointmentsWriteAll), id, request));
    }

    [HttpPut("{id:guid}")]
    [RequireGrant(Grants.AppointmentsWriteOwn, Grants.AppointmentsWriteAll)]
    public async Task<ActionResult<AppointmentDto>> Update(Guid id, [FromBody] AppointmentUpdateRequest request)
    {
        return Ok(await _appointmentService.Update(
            this.CurrentOrganizationId(), this.CurrentUserId(), this.HasGrant(Grants.AppointmentsWriteAll), id, request));
    }

    /// <summary>Brzo pomicanje termina (drag-and-drop na rasporedu) — samo StartsAt/trener/tvrtka, ostalo netaknuto.</summary>
    [HttpPatch("{id:guid}/move")]
    [RequireGrant(Grants.AppointmentsWriteOwn, Grants.AppointmentsWriteAll)]
    public async Task<ActionResult<AppointmentDto>> Move(Guid id, [FromBody] AppointmentMoveRequest request)
    {
        return Ok(await _appointmentService.Move(
            this.CurrentOrganizationId(), this.CurrentUserId(), this.HasGrant(Grants.AppointmentsWriteAll), id, request));
    }

    [HttpPost("{id:guid}/cancel")]
    [RequireGrant(Grants.AppointmentsWriteOwn, Grants.AppointmentsWriteAll)]
    public async Task<ActionResult<AppointmentDto>> Cancel(Guid id, [FromBody] AppointmentCancelRequest request)
    {
        return Ok(await _appointmentService.Cancel(
            this.CurrentOrganizationId(), this.CurrentUserId(), this.HasGrant(Grants.AppointmentsWriteAll), id, request));
    }

    [HttpPost("{id:guid}/no-show")]
    [RequireGrant(Grants.AppointmentsWriteOwn, Grants.AppointmentsWriteAll)]
    public async Task<ActionResult<AppointmentDto>> MarkNoShow(Guid id, [FromBody] AppointmentCancelRequest request)
    {
        return Ok(await _appointmentService.MarkNoShow(
            this.CurrentOrganizationId(), this.CurrentUserId(), this.HasGrant(Grants.AppointmentsWriteAll), id, request));
    }

    /// <summary>Trajno brisanje — samo istog dana kad je termin unesen (pogrešan unos).</summary>
    [HttpDelete("{id:guid}")]
    [RequireGrant(Grants.AppointmentsDelete)]
    public async Task<IActionResult> Delete(Guid id)
    {
        await _appointmentService.Delete(this.CurrentOrganizationId(), this.CurrentUserId(), id);
        return NoContent();
    }

    /// <summary>Generira niz individualnih termina (isti klijent(i)/usluga/trener/tvrtka/dan-u-tjednu/vrijeme) do datuma kraja.</summary>
    [HttpPost("recurring")]
    [RequireGrant(Grants.AppointmentsWriteOwn, Grants.AppointmentsWriteAll)]
    public async Task<ActionResult<List<AppointmentDto>>> CreateRecurring([FromBody] RecurringAppointmentCreateRequest request)
    {
        return Ok(await _appointmentService.CreateRecurring(
            this.CurrentOrganizationId(), this.CurrentUserId(), this.HasGrant(Grants.AppointmentsWriteAll), request));
    }
}
