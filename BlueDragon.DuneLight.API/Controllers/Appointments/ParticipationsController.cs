using System;
using System.Threading.Tasks;
using BlueDragon.DuneLight.API.Authorization;
using BlueDragon.DuneLight.API.Extensions;
using BlueDragon.DuneLight.Core.DTOs.Appointments;
using BlueDragon.DuneLight.Core.Enums;
using BlueDragon.DuneLight.Core.Interfaces.Appointments;
using BlueDragon.DuneLight.Core.Shared;
using BlueDragon.DuneLight.Core.Shared.Exceptions;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using Microsoft.AspNetCore.Mvc;

namespace BlueDragon.DuneLight.API.Controllers.Appointments;

/// <summary>
/// Phase M0 — participation-native naredbe: adresa je SUDJELOVANJE (izvršna i komercijalna jedinica), ne Booking.
/// Svaka naredba djeluje samo na adresirano sudjelovanje; ostala sudjelovanja istog Bookinga se ne diraju. Odgovor je
/// Booking (spremnik) s izvedenim sažecima i popisom sudjelovanja. Vlasništvo slijedi segment sudjelovanja.
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

    /// <summary>Phase M1H — ručni konačni iznos sudjelovanja (samo aktivno/Confirmed sudjelovanje individualnog termina;
    /// null = predložena cijena).</summary>
    [HttpPatch("{participationId:guid}/price")]
    [RequireGrant(Grants.AppointmentsWriteOwn, Grants.AppointmentsWriteAll)]
    public async Task<ActionResult<BookingDto>> SetPrice(Guid participationId, [FromBody] ParticipationPriceChangeRequest request)
    {
        return Ok(await _bookingService.SetParticipationPrice(
            this.CurrentOrganizationId(), this.CurrentUserId(), this.HasGrant(Grants.AppointmentsWriteAll), participationId, request));
    }

    /// <summary>K1-2 — označava dolazak klijenta (metapodatak, ne status; bez financijskog učinka). Confirmed/Completed, i prije
    /// početka. Upozorenje PARTICIPATION_NOT_COVERED / PARTICIPATION_PACKAGE_AVAILABLE kad sesija ima neplaćen dug.</summary>
    [HttpPatch("{participationId:guid}/arrival")]
    [RequireGrant(Grants.AppointmentsArrivalMark)]
    public async Task<ActionResult<BookingDto>> MarkArrival(Guid participationId)
    {
        return Ok(await _bookingService.MarkArrival(this.CurrentOrganizationId(), this.CurrentUserId(), participationId));
    }

    /// <summary>K1-2 — poništava označeni dolazak (povijest zadržava trag).</summary>
    [HttpDelete("{participationId:guid}/arrival")]
    [RequireGrant(Grants.AppointmentsArrivalMark)]
    public async Task<ActionResult<BookingDto>> ClearArrival(Guid participationId)
    {
        return Ok(await _bookingService.ClearArrival(this.CurrentOrganizationId(), this.CurrentUserId(), participationId));
    }

    /// <summary>Opći prijelaz sudjelovanja: check-in/odrađivanje (Completed, uz paket/naplatu/ručnu cijenu kroz tijelo
    /// zahtjeva), Cancelled, NoShow ili korekcija na Confirmed. Sažetak statusa Bookinga (Mixed) nikad nije cilj.</summary>
    [HttpPatch("{participationId:guid}/status")]
    [RequireGrant(Grants.AppointmentsWriteOwn, Grants.AppointmentsWriteAll)]
    public async Task<ActionResult<BookingDto>> SetStatus(Guid participationId, [FromBody] BookingSetStatusRequest request)
    {
        return Ok(await _bookingService.SetParticipationStatus(
            this.CurrentOrganizationId(), this.CurrentUserId(), this.HasGrant(Grants.AppointmentsWriteAll), participationId, request));
    }

    /// <summary>P1: otkazivanje sudjelovanja s obaveznim initiatorom (Client → politika i klasifikacija; Business →
    /// appointments.write.all + razlog, bez posljedice).</summary>
    [HttpPatch("{participationId:guid}/cancel")]
    [RequireGrant(Grants.AppointmentsWriteOwn, Grants.AppointmentsWriteAll)]
    public async Task<ActionResult<BookingDto>> Cancel(Guid participationId, [FromBody] BookingCancelRequest request)
    {
        EnsureNoPackageSelections(request?.PackageSelections);
        return Ok(await _bookingService.SetParticipationStatus(
            this.CurrentOrganizationId(), this.CurrentUserId(), this.HasGrant(Grants.AppointmentsWriteAll), participationId,
            new BookingSetStatusRequest
            {
                Status = BookingStatus.Cancelled,
                CancellationInitiator = request?.CancellationInitiator,
                CancellationReason = request?.CancellationReason,
                CancellationReasonCodeId = request?.CancellationReasonCodeId,
                ClientPackageId = request?.ClientPackageId,
                WaivePolicyConsequence = request?.WaivePolicyConsequence ?? false,
                WaiverReason = request?.WaiverReason,
                CorrectionReason = request?.CorrectionReason
            }));
    }

    /// <summary>P1: izostanak (tek od početka segmenta) — evaluira NoShow politiku.</summary>
    [HttpPatch("{participationId:guid}/no-show")]
    [RequireGrant(Grants.AppointmentsWriteOwn, Grants.AppointmentsWriteAll)]
    public async Task<ActionResult<BookingDto>> MarkNoShow(Guid participationId, [FromBody] NoShowRequest request)
    {
        EnsureNoPackageSelections(request?.PackageSelections);
        return Ok(await _bookingService.SetParticipationStatus(
            this.CurrentOrganizationId(), this.CurrentUserId(), this.HasGrant(Grants.AppointmentsWriteAll), participationId,
            new BookingSetStatusRequest
            {
                Status = BookingStatus.NoShow,
                NoShowReason = request?.NoShowReason,
                NoShowReasonCodeId = request?.NoShowReasonCodeId,
                ClientPackageId = request?.ClientPackageId,
                WaivePolicyConsequence = request?.WaivePolicyConsequence ?? false,
                WaiverReason = request?.WaiverReason,
                CorrectionReason = request?.CorrectionReason
            }));
    }

    /// <summary>Korekcija natrag na Confirmed. P1 (D12): poništenje aktivne posljedice sa stvarnim učinkom traži
    /// appointments.policy.override i razlog korekcije (correctionReason u tijelu; tijelo je opcionalno).</summary>
    [HttpPatch("{participationId:guid}/confirm")]
    [RequireGrant(Grants.AppointmentsWriteOwn, Grants.AppointmentsWriteAll)]
    public async Task<ActionResult<BookingDto>> Confirm(
        Guid participationId, [FromBody(EmptyBodyBehavior = EmptyBodyBehavior.Allow)] ParticipationConfirmRequest request)
    {
        return Ok(await _bookingService.SetParticipationStatus(
            this.CurrentOrganizationId(), this.CurrentUserId(), this.HasGrant(Grants.AppointmentsWriteAll), participationId,
            new BookingSetStatusRequest { Status = BookingStatus.Confirmed, CorrectionReason = request?.CorrectionReason }));
    }

    /// <summary>P1 (D10) — naknadni otpis AKTIVNE posljedice politike (cijele; razlog obavezan; nepovratno). Vraća jedinicu
    /// paketa potrošenu kao kaznu; novac se ne pomiče. Normalan pristup sudjelovanju se provjerava u servisu.</summary>
    [HttpPost("{participationId:guid}/policy-consequence/waive")]
    [RequireGrant(Grants.AppointmentsPolicyOverride)]
    public async Task<ActionResult<BookingDto>> WaivePolicyConsequence(Guid participationId, [FromBody] PolicyConsequenceWaiveRequest request)
    {
        return Ok(await _bookingService.WaivePolicyConsequence(
            this.CurrentOrganizationId(), this.CurrentUserId(), participationId, request));
    }

    /// <summary>P1 (D6): naredba nad JEDNIM sudjelovanjem bira paket kroz ClientPackageId; PackageSelections (paket po
    /// sudjelovanju) postoji samo za Booking-wide / appointment-wide naredbe — ovdje se odbija, nikad tiho ignorira.</summary>
    private static void EnsureNoPackageSelections(System.Collections.Generic.ICollection<ParticipationPackageSelection> selections)
    {
        if (selections is { Count: > 0 })
            throw new ValidationAppException("Naredba nad jednim sudjelovanjem bira paket kroz ClientPackageId, ne PackageSelections.");
    }
}
