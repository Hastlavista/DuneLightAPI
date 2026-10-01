using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using BlueDragon.DuneLight.Core.DTOs.Appointments;
using BlueDragon.DuneLight.Core.DTOs.Catalog;
using BlueDragon.DuneLight.Core.DTOs.Clients;
using BlueDragon.DuneLight.Core.Enums;
using BlueDragon.DuneLight.Core.Interfaces.Appointments;
using BlueDragon.DuneLight.Core.Interfaces.Catalog;
using BlueDragon.DuneLight.Core.Interfaces.Clients;
using BlueDragon.DuneLight.Core.Interfaces.Organization;
using BlueDragon.DuneLight.Core.Events;
using BlueDragon.DuneLight.Core.Shared;
using BlueDragon.DuneLight.Core.Shared.Exceptions;
using BlueDragon.DuneLight.Infrastructure.Domain.Models.Appointments;
using BlueDragon.DuneLight.Infrastructure.Domain.Models.Checkouts;
using BlueDragon.DuneLight.Infrastructure.Domain.Models.Clients;
using BlueDragon.DuneLight.Infrastructure.Domain.Models.Employees;
using BlueDragon.DuneLight.Infrastructure.Handlers.Interfaces;
using BlueDragon.DuneLight.Infrastructure.Outbox;
using BlueDragon.DuneLight.Infrastructure.UnitOfWork;
using BlueDragon.DuneLight.Infrastructure.Utils;
using Microsoft.EntityFrameworkCore;

namespace BlueDragon.DuneLight.Infrastructure.Services;

/// <summary>
/// Vidi IBookingService za domensku napomenu. Namjerno pokriva i individualne i grupne Bookinge kroz
/// jedan mehanizam (state machine + paket resolucija) — GroupAttendanceService je sada tanki adapter
/// preko ovoga koji čuva stari /api/groups/appointments/{id}/attendance ugovor (vidi tamo).
/// </summary>
public class BookingService : IBookingService
{
    private const string NotOwnerMessage = "Trener smije upravljati samo bookinzima na svojim vlastitim terminima.";

    private readonly IAppointmentHandler _appointmentHandler;
    private readonly ISchedulingOccupancyHandler _schedulingOccupancyHandler;
    private readonly IAppointmentAuditLogHandler _auditLogHandler;
    private readonly IClientPackageService _clientPackageService;
    private readonly IPackageConsumptionLedgerService _packageConsumptionLedgerService;
    private readonly IClientHandler _clientHandler;
    private readonly IEmployeeHandler _employeeHandler;
    private readonly IPricingService _pricingService;
    private readonly IOrganizationSettingsService _organizationSettingsService;
    private readonly IWaitlistPromotionService _waitlistPromotionService;
    private readonly IPaymentLedgerService _paymentLedgerService;
    private readonly ICheckoutHandler _checkoutHandler;
    private readonly IBookingSegmentParticipationHandler _participationHandler;
    private readonly ICommissionLedgerService _commissionLedgerService;
    private readonly IOutboxWriter _outboxWriter;
    private readonly INotificationHandler _notificationHandler;
    private readonly IUnitOfWorkFactory _unitOfWorkFactory;

    public BookingService(
        IAppointmentHandler appointmentHandler,
        ISchedulingOccupancyHandler schedulingOccupancyHandler,
        IAppointmentAuditLogHandler auditLogHandler,
        IClientPackageService clientPackageService,
        IPackageConsumptionLedgerService packageConsumptionLedgerService,
        IClientHandler clientHandler,
        IEmployeeHandler employeeHandler,
        IPricingService pricingService,
        IOrganizationSettingsService organizationSettingsService,
        IWaitlistPromotionService waitlistPromotionService,
        IPaymentLedgerService paymentLedgerService,
        ICheckoutHandler checkoutHandler,
        IBookingSegmentParticipationHandler participationHandler,
        ICommissionLedgerService commissionLedgerService,
        IOutboxWriter outboxWriter,
        INotificationHandler notificationHandler,
        IUnitOfWorkFactory unitOfWorkFactory)
    {
        _appointmentHandler = appointmentHandler;
        _schedulingOccupancyHandler = schedulingOccupancyHandler;
        _auditLogHandler = auditLogHandler;
        _clientPackageService = clientPackageService;
        _packageConsumptionLedgerService = packageConsumptionLedgerService;
        _clientHandler = clientHandler;
        _employeeHandler = employeeHandler;
        _pricingService = pricingService;
        _organizationSettingsService = organizationSettingsService;
        _waitlistPromotionService = waitlistPromotionService;
        _paymentLedgerService = paymentLedgerService;
        _checkoutHandler = checkoutHandler;
        _participationHandler = participationHandler;
        _commissionLedgerService = commissionLedgerService;
        _outboxWriter = outboxWriter;
        _notificationHandler = notificationHandler;
        _unitOfWorkFactory = unitOfWorkFactory;
    }

    /// <summary>Isti IPricingService poziv kao AppointmentService.ResolveServicePrice — ne duplicira logiku
    /// razrješavanja cijene, samo poziva centralni resolver po Service/Company/datumu termina.</summary>
    /// <remarks>Phase D3B2: vraća cijelo razrješavanje (Price + Source) — Source je istinit snapshot za
    /// BookingSegmentParticipation.BaseAmountSource (vidi BookingPricing.FromResolution).</remarks>
    private Task<ResolvePriceResponse> ResolveServicePrice(Guid organizationId, Guid serviceId, Guid companyId, DateTimeOffset date)
    {
        return _pricingService.ResolvePrice(organizationId, new ResolvePriceRequest
        {
            SubjectType = PricingSubjectType.Service,
            SubjectId = serviceId,
            CompanyId = companyId,
            Date = date
        });
    }

    public async Task<List<BookingDto>> GetForAppointment(Guid organizationId, Guid appointmentId)
    {
        Appointment appointment = await _appointmentHandler.GetWithBookingsForMutation(organizationId, appointmentId);
        if (appointment == null)
            throw new NotFoundAppException("Appointment", appointmentId);

        return appointment.Bookings.Select(b => ToDto(b, appointment.Form)).ToList();
    }

