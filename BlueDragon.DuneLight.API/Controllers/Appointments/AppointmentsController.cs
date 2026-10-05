using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using BlueDragon.DuneLight.API.Authorization;
using BlueDragon.DuneLight.API.Extensions;
using BlueDragon.DuneLight.Core.DTOs.Appointments;
using BlueDragon.DuneLight.Core.DTOs.Catalog;
using BlueDragon.DuneLight.Core.DTOs.Schedule;
using BlueDragon.DuneLight.Core.DTOs.ScheduleBreaks;
using BlueDragon.DuneLight.Core.Enums;
using BlueDragon.DuneLight.Core.Interfaces.Appointments;
using BlueDragon.DuneLight.Core.Interfaces.Catalog;
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
    private readonly IBookingService _bookingService;
    private readonly IWaitlistService _waitlistService;
    private readonly IScheduleBreakService _scheduleBreakService;
    private readonly IPaymentService _paymentService;
    private readonly IServiceAvailabilityService _serviceAvailabilityService;

    public AppointmentsController(
        IAppointmentService appointmentService, IBookingService bookingService, IWaitlistService waitlistService,
        IScheduleBreakService scheduleBreakService, IPaymentService paymentService,
        IServiceAvailabilityService serviceAvailabilityService)
    {
        _appointmentService = appointmentService;
        _bookingService = bookingService;
        _waitlistService = waitlistService;
        _scheduleBreakService = scheduleBreakService;
        _paymentService = paymentService;
        _serviceAvailabilityService = serviceAvailabilityService;
    }

    /// <summary>Usluge bookabilne u poslovnici za formu novog termina — namjerno gated iza appointments.write.own/all,
    /// ne catalog.services.view (vidi IServiceAvailabilityService.GetBookableServices), da zakazivanje termina ne
    /// ovisi o pristupu administraciji kataloga usluga. Vraća lagani DTO, ne puni ServiceDto.</summary>
    [HttpGet("services")]
    [RequireGrant(Grants.AppointmentsWriteOwn, Grants.AppointmentsWriteAll)]
    public async Task<ActionResult<List<AppointmentServiceOptionDto>>> GetBookableServices([FromQuery] Guid companyId)
    {
        return Ok(await _serviceAvailabilityService.GetBookableServices(this.CurrentOrganizationId(), companyId));
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
    public async Task<ActionResult<PagedResult<ClientAppointmentHistoryDto>>> GetByClient(Guid clientId, [FromQuery] PagedRequest request)
    {
        return Ok(await _appointmentService.GetByClient(this.CurrentOrganizationId(), clientId, request));
    }

    /// <summary>Povijest odrađenih termina po zaposleniku (segment zaposlenika s barem jednim Completed sudjelovanjem — usluge, individualni i grupni termini
    /// koje je stvarno odradio), najnoviji prvi.</summary>
    [HttpGet("by-employee/{employeeId:guid}")]
    [RequireGrant(Grants.AppointmentsView)]
    public async Task<ActionResult<PagedResult<AppointmentDto>>> GetByEmployee(Guid employeeId, [FromQuery] PagedRequest request)
    {
        return Ok(await _appointmentService.GetByEmployee(this.CurrentOrganizationId(), employeeId, request));
    }

    /// <summary>Phase M1B — ciljni segmentni ugovor kreiranja (termin + segmenti + sudionici po segmentu). Phase M1E: više
    /// segmenata je omogućeno (atomično). Phase M1G: segment ima 1..N ravnopravnih zaposlenika; za 2+ zaposlenika izvor cijene
    /// (PricingMode/PricingEmployeeId) je obavezan.</summary>
    [HttpPost]
    [RequireGrant(Grants.AppointmentsWriteOwn, Grants.AppointmentsWriteAll)]
    public async Task<ActionResult<AppointmentDto>> CreateSegmented([FromBody] AppointmentCreateRequest request)
    {
        AppointmentDto created = await _appointmentService.Create(
            this.CurrentOrganizationId(), this.CurrentUserId(), this.HasGrant(Grants.AppointmentsWriteAll), request);
        return CreatedAtAction(nameof(GetById), new { id = created.Id }, created);
    }

    /// <summary>Phase M1H — "upiši odrađeno" (POS): atomično stvara termin s JEDNIM eksplicitnim segmentom, po jedan
    /// Booking/sudjelovanje za svakog klijenta, odrađuje ih i primjenjuje namirenje (paket ili novac) po klijentu.</summary>
    [HttpPost("complete")]
    [RequireGrant(Grants.AppointmentsWriteOwn, Grants.AppointmentsWriteAll)]
    public async Task<ActionResult<AppointmentDto>> CompleteNow([FromBody] AppointmentCompleteNowRequest request)
    {
        AppointmentDto created = await _appointmentService.CompleteNow(
            this.CurrentOrganizationId(), this.CurrentUserId(), this.HasGrant(Grants.AppointmentsWriteAll), request);
        return CreatedAtAction(nameof(GetById), new { id = created.Id }, created);
    }

    /// <summary>Phase M1H — napomena termina (samo metapodatak agregata).</summary>
    [HttpPatch("{id:guid}/note")]
    [RequireGrant(Grants.AppointmentsWriteOwn, Grants.AppointmentsWriteAll)]
    public async Task<ActionResult<AppointmentDto>> ChangeNote(Guid id, [FromBody] AppointmentNoteChangeRequest request)
    {
        return Ok(await _appointmentService.ChangeNote(
            this.CurrentOrganizationId(), this.CurrentUserId(), this.HasGrant(Grants.AppointmentsWriteAll), id, request));
    }

    /// <summary>Phase M1E — dodaje NOVI segment postojećem (generičkom) terminu; navedeni klijenti sudjeluju u njemu (postojeći
    /// Booking klijenta se ponovno koristi). Validira se puno ciljno stanje uključujući sestrinske segmente.</summary>
    [HttpPost("{id:guid}/segments")]
    [RequireGrant(Grants.AppointmentsWriteOwn, Grants.AppointmentsWriteAll)]
    public async Task<ActionResult<AppointmentDto>> AddSegment(Guid id, [FromBody] AppointmentSegmentAddRequest request)
    {
        return Ok(await _appointmentService.AddSegment(
            this.CurrentOrganizationId(), this.CurrentUserId(), this.HasGrant(Grants.AppointmentsWriteAll), id, request));
    }

    /// <summary>Phase M1E — dodaje klijenta ODABRANIM segmentima termina (jedan Booking po klijentu, jedno sudjelovanje po
    /// segmentu, cijena po sudjelovanju). Paket/naplata se ne primjenjuju pri dodavanju.</summary>
    [HttpPost("{id:guid}/clients")]
    [RequireGrant(Grants.AppointmentsWriteOwn, Grants.AppointmentsWriteAll)]
    public async Task<ActionResult<AppointmentDto>> AddClient(Guid id, [FromBody] AppointmentClientAddRequest request)
    {
        return Ok(await _appointmentService.AddClient(
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

    /// <summary>Bookinzi (klijenti) na jednom terminu — korisno za duo/grupne termine gdje treba prikazati
    /// stanje po klijentu neovisno o cijelom terminu.</summary>
    [HttpGet("{appointmentId:guid}/bookings")]
    [RequireGrant(Grants.AppointmentsView)]
    public async Task<ActionResult<List<BookingDto>>> GetBookings(Guid appointmentId)
    {
        return Ok(await _bookingService.GetForAppointment(this.CurrentOrganizationId(), appointmentId));
    }

    /// <summary>Gost na GRUPNOM occurrenceu: novo sudjelovanje na eksplicitnom segmentu (SegmentId obavezan). Individualni
    /// termin dodaje klijente kroz POST {id}/clients.</summary>
    [HttpPost("{appointmentId:guid}/bookings")]
    [RequireGrant(Grants.AppointmentsWriteOwn, Grants.AppointmentsWriteAll)]
    public async Task<ActionResult<BookingDto>> AddGroupGuest(Guid appointmentId, [FromBody] BookingCreateRequest request)
    {
        return Ok(await _bookingService.AddGroupGuest(
            this.CurrentOrganizationId(), this.CurrentUserId(), this.HasGrant(Grants.AppointmentsWriteAll), appointmentId, request));
    }

    /// <summary>Otkazuje SAMO jednog klijenta na terminu (npr. jedan od dvoje na duo terminu) bez otkazivanja
    /// cijelog termina — za cijeli termin koristiti POST {id}/cancel. Phase M0: Booking-wide naredba — svako aktivno
    /// sudjelovanje klijenta na terminu prelazi u Cancelled (IBookingService.CancelBooking); za jedno sudjelovanje
    /// adresirati /api/participations/{participationId}/cancel.</summary>
    [HttpPatch("{appointmentId:guid}/bookings/{clientId:guid}/cancel")]
    [RequireGrant(Grants.AppointmentsWriteOwn, Grants.AppointmentsWriteAll)]
    public async Task<ActionResult<BookingDto>> CancelBooking(Guid appointmentId, Guid clientId, [FromBody] BookingCancelRequest request)
    {
        return Ok(await _bookingService.CancelBooking(
            this.CurrentOrganizationId(), this.CurrentUserId(), this.HasGrant(Grants.AppointmentsWriteAll), appointmentId, clientId, request));
    }

    /// <summary>Povijest Paymenta (monetarnih naplata) jednog Bookinga, uklj. voidane, preko svih njegovih
    /// povijesnih Checkout stavki — vidi IPaymentService. Kreiranje/void Paymenta ide kroz ICheckoutService
    /// (vidi CheckoutsController) jer Payment pripada Checkoutu, ne izravno Bookingu.</summary>
    [HttpGet("{appointmentId:guid}/bookings/{clientId:guid}/payments")]
    [RequireGrant(Grants.AppointmentsView)]
    public async Task<ActionResult<List<PaymentDto>>> GetPayments(Guid appointmentId, Guid clientId)
    {
        return Ok(await _paymentService.GetForBooking(this.CurrentOrganizationId(), appointmentId, clientId));
    }

    /// <summary>Lista čekanja termina (puna povijest, uklj. Promoted/Cancelled/Expired) — admin/trener roster prikaz.</summary>
    [HttpGet("{appointmentId:guid}/waitlist")]
    [RequireGrant(Grants.AppointmentsView)]
    public async Task<ActionResult<List<WaitlistEntryDto>>> GetWaitlist(Guid appointmentId)
    {
        return Ok(await _waitlistService.GetForAppointment(this.CurrentOrganizationId(), appointmentId));
    }

    /// <summary>Upis na listu čekanja — dopušteno samo kad je termin pun (inače CAPACITY_AVAILABLE).</summary>
    [HttpPost("{appointmentId:guid}/waitlist")]
    [RequireGrant(Grants.AppointmentsWriteOwn, Grants.AppointmentsWriteAll)]
    public async Task<ActionResult<WaitlistEntryDto>> JoinWaitlist(Guid appointmentId, [FromBody] WaitlistJoinRequest request)
    {
        return Ok(await _waitlistService.Join(
            this.CurrentOrganizationId(), this.CurrentUserId(), this.HasGrant(Grants.AppointmentsWriteAll), appointmentId, request));
    }

    /// <summary>Ručno uklanjanje s liste čekanja (Waiting -&gt; Cancelled) — idempotentno. Phase M1F: ?segmentId= je obavezan
    /// za višesegmentni occurrence (lista čekanja je po segmentu).</summary>
    [HttpDelete("{appointmentId:guid}/waitlist/{clientId:guid}")]
    [RequireGrant(Grants.AppointmentsWriteOwn, Grants.AppointmentsWriteAll)]
    public async Task<ActionResult<WaitlistEntryDto>> CancelWaitlistEntry(Guid appointmentId, Guid clientId, [FromQuery] Guid? segmentId)
    {
        return Ok(await _waitlistService.Cancel(
            this.CurrentOrganizationId(), this.CurrentUserId(), this.HasGrant(Grants.AppointmentsWriteAll), appointmentId, clientId, segmentId));
    }
}
