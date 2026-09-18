using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using BlueDragon.DuneLight.API.Authorization;
using BlueDragon.DuneLight.API.Extensions;
using BlueDragon.DuneLight.Core.DTOs.Appointments;
using BlueDragon.DuneLight.Core.DTOs.Schedule;
using BlueDragon.DuneLight.Core.DTOs.ScheduleBreaks;
using BlueDragon.DuneLight.Core.Enums;
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
    private readonly IBookingService _bookingService;
    private readonly IWaitlistService _waitlistService;
    private readonly IScheduleBreakService _scheduleBreakService;
    private readonly IPaymentService _paymentService;

    public AppointmentsController(
        IAppointmentService appointmentService, IBookingService bookingService, IWaitlistService waitlistService,
        IScheduleBreakService scheduleBreakService, IPaymentService paymentService)
    {
        _appointmentService = appointmentService;
        _bookingService = bookingService;
        _waitlistService = waitlistService;
        _scheduleBreakService = scheduleBreakService;
        _paymentService = paymentService;
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

    /// <summary>Povijest odrađenih termina po zaposleniku (samo Completed — usluge, individualni i grupni termini
    /// koje je stvarno odradio), najnoviji prvi.</summary>
    [HttpGet("by-employee/{employeeId:guid}")]
    [RequireGrant(Grants.AppointmentsView)]
    public async Task<ActionResult<PagedResult<AppointmentDto>>> GetByEmployee(Guid employeeId, [FromQuery] PagedRequest request)
    {
        return Ok(await _appointmentService.GetByEmployee(this.CurrentOrganizationId(), employeeId, request));
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

    /// <summary>Bookinzi (klijenti) na jednom terminu — korisno za duo/grupne termine gdje treba prikazati
    /// stanje po klijentu neovisno o cijelom terminu.</summary>
    [HttpGet("{appointmentId:guid}/bookings")]
    [RequireGrant(Grants.AppointmentsView)]
    public async Task<ActionResult<List<BookingDto>>> GetBookings(Guid appointmentId)
    {
        return Ok(await _bookingService.GetForAppointment(this.CurrentOrganizationId(), appointmentId));
    }

    /// <summary>Ad-hoc dodavanje Bookinga na postojeći termin (npr. dodatni gost na duo terminu bez pune izmjene).</summary>
    [HttpPost("{appointmentId:guid}/bookings")]
    [RequireGrant(Grants.AppointmentsWriteOwn, Grants.AppointmentsWriteAll)]
    public async Task<ActionResult<BookingDto>> AddBooking(Guid appointmentId, [FromBody] BookingCreateRequest request)
    {
        return Ok(await _bookingService.AddBooking(
            this.CurrentOrganizationId(), this.CurrentUserId(), this.HasGrant(Grants.AppointmentsWriteAll), appointmentId, request));
    }

    /// <summary>Otkazuje SAMO jednog klijenta na terminu (npr. jedan od dvoje na duo terminu) bez otkazivanja
    /// cijelog termina — za cijeli termin koristiti POST {id}/cancel.</summary>
    [HttpPatch("{appointmentId:guid}/bookings/{clientId:guid}/cancel")]
    [RequireGrant(Grants.AppointmentsWriteOwn, Grants.AppointmentsWriteAll)]
    public async Task<ActionResult<BookingDto>> CancelBooking(Guid appointmentId, Guid clientId, [FromBody] BookingCancelRequest request)
    {
        return Ok(await _bookingService.SetStatus(
            this.CurrentOrganizationId(), this.CurrentUserId(), this.HasGrant(Grants.AppointmentsWriteAll), appointmentId, clientId,
            new BookingSetStatusRequest
            {
                Status = BookingStatus.Cancelled,
                ReturnPackageEntry = request.ReturnPackageEntry,
                CancellationReason = request.CancellationReason
            }));
    }

    /// <summary>Izostanak SAMO jednog klijenta na terminu (npr. jedan od dvoje na duo terminu) bez da cijeli
    /// termin postane izostao — za cijeli termin koristiti POST {id}/no-show.</summary>
    [HttpPatch("{appointmentId:guid}/bookings/{clientId:guid}/no-show")]
    [RequireGrant(Grants.AppointmentsWriteOwn, Grants.AppointmentsWriteAll)]
    public async Task<ActionResult<BookingDto>> MarkBookingNoShow(Guid appointmentId, Guid clientId, [FromBody] BookingCancelRequest request)
    {
        return Ok(await _bookingService.SetStatus(
            this.CurrentOrganizationId(), this.CurrentUserId(), this.HasGrant(Grants.AppointmentsWriteAll), appointmentId, clientId,
            new BookingSetStatusRequest
            {
                Status = BookingStatus.NoShow,
                ReturnPackageEntry = request.ReturnPackageEntry,
                CancellationReason = request.CancellationReason
            }));
    }

    /// <summary>Poništava check-in/otkazivanje JEDNOG Bookinga natrag na Confirmed — administrativna korekcija.
    /// Za Form=Group dostupno s bilo kojeg terminalnog statusa (Completed/NoShow/Cancelled -&gt; Confirmed). Za
    /// Form=Individual namjerno UŽE — dostupno ISKLJUČIVO iz Completed (poništenje pogrešnog check-ina;
    /// Cancelled/NoShow nemaju povratnu putanju, vidi BookingService.ApplyIndividualCompletionCorrection), uklj.
    /// void check-in-generated Paymenta, povrat paket-ulaska, reverziju CommissionEntry i povratak
    /// Appointment.Status na Scheduled (bezuvjetno, i na multi-klijent terminu gdje sestrinski Booking ostaje
    /// Completed — vidi tamo). Vraćanje paket-ulaska, storniranje pripadajuće Notification pojave i poništenje
    /// naplate check-ina rješava isključivo BookingService.SetStatus.</summary>
    [HttpPatch("{appointmentId:guid}/bookings/{clientId:guid}/confirm")]
    [RequireGrant(Grants.AppointmentsWriteOwn, Grants.AppointmentsWriteAll)]
    public async Task<ActionResult<BookingDto>> ConfirmBooking(Guid appointmentId, Guid clientId)
    {
        return Ok(await _bookingService.SetStatus(
            this.CurrentOrganizationId(), this.CurrentUserId(), this.HasGrant(Grants.AppointmentsWriteAll), appointmentId, clientId,
            new BookingSetStatusRequest
            {
                Status = BookingStatus.Confirmed
            }));
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

    /// <summary>Ručno uklanjanje s liste čekanja (Waiting -&gt; Cancelled) — idempotentno.</summary>
    [HttpDelete("{appointmentId:guid}/waitlist/{clientId:guid}")]
    [RequireGrant(Grants.AppointmentsWriteOwn, Grants.AppointmentsWriteAll)]
    public async Task<ActionResult<WaitlistEntryDto>> CancelWaitlistEntry(Guid appointmentId, Guid clientId)
    {
        return Ok(await _waitlistService.Cancel(
            this.CurrentOrganizationId(), this.CurrentUserId(), this.HasGrant(Grants.AppointmentsWriteAll), appointmentId, clientId));
    }
}