    public async Task<BookingDto> AddBooking(Guid organizationId, Guid userId, bool hasFullScope, Guid appointmentId, BookingCreateRequest request)
    {
        Appointment appointment = await _appointmentHandler.GetWithBookingsForMutation(organizationId, appointmentId);
        if (appointment == null)
            throw new NotFoundAppException("Appointment", appointmentId);

        if (appointment.Status == AppointmentStatus.Cancelled)
            throw new BusinessRuleException(ErrorCodes.AppointmentNotMovable, "Otkazan termin se ne može dopunjavati novim rezervacijama.");

        await AppointmentOwnership.EnsureCallerIsAssigned(_employeeHandler, organizationId, userId, hasFullScope, appointment, NotOwnerMessage);

        Booking existing = appointment.Bookings.FirstOrDefault(b => b.ClientId == request.ClientId);
        if (existing != null)
            return ToDto(existing, appointment.Form);

        Client client = await LoadEligibleClient(organizationId, request.ClientId);

        await EnsureClientHasNoOverlap(organizationId, appointment, request.ClientId);

        AppointmentExecutionContext execution = ExecutionContextResolver.ForAppointment(appointment);
        ResolvePriceResponse resolvedPrice = await ResolveServicePrice(organizationId, execution.ServiceId, execution.CompanyId, execution.StartsAt);

        Booking booking = BookingFactory.CreateConfirmed(
            organizationId, AppointmentSegments.GetSingleExecutionSegment(appointment), request.ClientId,
            BookingPricing.AtSuggested(resolvedPrice), DateTimeOffset.UtcNow);

        await using (IUnitOfWork uow = await _unitOfWorkFactory.Begin())
        {
            if (appointment.Form == AppointmentForm.Group)
                await EnsureGroupCapacityAvailable(uow, organizationId, appointmentId);

            await _appointmentHandler.AddBooking(uow, booking);
            await uow.CommitAsync();
        }

        booking.Client = client;
        return ToDto(booking, appointment.Form);
    }

    /// <summary>Tanki wrapper preko GroupCapacityGuard (dijeljen s GroupService.AddMember — vidi ondje za puni
    /// opis count semantike). Zaključava Appointment redak (FOR UPDATE) — SetStatus (jedini pozivatelj za
    /// postojeći Booking) već zaključava Appointment PRIJE Bookinga (vidi tamo za redoslijed zaključavanja), pa
    /// ovaj (redundantan, besplatan unutar iste transakcije) re-lock ovdje ne uvodi obrnut poredak. AddBooking
    /// (drugi pozivatelj) zove ovo PRIJE ikakvog Booking retka jer se on tek stvara u istoj transakciji.</summary>
    private Task EnsureGroupCapacityAvailable(IUnitOfWork uow, Guid organizationId, Guid appointmentId)
    {
        return GroupCapacityGuard.EnsureAvailable(_appointmentHandler, uow, organizationId, appointmentId);
    }

    /// <summary>PRIVREMENA BookingId kompatibilnost (Phase M0): (termin, klijent) adresira Booking; naredba smije djelovati
    /// samo kad Booking ima TOČNO JEDNO sudjelovanje (inače BOOKING_PARTICIPATION_AMBIGUOUS) i odmah delegira na
    /// participation-native prijelaz — bez vlastite poslovne logike. Jedina iznimka je nastanak gost-Bookinga na grupnom
    /// terminu (privremeni jednostruki nastanak: BookingFactory stvara Booking s jednim sudjelovanjem na izvršnom segmentu).</summary>
    public async Task<BookingDto> SetStatus(
        Guid organizationId, Guid userId, bool hasFullScope, Guid appointmentId, Guid clientId, BookingSetStatusRequest request)
    {
        Appointment appointment = await _appointmentHandler.GetWithBookingsForMutation(organizationId, appointmentId);
        if (appointment == null)
            throw new NotFoundAppException("Appointment", appointmentId);

        await AppointmentOwnership.EnsureCallerIsAssigned(_employeeHandler, organizationId, userId, hasFullScope, appointment, NotOwnerMessage);

        Booking booking = appointment.Bookings.FirstOrDefault(b => b.ClientId == clientId);
        if (booking != null)
            return await TransitionParticipation(
                organizationId, userId, appointment, BookingParticipations.GetSingleParticipation(booking).Id.GetValueOrDefault(), request);

        return await CheckInNewGuest(organizationId, userId, appointment, clientId, request);
    }

    /// <summary>Phase M0 — participation-native prijelaz: naredba adresira JEDNO sudjelovanje i djeluje samo na njega
    /// (ostala sudjelovanja istog Bookinga se ne diraju). Ista pravila prijelaza kao SetStatus (vidi IBookingService).</summary>
    public async Task<BookingDto> SetParticipationStatus(
        Guid organizationId, Guid userId, bool hasFullScope, Guid participationId, BookingSetStatusRequest request)
    {
        Guid? appointmentId = await _participationHandler.GetAppointmentIdOf(organizationId, participationId);
        if (appointmentId == null)
            throw new NotFoundAppException("Participation", participationId);

        Appointment appointment = await _appointmentHandler.GetWithBookingsForMutation(organizationId, appointmentId.Value);
        if (appointment == null)
            throw new NotFoundAppException("Appointment", appointmentId.Value);

        await AppointmentOwnership.EnsureCallerIsAssigned(_employeeHandler, organizationId, userId, hasFullScope, appointment, NotOwnerMessage);

        return await TransitionParticipation(organizationId, userId, appointment, participationId, request);
    }

    /// <summary>Phase M0 — Booking-wide otkazivanje: Booking nema vlastiti status, pa se naredba izvršava kao kontrolirani
    /// prijelaz Confirmed -&gt; Cancelled SVAKOG aktivnog (Confirmed) sudjelovanja, u jednoj transakciji, sa zaključanim
    /// svim sudjelovanjima Bookinga (redoslijed po Id-u); terminalna sudjelovanja (Completed/NoShow/Cancelled) su povijest i
    /// ostaju netaknuta. Svako otkazano sudjelovanje dobiva vlastiti StatusVersion inkrement, audit i Outbox pojavu.
    /// PRIVREMENA kompatibilnost: Booking s točno jednim sudjelovanjem koje NIJE Confirmed delegira na participation-native
    /// prijelaz (dosadašnje ponašanje: grupno poništenje check-ina + otkazivanje, AlreadyCompleted za individualni,
    /// idempotentan Cancelled).</summary>
    public async Task<BookingDto> CancelBooking(
        Guid organizationId, Guid userId, bool hasFullScope, Guid appointmentId, Guid clientId, BookingCancelRequest request)
    {
        Appointment appointment = await _appointmentHandler.GetWithBookingsForMutation(organizationId, appointmentId);
        if (appointment == null)
            throw new NotFoundAppException("Appointment", appointmentId);

        await AppointmentOwnership.EnsureCallerIsAssigned(_employeeHandler, organizationId, userId, hasFullScope, appointment, NotOwnerMessage);

        Booking booking = appointment.Bookings.FirstOrDefault(b => b.ClientId == clientId);
        if (booking == null)
            throw new NotFoundAppException("Booking", clientId);

        BookingSetStatusRequest cancel = new BookingSetStatusRequest
        {
            Status = BookingStatus.Cancelled,
            ReturnPackageEntry = request.ReturnPackageEntry,
            CancellationReason = request.CancellationReason
        };

        if (!booking.Participations.Any(p => p.Status == ParticipationStatus.Confirmed))
        {
            if (booking.Participations.Count == 1)
                return await TransitionParticipation(organizationId, userId, appointment, booking.Participations[0].Id.GetValueOrDefault(), cancel);

            throw new BusinessRuleException(ErrorCodes.AlreadyCompleted, "Booking nema aktivnih sudjelovanja za otkazivanje.");
        }

        try
        {
            await using IUnitOfWork uow = await _unitOfWorkFactory.Begin();

            // Appointment PA sudjelovanja (vidi TransitionParticipation za redoslijed zaključavanja).
            if (await _appointmentHandler.GetForUpdate(uow, organizationId, appointmentId) == null)
                throw new NotFoundAppException("Appointment", appointmentId);

            Booking locked = await _participationHandler.GetBookingWithLockedParticipations(
                uow, organizationId, booking.Id.GetValueOrDefault());
            if (locked == null)
                throw new NotFoundAppException("Booking", clientId);

            // Pod lockom: aktivna sudjelovanja u stabilnom redoslijedu (po Id-u, kao i lock).
            foreach (BookingSegmentParticipation participation in locked.Participations
                         .Where(p => p.Status == ParticipationStatus.Confirmed)
                         .OrderBy(p => p.Id)
                         .ToList())
                await ApplyTransitionInTransaction(uow, organizationId, userId, appointment, locked, participation, isNewGuestBooking: false, cancel);

            await uow.CommitAsync();
        }
        catch (DbUpdateConcurrencyException)
        {
            throw new BusinessRuleException(
                ErrorCodes.ConcurrencyConflict, "Podaci su upravo promijenjeni od strane drugog zahtjeva — pokušajte ponovno.");
        }

        Booking refreshed = await _appointmentHandler.GetBooking(organizationId, appointmentId, clientId);
        return ToDto(refreshed, appointment.Form);
    }

