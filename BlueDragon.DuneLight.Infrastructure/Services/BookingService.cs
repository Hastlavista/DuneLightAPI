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
    private readonly IAppointmentHandler _appointmentHandler;
    private readonly IAppointmentAuditLogHandler _auditLogHandler;
    private readonly IClientPackageService _clientPackageService;
    private readonly IClientPackageHandler _clientPackageHandler;
    private readonly IClientHandler _clientHandler;
    private readonly IEmployeeHandler _employeeHandler;
    private readonly IPricingService _pricingService;
    private readonly IOrganizationSettingsService _organizationSettingsService;
    private readonly IWaitlistPromotionService _waitlistPromotionService;
    private readonly IPaymentLedgerService _paymentLedgerService;
    private readonly ICheckoutHandler _checkoutHandler;
    private readonly IOutboxWriter _outboxWriter;
    private readonly INotificationHandler _notificationHandler;
    private readonly IUnitOfWorkFactory _unitOfWorkFactory;

    public BookingService(
        IAppointmentHandler appointmentHandler,
        IAppointmentAuditLogHandler auditLogHandler,
        IClientPackageService clientPackageService,
        IClientPackageHandler clientPackageHandler,
        IClientHandler clientHandler,
        IEmployeeHandler employeeHandler,
        IPricingService pricingService,
        IOrganizationSettingsService organizationSettingsService,
        IWaitlistPromotionService waitlistPromotionService,
        IPaymentLedgerService paymentLedgerService,
        ICheckoutHandler checkoutHandler,
        IOutboxWriter outboxWriter,
        INotificationHandler notificationHandler,
        IUnitOfWorkFactory unitOfWorkFactory)
    {
        _appointmentHandler = appointmentHandler;
        _auditLogHandler = auditLogHandler;
        _clientPackageService = clientPackageService;
        _clientPackageHandler = clientPackageHandler;
        _clientHandler = clientHandler;
        _employeeHandler = employeeHandler;
        _pricingService = pricingService;
        _organizationSettingsService = organizationSettingsService;
        _waitlistPromotionService = waitlistPromotionService;
        _paymentLedgerService = paymentLedgerService;
        _checkoutHandler = checkoutHandler;
        _outboxWriter = outboxWriter;
        _notificationHandler = notificationHandler;
        _unitOfWorkFactory = unitOfWorkFactory;
    }

    /// <summary>Isti IPricingService poziv kao AppointmentService.ResolveSuggestedAmount — ne duplicira logiku
    /// razrješavanja cijene, samo poziva centralni resolver po Service/Company/datumu termina.</summary>
    private async Task<decimal> ResolveSuggestedAmount(Guid organizationId, Guid serviceId, Guid companyId, DateTimeOffset date)
    {
        ResolvePriceResponse resolved = await _pricingService.ResolvePrice(organizationId, new ResolvePriceRequest
        {
            SubjectType = PricingSubjectType.Service,
            SubjectId = serviceId,
            CompanyId = companyId,
            Date = date
        });
        return resolved.Price;
    }

    public async Task<List<BookingDto>> GetForAppointment(Guid organizationId, Guid appointmentId)
    {
        Appointment appointment = await _appointmentHandler.GetWithBookingsForMutation(organizationId, appointmentId);
        if (appointment == null)
            throw new NotFoundAppException("Appointment", appointmentId);

        return appointment.Bookings.Select(ToDto).ToList();
    }

    public async Task<BookingDto> AddBooking(Guid organizationId, Guid userId, bool hasFullScope, Guid appointmentId, BookingCreateRequest request)
    {
        Appointment appointment = await _appointmentHandler.GetWithBookingsForMutation(organizationId, appointmentId);
        if (appointment == null)
            throw new NotFoundAppException("Appointment", appointmentId);

        if (appointment.Status == AppointmentStatus.Cancelled)
            throw new BusinessRuleException(ErrorCodes.AppointmentNotMovable, "Otkazan termin se ne može dopunjavati novim rezervacijama.");

        await ValidateOwnership(organizationId, userId, hasFullScope, appointment.EmployeeId);

        Booking existing = appointment.Bookings.FirstOrDefault(b => b.ClientId == request.ClientId);
        if (existing != null)
            return ToDto(existing);

        Client client = await LoadEligibleClient(organizationId, request.ClientId);

        await EnsureClientHasNoOverlap(organizationId, appointment, request.ClientId);

        decimal suggestedAmount = await ResolveSuggestedAmount(organizationId, appointment.ServiceId, appointment.CompanyId, appointment.StartsAt);

        Booking booking = new Booking
        {
            Id = Guid.NewGuid(),
            OrganizationId = organizationId,
            AppointmentId = appointmentId,
            ClientId = request.ClientId,
            Status = BookingStatus.Confirmed,
            Amount = suggestedAmount,
            SuggestedAmount = suggestedAmount,
            CreatedAt = DateTimeOffset.UtcNow
        };

        await using (IUnitOfWork uow = await _unitOfWorkFactory.Begin())
        {
            if (appointment.Form == AppointmentForm.Group)
                await EnsureGroupCapacityAvailable(uow, organizationId, appointmentId);

            await _appointmentHandler.AddBooking(uow, booking);
            await uow.CommitAsync();
        }

        booking.Client = client;
        return ToDto(booking);
    }

    /// <summary>Zaključava Appointment redak (FOR UPDATE) i ponovno broji Confirmed Bookinge prije stvaranja novog
    /// aktivnog (Confirmed) Bookinga na grupnom terminu — isti izvor istine (IAppointmentHandler.CountConfirmedBookings)
    /// i isti lock kao WaitlistService.PromoteEligibleWaiters (GetForUpdateWithGroup), tako da dva konkurentna
    /// zahtjeva za posljednje slobodno mjesto ne mogu oba proći (drugi poziv čeka na lock pa svježe broji nakon
    /// commita prvog — vidi AppointmentHandler.GetForUpdateWithGroup). No-op za termine koji nisu Form=Group.</summary>
    private async Task EnsureGroupCapacityAvailable(IUnitOfWork uow, Guid organizationId, Guid appointmentId)
    {
        Appointment locked = await _appointmentHandler.GetForUpdateWithGroup(uow, organizationId, appointmentId);
        if (locked == null || locked.Form != AppointmentForm.Group || locked.Group == null)
            return;

        int confirmedCount = await _appointmentHandler.CountConfirmedBookings(uow, organizationId, appointmentId);
        if (confirmedCount >= locked.Group.Capacity)
            throw new BusinessRuleException(
                ErrorCodes.GroupCapacityReached, "Grupa je popunjena — kapacitet je dosegnut.",
                new { capacity = locked.Group.Capacity, confirmedCount });
    }

    public async Task<BookingDto> SetStatus(
        Guid organizationId, Guid userId, bool hasFullScope, Guid appointmentId, Guid clientId, BookingSetStatusRequest request)
    {
        Appointment appointment = await _appointmentHandler.GetWithBookingsForMutation(organizationId, appointmentId);
        if (appointment == null)
            throw new NotFoundAppException("Appointment", appointmentId);

        await ValidateOwnership(organizationId, userId, hasFullScope, appointment.EmployeeId);

        bool isGroup = appointment.Form == AppointmentForm.Group;

        if (!isGroup && request.Status == BookingStatus.Completed)
            throw new ValidationAppException(
                "Individualni termin se odrađuje kroz complete/complete-existing (naplata je zajednička za cijeli termin), ne po pojedinom bookingu.");

        if (!isGroup && request.Status == BookingStatus.Confirmed)
            throw new ValidationAppException("Povratak na Confirmed dostupan je samo za grupne bookinge (poništenje check-ina).");

        Booking booking = appointment.Bookings.FirstOrDefault(b => b.ClientId == clientId);
        bool isNewGuestBooking = booking == null;

        if (booking == null)
        {
            // Gost izvan popisa članova koji se čekira izravno kroz SetStatus bez prethodnog AddBooking poziva
            // (isto ponašanje kao staro GroupAttendanceService.HandleAttended/HandleNotAttended kad existing==null)
            // — dopušteno samo za Form=Group, individualni Bookinzi uvijek postoje od kreiranja termina.
            if (!isGroup)
                throw new NotFoundAppException("Booking", clientId);

            await LoadEligibleClient(organizationId, clientId);

            if ((request.Status == BookingStatus.Completed || request.Status == BookingStatus.NoShow) &&
                appointment.StartsAt > DateTimeOffset.UtcNow)
            {
                throw new BusinessRuleException(
                    ErrorCodes.AttendanceBeforeStart,
                    "Prisustvo gosta (Completed/NoShow) može se evidentirati tek nakon početka termina.");
            }

            await EnsureClientHasNoOverlap(organizationId, appointment, clientId);

            decimal newBookingSuggestedAmount = await ResolveSuggestedAmount(
                organizationId, appointment.ServiceId, appointment.CompanyId, appointment.StartsAt);

            booking = new Booking
            {
                Id = Guid.NewGuid(),
                OrganizationId = organizationId,
                AppointmentId = appointmentId,
                ClientId = clientId,
                Status = BookingStatus.Confirmed,
                Amount = newBookingSuggestedAmount,
                SuggestedAmount = newBookingSuggestedAmount,
                CreatedAt = DateTimeOffset.UtcNow
            };
        }

        try
        {
            await using IUnitOfWork uow = await _unitOfWorkFactory.Begin();

            if (!isNewGuestBooking)
            {
                // Zaključava OVAJ Booking redak (FOR UPDATE) i od ovog trenutka koristi njegovo stanje POD
                // LOCKOM kao ishodišnu točku za prijelaz — serijalizira ovu (eventualnu) administrativnu
                // korekciju s konkurentnim BookingNoShowNotificationHandler/BookingCancelledNotificationHandler
                // koji zaključavaju ISTI redak prije donošenja Notification odluke (vidi spec section 2-4/39).
                // PostgreSQL garantira JEDAN od dva ishoda bez obzira koji konkurent prvi stigne do lock-a —
                // bez ovoga je moguće da Outbox worker stvori Pending Notification ZA STARU pojavu nakon što je
                // korekcija već commitala (i obrnuto), ostavljajući nekonzistentnu kombinaciju Booking.Status +
                // Notification.Status (vidi spec section 1).
                booking = await _appointmentHandler.GetBookingForUpdate(uow, organizationId, booking.Id.GetValueOrDefault());
                if (booking == null)
                    throw new NotFoundAppException("Booking", clientId);
            }

            BookingStatus oldStatus = booking.Status;
            int oldStatusVersion = booking.StatusVersion;

            // Kapacitet se provjerava SAMO kad ovaj poziv stvarno persistira NOVI Confirmed (mjesto-zauzimajući)
            // Booking — gost-čekiranje kroz GroupAttendanceService uvijek šalje Completed/NoShow (nikad Confirmed),
            // pa ovo namjerno ne dira postojeći check-in tok (vidi ApplyGroupTransition, booking.Status na kraju =
            // request.Status). Zatvara direktno Confirmed-kreiranje kroz SetStatus kao dodatni obilazni put mimo
            // AddBooking (vidi EnsureGroupCapacityAvailable).
            if (isNewGuestBooking && isGroup && request.Status == BookingStatus.Confirmed)
                await EnsureGroupCapacityAvailable(uow, organizationId, appointmentId);

            (PaymentMethod Method, decimal Amount)? pendingPayment = null;
            if (isGroup)
                pendingPayment = await ApplyGroupTransition(uow, organizationId, userId, appointment, booking, request);
            else
                await ApplyIndividualTransition(uow, organizationId, userId, appointment, booking, request);

            booking.Note = request.Note ?? booking.Note;
            booking.UpdatedAt = DateTimeOffset.UtcNow;
            booking.UpdatedBy = userId;

            if (booking.Id.HasValue && await BookingExists(uow, booking.Id.Value))
                await _appointmentHandler.UpdateBooking(uow, booking);
            else
                await _appointmentHandler.AddBooking(uow, booking);

            // Payment mora ići TEK nakon što je Booking redak stvarno persistiran (FK payments.booking_id) —
            // zato se ne stvara unutar ResolveCoverage/ApplyGroupTransition, nego ovdje, nakon AddBooking/UpdateBooking.
            if (pendingPayment.HasValue)
                await _paymentLedgerService.RecordPayment(
                    uow, organizationId, userId, appointment.CompanyId, booking, pendingPayment.Value.Method, pendingPayment.Value.Amount,
                    note: null, isCheckInGenerated: true);

            if (oldStatus != booking.Status)
            {
                await _auditLogHandler.Add(uow, new AppointmentAuditLog
                {
                    Id = Guid.NewGuid(),
                    AppointmentId = appointmentId,
                    BookingId = booking.Id,
                    ChangeType = "BookingStatus",
                    OldValue = oldStatus.ToString(),
                    NewValue = booking.Status.ToString(),
                    ChangedAt = DateTimeOffset.UtcNow,
                    ChangedBy = userId
                });
            }

            // Notification-producing Outbox event — samo za STVARAN prijelaz Confirmed -> Cancelled/NoShow (ne
            // za idempotentne ponovljene pokušaje niti za Completed/poništenje), ista provjera pokriva i
            // individualni i grupni put jer oboje ovdje završavaju istim booking.Status = request.Status (vidi
            // spec section 32/36). Ista uow transakcija kao domenska mutacija — rollback briše i ovaj redak.
            if (oldStatus == BookingStatus.Confirmed && booking.Status == BookingStatus.Cancelled)
            {
                await _outboxWriter.Add(
                    uow, organizationId, OutboxEventTypes.BookingCancelledV1,
                    new BookingCancelledEvent
                    {
                        OrganizationId = organizationId,
                        BookingId = booking.Id.GetValueOrDefault(),
                        AppointmentId = appointmentId,
                        ClientId = booking.ClientId,
                        CompanyId = appointment.CompanyId,
                        StatusVersion = booking.StatusVersion,
                        OccurredAt = DateTimeOffset.UtcNow
                    },
                    DateTimeOffset.UtcNow,
                    idempotencyKey: $"booking-cancelled:{booking.Id.GetValueOrDefault()}:{booking.StatusVersion}");
            }
            else if (oldStatus == BookingStatus.Confirmed && booking.Status == BookingStatus.NoShow)
            {
                await _outboxWriter.Add(
                    uow, organizationId, OutboxEventTypes.BookingNoShowV1,
                    new BookingNoShowEvent
                    {
                        OrganizationId = organizationId,
                        BookingId = booking.Id.GetValueOrDefault(),
                        AppointmentId = appointmentId,
                        ClientId = booking.ClientId,
                        CompanyId = appointment.CompanyId,
                        StatusVersion = booking.StatusVersion,
                        OccurredAt = DateTimeOffset.UtcNow
                    },
                    DateTimeOffset.UtcNow,
                    idempotencyKey: $"booking-noshow:{booking.Id.GetValueOrDefault()}:{booking.StatusVersion}");
            }
            else if (oldStatus == BookingStatus.NoShow && booking.Status == BookingStatus.Confirmed)
            {
                // Uska administrativna korekcija (vidi spec section 2/11-12/37) — cilja TOČNO onu NoShow pojavu
                // koja se ovime korigira (oldStatusVersion, pročitan PRIJE inkrementa na Confirmed gore), NIKAD
                // neku buduću NoShow pojavu istog Bookinga (vidi spec section 11). Ako je odgovarajući
                // booking.no-show.v1 Notification VEĆ obrađen kao Pending prije ove korekcije, markira ga
                // Cancelled u ISTOJ transakciji. Ako Outbox još nije stigao obraditi izvorni event, ovo je no-op
                // — tu race pokriva occurrence-svjesna re-provjera unutar BookingNoShowNotificationHandler, koji
                // zaključava ISTI Booking redak prije donošenja svoje odluke (vidi FOR UPDATE lock iznad).
                await _notificationHandler.CancelIfPending(
                    uow, organizationId, NotificationType.BookingNoShow, NotificationSourceType.Booking,
                    booking.Id.GetValueOrDefault(), oldStatusVersion);
            }
            else if (oldStatus == BookingStatus.Cancelled && booking.Status == BookingStatus.Confirmed)
            {
                // Isti obrazac kao NoShow korekcija iznad, za Cancelled -> Confirmed (vidi spec section 13) —
                // zatvara asimetriju gdje je do sada samo NoShow imao ovo čišćenje.
                await _notificationHandler.CancelIfPending(
                    uow, organizationId, NotificationType.BookingCancelled, NotificationSourceType.Booking,
                    booking.Id.GetValueOrDefault(), oldStatusVersion);
            }

            // Oslobođeno mjesto na grupnom terminu -> pokušaj promocije liste čekanja (spec section 12/40) — samo
            // za stvaran prijelaz Confirmed->Cancelled (booking koji je stvarno zauzimao mjesto), ne za NoShow
            // (izostanak ne oslobađa smisleno mjesto, termin se već odvija/odvio) niti za Completed/poništenje.
            if (isGroup && oldStatus == BookingStatus.Confirmed && booking.Status == BookingStatus.Cancelled)
                await _waitlistPromotionService.PromoteEligibleWaiters(uow, organizationId, appointmentId, userId);

            await uow.CommitAsync();
        }
        catch (DbUpdateConcurrencyException)
        {
            throw new BusinessRuleException(
                ErrorCodes.ConcurrencyConflict, "Podaci su upravo promijenjeni od strane drugog zahtjeva — pokušajte ponovno.");
        }

        Booking refreshed = await _appointmentHandler.GetBooking(organizationId, appointmentId, clientId);
        return ToDto(refreshed);
    }

    private async Task<bool> BookingExists(IUnitOfWork uow, Guid bookingId)
    {
        return await uow.Context.Bookings.AnyAsync(b => b.Id == bookingId);
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
        List<Appointment> overlapping = await _appointmentHandler.GetOverlappingForClients(
            organizationId, new List<Guid> { clientId }, appointment.StartsAt, appointment.DurationMinutes, excludeId: appointment.Id);

        if (overlapping.Count > 0)
            throw new BusinessRuleException(
                ErrorCodes.AppointmentOverlap,
                "Klijent je već zakazan u vremenskom razdoblju ovog termina.");
    }

    /// <summary>Form=Group: check-in (Confirmed/NoShow/Cancelled -> Completed) razrješava pokriće/skida ulazak;
    /// bilo koji prijelaz DALJE OD Completed (poništenje) automatski vraća već skinuti ulazak — isto ponašanje
    /// kao staro GroupAttendanceService (vidi domensku napomenu na Booking.cs).</summary>
    private async Task<(PaymentMethod Method, decimal Amount)?> ApplyGroupTransition(
        IUnitOfWork uow, Guid organizationId, Guid userId, Appointment appointment, Booking booking, BookingSetStatusRequest request)
    {
        (PaymentMethod Method, decimal Amount)? pendingPayment = null;

        if (request.Status == BookingStatus.Completed)
        {
            bool needsFreshCoverage = booking.Status != BookingStatus.Completed ||
                (booking.PackageCoverageApplied && booking.PackageCoverageReturned);

            if (needsFreshCoverage)
                pendingPayment = await ResolveCoverage(uow, organizationId, userId, appointment, booking, request);
        }
        else if (booking.PackageCoverageApplied && !booking.PackageCoverageReturned && booking.ClientPackageId.HasValue)
        {
            await ReturnPackageEntryInTransaction(uow, organizationId, booking.ClientPackageId.Value, appointment.ServiceId, userId);

            booking.PackageCoverageReturned = true;
            booking.PackageCoverageReturnedAt = DateTimeOffset.UtcNow;
            booking.PackageCoverageReturnedBy = userId;

            await _auditLogHandler.Add(uow, new AppointmentAuditLog
            {
                Id = Guid.NewGuid(),
                AppointmentId = appointment.Id.GetValueOrDefault(),
                BookingId = booking.Id,
                ChangeType = "BookingPackageCoverageReturned",
                OldValue = "Applied",
                NewValue = "Returned",
                ChangedAt = DateTimeOffset.UtcNow,
                ChangedBy = userId
            });
        }

        // booking.Status ovdje je JOŠ uvijek stari status (mijenja se tek ispod) — reset naplate se primjenjuje
        // SAMO kad se stvarno poništava već odrađen/plaćen check-in (Completed -> bilo što drugo), ne kad se
        // otkazuje/izostaje booking koji nikad nije bio čekiran (Confirmed -> Cancelled/NoShow već ima
        // Amount=0 od kreiranja, ništa za poništiti — a SuggestedAmount snapshotiran kod generiranja termina se
        // ne smije nepotrebno brisati, vidi spec section 17). Ovo je poništenje POGREŠNOG check-ina (osoblje
        // krivo kliknulo), NE opća cancel/no-show putanja — zato se check-in-generated Payment VOIDA (ne briše,
        // vidi Payment.cs "Void naspram Refund") dok se ručno dodani Paymenti iste rezervacije NE diraju (vidi
        // VoidCheckInGeneratedPayments); za razliku od stvarnog otkazivanja/no-showa koji nijedan Payment ne dira
        // (vidi spec section 27/28).
        if (booking.Status == BookingStatus.Completed && request.Status != BookingStatus.Completed)
        {
            await _paymentLedgerService.VoidCheckInGeneratedPayments(uow, organizationId, userId, booking, "Poništen check-in");

            booking.Amount = 0;
            booking.SuggestedAmount = 0;
            booking.IsAmountManuallyOverridden = false;
        }

        if (request.Status == BookingStatus.Cancelled)
        {
            int cutoffMinutes = await _organizationSettingsService.GetCancellationCutoffMinutes(organizationId);
            booking.IsLateCancellation = BookingCancellationPolicy.IsLateCancellation(appointment.StartsAt, DateTimeOffset.UtcNow, cutoffMinutes);
        }

        BookingStatusVersioning.TrySetStatus(booking, request.Status);
        if (request.Status == BookingStatus.Cancelled || request.Status == BookingStatus.NoShow)
            booking.CancellationReason = request.CancellationReason;

        return pendingPayment;
    }

    /// <summary>Form=Individual: samo Confirmed -> Cancelled/NoShow, terminalno (bez povratka kroz ovaj put) —
    /// povrat ulaska iz paketa je EKSPLICITNA odluka (ReturnPackageEntry), isto ponašanje kao staro
    /// AppointmentCancelRequest.ReturnEntryForClientIds, sad po jednom Bookingu umjesto batch liste.</summary>
    private async Task ApplyIndividualTransition(
        IUnitOfWork uow, Guid organizationId, Guid userId, Appointment appointment, Booking booking, BookingSetStatusRequest request)
    {
        if (booking.Status != BookingStatus.Confirmed)
            throw new BusinessRuleException(ErrorCodes.AlreadyCompleted, "Booking je već u terminalnom stanju.");

        if (request.ReturnPackageEntry && booking.PackageCoverageApplied && !booking.PackageCoverageReturned && booking.ClientPackageId.HasValue)
        {
            await ReturnPackageEntryInTransaction(uow, organizationId, booking.ClientPackageId.Value, appointment.ServiceId, userId);

            booking.PackageCoverageReturned = true;
            booking.PackageCoverageReturnedAt = DateTimeOffset.UtcNow;
            booking.PackageCoverageReturnedBy = userId;

            await _auditLogHandler.Add(uow, new AppointmentAuditLog
            {
                Id = Guid.NewGuid(),
                AppointmentId = appointment.Id.GetValueOrDefault(),
                BookingId = booking.Id,
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
            booking.IsLateCancellation = BookingCancellationPolicy.IsLateCancellation(appointment.StartsAt, DateTimeOffset.UtcNow, cutoffMinutes);
        }

        BookingStatusVersioning.TrySetStatus(booking, request.Status);
        booking.CancellationReason = request.CancellationReason;
    }

    /// <summary>Razrješava CoverageType/ClientPackageId za prvi (ili ponovljeni nakon vraćanja) check-in — skida
    /// ulazak kod SessionPackage, ništa ne skida kod MonthlyPackage (neograničen brojač), SinglePaid bez paketa.
    /// Isto ponašanje kao staro GroupAttendanceService.ResolveCoverage, PROŠIREN da uz pokriće razrješava i
    /// komercijalno stanje (Amount/SuggestedAmount) — grupni termin prije ovog zahvata nikad nije imao cijenu
    /// (uvijek 0 na Appointment), pa se ovdje prvi put snapshotta stvarna cijena preko istog IPricingService
    /// poziva kao za Individual (vidi ResolveSuggestedAmount). Vraća (Method, Amount) ako treba stvoriti stvaran
    /// Payment NAKON što pozivatelj persistira Booking redak (FK payments.booking_id — vidi SetStatus), null
    /// ako se ne naplaćuje sada (paket-pokriveno, gratis, ili bez zatraženog PaymentMethod-a).</summary>
    private async Task<(PaymentMethod Method, decimal Amount)?> ResolveCoverage(
        IUnitOfWork uow, Guid organizationId, Guid userId, Appointment appointment, Booking booking, BookingSetStatusRequest request)
    {
        List<ClientPackageDto> eligible = await _clientPackageService.GetEligibleForService(
            organizationId, booking.ClientId, appointment.ServiceId, appointment.StartsAt);

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

        decimal suggestedAmount = await ResolveSuggestedAmount(organizationId, appointment.ServiceId, appointment.CompanyId, appointment.StartsAt);
        decimal amount = request.Amount ?? suggestedAmount;
        booking.SuggestedAmount = suggestedAmount;
        booking.Amount = amount;
        booking.IsAmountManuallyOverridden = request.Amount.HasValue && request.Amount.Value != suggestedAmount;

        if (selected == null)
        {
            booking.CoverageType = AttendanceCoverageType.SinglePaid;
            booking.ClientPackageId = null;
            booking.PackageCoverageApplied = false;
            booking.PackageCoverageReturned = false;
            booking.PackageCoverageReturnedAt = null;
            booking.PackageCoverageReturnedBy = null;

            // Bez paketa, naplata je EKSPLICITNA odluka osoblja (vidi BookingSetStatusRequest.PaymentMethod) —
            // izostanak znači "evidentirano, još neplaćeno", isto ponašanje kao prije uvođenja naplate na grupne
            // bookinge (stari kontrakt SetGroupAttendanceRequest bez ovih polja). Payment se stvara samo za
            // stvarno pozitivan iznos — Amount=0 (gratis) nikad ne stvara lažan Payment (vidi Payment.cs).
            if (request.PaymentMethod.HasValue && request.IsPaid && amount > 0m)
                return (request.PaymentMethod.Value, amount);

            return null;
        }

        // Paket-namirenje i novčano namirenje su MEĐUSOBNO ISKLJUČIVI za jednu Booking obvezu dok nemamo
        // surcharge/refund/store-credit semantiku (vidi spec fix section 2) — ako booking VEĆ ima aktivnu
        // (Payment.Status=Completed) novčanu alokaciju preko bilo koje svoje CheckoutItem stavke (npr. staff je
        // djelomično naplatio kroz POS Checkout prije ovog check-ina), primjena paket-pokrića se odbija umjesto
        // da tiho "osiroti" već primljen novac.
        List<CheckoutItem> existingCheckoutItems = await _checkoutHandler.GetItemsForBooking(uow, organizationId, booking.Id.GetValueOrDefault());
        decimal existingMonetaryPaid = BookingFinancialsCalculator.CalculatePaidAmount(existingCheckoutItems);
        if (existingMonetaryPaid > 0m)
            throw new BusinessRuleException(
                ErrorCodes.BookingAlreadyHasMonetaryPayment,
                "Booking već ima aktivnu novčanu uplatu — pokriće paketom se ne može primijeniti dok se ne poništi ta uplata.",
                new { existingMonetaryPaid });

        bool isUnlimited = IsUnlimited(selected, appointment.ServiceId);

        booking.ClientPackageId = selected.Id;
        booking.PackageCoverageReturned = false;
        booking.PackageCoverageReturnedAt = null;
        booking.PackageCoverageReturnedBy = null;

        // Paket podmiruje obvezu bez obzira na zatraženi PaymentMethod (spec section 32) — Amount i dalje nosi
        // redovnu/predloženu cijenu (retail vrijednost), ne 0 (isto ponašanje kao Individual complete). Paket
        // NIKAD ne stvara Payment (nije novac, vidi Payment.cs/spec section 3/40) — OutstandingAmount postaje 0
        // preko PackageCoverageApplied u BookingFinancialsCalculator, ne preko Paymenta.
        //
        // PackageCoverageApplied = true u OBA slučaja (SessionPackage/MonthlyPackage) — entitlement je STVARNO
        // PRIMIJENJEN na ovaj booking čim se check-in razriješi paketom, bez obzira postoji li brojač ulazaka za
        // smanjiti. ClientPackageEntryMutator.Deduct je odgovoran SAMO za numeričku mutaciju (kod neograničenog
        // paketa je no-op jer nema brojača), NIJE jedini izvor istine za "je li pokriće primijenjeno" — vidi
        // domensku napomenu na Booking.PackageCoverageApplied.
        if (isUnlimited)
        {
            booking.CoverageType = AttendanceCoverageType.MonthlyPackage;
            booking.PackageCoverageApplied = true;

            await _auditLogHandler.Add(uow, new AppointmentAuditLog
            {
                Id = Guid.NewGuid(),
                AppointmentId = appointment.Id.GetValueOrDefault(),
                BookingId = booking.Id,
                ChangeType = "BookingPackageCoverageApplied",
                OldValue = null,
                NewValue = selected.Id.ToString(),
                ChangedAt = DateTimeOffset.UtcNow,
                ChangedBy = userId
            });
        }
        else
        {
            booking.CoverageType = AttendanceCoverageType.SessionPackage;
            await DeductPackageEntryInTransaction(uow, organizationId, selected.Id, appointment.ServiceId, userId);
            booking.PackageCoverageApplied = true;

            await _auditLogHandler.Add(uow, new AppointmentAuditLog
            {
                Id = Guid.NewGuid(),
                AppointmentId = appointment.Id.GetValueOrDefault(),
                BookingId = booking.Id,
                ChangeType = "BookingPackageCoverageApplied",
                OldValue = null,
                NewValue = selected.Id.ToString(),
                ChangedAt = DateTimeOffset.UtcNow,
                ChangedBy = userId
            });
        }

        return null;
    }

    private async Task DeductPackageEntryInTransaction(IUnitOfWork uow, Guid organizationId, Guid clientPackageId, Guid serviceId, Guid userId)
    {
        ClientPackage clientPackage = await _clientPackageHandler.GetById(uow, organizationId, clientPackageId);
        if (clientPackage == null)
            throw new NotFoundAppException("ClientPackage", clientPackageId);

        ClientPackageEntryMutator.Deduct(clientPackage, serviceId, DateTimeOffset.UtcNow);
        clientPackage.UpdatedAt = DateTimeOffset.UtcNow;
        clientPackage.UpdatedBy = userId;

        await _clientPackageHandler.Update(uow, clientPackage);
    }

    private async Task ReturnPackageEntryInTransaction(IUnitOfWork uow, Guid organizationId, Guid clientPackageId, Guid serviceId, Guid userId)
    {
        ClientPackage clientPackage = await _clientPackageHandler.GetById(uow, organizationId, clientPackageId);
        if (clientPackage == null)
            throw new NotFoundAppException("ClientPackage", clientPackageId);

        ClientPackageEntryMutator.Return(clientPackage, serviceId, DateTimeOffset.UtcNow);
        clientPackage.UpdatedAt = DateTimeOffset.UtcNow;
        clientPackage.UpdatedBy = userId;

        await _clientPackageHandler.Update(uow, clientPackage);
    }

    private static bool IsUnlimited(ClientPackageDto package, Guid serviceId)
    {
        if (package.EntryMode == PackageEntryMode.SharedPool)
            return !package.RemainingSharedEntries.HasValue;

        ClientPackageServiceEntryDto entry = package.ServiceEntries.FirstOrDefault(e => e.ServiceId == serviceId);
        return entry == null || !entry.RemainingEntries.HasValue;
    }

    private async Task ValidateOwnership(Guid organizationId, Guid userId, bool hasFullScope, Guid? appointmentEmployeeId)
    {
        if (hasFullScope)
            return;

        Employee employee = await _employeeHandler.GetByUserId(organizationId, userId);
        if (employee == null || !appointmentEmployeeId.HasValue || employee.Id != appointmentEmployeeId)
            throw new BusinessRuleException(ErrorCodes.NotOwner, "Trener smije upravljati samo bookinzima na svojim vlastitim terminima.");
    }

    private static BookingDto ToDto(Booking booking)
    {
        decimal paidAmount = BookingFinancialsCalculator.CalculatePaidAmount(booking);
        decimal outstandingAmount = BookingFinancialsCalculator.CalculateOutstanding(booking);

        return new BookingDto
        {
            Id = booking.Id.GetValueOrDefault(),
            ClientId = booking.ClientId,
            ClientName = booking.Client != null && booking.Client.OrganizationId == booking.OrganizationId
                ? $"{booking.Client.FirstName} {booking.Client.LastName}"
                : null,
            Status = booking.Status,
            Amount = booking.Amount,
            SuggestedAmount = booking.SuggestedAmount,
            IsAmountManuallyOverridden = booking.IsAmountManuallyOverridden,
            PaidAmount = paidAmount,
            OutstandingAmount = outstandingAmount,
            IsPaid = outstandingAmount <= 0m,
            ClientPackageId = booking.ClientPackageId,
            CoverageType = booking.CoverageType,
            PackageCoverageApplied = booking.PackageCoverageApplied,
            PackageCoverageReturned = booking.PackageCoverageReturned,
            Payments = BookingFinancialsCalculator.GetPayments(booking).Select(PaymentDtoFactory.ToDto).ToList(),
            Note = booking.Note,
            CancellationReason = booking.CancellationReason,
            IsLateCancellation = booking.IsLateCancellation
        };
    }
}
