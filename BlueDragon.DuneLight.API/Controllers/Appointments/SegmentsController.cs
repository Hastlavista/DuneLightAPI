using System;
using System.Threading.Tasks;
using BlueDragon.DuneLight.API.Authorization;
using BlueDragon.DuneLight.API.Extensions;
using BlueDragon.DuneLight.Core.DTOs.Appointments;
using BlueDragon.DuneLight.Core.Interfaces.Appointments;
using BlueDragon.DuneLight.Core.Shared;
using Microsoft.AspNetCore.Mvc;

namespace BlueDragon.DuneLight.API.Controllers.Appointments;

/// <summary>
/// Phase M1E — segment-native naredbe: adresa je SEGMENT (izvršna jedinica: vrijeme, usluga, zaposlenik, prostorija,
/// resursi). Svaka naredba mijenja samo adresirani segment i validira puno ciljno stanje termina (sestrinski segmenti
/// ostaju vidljivi). Own-opseg smije mijenjati samo segment kojem je dodijeljen. Odgovor je cijeli termin.
/// </summary>
[ApiController]
[Route("api/segments")]
[Produces("application/json")]
public class SegmentsController : ControllerBase
{
    private readonly IAppointmentService _appointmentService;

    public SegmentsController(IAppointmentService appointmentService)
    {
        _appointmentService = appointmentService;
    }

    [HttpPatch("{segmentId:guid}/time")]
    [RequireGrant(Grants.AppointmentsWriteOwn, Grants.AppointmentsWriteAll)]
    public async Task<ActionResult<AppointmentDto>> ChangeTime(Guid segmentId, [FromBody] AppointmentSegmentTimeChangeRequest request)
    {
        return Ok(await _appointmentService.ChangeSegmentTime(
            this.CurrentOrganizationId(), this.CurrentUserId(), this.HasGrant(Grants.AppointmentsWriteAll), segmentId, request));
    }

    [HttpPatch("{segmentId:guid}/service")]
    [RequireGrant(Grants.AppointmentsWriteOwn, Grants.AppointmentsWriteAll)]
    public async Task<ActionResult<AppointmentDto>> ChangeService(Guid segmentId, [FromBody] AppointmentSegmentServiceChangeRequest request)
    {
        return Ok(await _appointmentService.ChangeSegmentService(
            this.CurrentOrganizationId(), this.CurrentUserId(), this.HasGrant(Grants.AppointmentsWriteAll), segmentId, request));
    }

    [HttpPatch("{segmentId:guid}/employees")]
    [RequireGrant(Grants.AppointmentsWriteOwn, Grants.AppointmentsWriteAll)]
    public async Task<ActionResult<AppointmentDto>> ChangeEmployees(Guid segmentId, [FromBody] AppointmentSegmentEmployeesChangeRequest request)
    {
        return Ok(await _appointmentService.ChangeSegmentEmployees(
            this.CurrentOrganizationId(), this.CurrentUserId(), this.HasGrant(Grants.AppointmentsWriteAll), segmentId, request));
    }

    /// <summary>Phase M1G — izvor cijene segmenta bez promjene zaposlenika (Standard ili Employee + zaposlenik segmenta).</summary>
    [HttpPatch("{segmentId:guid}/pricing-source")]
    [RequireGrant(Grants.AppointmentsWriteOwn, Grants.AppointmentsWriteAll)]
    public async Task<ActionResult<AppointmentDto>> ChangePricingSource(Guid segmentId, [FromBody] AppointmentSegmentPricingSourceChangeRequest request)
    {
        return Ok(await _appointmentService.ChangeSegmentPricingSource(
            this.CurrentOrganizationId(), this.CurrentUserId(), this.HasGrant(Grants.AppointmentsWriteAll), segmentId, request));
    }

    [HttpPatch("{segmentId:guid}/room")]
    [RequireGrant(Grants.AppointmentsWriteOwn, Grants.AppointmentsWriteAll)]
    public async Task<ActionResult<AppointmentDto>> ChangeRoom(Guid segmentId, [FromBody] AppointmentSegmentRoomChangeRequest request)
    {
        return Ok(await _appointmentService.ChangeSegmentRoom(
            this.CurrentOrganizationId(), this.CurrentUserId(), this.HasGrant(Grants.AppointmentsWriteAll), segmentId, request));
    }

    [HttpPut("{segmentId:guid}/resources")]
    [RequireGrant(Grants.AppointmentsWriteOwn, Grants.AppointmentsWriteAll)]
    public async Task<ActionResult<AppointmentDto>> ChangeResources(Guid segmentId, [FromBody] AppointmentSegmentResourcesChangeRequest request)
    {
        return Ok(await _appointmentService.ChangeSegmentResources(
            this.CurrentOrganizationId(), this.CurrentUserId(), this.HasGrant(Grants.AppointmentsWriteAll), segmentId, request));
    }

    /// <summary>Uklanja segment: samo ako su sva njegova sudjelovanja netaknuta i nije posljednji segment termina.</summary>
    [HttpDelete("{segmentId:guid}")]
    [RequireGrant(Grants.AppointmentsWriteOwn, Grants.AppointmentsWriteAll)]
    public async Task<ActionResult<AppointmentDto>> Remove(Guid segmentId)
    {
        return Ok(await _appointmentService.RemoveSegment(
            this.CurrentOrganizationId(), this.CurrentUserId(), this.HasGrant(Grants.AppointmentsWriteAll), segmentId));
    }
}