    /// <summary>Pravila prijelaza koja ovise o stanju sudjelovanja PRIJE transakcije (ista kao prije Phase M0, sad nad
    /// adresiranim sudjelovanjem).</summary>
    private static void ValidateTransition(Appointment appointment, BookingSegmentParticipation participation, BookingSetStatusRequest request)
    {
        bool isGroup = appointment.Form == AppointmentForm.Group;

        if (!isGroup && request.Status == BookingStatus.Completed)
            throw new ValidationAppException(
                "Individualni termin se odrađuje kroz complete/complete-existing (naplata je zajednička za cijeli termin), ne po pojedinom bookingu.");

        // Individual: Confirmed je dopušten kao korekcija odrađenog check-ina (Completed -> Confirmed, vidi
        // ApplyIndividualCompletionCorrection), korekcija pogrešno evidentiranog izostanka (NoShow -> Confirmed, vidi
        // ApplyIndividualNoShowCorrection) ili kao idempotentan retry (Confirmed -> Confirmed no-op) — Cancelled ->
        // Confirmed I DALJE NIJE podržan prijelaz za Individual (namjerno uže od Group, koji dopušta povratak s BILO
        // KOJEG terminalnog statusa).
        if (!isGroup && request.Status == BookingStatus.Confirmed && participation.Status == ParticipationStatus.Cancelled)
            throw new ValidationAppException(
                "Povratak na Confirmed za individualni booking dopušten je samo korekcijom odrađenog check-ina " +
                "(Completed -> Confirmed) ili pogrešno evidentiranog izostanka (NoShow -> Confirmed) — Cancelled nema povratnu putanju.");
    }

    /// <summary>Phase M0 — jezgra participation-native prijelaza postojećeg sudjelovanja: Appointment-PA-sudjelovanje
    /// zaključavanje, prijelaz u transakciji, svježi DTO Bookinga.</summary>
    private async Task<BookingDto> TransitionParticipation(
        Guid organizationId, Guid userId, Appointment appointment, Guid participationId, BookingSetStatusRequest request)
    {
        Guid appointmentId = appointment.Id.GetValueOrDefault();
        Booking preloaded = appointment.Bookings.FirstOrDefault(b => b.Participations.Any(p => p.Id == participationId));
        if (preloaded == null)
            throw new NotFoundAppException("Participation", participationId);

        ValidateTransition(appointment, BookingParticipations.ById(preloaded, participationId), request);

        try
        {
            await using IUnitOfWork uow = await _unitOfWorkFactory.Begin();

            // Appointment-PA-sudjelovanje redoslijed zaključavanja — USKLAĐENO s dominantnim redoslijedom u agregatu
            // (AppointmentService.CompleteExisting/ChangeToTerminalStatus/CompleteGroupAppointment, GroupService.AddMember,
            // WaitlistPromotionService.PromoteEligibleWaiters SVI zaključavaju Appointment PRIJE bilo kakve mutacije
            // sudjelovanja). Appointment lock čuva INVARIJANTE SPREMNIKA (kapacitet grupe, Appointment.Status kod
            // korekcije completiona) — ne izvršno stanje pojedinog sudjelovanja.
            if (await _appointmentHandler.GetForUpdate(uow, organizationId, appointmentId) == null)
                throw new NotFoundAppException("Appointment", appointmentId);

            // Phase M0: zaključava SUDJELOVANJE (ne Booking redak) i od ovog trenutka koristi njegovo stanje POD LOCKOM —
            // serijalizira ovaj prijelaz s konkurentnim prijelazom/plaćanjem/potrošnjom ISTOG sudjelovanja i s
            // BookingNoShow/BookingCancelled notification handlerima koji zaključavaju isti redak. Drugo sudjelovanje
            // istog Bookinga se NE blokira.
            Booking booking = await _participationHandler.GetBookingWithLockedParticipation(uow, organizationId, participationId);
            if (booking == null)
                throw new NotFoundAppException("Participation", participationId);

            BookingSegmentParticipation participation = BookingParticipations.ById(booking, participationId);
            ValidateTransition(appointment, participation, request);

            await ApplyTransitionInTransaction(uow, organizationId, userId, appointment, booking, participation, isNewGuestBooking: false, request);

            await uow.CommitAsync();
        }
        catch (DbUpdateConcurrencyException)
        {
            throw new BusinessRuleException(
                ErrorCodes.ConcurrencyConflict, "Podaci su upravo promijenjeni od strane drugog zahtjeva — pokušajte ponovno.");
        }

        Booking refreshed = await _appointmentHandler.GetBooking(organizationId, appointmentId, preloaded.ClientId);
        return ToDto(refreshed, appointment.Form);
    }

