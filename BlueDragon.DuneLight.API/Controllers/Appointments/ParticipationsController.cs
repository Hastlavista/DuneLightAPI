using System;
using System.Threading.Tasks;
using BlueDragon.DuneLight.API.Authorization;
using BlueDragon.DuneLight.API.Extensions;
using BlueDragon.DuneLight.Core.DTOs.Appointments;
using BlueDragon.DuneLight.Core.Enums;
using BlueDragon.DuneLight.Core.Interfaces.Appointments;
using BlueDragon.DuneLight.Core.Shared;
using Microsoft.AspNetCore.Mvc;

namespace BlueDragon.DuneLight.API.Controllers.Appointments;

/// <summary>
/// Phase M0 — participation-native naredbe: adresa je SUDJELOVANJE (izvršna i komercijalna jedinica), ne Booking.
/// Svaka naredba djeluje samo na adresirano sudjelovanje; ostala sudjelovanja istog Bookinga se ne diraju. Odgovor je
/// Booking (spremnik) s izvedenim sažecima i popisom sudjelovanja. Ista pravila prijelaza/vlasništva kao
/// /api/appointments/{appointmentId}/bookings/{clientId}/... (koji su sada privremena kompatibilnost za Booking s
/// točno jednim sudjelovanjem).
/// </summary>
[ApiController]
[Route("api/participations")]
[Produces("application/json")]
public class ParticipationsController : ControllerBase
{
    private readonly IBookingService _bookingService;
    private readonly IAppointmentService _appointmentService;

    public ParticipationsController(IBookingService bookingService, IAppointmentService appointmentService)
    {
        _bookingService = bookingService;
        _appointmentService = appointmentService;
    }

    /// <summary>Phase M1E — uklanja klijenta iz JEDNOG segmenta: samo netaknuto sudjelovanje (bez statusa, paketa, naplate);
    /// prazan Booking se uklanja. Sudjelovanje s poviješću se otkazuje (PATCH .../cancel).</summary>
    [HttpDelete("{participationId:guid}")]
    [RequireGrant(Grants.AppointmentsWriteOwn, Grants.AppointmentsWriteAll)]
    public async Task<ActionResult<AppointmentDto>> Remove(Guid participationId)
    {
        return Ok(await _appointmentService.RemoveParticipation(
            this.CurrentOrganizationId(), this.CurrentUserId(), this.HasGrant(Grants.AppointmentsWriteAll), participationId));
    }

    /// <summary>Opći prijelaz sudjelovanja: check-in (Completed, uz paket/naplatu/ručnu cijenu kroz tijelo zahtjeva — samo
    /// Form=Group), Cancelled, NoShow ili korekcija na Confirmed. Sažetak statusa Bookinga (Mixed) nikad nije cilj.</summary>
    [HttpPatch("{participationId:guid}/status")]
    [RequireGrant(Grants.AppointmentsWriteOwn, Grants.AppointmentsWriteAll)]
    public async Task<ActionResult<BookingDto>> SetStatus(Guid participationId, [FromBody] BookingSetStatusRequest request)
    {
        return Ok(await _bookingService.SetParticipationStatus(
            this.CurrentOrganizationId(), this.CurrentUserId(), this.HasGrant(Grants.AppointmentsWriteAll), participationId, request));
    }

    [HttpPatch("{participationId:guid}/cancel")]
    [RequireGrant(Grants.AppointmentsWriteOwn, Grants.AppointmentsWriteAll)]
    public async Task<ActionResult<BookingDto>> Cancel(Guid participationId, [FromBody] BookingCancelRequest request)
    {
        return Ok(await _bookingService.SetParticipationStatus(
            this.CurrentOrganizationId(), this.CurrentUserId(), this.HasGrant(Grants.AppointmentsWriteAll), participationId,
            new BookingSetStatusRequest
            {
                Status = BookingStatus.Cancelled,
                ReturnPackageEntry = request.ReturnPackageEntry,
                CancellationReason = request.CancellationReason
            }));
    }

    [HttpPatch("{participationId:guid}/no-show")]
    [RequireGrant(Grants.AppointmentsWriteOwn, Grants.AppointmentsWriteAll)]
    public async Task<ActionResult<BookingDto>> MarkNoShow(Guid participationId, [FromBody] BookingCancelRequest request)
    {
        return Ok(await _bookingService.SetParticipationStatus(
            this.CurrentOrganizationId(), this.CurrentUserId(), this.HasGrant(Grants.AppointmentsWriteAll), participationId,
            new BookingSetStatusRequest
            {
                Status = BookingStatus.NoShow,
                ReturnPackageEntry = request.ReturnPackageEntry,
                CancellationReason = request.CancellationReason
            }));
    }

    [HttpPatch("{participationId:guid}/confirm")]
    [RequireGrant(Grants.AppointmentsWriteOwn, Grants.AppointmentsWriteAll)]
    public async Task<ActionResult<BookingDto>> Confirm(Guid participationId)
    {
        return Ok(await _bookingService.SetParticipationStatus(
            this.CurrentOrganizationId(), this.CurrentUserId(), this.HasGrant(Grants.AppointmentsWriteAll), participationId,
            new BookingSetStatusRequest { Status = BookingStatus.Confirmed }));
    }
}
