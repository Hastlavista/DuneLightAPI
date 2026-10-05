using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using BlueDragon.DuneLight.Core.DTOs.Appointments;
using BlueDragon.DuneLight.Core.DTOs.Catalog;
using BlueDragon.DuneLight.Core.DTOs.Clients;
using BlueDragon.DuneLight.Core.Enums;
using BlueDragon.DuneLight.Core.Interfaces;
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
public class BookingService : IBookingService, IParticipationLifecycleService
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
    private readonly IGrantResolver _grantResolver;

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
        IUnitOfWorkFactory unitOfWorkFactory,
        IGrantResolver grantResolver)
    {
        _grantResolver = grantResolver;
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
    private Task<ResolvePriceResponse> ResolveServicePrice(
        Guid organizationId, Guid serviceId, Guid companyId, Guid? pricingEmployeeId, DateTimeOffset date)
    {
        return _pricingService.ResolvePrice(organizationId, new ResolvePriceRequest
        {
            SubjectType = PricingSubjectType.Service,
            SubjectId = serviceId,
            CompanyId = companyId,
            EmployeeId = pricingEmployeeId,
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

    public async Task<BookingDto> AddGroupGuest(Guid organizationId, Guid userId, bool hasFullScope, Guid appointmentId, BookingCreateRequest request)
    {
        Appointment appointment = await _appointmentHandler.GetWithBookingsForMutation(organizationId, appointmentId);
        if (appointment == null)
            throw new NotFoundAppException("Appointment", appointmentId);

        if (appointment.Status == AppointmentStatus.Cancelled)
            throw new BusinessRuleException(ErrorCodes.AppointmentNotMovable, "Otkazan termin se ne može dopunjavati novim rezervacijama.");

        // Gost se dodaje u EKSPLICITNO odabrani segment grupnog occurrencea (individualni termin: IAppointmentService.AddClient).
        AppointmentSegment segment = GroupOccurrenceSegments.Require(appointment, request.SegmentId);
        await AppointmentOwnership.EnsureCallerOwnsSegments(_employeeHandler, organizationId, userId, hasFullScope, new[] { segment }, NotOwnerMessage);

        // Idempotentno: klijent koji već sudjeluje u tom segmentu vraća postojeći Booking.
        Booking existing = appointment.Bookings.FirstOrDefault(b => b.ClientId == request.ClientId);
        if (existing != null && existing.Participations.Any(p => p.AppointmentSegmentId == segment.Id))
            return ToDto(existing, appointment.Form);

        await GroupCapacityOverride.EnsureAllowed(_grantResolver, organizationId, userId, request.OverrideCapacity);

        Client client = await LoadEligibleClient(organizationId, request.ClientId);

        SegmentExecutionContext execution = ExecutionContextResolver.ForSegment(appointment, segment);
        ResolvePriceResponse resolvedPrice = await ResolveServicePrice(organizationId, execution.ServiceId, execution.CompanyId, execution.PricingEmployeeId, execution.StartsAt);

        await using (IUnitOfWork uow = await _unitOfWorkFactory.Begin())
        {
            // Tvrda invarijanta klijenta nad KONKRETNIM segmentom (vide se i sestrinski segmenti) — PRVI lock transakcije.
            await ClaimActivation(uow, organizationId, appointment, segment, request.ClientId);

            // Meki kapacitet TOG segmenta (kapacitet njegovog predloška) — prekoračenje samo eksplicitno.
            await GroupCapacityGuard.EnsureAvailable(
                    _appointmentHandler, uow, organizationId, appointmentId, segment.Id.GetValueOrDefault(), request.OverrideCapacity);

            DateTimeOffset now = DateTimeOffset.UtcNow;
            if (existing == null)
            {
                existing = BookingFactory.CreateConfirmed(organizationId, segment, request.ClientId, BookingPricing.AtSuggested(resolvedPrice), now);
                await _appointmentHandler.AddBooking(uow, existing);
            }
            else
            {
                // Phase M1F: jedan Booking po klijentu u occurrenceu — samo novo sudjelovanje na odabranom segmentu.
                uow.Context.BookingSegmentParticipations.Add(BookingFactory.AddParticipation(
                    existing, segment, ParticipationStatus.Confirmed, BookingPricing.AtSuggested(resolvedPrice), now));
                await uow.Context.SaveChangesAsync();
            }

            // Novo Confirmed sudjelovanje — termin se ponovno izvodi (npr. Closed -> Scheduled).
            await AppointmentLifecycle.Refresh(_appointmentHandler, _auditLogHandler, uow, organizationId, appointmentId, userId);
            await uow.CommitAsync();
        }

        existing.Client = client;
        return ToDto(existing, appointment.Form);
    }

    /// <summary>Phase M1H — grupni occurrence, adresa (termin, klijent, EKSPLICITNI segment): prijelaz sudjelovanja klijenta na
    /// tom segmentu, ili — kad ga nema — check-in gosta (novo sudjelovanje; postojeći Booking klijenta se ponovno koristi).
    /// Vlasništvo slijedi segment (provjera u TransitionParticipation/CheckInNewGuest).</summary>
    public async Task<BookingDto> SetStatusOnSegment(
        Guid organizationId, Guid userId, bool hasFullScope, Guid appointmentId, Guid clientId, BookingSetStatusRequest request)
    {
        Appointment appointment = await _appointmentHandler.GetWithBookingsForMutation(organizationId, appointmentId);
        if (appointment == null)
            throw new NotFoundAppException("Appointment", appointmentId);

        AppointmentSegment segment = GroupOccurrenceSegments.Require(appointment, request.SegmentId);
        Booking booking = appointment.Bookings.FirstOrDefault(b => b.ClientId == clientId);
        BookingSegmentParticipation onSegment = booking?.Participations.FirstOrDefault(p => p.AppointmentSegmentId == segment.Id);
        if (onSegment != null)
            return await TransitionParticipation(organizationId, userId, hasFullScope, appointment, onSegment.Id.GetValueOrDefault(), request);
        return await CheckInNewGuest(organizationId, userId, hasFullScope, appointment, clientId, request, booking);
    }

    /// <summary>Phase M0 — participation-native prijelaz: naredba adresira JEDNO sudjelovanje i djeluje samo na njega
    /// (ostala sudjelovanja istog Bookinga se ne diraju). Ista pravila prijelaza kao SetStatusOnSegment (vidi IBookingService).</summary>
    public async Task<BookingDto> SetParticipationStatus(
        Guid organizationId, Guid userId, bool hasFullScope, Guid participationId, BookingSetStatusRequest request)
    {
        Guid? appointmentId = await _participationHandler.GetAppointmentIdOf(organizationId, participationId);
        if (appointmentId == null)
            throw new NotFoundAppException("Participation", participationId);

        Appointment appointment = await _appointmentHandler.GetWithBookingsForMutation(organizationId, appointmentId.Value);
        if (appointment == null)
            throw new NotFoundAppException("Appointment", appointmentId.Value);

        return await TransitionParticipation(organizationId, userId, hasFullScope, appointment, participationId, request);
    }

    /// <summary>Phase M0 — Booking-wide otkazivanje: Booking nema vlastiti status, pa se naredba izvršava kao kontrolirani
    /// prijelaz Confirmed -&gt; Cancelled SVAKOG aktivnog (Confirmed) sudjelovanja, u jednoj transakciji, sa zaključanim
    /// svim sudjelovanjima Bookinga (redoslijed po Id-u); terminalna sudjelovanja (Completed/NoShow/Cancelled) su povijest i
    /// ostaju netaknuta. Svako otkazano sudjelovanje dobiva vlastiti StatusVersion inkrement, audit i Outbox pojavu. Booking
    /// bez aktivnih sudjelovanja → ALREADY_COMPLETED (korekcija terminalnog sudjelovanja ide kroz ParticipationId).</summary>
    public async Task<BookingDto> CancelBooking(
        Guid organizationId, Guid userId, bool hasFullScope, Guid appointmentId, Guid clientId, BookingCancelRequest request)
    {
        Appointment appointment = await _appointmentHandler.GetWithBookingsForMutation(organizationId, appointmentId);
        if (appointment == null)
            throw new NotFoundAppException("Appointment", appointmentId);

        Booking booking = appointment.Bookings.FirstOrDefault(b => b.ClientId == clientId);
        if (booking == null)
            throw new NotFoundAppException("Booking", clientId);

        BookingSetStatusRequest cancel = new BookingSetStatusRequest
        {
            Status = BookingStatus.Cancelled,
            ReturnPackageEntry = request.ReturnPackageEntry,
            CancellationReason = request.CancellationReason
        };

        // Booking bez sudjelovanja je narušen integritet (nikad "nema što otkazati").
        if (booking.Participations.Count == 0)
            throw new InvalidBookingParticipationStateException($"Booking {booking.Id} nema sudjelovanja.");

        // Phase M1H: Booking-wide naredba otkazuje SAMO aktivna sudjelovanja; terminalna sudjelovanja (odrađena, otkazana,
        // izostala) se ne diraju — njihova korekcija ide kroz sudjelovanje (ParticipationId).
        if (!booking.Participations.Any(p => p.Status == ParticipationStatus.Confirmed))
            throw new BusinessRuleException(ErrorCodes.AlreadyCompleted, "Booking nema aktivnih sudjelovanja za otkazivanje.");

        // Phase M1B: own-opseg mora posjedovati SVAKI segment na kojem se otkazuje aktivno sudjelovanje.
        HashSet<Guid> affectedSegmentIds = booking.Participations
            .Where(p => p.Status == ParticipationStatus.Confirmed).Select(p => p.AppointmentSegmentId).ToHashSet();
        await AppointmentOwnership.EnsureCallerOwnsSegments(_employeeHandler, organizationId, userId, hasFullScope,
            appointment.Segments.Where(s => affectedSegmentIds.Contains(s.Id.GetValueOrDefault())), NotOwnerMessage);

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

            // Pod lockom: aktivna sudjelovanja u stabilnom redoslijedu (po Id-u, kao i lock). Phase M1E: vlasništvo je
            // sve-ili-ništa nad PROVJERENIM segmentima — aktivno sudjelovanje na segmentu dodanom u međuvremenu (izvan
            // provjerenog skupa) znači zastarjelu provjeru → CONCURRENCY_CONFLICT, nikad djelomično otkazivanje.
            List<BookingSegmentParticipation> active = locked.Participations
                .Where(p => p.Status == ParticipationStatus.Confirmed)
                .OrderBy(p => p.Id)
                .ToList();
            if (active.Any(p => !affectedSegmentIds.Contains(p.AppointmentSegmentId)))
                throw new BusinessRuleException(
                    ErrorCodes.ConcurrencyConflict, "Podaci su upravo promijenjeni od strane drugog zahtjeva — pokušajte ponovno.");
            foreach (BookingSegmentParticipation participation in active)
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

    /// <summary>Phase M1H — ručni konačni iznos JEDNOG sudjelovanja (participation-native, ista semantika kao ručni iznos
    /// dosadašnje izmjene termina): samo individualni termin i samo AKTIVNO (Confirmed) sudjelovanje — terminalna
    /// sudjelovanja su povijest (iznos se ne mijenja naknadno); grupno sudjelovanje dobiva cijenu pri check-inu. Predložena
    /// cijena i snapshot razrješavanja cjenika ostaju netaknuti; null vraća iznos na predloženu cijenu. Iznos manji od već
    /// naplaćenog novca se odbija (namirenje se izvodi iz konačnog iznosa). Vlasništvo slijedi segment sudjelovanja.</summary>
    public async Task<BookingDto> SetParticipationPrice(
        Guid organizationId, Guid userId, bool hasFullScope, Guid participationId, ParticipationPriceChangeRequest request)
    {
        Guid appointmentId = await _participationHandler.GetAppointmentIdOf(organizationId, participationId)
            ?? throw new NotFoundAppException("Participation", participationId);
        Appointment appointment = await _appointmentHandler.GetWithBookingsForMutation(organizationId, appointmentId)
            ?? throw new NotFoundAppException("Appointment", appointmentId);
        if (appointment.Form != AppointmentForm.Individual)
            throw new ValidationAppException("Ručni iznos grupnog sudjelovanja se postavlja pri check-inu (prisutnost).");

        Booking preloaded = appointment.Bookings.First(b => b.Participations.Any(p => p.Id == participationId));
        BookingSegmentParticipation addressed = BookingParticipations.ById(preloaded, participationId);
        await AppointmentOwnership.EnsureCallerOwnsSegments(_employeeHandler, organizationId, userId, hasFullScope,
            appointment.Segments.Where(seg => seg.Id == addressed.AppointmentSegmentId), NotOwnerMessage);

        try
        {
            await using IUnitOfWork uow = await _unitOfWorkFactory.Begin();
            if (await _appointmentHandler.GetForUpdate(uow, organizationId, appointmentId) == null)
                throw new NotFoundAppException("Appointment", appointmentId);
            Booking booking = await _participationHandler.GetBookingWithLockedParticipation(uow, organizationId, participationId)
                ?? throw new NotFoundAppException("Participation", participationId);
            BookingSegmentParticipation participation = BookingParticipations.ById(booking, participationId);

            if (participation.Status != ParticipationStatus.Confirmed)
                throw new BusinessRuleException(ErrorCodes.AlreadyCompleted,
                    "Iznos se mijenja samo za aktivno (Confirmed) sudjelovanje — terminalno sudjelovanje je povijest.");

            decimal oldAmount = participation.Amount;
            decimal newAmount = request?.Amount ?? participation.SuggestedAmount;
            // Namirenje se čita svježe pod lockom sudjelovanja (sve stavke koje ga namiruju, svih checkouta) — praćeni
            // Booking graf ih namjerno ne učitava.
            List<CheckoutItem> settlementItems = await _checkoutHandler.GetItemsForParticipation(uow, organizationId, participationId);
            if (ParticipationSettlement.SettledAmountOf(settlementItems) > newAmount)
                throw new BusinessRuleException(ErrorCodes.PaymentExceedsOutstandingAmount,
                    "Iznos ne smije biti manji od već naplaćenog iznosa sudjelovanja.");

            if (newAmount != oldAmount || participation.IsAmountManuallyOverridden != (newAmount != participation.SuggestedAmount))
            {
                ParticipationPrice.Apply(participation, new BookingPricing(
                    newAmount, participation.SuggestedAmount, newAmount != participation.SuggestedAmount,
                    participation.BaseAmount, participation.BaseAmountSource, participation.PricingMode, participation.PricingEmployeeId));
                booking.UpdatedAt = DateTimeOffset.UtcNow;
                booking.UpdatedBy = userId;
                await _appointmentHandler.UpdateBooking(uow, booking);
                await _auditLogHandler.Add(uow, new AppointmentAuditLog
                {
                    Id = Guid.NewGuid(),
                    AppointmentId = appointmentId,
                    BookingId = booking.Id,
                    BookingSegmentParticipationId = participation.Id,
                    ChangeType = "Amount",
                    OldValue = oldAmount.ToString(System.Globalization.CultureInfo.InvariantCulture),
                    NewValue = newAmount.ToString(System.Globalization.CultureInfo.InvariantCulture),
                    ChangedAt = DateTimeOffset.UtcNow,
                    ChangedBy = userId
                });
            }

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

    /// <summary>Pravila prijelaza koja ovise o stanju sudjelovanja PRIJE transakcije (ista kao prije Phase M0, sad nad
    /// adresiranim sudjelovanjem).</summary>
    private static void ValidateTransition(
        Appointment appointment, BookingSegmentParticipation participation, BookingSetStatusRequest request)
    {
        bool isGroup = appointment.Form == AppointmentForm.Group;

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
        Guid organizationId, Guid userId, bool hasFullScope, Appointment appointment, Guid participationId, BookingSetStatusRequest request)
    {
        Guid appointmentId = appointment.Id.GetValueOrDefault();
        Booking preloaded = appointment.Bookings.FirstOrDefault(b => b.Participations.Any(p => p.Id == participationId));
        if (preloaded == null)
            throw new NotFoundAppException("Participation", participationId);

        // Phase M1B: vlasništvo slijedi SEGMENT sudjelovanja (own-opseg smije mijenjati sudjelovanja svojih segmenata).
        BookingSegmentParticipation addressed = BookingParticipations.ById(preloaded, participationId);
        await AppointmentOwnership.EnsureCallerOwnsSegments(_employeeHandler, organizationId, userId, hasFullScope,
            appointment.Segments.Where(seg => seg.Id == addressed.AppointmentSegmentId), NotOwnerMessage);

        ValidateTransition(appointment, addressed, request);
        if (request.OverrideCapacity && appointment.Form != AppointmentForm.Group)
            throw new ValidationAppException("Override kapaciteta postoji samo za grupne termine.");
        await GroupCapacityOverride.EnsureAllowed(_grantResolver, organizationId, userId, request.OverrideCapacity);

        // Phase M1C: prijelaz koji sudjelovanje VRAĆA u zauzimanje rasporeda (Cancelled/NoShow -> Confirmed/Completed) je novi
        // zahtjev za klijentov raspored (i za zaposlenike segmenta, ako segment trenutno ne rezervira slot jer je termin
        // eksplicitno otkazan) — ista tvrda invarijanta kao nastanak sudjelovanja.
        AppointmentSegment addressedSegment = appointment.Segments.Single(seg => seg.Id == addressed.AppointmentSegmentId);
        bool targetOccupies = ParticipationOccupancy.Occupies(BookingParticipations.ToParticipationStatus(request.Status));
        bool claimsSchedule = targetOccupies && !ParticipationOccupancy.Occupies(addressed.Status);

        try
        {
            await using IUnitOfWork uow = await _unitOfWorkFactory.Begin();

            // Phase M1D: uključuje kapacitet prostorije (+1 osoba) i — kad ova korekcija ponovno aktivira segment eksplicitno
            // otkazanog termina — zaposlenike, sve osobe i resurse segmenta. Neuspjeh baca prije ikakve izmjene (atomično).
            if (claimsSchedule)
                await ClaimActivation(uow, organizationId, appointment, addressedSegment, preloaded.ClientId);

            // Appointment-PA-sudjelovanje redoslijed zaključavanja — USKLAĐENO s dominantnim redoslijedom u agregatu
            // (AppointmentService.CompleteNow/ChangeToTerminalStatus/CompleteGroupAppointment, GroupService.AddMember,
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
            // Stanje se promijenilo od pred-transakcijskog čitanja tako da je prijelaz POSTAO reaktivacija bez provjere
            // rasporeda — ne nastavlja se naslijepo.
            if (targetOccupies && !claimsSchedule && !ParticipationOccupancy.Occupies(participation.Status))
                throw new BusinessRuleException(
                    ErrorCodes.ConcurrencyConflict, "Podaci su upravo promijenjeni od strane drugog zahtjeva — pokušajte ponovno.");

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

    /// <summary>Gost izvan popisa članova koji se čekira izravno kroz SetStatusOnSegment bez prethodnog AddGroupGuest poziva (isto
    /// ponašanje kao staro GroupAttendanceService.HandleAttended/HandleNotAttended kad existing==null) — dopušteno samo za
    /// Form=Group, individualni Bookinzi uvijek postoje od kreiranja termina. Privremeni jednostruki NASTANAK.</summary>
    private async Task<BookingDto> CheckInNewGuest(
        Guid organizationId, Guid userId, bool hasFullScope, Appointment appointment, Guid clientId, BookingSetStatusRequest request,
        Booking existingBooking)
    {
        Guid appointmentId = appointment.Id.GetValueOrDefault();
        // Gost bez Bookinga postoji samo na GRUPNOM occurrenceu (provjera oblika PRIJE razrješavanja segmenta).
        if (appointment.Form != AppointmentForm.Group)
        {
            if (request.Status == BookingStatus.Completed)
                throw new ValidationAppException(
                    "Individualni termin se odrađuje po sudjelovanju (participations/{id}/status), ne kroz grupni put prisutnosti.");
            throw new NotFoundAppException("Booking", clientId);
        }

        // Gost sudjeluje u EKSPLICITNO odabranom segmentu occurrencea. Gost NE postaje član grupe.
        AppointmentSegment segment = GroupOccurrenceSegments.Require(appointment, request.SegmentId);
        await AppointmentOwnership.EnsureCallerOwnsSegments(_employeeHandler, organizationId, userId, hasFullScope, new[] { segment }, NotOwnerMessage);
        await GroupCapacityOverride.EnsureAllowed(_grantResolver, organizationId, userId, request.OverrideCapacity);

        await LoadEligibleClient(organizationId, clientId);

        if ((request.Status == BookingStatus.Completed || request.Status == BookingStatus.NoShow) &&
            segment.PlannedStart > DateTimeOffset.UtcNow)
        {
            throw new BusinessRuleException(
                ErrorCodes.AttendanceBeforeStart,
                "Prisustvo gosta (Completed/NoShow) može se evidentirati tek nakon početka termina.");
        }

        SegmentExecutionContext guestExecution = ExecutionContextResolver.ForSegment(appointment, segment);
        ResolvePriceResponse guestPrice = await ResolveServicePrice(
            organizationId, guestExecution.ServiceId, guestExecution.CompanyId, guestExecution.PricingEmployeeId, guestExecution.StartsAt);

        DateTimeOffset now = DateTimeOffset.UtcNow;
        Booking booking = existingBooking ?? BookingFactory.CreateConfirmed(
            organizationId, segment, clientId, BookingPricing.AtSuggested(guestPrice), now);

        try
        {
            await using IUnitOfWork uow = await _unitOfWorkFactory.Begin();
            // Gost čije sudjelovanje zauzima raspored (Confirmed/Completed) podliježe tvrdoj invarijanti klijenta;
            // gost evidentiran kao Cancelled/NoShow ne zauzima raspored pa ne stvara sudar.
            if (ParticipationOccupancy.Occupies(BookingParticipations.ToParticipationStatus(request.Status)))
                await ClaimActivation(uow, organizationId, appointment, segment, clientId);

            BookingSegmentParticipation participation;
            if (existingBooking == null)
            {
                participation = booking.Participations.Single();
                await ApplyTransitionInTransaction(uow, organizationId, userId, appointment, booking, participation, isNewGuestBooking: true, request);
            }
            else
            {
                // Postojeći Booking occurrencea: novo (Confirmed) sudjelovanje na tom segmentu se upisuje PRIJE prijelaza; novo
                // poslovno mjesto se provjerava ovdje (prijelaz ga vidi kao Confirmed i ne bi ga brojao).
                if (request.Status == BookingStatus.Confirmed)
                    await GroupCapacityGuard.EnsureAvailable(
                        _appointmentHandler, uow, organizationId, appointmentId, segment.Id.GetValueOrDefault(), request.OverrideCapacity);
                // Booking se ponovno čita PRAĆEN unutar transakcije (zaključana sudjelovanja), kao kod ostalih prijelaza.
                Booking tracked = await _participationHandler.GetBookingWithLockedParticipations(uow, organizationId, booking.Id.GetValueOrDefault())
                    ?? throw new NotFoundAppException("Booking", clientId);
                if (tracked.Participations.Any(p => p.AppointmentSegmentId == segment.Id))
                    throw new BusinessRuleException(ErrorCodes.ConcurrencyConflict, "Podaci su upravo promijenjeni od strane drugog zahtjeva — pokušajte ponovno.");
                participation = BookingFactory.AddParticipation(tracked, segment, ParticipationStatus.Confirmed, BookingPricing.AtSuggested(guestPrice), now);
                uow.Context.BookingSegmentParticipations.Add(participation);
                await uow.Context.SaveChangesAsync();
                await ApplyTransitionInTransaction(uow, organizationId, userId, appointment, tracked, participation, isNewGuestBooking: false, request);
            }

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
    public Task ApplyTransitionInTransaction(
        IUnitOfWork uow, Guid organizationId, Guid userId, Appointment appointment, Booking booking,
        BookingSegmentParticipation participation, BookingSetStatusRequest request) =>
        ApplyTransitionInTransaction(uow, organizationId, userId, appointment, booking, participation, isNewGuestBooking: false, request);

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

        // Phase M1F: meki kapacitet SEGMENTA ovog sudjelovanja (kapacitet njegovog predloška); prekoračenje samo eksplicitno.
        if (isGroup && request.Status == BookingStatus.Confirmed && (isNewGuestBooking || isExistingReturningToConfirmed))
            await GroupCapacityGuard.EnsureAvailable(
                _appointmentHandler, uow, organizationId, appointmentId, participation.AppointmentSegmentId, request.OverrideCapacity);

        (PaymentMethod Method, decimal Amount)? pendingPayment = null;
        if (isGroup)
            pendingPayment = await ApplyGroupTransition(uow, organizationId, userId, appointment, booking, participation, request);
        else if (request.Status == BookingStatus.Completed)
            pendingPayment = await ApplyIndividualCompletion(uow, organizationId, userId, appointment, booking, participation, request);
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

        // Phase M1E: individualno sudjelovanje odrađeno participation-native naredbom zarađuje proviziju u ISTOJ transakciji
        // (isti put koristi i CompleteNow — po sudjelovanju; grupna provizija ostaje po sesiji kod close-outa).
        if (!isGroup && oldStatus == ParticipationStatus.Confirmed && newStatus == ParticipationStatus.Completed)
            await _commissionLedgerService.GenerateForIndividualServiceCompletion(
                uow, organizationId, ExecutionContextResolver.ForParticipation(appointment, booking, participation), participation);

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

        // Phase M1A: životni ciklus termina se izvodi iz SVIH sudjelovanja tek NAKON prijelaza i svih nuspojava (uključujući
        // promociju liste čekanja, koja može dodati Confirmed sudjelovanje — zato i dolazi prije izvođenja). Korekcija koja
        // vrati sudjelovanje na Confirmed time automatski vraća Closed/Cancelled termin u Scheduled.
        await AppointmentLifecycle.Refresh(_appointmentHandler, _auditLogHandler, uow, organizationId, appointmentId, userId);
    }

    /// <summary>Centralizira isti tenant/operational eligibility lanac koji koristi AddGroupGuest i direct guest attendance.
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

    /// <summary>Phase M1C/M1D — sudjelovanje klijenta koje POSTAJE zauzimajuće na konkretnom segmentu: tvrdo preklapanje
    /// klijenta (i zaposlenika, ako se segment reaktivira), kapacitet prostorije (osobe) i — pri reaktivaciji — resursa, pod
    /// zaključanim subjektima; PRVI lock transakcije.</summary>
    private async Task ClaimActivation(IUnitOfWork uow, Guid organizationId, Appointment appointment, AppointmentSegment segment, Guid clientId)
    {
        List<ResourceClaim> segmentResources = await _schedulingOccupancyHandler.GetSegmentResources(uow, segment.Id.GetValueOrDefault());
        IReadOnlyList<ResourceClaim> resources = SegmentOccupancy.Reserves(appointment, segment) ? Array.Empty<ResourceClaim>() : segmentResources;
        await SchedulingConflictGuard.ClaimParticipationActivation(
            _schedulingOccupancyHandler, uow, organizationId, SegmentClaim.ForParticipationActivation(appointment, segment, clientId, resources));

        // Phase M1E: zahtjev je izveden iz PROČITANOG okvira segmenta (vrijeme, prostorija, zaposlenici, resursi) — pod
        // Appointment lockom (postojeći redoslijed: subjekti → Appointment) provjerava se da ga segmentna naredba u
        // međuvremenu nije promijenila; lock se drži do commita, pa prepisivač segmenta vidi novog sudionika.
        await SegmentSnapshot.VerifyUnderLock(_appointmentHandler, uow, organizationId, appointment.Id.GetValueOrDefault(),
            new[] { SegmentSnapshot.Capture(appointment, segment, segmentResources) }, includeParticipants: false);
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
                ExecutionContextResolver.ForParticipation(appointment, booking, participation), DateTimeOffset.UtcNow, cutoffMinutes));
        }

        ParticipationLifecycle.TrySetStatus(participation, BookingParticipations.ToParticipationStatus(request.Status));
        if (request.Status == BookingStatus.Cancelled || request.Status == BookingStatus.NoShow)
            ParticipationLifecycle.SetCancellationReason(participation, request.CancellationReason);

        return pendingPayment;
    }

    /// <summary>Phase M1E — Form=Individual, Confirmed -&gt; Completed JEDNOG sudjelovanja (ciljni put višesegmentnog termina):
    /// cijena se mijenja samo uz eksplicitni ručni iznos (inače ostaje cijena sudjelovanja), paket se primjenjuje SAMO uz
    /// eksplicitni ClientPackageId (bez automatskog odabira — isto kao CompleteNow; valjanost na datum
    /// izvođenja SEGMENTA), inače se uz PaymentMethod + IsPaid stvara Payment za iznos tog sudjelovanja. Paket i novac su
    /// isključivi SAMO unutar ovog sudjelovanja (ledger/SettlementExclusivityPolicy) — druga sudjelovanja istog Bookinga se
    /// namiruju neovisno. Već odrađeno sudjelovanje je idempotentno; drugi terminalni status nema put u Completed.</summary>
    private async Task<(PaymentMethod Method, decimal Amount)?> ApplyIndividualCompletion(
        IUnitOfWork uow, Guid organizationId, Guid userId, Appointment appointment, Booking booking,
        BookingSegmentParticipation participation, BookingSetStatusRequest request)
    {
        if (participation.Status == ParticipationStatus.Completed)
            return null;
        if (participation.Status != ParticipationStatus.Confirmed)
            throw new BusinessRuleException(ErrorCodes.AlreadyCompleted, "Sudjelovanje je već u terminalnom stanju.");

        ParticipationExecutionContext execution = ExecutionContextResolver.ForParticipation(appointment, booking, participation);
        if (request.Amount.HasValue)
            ParticipationPrice.Apply(participation, BookingPricing.FromResolution(
                await ResolveServicePrice(organizationId, execution.ServiceId, execution.CompanyId, execution.PricingEmployeeId, execution.StartsAt), request.Amount));

        ParticipationLifecycle.TrySetStatus(participation, ParticipationStatus.Completed);

        if (request.ClientPackageId.HasValue)
        {
            List<ClientPackageDto> eligible = await _clientPackageService.GetEligibleForService(
                organizationId, execution.ClientId, execution.ServiceId, execution.StartsAt, execution.CompanyId);
            if (eligible.All(p => p.Id != request.ClientPackageId.Value))
                throw new BusinessRuleException(ErrorCodes.PackageNotEligible, "Odabrani paket nije valjan za klijenta ili ne pokriva ovu uslugu.");

            await _packageConsumptionLedgerService.Consume(
                uow, organizationId, userId, participation, execution, request.ClientPackageId.Value, BookingStatus.Completed);
            await _auditLogHandler.Add(uow, new AppointmentAuditLog
            {
                Id = Guid.NewGuid(),
                AppointmentId = appointment.Id.GetValueOrDefault(),
                BookingId = booking.Id,
                BookingSegmentParticipationId = participation.Id,
                ChangeType = "BookingPackageCoverageApplied",
                OldValue = null,
                NewValue = request.ClientPackageId.Value.ToString(),
                ChangedAt = DateTimeOffset.UtcNow,
                ChangedBy = userId
            });
            return null;
        }

        return request.PaymentMethod.HasValue && request.IsPaid && participation.Amount > 0m
            ? (request.PaymentMethod.Value, participation.Amount)
            : null;
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
                ExecutionContextResolver.ForParticipation(appointment, booking, participation), DateTimeOffset.UtcNow, cutoffMinutes));
        }

        ParticipationLifecycle.TrySetStatus(participation, BookingParticipations.ToParticipationStatus(request.Status));
        ParticipationLifecycle.SetCancellationReason(participation, request.CancellationReason);
    }

    /// <summary>Form=Individual, Completed -&gt; Confirmed: uska administrativna korekcija pogrešno odrađenog
    /// check-ina (P1 korekcijski tok, vidi Booking.cs/IBookingService.SetParticipationStatus) — pozivatelj (prijelaz sudjelovanja) je već
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
    /// 7) Phase M1A: termin se NE vraća ovdje — nakon prijelaza ga AppointmentLifecycle.Refresh izvodi iz SVIH
    ///    sudjelovanja (ovo sudjelovanje je opet Confirmed, pa je termin Scheduled bez obzira na sestrinska sudjelovanja).
    ///    Completion je idempotentan po sudjelovanju (Payment/CommissionEntry samo za sudjelovanje koje STVARNO mijenja
    ///    status), pa ponovni completion nakon korekcije sigurno preskače već-Completed sestrinsko sudjelovanje.
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

        // Phase M1A: povratak termina u Scheduled nije zaseban korak — izvodi ga AppointmentLifecycle.Refresh nakon prijelaza.
        ParticipationLifecycle.TrySetStatus(participation, ParticipationStatus.Confirmed);
    }

    /// <summary>Form=Individual, NoShow -&gt; Confirmed: uska administrativna korekcija pogrešno evidentiranog
    /// izostanka (P1 operativna korekcija, live E2E pokazao da individualni Booking nije imao povratnu putanju s
    /// NoShow-a — vidi Booking.cs/IBookingService.SetParticipationStatus). Namjerno NE dijeli tijelo s
    /// ApplyIndividualCompletionCorrection iako je pozivatelj (prijelaz sudjelovanja) isti ulaz (Status=Confirmed): Confirmed
    /// -&gt; NoShow (ApplyIndividualTransition) NIKAD ne stvara Payment/CommissionEntry/paket-pokriće za
    /// Individual (ta se stanja postavljaju isključivo u completion toku — prijelaz u Completed / CompleteNow,
    /// koji individualni NoShow po definiciji nikad prošao, booking mora biti Confirmed da bi
    /// uopće postao NoShow, vidi ApplyIndividualTransition), pa nema što reverzirati i poziv na
    /// VoidCheckInGeneratedPayments/ReverseForIndividualServiceCorrection/hasNonReversiblePayment-provjeru bio bi
    /// mrtav kod u najboljem slučaju, a u najgorem bi hasNonReversiblePayment-provjera odbila ovu korekciju zbog
    /// POTPUNO NEPOVEZANE ručne uplate na istom Bookingu koju NoShow nikad nije dirao (vidi spec section 7 — "no
    /// unrelated financial/package/commission mutation").
    ///
    /// 1) ParticipationLifecycle.TrySetStatus na kraju — jedina dozvoljena mutacijska putanja za Status, no-op
    ///    (bez inkrementa) ako je booking već Confirmed (idempotentan retry).
    /// 2) Phase M1A: termin (npr. Closed zbog ovog izostanka) vraća u Scheduled AppointmentLifecycle.Refresh nakon prijelaza —
    ///    ne zaseban korak.</summary>
    private async Task ApplyIndividualNoShowCorrection(
        IUnitOfWork uow, Guid organizationId, Guid userId, Appointment appointment, BookingSegmentParticipation participation)
    {
        // Phase M1A: povratak termina u Scheduled nije zaseban korak — izvodi ga AppointmentLifecycle.Refresh nakon prijelaza.
        ParticipationLifecycle.TrySetStatus(participation, ParticipationStatus.Confirmed);
    }

    /// <summary>Razrješava CoverageType/ClientPackageId za prvi (ili ponovljeni nakon vraćanja) check-in — skida
    /// ulazak kod SessionPackage, ništa ne skida kod MonthlyPackage (neograničen brojač), SinglePaid bez paketa.
    /// Isto ponašanje kao staro GroupAttendanceService.ResolveCoverage, PROŠIREN da uz pokriće razrješava i
    /// komercijalno stanje (Amount/SuggestedAmount) — grupni termin prije ovog zahvata nikad nije imao cijenu
    /// (uvijek 0 na Appointment), pa se ovdje prvi put snapshotta stvarna cijena preko istog IPricingService
    /// poziva kao za Individual (vidi ResolveServicePrice). Vraća (Method, Amount) ako treba stvoriti stvaran
    /// Payment NAKON što pozivatelj persistira Booking redak (FK payments.booking_id — vidi prijelaz sudjelovanja), null
    /// ako se ne naplaćuje sada (paket-pokriveno, gratis, ili bez zatraženog PaymentMethod-a).</summary>
    private async Task<(PaymentMethod Method, decimal Amount)?> ResolveCoverage(
        IUnitOfWork uow, Guid organizationId, Guid userId, Appointment appointment, Booking booking,
        BookingSegmentParticipation participation, BookingSetStatusRequest request)
    {
        ParticipationExecutionContext execution = ExecutionContextResolver.ForParticipation(appointment, booking, participation);

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
            await ResolveServicePrice(organizationId, execution.ServiceId, execution.CompanyId, execution.PricingEmployeeId, execution.StartsAt), request.Amount);
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