    /// <summary>Gost izvan popisa članova koji se čekira izravno kroz SetStatus bez prethodnog AddBooking poziva (isto
    /// ponašanje kao staro GroupAttendanceService.HandleAttended/HandleNotAttended kad existing==null) — dopušteno samo za
    /// Form=Group, individualni Bookinzi uvijek postoje od kreiranja termina. Privremeni jednostruki NASTANAK.</summary>
    private async Task<BookingDto> CheckInNewGuest(
        Guid organizationId, Guid userId, Appointment appointment, Guid clientId, BookingSetStatusRequest request)
    {
        Guid appointmentId = appointment.Id.GetValueOrDefault();
        if (appointment.Form != AppointmentForm.Group)
        {
            if (request.Status == BookingStatus.Completed)
                throw new ValidationAppException(
                    "Individualni termin se odrađuje kroz complete/complete-existing (naplata je zajednička za cijeli termin), ne po pojedinom bookingu.");
            throw new NotFoundAppException("Booking", clientId);
        }

        await LoadEligibleClient(organizationId, clientId);

        if ((request.Status == BookingStatus.Completed || request.Status == BookingStatus.NoShow) &&
            AppointmentFrame.Of(appointment).StartsAt > DateTimeOffset.UtcNow)
        {
            throw new BusinessRuleException(
                ErrorCodes.AttendanceBeforeStart,
                "Prisustvo gosta (Completed/NoShow) može se evidentirati tek nakon početka termina.");
        }

        await EnsureClientHasNoOverlap(organizationId, appointment, clientId);

        AppointmentExecutionContext guestExecution = ExecutionContextResolver.ForAppointment(appointment);
        ResolvePriceResponse guestPrice = await ResolveServicePrice(
            organizationId, guestExecution.ServiceId, guestExecution.CompanyId, guestExecution.StartsAt);

        Booking booking = BookingFactory.CreateConfirmed(
            organizationId, AppointmentSegments.GetSingleExecutionSegment(appointment), clientId,
            BookingPricing.AtSuggested(guestPrice), DateTimeOffset.UtcNow);

        try
        {
            await using IUnitOfWork uow = await _unitOfWorkFactory.Begin();
            await ApplyTransitionInTransaction(
                uow, organizationId, userId, appointment, booking, booking.Participations.Single(), isNewGuestBooking: true, request);
            await uow.CommitAsync();
        }
        catch (DbUpdateConcurrencyException)
        {
            throw new BusinessRuleException(
                ErrorCodes.ConcurrencyConflict, "Podaci su upravo promijenjeni od strane drugog zahtjeva — pokušajte ponovno.");
        }

        Booking refreshed = await _appointmentHandler.GetBooking(organizationId, appointmentId, clientId);
        return ToDto(refreshed, appointment.Form);
    }

    /// <summary>Phase M0 — prijelaz JEDNOG sudjelovanja unutar pozivateljeve transakcije (pozivatelj je već zaključao
    /// Appointment i to sudjelovanje). Sve nuspojave (kapacitet, paket, plaćanje, provizija, audit, Outbox, Notification,
    /// lista čekanja) su po SUDJELOVANJU: StatusVersion, audit StatusVersion i identitet pojave su verzija OVOG
    /// sudjelovanja.</summary>
    private async Task ApplyTransitionInTransaction(
        IUnitOfWork uow, Guid organizationId, Guid userId, Appointment appointment, Booking booking,
        BookingSegmentParticipation participation, bool isNewGuestBooking, BookingSetStatusRequest request)
    {
        Guid appointmentId = appointment.Id.GetValueOrDefault();
        bool isGroup = appointment.Form == AppointmentForm.Group;
        ParticipationStatus oldStatus = participation.Status;
        int oldStatusVersion = participation.StatusVersion;

        // Kapacitet se provjerava kad ovaj poziv stvarno persistira NOVI Confirmed (mjesto-zauzimajući) status —
        // bilo za posve novi gost-Booking (isNewGuestBooking), bilo za POSTOJEĆE sudjelovanje koje se administrativno
        // vraća na Confirmed (Completed/NoShow/Cancelled -> Confirmed kroz PATCH .../confirm). Idempotentan Confirmed ->
        // Confirmed nikad ne ulazi ovamo — ne zauzima novo mjesto pa ostaje no-op čak i kad je grupa puna. Za POSTOJEĆE
        // sudjelovanje provjera je namjerno future-only (početak segmenta termina u budućnosti) — nakon početka termina
        // nominalni kapacitet više ne ograničava korekciju povijesne prisutnosti.
        // Phase M0: "buduće" = početak SEGMENTA ovog sudjelovanja (ne okvir termina).
        DateTimeOffset participationStartsAt = ExecutionContextResolver.ForParticipation(appointment, booking, participation).StartsAt;
        bool isExistingReturningToConfirmed =
            !isNewGuestBooking && oldStatus != ParticipationStatus.Confirmed && participationStartsAt > DateTimeOffset.UtcNow;

        if (isGroup && request.Status == BookingStatus.Confirmed && (isNewGuestBooking || isExistingReturningToConfirmed))
            await EnsureGroupCapacityAvailable(uow, organizationId, appointmentId);

        (PaymentMethod Method, decimal Amount)? pendingPayment = null;
        if (isGroup)
            pendingPayment = await ApplyGroupTransition(uow, organizationId, userId, appointment, booking, participation, request);
        else if (request.Status == BookingStatus.Confirmed && oldStatus == ParticipationStatus.NoShow)
            await ApplyIndividualNoShowCorrection(uow, organizationId, userId, appointment, participation);
        else if (request.Status == BookingStatus.Confirmed)
            await ApplyIndividualCompletionCorrection(uow, organizationId, userId, appointment, booking, participation);
        else
            await ApplyIndividualTransition(uow, organizationId, userId, appointment, booking, participation, request);

        booking.Note = request.Note ?? booking.Note;
        booking.UpdatedAt = DateTimeOffset.UtcNow;
        booking.UpdatedBy = userId;

        if (isNewGuestBooking)
            await _appointmentHandler.AddBooking(uow, booking);
        else
            await _appointmentHandler.UpdateBooking(uow, booking);

        // Payment mora ići TEK nakon što je redak sudjelovanja stvarno persistiran (FK checkout_items -> sudjelovanje).
        if (pendingPayment.HasValue)
            await _paymentLedgerService.RecordPayment(
                uow, organizationId, userId, appointment.CompanyId, booking, participation, pendingPayment.Value.Method, pendingPayment.Value.Amount,
                note: null, isCheckInGenerated: true);

        ParticipationStatus newStatus = participation.Status;
        if (oldStatus != newStatus)
        {
            await _auditLogHandler.Add(uow, new AppointmentAuditLog
            {
                Id = Guid.NewGuid(),
                AppointmentId = appointmentId,
                BookingId = booking.Id,
                BookingSegmentParticipationId = participation.Id,
                ChangeType = "BookingStatus",
                OldValue = oldStatus.ToString(),
                NewValue = newStatus.ToString(),
                StatusVersion = participation.StatusVersion,
                ChangedAt = DateTimeOffset.UtcNow,
                ChangedBy = userId
            });
        }

        // Notification-producing Outbox event — samo za STVARAN prijelaz Confirmed -> Cancelled/NoShow (ne za idempotentne
        // ponovljene pokušaje niti za Completed/poništenje). Identitet pojave je (sudjelovanje, StatusVersion). Ista uow
        // transakcija kao domenska mutacija — rollback briše i ovaj redak.
        if (oldStatus == ParticipationStatus.Confirmed && newStatus == ParticipationStatus.Cancelled)
            await ParticipationEvents.WriteCancelled(_outboxWriter, uow, organizationId, appointment, booking, participation);
        else if (oldStatus == ParticipationStatus.Confirmed && newStatus == ParticipationStatus.NoShow)
            await ParticipationEvents.WriteNoShow(_outboxWriter, uow, organizationId, appointment, booking, participation);
        else if (oldStatus == ParticipationStatus.NoShow && newStatus == ParticipationStatus.Confirmed)
        {
            // Uska administrativna korekcija — cilja TOČNO onu NoShow pojavu koja se ovime korigira (oldStatusVersion,
            // pročitan PRIJE inkrementa), NIKAD neku buduću NoShow pojavu istog sudjelovanja. Ako je odgovarajući
            // Notification VEĆ obrađen kao Pending, markira ga Cancelled u ISTOJ transakciji; ako Outbox još nije stigao
            // obraditi izvorni event, ovo je no-op — tu race pokriva occurrence-svjesna re-provjera unutar
            // BookingNoShowNotificationHandler, koji zaključava ISTO sudjelovanje prije svoje odluke.
            await _notificationHandler.CancelIfPending(
                uow, organizationId, NotificationType.BookingNoShow, NotificationSourceType.Participation,
                participation.Id.GetValueOrDefault(), oldStatusVersion);
        }
        else if (oldStatus == ParticipationStatus.Cancelled && newStatus == ParticipationStatus.Confirmed)
        {
            await _notificationHandler.CancelIfPending(
                uow, organizationId, NotificationType.BookingCancelled, NotificationSourceType.Participation,
                participation.Id.GetValueOrDefault(), oldStatusVersion);
        }

        // Oslobođeno mjesto na grupnom terminu -> pokušaj promocije liste čekanja — samo za stvaran prijelaz
        // Confirmed->Cancelled (sudjelovanje koje je stvarno zauzimalo mjesto), ne za NoShow niti za Completed/poništenje.
        if (isGroup && oldStatus == ParticipationStatus.Confirmed && newStatus == ParticipationStatus.Cancelled)
            await _waitlistPromotionService.PromoteEligibleWaiters(uow, organizationId, appointmentId, userId);
    }

    /// <summary>Centralizira isti tenant/operational eligibility lanac koji koristi AddBooking i direct guest attendance.
    /// Tenant-strani ID namjerno izgleda kao NotFound, nikad se ne oslanja na globalni clients.id FK.</summary>
    private async Task<Client> LoadEligibleClient(Guid organizationId, Guid clientId)
    {
        Client client = await _clientHandler.GetByIdLight(organizationId, clientId);
        if (client == null)
            throw new NotFoundAppException("Client", clientId);
        if (!client.IsActive)
            throw new BusinessRuleException(ErrorCodes.InactiveClient, "Klijent nije aktivan.");
        if (client.IsAnonymized)
            throw new BusinessRuleException(ErrorCodes.ClientAnonymized, "Klijent je anonimiziran.");

        return client;
    }

    /// <summary>Koristi postojeći AppointmentHandler overlap kriterij: samo aktivni Booking statusi na
    /// neotkazanim terminima blokiraju interval. Pravilo ostaje strict-open interval pa su susjedni termini valjani.</summary>
    private async Task EnsureClientHasNoOverlap(Guid organizationId, Appointment appointment, Guid clientId)
    {
        AppointmentFrame frame = AppointmentFrame.Of(appointment);
        List<OccupancySlot> overlapping = await _schedulingOccupancyHandler.GetOverlappingForClients(
            organizationId, new List<Guid> { clientId }, frame.StartsAt, frame.DurationMinutes, excludeId: appointment.Id);

        if (overlapping.Count > 0)
            throw new BusinessRuleException(
                ErrorCodes.AppointmentOverlap,
                "Klijent je već zakazan u vremenskom razdoblju ovog termina.");
    }

    /// <summary>Form=Group: check-in (Confirmed/NoShow/Cancelled -> Completed) razrješava pokriće/skida ulazak;
    /// bilo koji prijelaz DALJE OD Completed (poništenje) automatski vraća već skinuti ulazak — isto ponašanje
    /// kao staro GroupAttendanceService (vidi domensku napomenu na Booking.cs).</summary>
    private async Task<(PaymentMethod Method, decimal Amount)?> ApplyGroupTransition(
        IUnitOfWork uow, Guid organizationId, Guid userId, Appointment appointment, Booking booking,
        BookingSegmentParticipation participation, BookingSetStatusRequest request)
    {
        (PaymentMethod Method, decimal Amount)? pendingPayment = null;

        if (request.Status == BookingStatus.Completed)
        {
            PackageCoverageView coverage = PackageConsumptions.CoverageOf(participation, AppointmentForm.Group);
            bool needsFreshCoverage = participation.Status != ParticipationStatus.Completed ||
                (coverage.PackageCoverageApplied && coverage.PackageCoverageReturned);

            if (needsFreshCoverage)
                pendingPayment = await ResolveCoverage(uow, organizationId, userId, appointment, booking, participation, request);
        }
        else if (await _packageConsumptionLedgerService.ReverseActive(
                     uow, organizationId, userId, participation, ReversalReasonFor(request.Status)))
        {
            // Phase D3B3A: poništenje AKTIVNE potrošnje paketa (ledger) — vraća ulazak, zapis potrošnje ostaje.
            await _auditLogHandler.Add(uow, new AppointmentAuditLog
            {
                Id = Guid.NewGuid(),
                AppointmentId = appointment.Id.GetValueOrDefault(),
                BookingId = booking.Id,
                BookingSegmentParticipationId = participation.Id,
                ChangeType = "BookingPackageCoverageReturned",
                OldValue = "Applied",
                NewValue = "Returned",
                ChangedAt = DateTimeOffset.UtcNow,
                ChangedBy = userId
            });
        }

        // participation.Status ovdje je JOŠ uvijek stari status (mijenja se tek ispod) — reset naplate se primjenjuje
        // SAMO kad se stvarno poništava već odrađen/plaćen check-in (Completed -> bilo što drugo), ne kad se
        // otkazuje/izostaje booking koji nikad nije bio čekiran (Confirmed -> Cancelled/NoShow već ima
        // Amount=0 od kreiranja, ništa za poništiti — a SuggestedAmount snapshotiran kod generiranja termina se
        // ne smije nepotrebno brisati, vidi spec section 17). Ovo je poništenje POGREŠNOG check-ina (osoblje
        // krivo kliknulo), NE opća cancel/no-show putanja — zato se check-in-generated Payment VOIDA (ne briše,
        // vidi Payment.cs "Void naspram Refund") dok se ručno dodani Paymenti iste rezervacije NE diraju (vidi
        // VoidCheckInGeneratedPayments); za razliku od stvarnog otkazivanja/no-showa koji nijedan Payment ne dira
        // (vidi spec section 27/28).
        if (participation.Status == ParticipationStatus.Completed && request.Status != BookingStatus.Completed)
        {
            await _paymentLedgerService.VoidCheckInGeneratedPayments(uow, organizationId, userId, booking, participation, "Poništen check-in");

            ParticipationPrice.Apply(participation, BookingPricing.Zero);
        }

        if (request.Status == BookingStatus.Cancelled)
        {
            int cutoffMinutes = await _organizationSettingsService.GetCancellationCutoffMinutes(organizationId);
            ParticipationLifecycle.SetLateCancellation(participation, BookingCancellationPolicy.IsLateCancellation(
                ExecutionContextResolver.ForParticipation(appointment, booking, participation).StartsAt, DateTimeOffset.UtcNow, cutoffMinutes));
        }

        ParticipationLifecycle.TrySetStatus(participation, BookingParticipations.ToParticipationStatus(request.Status));
        if (request.Status == BookingStatus.Cancelled || request.Status == BookingStatus.NoShow)
            ParticipationLifecycle.SetCancellationReason(participation, request.CancellationReason);

        return pendingPayment;
    }

    /// <summary>Form=Individual: samo Confirmed -> Cancelled/NoShow, terminalno (bez povratka kroz ovaj put) —
    /// povrat ulaska iz paketa je EKSPLICITNA odluka (ReturnPackageEntry), isto ponašanje kao staro
    /// AppointmentCancelRequest.ReturnEntryForClientIds, sad po jednom Bookingu umjesto batch liste.</summary>
    private async Task ApplyIndividualTransition(
        IUnitOfWork uow, Guid organizationId, Guid userId, Appointment appointment, Booking booking,
        BookingSegmentParticipation participation, BookingSetStatusRequest request)
    {
        if (participation.Status != ParticipationStatus.Confirmed)
            throw new BusinessRuleException(ErrorCodes.AlreadyCompleted, "Booking je već u terminalnom stanju.");

        if (request.ReturnPackageEntry && await _packageConsumptionLedgerService.ReverseActive(
                uow, organizationId, userId, participation, ReversalReasonFor(request.Status)))
        {
            await _auditLogHandler.Add(uow, new AppointmentAuditLog
            {
                Id = Guid.NewGuid(),
                AppointmentId = appointment.Id.GetValueOrDefault(),
                BookingId = booking.Id,
                BookingSegmentParticipationId = participation.Id,
                ChangeType = "BookingPackageCoverageReturned",
                OldValue = "Applied",
                NewValue = "Returned",
                ChangedAt = DateTimeOffset.UtcNow,
                ChangedBy = userId
            });
        }

        if (request.Status == BookingStatus.Cancelled)
        {
            int cutoffMinutes = await _organizationSettingsService.GetCancellationCutoffMinutes(organizationId);
            ParticipationLifecycle.SetLateCancellation(participation, BookingCancellationPolicy.IsLateCancellation(
                ExecutionContextResolver.ForParticipation(appointment, booking, participation).StartsAt, DateTimeOffset.UtcNow, cutoffMinutes));
        }

        ParticipationLifecycle.TrySetStatus(participation, BookingParticipations.ToParticipationStatus(request.Status));
        ParticipationLifecycle.SetCancellationReason(participation, request.CancellationReason);
    }

    /// <summary>Form=Individual, Completed -&gt; Confirmed: uska administrativna korekcija pogrešno odrađenog
    /// check-ina (P1 korekcijski tok, vidi Booking.cs/IBookingService.SetStatus) — pozivatelj (SetStatus) je već
    /// validirao da je ovo dopušteno (sudjelovanje je Completed, ili već Confirmed za idempotentan retry) prije
    /// poziva. Za razliku od ApplyGroupTransition (ista vrsta korekcije, ali implicitna za "bilo koji prijelaz
    /// DALJE OD Completed" i BEZ komisijske reverzije jer Group nema komisijski izvor po Bookingu), ovo je
    /// eksplicitna, samostalna putanja:
    ///
    /// 1) KRITIČNO (spec section 10/30): ako Booking ima AKTIVAN novčani Payment koji NIJE check-in-generated
    ///    (ručno dodan preko redovnog POS Checkouta), korekcija se ODBIJA (409) umjesto da tiho ostavi taj novac
    ///    "osirotjelim" — Group ovo ne provjerava jer taj zahtjev nikad nije postavljen za Group.
    /// 2) Check-in-generated Payment(i) se voidaju (isti mehanizam kao Group — IPaymentLedgerService.
    ///    VoidCheckInGeneratedPayments, koji uz njih voida i njihov jednostavačni auto-Checkout, vidi
    ///    PaymentService.TryVoidSoleAutoCheckout).
    /// 3) Paket-ulazak se vraća ako je primijenjen i još nije vraćen (isti ReturnPackageEntryInTransaction poziv
    ///    kao Group/ApplyIndividualTransition).
    /// 4) cijena Bookinga (Amount/SuggestedAmount na sudjelovanju) se NAMJERNO NE resetiraju na 0 (za razliku od Group!) — Individual Booking
    ///    ima svoju cijenu popunjenu OD TRENUTKA KREIRANJA termina (ne tek od check-ina kao Group, vidi Booking.cs
    ///    domensku napomenu), pa bi brisanje na 0 privremeno prikazalo stvaran zakazan/naplativ termin kao
    ///    besplatan. Booking financials (PaidAmount/OutstandingAmount) se ispravno PREPRAVLJAJU ParticipationSettlement
    ///    izvedbom iz aktivnog (non-voided) stanja nakon Payment voida — Amount ostaje isti, OutstandingAmount se
    ///    vraća na puni iznos automatski (vidi "Do NOT simply copy Group behavior blindly", spec section 4).
    /// 5) CommissionEntry zarađen OVIM completionom prelazi Earned -&gt; Reversed (ICommissionLedgerService.
    ///    ReverseForIndividualServiceCorrection) — jedini dio ove korekcije bez Group ekvivalenta.
    /// 6) ParticipationLifecycle.TrySetStatus na kraju — jedina dozvoljena mutacijska putanja za Status, no-op
    ///    (bez inkrementa) ako je booking već Confirmed (idempotentan retry, vidi spec section 15/41).
    /// 7) Appointment.Status Completed -&gt; Scheduled, SAMO ako je korekcija stvaran prijelaz (ne no-op) —
    ///    BEZOVJETNO na sestrinske Booking statuse (vidi TryRevertAppointmentCompletion): Appointment=Completed
    ///    znači "nema nerazriješenih Confirmed Bookinga", pa čim JEDAN Booking na terminu ponovno postane
    ///    Confirmed, termin više nije potpuno odrađen bez obzira odrađuje li se neki SESTRINSKI Booking na istom
    ///    terminu (duo/multi-klijent Individual) i dalje Completed — isto ponašanje kao GroupCapacityGuard koji
    ///    dopušta Appointment=Scheduled uz sestrinski Booking bilo kojeg statusa. CompleteExisting je učinjen
    ///    idempotentnim po retku (generira Payment/CommissionEntry SAMO za redak koji STVARNO mijenja status u
    ///    OVOM pozivu) upravo da bi ponovni completion nakon ovoga (npr. ponovno slanje CIJELOG originalnog
    ///    ClientIds popisa da se izbjegne UpdateWithBookingsCore hard-delete sestrinskih redaka izostavljenih iz
    ///    popisa) mogao sigurno preskočiti već-Completed sestrinski redak bez duplog Paymenta/CommissionEntry.
    ///
    /// Sve gornje pod-operacije su same po sebi idempotentne (guard po postojećem stanju, ne po ulaznom statusu),
    /// pa se ova metoda namjerno poziva BEZOVJETNO i za pravu korekciju (Completed-&gt;Confirmed) i za idempotentan
    /// retry (Confirmed-&gt;Confirmed) — isti obrazac kao ApplyGroupTransition.</summary>
    private async Task ApplyIndividualCompletionCorrection(
        IUnitOfWork uow, Guid organizationId, Guid userId, Appointment appointment, Booking booking, BookingSegmentParticipation participation)
    {
        // Phase D3B3B: namirenje je na sudjelovanju — sve stavke koje ga namiruju (kroz vrijeme, svih checkouta).
        List<CheckoutItem> items = await _checkoutHandler.GetItemsForParticipation(
            uow, organizationId, participation.Id.GetValueOrDefault());

        bool hasNonReversiblePayment = items
            .SelectMany(i => i.Allocations)
            .Any(a => a.Payment != null && a.Payment.Status == PaymentStatus.Completed && !a.Payment.IsCheckInGenerated);

        if (hasNonReversiblePayment)
            throw new BusinessRuleException(
                ErrorCodes.BookingHasNonReversiblePayment,
                "Booking ima aktivnu ručno dodanu novčanu uplatu (izvan check-in toka) — korekcija check-ina nije " +
                "moguća dok se ta uplata ne riješi kroz Checkout (poništenje bi tiho osirotjelo primljen novac).",
                new { bookingId = booking.Id, participationId = participation.Id });

        await _paymentLedgerService.VoidCheckInGeneratedPayments(
            uow, organizationId, userId, booking, participation, "Poništen check-in (korekcija Completed -> Confirmed)");

        if (await _packageConsumptionLedgerService.ReverseActive(
                uow, organizationId, userId, participation, PackageConsumptionReversalReason.CompletionCorrection))
        {
            await _auditLogHandler.Add(uow, new AppointmentAuditLog
            {
                Id = Guid.NewGuid(),
                AppointmentId = appointment.Id.GetValueOrDefault(),
                BookingId = booking.Id,
                BookingSegmentParticipationId = participation.Id,
                ChangeType = "BookingPackageCoverageReturned",
                OldValue = "Applied",
                NewValue = "Returned",
                ChangedAt = DateTimeOffset.UtcNow,
                ChangedBy = userId
            });
        }

        await _commissionLedgerService.ReverseForIndividualServiceCorrection(uow, organizationId, userId, participation);

        bool wasRealTransition = ParticipationLifecycle.TrySetStatus(participation, ParticipationStatus.Confirmed);
        if (wasRealTransition)
            await TryRevertAppointmentCompletion(uow, organizationId, userId, appointment);
    }

    /// <summary>Form=Individual, NoShow -&gt; Confirmed: uska administrativna korekcija pogrešno evidentiranog
    /// izostanka (P1 operativna korekcija, live E2E pokazao da individualni Booking nije imao povratnu putanju s
    /// NoShow-a — vidi Booking.cs/IBookingService.SetStatus). Namjerno NE dijeli tijelo s
    /// ApplyIndividualCompletionCorrection iako je pozivatelj (SetStatus) isti ulaz (Status=Confirmed): Confirmed
    /// -&gt; NoShow (ApplyIndividualTransition) NIKAD ne stvara Payment/CommissionEntry/paket-pokriće za
    /// Individual (ta se stanja postavljaju isključivo u completion toku — ResolveCoverage/CompleteNew/
    /// CompleteExisting, koji je individualni NoShow po definiciji nikad prošao, booking mora biti Confirmed da bi
    /// uopće postao NoShow, vidi ApplyIndividualTransition), pa nema što reverzirati i poziv na
    /// VoidCheckInGeneratedPayments/ReverseForIndividualServiceCorrection/hasNonReversiblePayment-provjeru bio bi
    /// mrtav kod u najboljem slučaju, a u najgorem bi hasNonReversiblePayment-provjera odbila ovu korekciju zbog
    /// POTPUNO NEPOVEZANE ručne uplate na istom Bookingu koju NoShow nikad nije dirao (vidi spec section 7 — "no
    /// unrelated financial/package/commission mutation").
    ///
    /// 1) ParticipationLifecycle.TrySetStatus na kraju — jedina dozvoljena mutacijska putanja za Status, no-op
    ///    (bez inkrementa) ako je booking već Confirmed (idempotentan retry).
    /// 2) Appointment.Status Completed -&gt; Scheduled, SAMO ako je korekcija stvaran prijelaz (ne no-op) I ako je
    ///    Appointment stvarno Completed — isti TryRevertAppointmentCompletion poziv kao
    ///    ApplyIndividualCompletionCorrection, potreban za multi-klijent rub-slučaj: ApplyIndividualTransition
    ///    (Confirmed -&gt; NoShow) NIKAD ne dira Appointment.Status, pa Appointment ostaje Scheduled dok se izostanak
    ///    evidentira — ALI ako je u međuvremenu SESTRINSKI Booking na istom terminu odradio cijeli termin kroz
    ///    CompleteExisting (koji Appointment.Status postavlja na Completed bezuvjetno, neovisno o statusu bookinga
    ///    izvan poslanog ClientIds popisa), Appointment može biti Completed dok je OVAJ Booking i dalje NoShow.
    ///    Vraćanje na Confirmed tad mora ponovno probiti isti "Completed = nema nerazriješenih Confirmed
    ///    Bookinga" invarijant kao i Completed-&gt;Confirmed korekcija.</summary>
    private async Task ApplyIndividualNoShowCorrection(
        IUnitOfWork uow, Guid organizationId, Guid userId, Appointment appointment, BookingSegmentParticipation participation)
    {
        bool wasRealTransition = ParticipationLifecycle.TrySetStatus(participation, ParticipationStatus.Confirmed);
        if (wasRealTransition)
            await TryRevertAppointmentCompletion(uow, organizationId, userId, appointment);
    }

    /// <summary>Vraća Appointment.Status Completed -&gt; Scheduled — oba pozivatelja (ApplyIndividualCompletionCorrection
    /// za Completed-&gt;Confirmed, ApplyIndividualNoShowCorrection za NoShow-&gt;Confirmed) već su utvrdila da je OVAJ
    /// poziv stvaran prijelaz na Confirmed (ne idempotentan retry) prije poziva. Namjerno BEZUVJETNO, bez obzira
    /// ostaje li neki SESTRINSKI Booking na istom terminu (duo/multi-klijent Individual, vidi Booking.cs
    /// "mješovito plaćanje na istom terminu") i dalje Completed — vidi Appointment.Status napomenu na
    /// ApplyIndividualCompletionCorrection za obrazloženje invarijante ("Completed = nema nerazriješenih Confirmed
    /// Bookinga"). Bez ovoga bi AppointmentService.CompleteExisting trajno odbijao ponovan completion istog
    /// termina s ALREADY_COMPLETED nakon korekcije (vidi spec section 6/45).
    ///
    /// Zaključava Appointment redak (FOR UPDATE) — SetStatus je već zaključao Appointment PRIJE Bookinga na
    /// ulazu u ovu transakciju (vidi tamo za puni opis redoslijeda), pa je ovo besplatan re-lock ISTOG retka
    /// unutar iste transakcije, ne novo zaključavanje. Bare fetch (bez Include) je dovoljan jer više ne čitamo
    /// sestrinske Bookinge ovdje.</summary>
    private async Task TryRevertAppointmentCompletion(IUnitOfWork uow, Guid organizationId, Guid userId, Appointment appointment)
    {
        Appointment locked = await _appointmentHandler.GetForUpdate(uow, organizationId, appointment.Id.GetValueOrDefault());
        if (locked == null || locked.Status != AppointmentStatus.Completed)
            return;

        AppointmentStatus oldStatus = locked.Status;
        locked.Status = AppointmentStatus.Scheduled;
        locked.UpdatedAt = DateTimeOffset.UtcNow;
        locked.UpdatedBy = userId;

        await _appointmentHandler.UpdateScalar(uow, locked);

        await _auditLogHandler.Add(uow, new AppointmentAuditLog
        {
            Id = Guid.NewGuid(),
            AppointmentId = appointment.Id.GetValueOrDefault(),
            ChangeType = "Status",
            OldValue = oldStatus.ToString(),
            NewValue = locked.Status.ToString(),
            ChangedAt = DateTimeOffset.UtcNow,
            ChangedBy = userId
        });
    }

    /// <summary>Razrješava CoverageType/ClientPackageId za prvi (ili ponovljeni nakon vraćanja) check-in — skida
    /// ulazak kod SessionPackage, ništa ne skida kod MonthlyPackage (neograničen brojač), SinglePaid bez paketa.
    /// Isto ponašanje kao staro GroupAttendanceService.ResolveCoverage, PROŠIREN da uz pokriće razrješava i
    /// komercijalno stanje (Amount/SuggestedAmount) — grupni termin prije ovog zahvata nikad nije imao cijenu
    /// (uvijek 0 na Appointment), pa se ovdje prvi put snapshotta stvarna cijena preko istog IPricingService
    /// poziva kao za Individual (vidi ResolveServicePrice). Vraća (Method, Amount) ako treba stvoriti stvaran
    /// Payment NAKON što pozivatelj persistira Booking redak (FK payments.booking_id — vidi SetStatus), null
    /// ako se ne naplaćuje sada (paket-pokriveno, gratis, ili bez zatraženog PaymentMethod-a).</summary>
    private async Task<(PaymentMethod Method, decimal Amount)?> ResolveCoverage(
        IUnitOfWork uow, Guid organizationId, Guid userId, Appointment appointment, Booking booking,
        BookingSegmentParticipation participation, BookingSetStatusRequest request)
    {
        BookingExecutionContext execution = ExecutionContextResolver.ForParticipation(appointment, booking, participation);

        List<ClientPackageDto> eligible = await _clientPackageService.GetEligibleForService(
            organizationId, execution.ClientId, execution.ServiceId, execution.StartsAt, execution.CompanyId);

        ClientPackageDto selected;
        if (request.ClientPackageId.HasValue)
        {
            selected = eligible.FirstOrDefault(p => p.Id == request.ClientPackageId.Value);
            if (selected == null)
                throw new BusinessRuleException(ErrorCodes.PackageNotEligible, "Odabrani paket nije valjan za klijenta ili ne pokriva ovu uslugu.");
        }
        else if (eligible.Count == 1)
        {
            selected = eligible[0];
        }
        else if (eligible.Count > 1)
        {
            throw new ValidationAppException("Klijent ima više prihvatljivih paketa — potrebno je ručno odabrati jedan (ClientPackageId).");
        }
        else
        {
            selected = null;
        }

        BookingPricing pricing = BookingPricing.FromResolution(
            await ResolveServicePrice(organizationId, execution.ServiceId, execution.CompanyId, execution.StartsAt), request.Amount);
        decimal amount = pricing.Amount;
        ParticipationPrice.Apply(participation, pricing);

        if (selected == null)
        {
            // Phase D3B3A: "SinglePaid" se više ne sprema — Completed grupno sudjelovanje bez aktivne potrošnje paketa
            // JEST check-in razriješen bez paketa (PackageConsumptions.CoverageOf).
            // Bez paketa, naplata je EKSPLICITNA odluka osoblja (vidi BookingSetStatusRequest.PaymentMethod) —
            // izostanak znači "evidentirano, još neplaćeno", isto ponašanje kao prije uvođenja naplate na grupne
            // bookinge (stari kontrakt SetGroupAttendanceRequest bez ovih polja). Payment se stvara samo za
            // stvarno pozitivan iznos — Amount=0 (gratis) nikad ne stvara lažan Payment (vidi Payment.cs).
            if (request.PaymentMethod.HasValue && request.IsPaid && amount > 0m)
                return (request.PaymentMethod.Value, amount);

            return null;
        }

        // Paket-namirenje i novčano namirenje su MEĐUSOBNO ISKLJUČIVI — Phase D3B3B: provjeru (već postojeća aktivna
        // novčana alokacija sudjelovanja odbija primjenu paketa) provodi ledger potrošnje kroz jedino pravilo
        // SettlementExclusivityPolicy, na granici sudjelovanja.

        // Paket podmiruje obvezu bez obzira na zatraženi PaymentMethod (spec section 32) — Amount i dalje nosi
        // redovnu/predloženu cijenu (retail vrijednost), ne 0 (isto ponašanje kao Individual complete). Paket
        // NIKAD ne stvara Payment (nije novac, vidi Payment.cs/spec section 3/40) — OutstandingAmount postaje 0
        // preko aktivne potrošnje paketa u ParticipationSettlement, ne preko Paymenta.
        //
        // Phase D3B3A: entitlement se primjenjuje kroz ledger (PackageConsumption na sudjelovanju) u OBA slučaja —
        // paket s brojačem skida jedinicu (Units = 1, prikaz SessionPackage), neograničen paket nema brojača (Units = 0,
        // prikaz MonthlyPackage); valjanost na datum izvođenja usluge (F-08).
        await _packageConsumptionLedgerService.Consume(
            uow, organizationId, userId, participation, execution, selected.Id, BookingStatus.Completed);

        await _auditLogHandler.Add(uow, new AppointmentAuditLog
        {
            Id = Guid.NewGuid(),
            AppointmentId = appointment.Id.GetValueOrDefault(),
            BookingId = booking.Id,
            BookingSegmentParticipationId = participation.Id,
            ChangeType = "BookingPackageCoverageApplied",
            OldValue = null,
            NewValue = selected.Id.ToString(),
            ChangedAt = DateTimeOffset.UtcNow,
            ChangedBy = userId
        });

        return null;
    }

    /// <summary>Phase D3B3A: razlog poništenja potrošnje paketa prema ciljnom statusu prijelaza.</summary>
    private static PackageConsumptionReversalReason ReversalReasonFor(BookingStatus target) => target switch
    {
        BookingStatus.Cancelled => PackageConsumptionReversalReason.Cancellation,
        BookingStatus.NoShow => PackageConsumptionReversalReason.NoShow,
        _ => PackageConsumptionReversalReason.CompletionCorrection
    };

    private static BookingDto ToDto(Booking booking, AppointmentForm form) => BookingReadModel.ToDto(
        booking, form,
        booking.Client != null && booking.Client.OrganizationId == booking.OrganizationId
            ? $"{booking.Client.FirstName} {booking.Client.LastName}"
            : null);
}
