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
    private readonly IParticipationPolicyService _participationPolicyService;
    private readonly IWaitlistPromotionService _waitlistPromotionService;
    private readonly IPaymentLedgerService _paymentLedgerService;
    private readonly ICheckoutHandler _checkoutHandler;
    private readonly IBookingSegmentParticipationHandler _participationHandler;
    private readonly ICommissionLedgerService _commissionLedgerService;
    private readonly IOutboxWriter _outboxWriter;
    private readonly INotificationHandler _notificationHandler;
    private readonly IUnitOfWorkFactory _unitOfWorkFactory;
    private readonly IGrantResolver _grantResolver;
    private readonly IMembershipCoverageService _membershipCoverage;
    private readonly ICancellationReasonService _cancellationReasonService;
    private readonly IOrganizationCalendarService _organizationCalendarService;

    public BookingService(
        IAppointmentHandler appointmentHandler,
        ISchedulingOccupancyHandler schedulingOccupancyHandler,
        IAppointmentAuditLogHandler auditLogHandler,
        IClientPackageService clientPackageService,
        IPackageConsumptionLedgerService packageConsumptionLedgerService,
        IClientHandler clientHandler,
        IEmployeeHandler employeeHandler,
        IPricingService pricingService,
        IParticipationPolicyService participationPolicyService,
        IWaitlistPromotionService waitlistPromotionService,
        IPaymentLedgerService paymentLedgerService,
        ICheckoutHandler checkoutHandler,
        IBookingSegmentParticipationHandler participationHandler,
        ICommissionLedgerService commissionLedgerService,
        IOutboxWriter outboxWriter,
        INotificationHandler notificationHandler,
        IUnitOfWorkFactory unitOfWorkFactory,
        IGrantResolver grantResolver,
        IMembershipCoverageService membershipCoverage,
        ICancellationReasonService cancellationReasonService,
        IOrganizationCalendarService organizationCalendarService)
    {
        _organizationCalendarService = organizationCalendarService;
        _cancellationReasonService = cancellationReasonService;
        _grantResolver = grantResolver;
        _membershipCoverage = membershipCoverage;
        _appointmentHandler = appointmentHandler;
        _schedulingOccupancyHandler = schedulingOccupancyHandler;
        _auditLogHandler = auditLogHandler;
        _clientPackageService = clientPackageService;
        _packageConsumptionLedgerService = packageConsumptionLedgerService;
        _clientHandler = clientHandler;
        _employeeHandler = employeeHandler;
        _pricingService = pricingService;
        _participationPolicyService = participationPolicyService;
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
            BookingSegmentParticipation added;
            if (existing == null)
            {
                existing = BookingFactory.CreateConfirmed(organizationId, segment, request.ClientId, BookingPricing.AtSuggested(resolvedPrice), now);
                await _appointmentHandler.AddBooking(uow, existing);
                added = existing.Participations.Single();
            }
            else
            {
                // Phase M1F: jedan Booking po klijentu u occurrenceu — samo novo sudjelovanje na odabranom segmentu.
                added = BookingFactory.AddParticipation(existing, segment, ParticipationStatus.Confirmed, BookingPricing.AtSuggested(resolvedPrice), now);
                uow.Context.BookingSegmentParticipations.Add(added);
                await uow.Context.SaveChangesAsync();
            }

            // P2 (2D): claim na rezervaciji (no-op bez članarine).
            await _membershipCoverage.SyncParticipation(uow, organizationId, userId, appointment, existing, added,
                MembershipCoverageEvent.Booking, MembershipCoverageMode.Interactive);

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
        await EnsureCommandAllowed(organizationId, userId, request);
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

        await EnsureCommandAllowed(organizationId, userId, request);
        return await TransitionParticipation(organizationId, userId, hasFullScope, appointment, participationId, request);
    }

    /// <summary>Phase M0 — Booking-wide otkazivanje: Booking nema vlastiti status, pa se naredba izvršava kao kontrolirani
    /// prijelaz Confirmed -&gt; Cancelled SVAKOG aktivnog (Confirmed) sudjelovanja, u jednoj transakciji, sa zaključanim
    /// svim sudjelovanjima Bookinga (redoslijed po Id-u); terminalna sudjelovanja (Completed/NoShow/Cancelled) su povijest i
    /// ostaju netaknuta. Svako otkazano sudjelovanje dobiva vlastiti StatusVersion inkrement, audit i Outbox pojavu.
    /// P1: jedan serverski timestamp događaja za sva sudjelovanja; initiator Client klasificira svako sudjelovanje po
    /// početku njegovog segmenta (Client nakon početka bilo kojeg → CANCELLATION_AFTER_START, ništa se ne mijenja);
    /// Booking bez aktivnih sudjelovanja → NO_ACTIVE_PARTICIPATIONS (D12/D13).</summary>
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
            CancellationInitiator = request?.CancellationInitiator,
            CancellationReason = request?.CancellationReason,
            CancellationReasonCodeId = request?.CancellationReasonCodeId,
            WaivePolicyConsequence = request?.WaivePolicyConsequence ?? false,
            WaiverReason = request?.WaiverReason,
            CorrectionReason = request?.CorrectionReason
        };
        await EnsureCommandAllowed(organizationId, userId, cancel);

        // Booking bez sudjelovanja je narušen integritet (nikad "nema što otkazati").
        if (booking.Participations.Count == 0)
            throw new InvalidBookingParticipationStateException($"Booking {booking.Id} nema sudjelovanja.");

        // Phase M1H/P1 (D12): Booking-wide naredba otkazuje SAMO aktivna sudjelovanja; terminalna sudjelovanja (odrađena,
        // otkazana, izostala) se ne diraju — njihova korekcija ide kroz sudjelovanje (ParticipationId).
        if (!booking.Participations.Any(p => p.Status == ParticipationStatus.Confirmed))
            throw new BusinessRuleException(ErrorCodes.NoActiveParticipations, "Booking nema aktivnih sudjelovanja za otkazivanje.");

        // P1 (D6): paket za kaznu se bira PO SUDJELOVANJU (segmenti mogu imati različite usluge).
        IReadOnlyDictionary<Guid, Guid> packageSelections = ParticipationPackageSelections.Resolve(
            request?.ClientPackageId, request?.PackageSelections,
            booking.Participations.Where(p => p.Status == ParticipationStatus.Confirmed).Select(p => p.Id.GetValueOrDefault()).ToList());

        // Phase M1B: own-opseg mora posjedovati SVAKI segment na kojem se otkazuje aktivno sudjelovanje.
        HashSet<Guid> affectedSegmentIds = booking.Participations
            .Where(p => p.Status == ParticipationStatus.Confirmed).Select(p => p.AppointmentSegmentId).ToHashSet();
        await AppointmentOwnership.EnsureCallerOwnsSegments(_employeeHandler, organizationId, userId, hasFullScope,
            appointment.Segments.Where(s => affectedSegmentIds.Contains(s.Id.GetValueOrDefault())), NotOwnerMessage);

        try
        {
            await using IUnitOfWork uow = await _unitOfWorkFactory.Begin();

            // Appointment PA sudjelovanja (vidi TransitionParticipation za redoslijed zaključavanja). Prijelazi koriste
            // ZAKLJUČANI okvir termina (segmenti svježi pod lockom — klasifikacija i guardovi po stvarnom PlannedStart).
            Appointment lockedAppointment = await _appointmentHandler.GetForUpdate(uow, organizationId, appointmentId)
                ?? throw new NotFoundAppException("Appointment", appointmentId);

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
            DateTimeOffset eventAt = DateTimeOffset.UtcNow;
            foreach (BookingSegmentParticipation participation in active)
                await ApplyTransitionInTransaction(uow, organizationId, userId, lockedAppointment, locked, participation,
                    ParticipationPackageSelections.ForParticipation(cancel, packageSelections, participation.Id.GetValueOrDefault()),
                    new TransitionOptions(IsNewGuestBooking: false, IsCascade: false, eventAt));

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

    /// <summary>K1-2 — označava dolazak klijenta (metapodatak, ne status; bez financijskog učinka). Dopušteno na Confirmed i
    /// Completed, i prije početka; ponovno označavanje ne mijenja prvi zapis. Grant appointments.arrival.mark (bilo koji termin,
    /// bez prava uređivanja termina). K1-9: sesija s neplaćenim dugom vraća upozorenje.</summary>
    public async Task<BookingDto> MarkArrival(Guid organizationId, Guid userId, Guid participationId)
    {
        (Appointment appointment, Guid clientId) = await MutateArrival(organizationId, participationId, async (uow, appointmentId, booking, participation) =>
        {
            if (!ParticipationOccupancy.Occupies(participation.Status))
                throw new BusinessRuleException(ErrorCodes.ParticipationArrivalNotAllowed,
                    "Dolazak se označava samo na aktivnom (Confirmed) ili odrađenom (Completed) sudjelovanju.");
            if (participation.ArrivedAt.HasValue)
                return;

            participation.ArrivedAt = DateTimeOffset.UtcNow;
            participation.ArrivedBy = userId;
            await _auditLogHandler.Add(uow, ArrivalAudit(appointmentId, booking, participation, userId,
                oldValue: null, newValue: participation.ArrivedAt.Value.ToString("O")));
        });

        Booking refreshed = await _appointmentHandler.GetBooking(organizationId, appointment.Id.GetValueOrDefault(), clientId);
        BookingDto dto = ToDto(refreshed, appointment.Form);
        dto.Warnings.AddRange(await ParticipationCoverageWarnings.For(
            _clientPackageService, organizationId, appointment, clientId, dto.Participations.Single(p => p.Id == participationId)));
        return dto;
    }

    /// <summary>K1-2 — poništava označeni dolazak (pogrešan klik); povijest zadržava oba zapisa. Bez dolaska je no-op.</summary>
    public async Task<BookingDto> ClearArrival(Guid organizationId, Guid userId, Guid participationId)
    {
        (Appointment appointment, Guid clientId) = await MutateArrival(organizationId, participationId, async (uow, appointmentId, booking, participation) =>
        {
            if (participation.ArrivedAt.HasValue)
                await ClearArrivalInTransaction(uow, appointmentId, booking, participation, userId, "Cleared:Manual");
        });

        Booking refreshed = await _appointmentHandler.GetBooking(organizationId, appointment.Id.GetValueOrDefault(), clientId);
        return ToDto(refreshed, appointment.Form);
    }

    /// <summary>K1-2 — zajednički okvir naredbi dolaska: isti redoslijed zaključavanja kao prijelaz (termin, pa sudjelovanje).</summary>
    private async Task<(Appointment Appointment, Guid ClientId)> MutateArrival(
        Guid organizationId, Guid participationId, Func<IUnitOfWork, Guid, Booking, BookingSegmentParticipation, Task> mutate)
    {
        Guid appointmentId = await _participationHandler.GetAppointmentIdOf(organizationId, participationId)
            ?? throw new NotFoundAppException("Participation", participationId);
        Appointment appointment = await _appointmentHandler.GetWithBookingsForMutation(organizationId, appointmentId)
            ?? throw new NotFoundAppException("Appointment", appointmentId);
        Guid clientId;

        try
        {
            await using IUnitOfWork uow = await _unitOfWorkFactory.Begin();
            if (await _appointmentHandler.GetForUpdate(uow, organizationId, appointmentId) == null)
                throw new NotFoundAppException("Appointment", appointmentId);
            Booking booking = await _participationHandler.GetBookingWithLockedParticipation(uow, organizationId, participationId)
                ?? throw new NotFoundAppException("Participation", participationId);
            BookingSegmentParticipation participation = BookingParticipations.ById(booking, participationId);
            clientId = booking.ClientId;

            await mutate(uow, appointmentId, booking, participation);
            booking.UpdatedAt = DateTimeOffset.UtcNow;
            await _appointmentHandler.UpdateBooking(uow, booking);
            await uow.CommitAsync();
        }
        catch (DbUpdateConcurrencyException)
        {
            throw new BusinessRuleException(
                ErrorCodes.ConcurrencyConflict, "Podaci su upravo promijenjeni od strane drugog zahtjeva — pokušajte ponovno.");
        }

        return (appointment, clientId);
    }

    /// <summary>K1-2 — briše dolazak i trajno zapisuje u povijest što je obrisano (tko/kada je bio označen) i zašto
    /// (Cleared:Manual, Cleared:NoShow, Cleared:Cancelled).</summary>
    private async Task ClearArrivalInTransaction(
        IUnitOfWork uow, Guid appointmentId, Booking booking, BookingSegmentParticipation participation, Guid userId, string reason)
    {
        string old = $"{participation.ArrivedAt.Value:O}|{participation.ArrivedBy}";
        participation.ArrivedAt = null;
        participation.ArrivedBy = null;
        await _auditLogHandler.Add(uow, ArrivalAudit(appointmentId, booking, participation, userId, old, reason));
    }

    private static AppointmentAuditLog ArrivalAudit(
        Guid appointmentId, Booking booking, BookingSegmentParticipation participation, Guid userId, string oldValue, string newValue) => new()
    {
        Id = Guid.NewGuid(),
        AppointmentId = appointmentId,
        BookingId = booking.Id,
        BookingSegmentParticipationId = participation.Id,
        ChangeType = "Arrival",
        OldValue = oldValue,
        NewValue = newValue,
        StatusVersion = participation.StatusVersion,
        ChangedAt = DateTimeOffset.UtcNow,
        ChangedBy = userId
    };

    /// <summary>P1 (D10) — naknadni otpis aktivne posljedice politike (vidi IBookingService). Normalan pristup sudjelovanju:
    /// appointments.write.all ili own (segment sudjelovanja); za grupni occurrence i groups.attendance.all/own. Ovlast
    /// appointments.policy.fee.waive / .unit.waive (po učinku posljedice, K2) nikad ne širi own opseg.</summary>
    public async Task<BookingDto> WaivePolicyConsequence(
        Guid organizationId, Guid userId, Guid participationId, PolicyConsequenceWaiveRequest request)
    {
        await PolicyOverride.EnsureWaiverRequestAllowed(_grantResolver, organizationId, userId, request?.WaiverReason);
        GrantContext grants = await _grantResolver.Resolve(organizationId, userId);

        Guid appointmentId = await _participationHandler.GetAppointmentIdOf(organizationId, participationId)
            ?? throw new NotFoundAppException("Participation", participationId);
        Appointment appointment = await _appointmentHandler.GetWithBookingsForMutation(organizationId, appointmentId)
            ?? throw new NotFoundAppException("Appointment", appointmentId);

        bool isGroup = appointment.Form == AppointmentForm.Group;
        bool fullScope = grants.Has(Grants.AppointmentsWriteAll) || (isGroup && grants.Has(Grants.GroupsAttendanceAll));
        bool ownScope = grants.Has(Grants.AppointmentsWriteOwn) || (isGroup && grants.Has(Grants.GroupsAttendanceOwn));
        if (!fullScope && !ownScope)
            throw new ForbiddenAppException("Nemate pristup ovom sudjelovanju.");

        Booking preloaded = appointment.Bookings.First(b => b.Participations.Any(p => p.Id == participationId));
        BookingSegmentParticipation addressed = BookingParticipations.ById(preloaded, participationId);
        await AppointmentOwnership.EnsureCallerOwnsSegments(_employeeHandler, organizationId, userId, fullScope,
            appointment.Segments.Where(seg => seg.Id == addressed.AppointmentSegmentId), NotOwnerMessage);

        try
        {
            await using IUnitOfWork uow = await _unitOfWorkFactory.Begin();
            Appointment lockedAppointment = await _appointmentHandler.GetForUpdate(uow, organizationId, appointmentId)
                ?? throw new NotFoundAppException("Appointment", appointmentId);
            Booking booking = await _participationHandler.GetBookingWithLockedParticipation(uow, organizationId, participationId)
                ?? throw new NotFoundAppException("Participation", participationId);
            BookingSegmentParticipation participation = BookingParticipations.ById(booking, participationId);

            // K2 (12.2): grant po učinku aktivne posljedice (naknada ili jedinica/kredit), pod lockom sudjelovanja.
            ParticipationPolicyConsequence active = PolicyConsequences.ActiveOf(participation);
            if (active != null)
                PolicyOverride.EnsureWaiverAllowed(grants, PolicyOverride.EffectOf(participation, active));

            await _participationPolicyService.WaiveActive(
                uow, organizationId, userId, lockedAppointment, booking, participation, request.WaiverReason, DateTimeOffset.UtcNow);
            // P2 (Q31.3): otpis vraća claim zadržan uz posljedicu (i oslobađa mjesto); bez članarine no-op.
            await _membershipCoverage.SyncParticipation(uow, organizationId, userId, lockedAppointment, booking, participation,
                MembershipCoverageEvent.PolicyEvent, MembershipCoverageMode.Automatic);
            // P2 (2F, Q38): oproštena naknada poništava proviziju na naknadu.
            await _commissionLedgerService.SyncPolicyFeeCommission(uow, organizationId, userId, participationId);
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

    /// <summary>P1 (D2/D10) — oblik i autorizacija naredbe koji ne ovise o stanju sudjelovanja (provjera PRIJE transakcije):
    /// otkazivanje traži initiator (Client | Business; System se nikad ne prihvaća iz zahtjeva — postavlja ga samo interni
    /// kod); Business traži appointments.write.all i razlog (initiator se ne može koristiti za zaobilaženje politike); otpis u
    /// trenutku događaja traži razlog i grant otpisa (K2: točan grant po učinku provjerava se kad je posljedica izračunata —
    /// ParticipationPolicyService; nikad ne širi own opseg).</summary>
    private async Task EnsureCommandAllowed(Guid organizationId, Guid userId, BookingSetStatusRequest request)
    {
        if (request == null)
            throw new ValidationAppException("Zahtjev je obavezan.");
        ParticipationStatus target = BookingParticipations.ToParticipationStatus(request.Status);
        GrantContext grants = null;

        if (target == ParticipationStatus.Cancelled)
        {
            switch (request.CancellationInitiator)
            {
                case null:
                    throw new ValidationAppException("Initiator otkazivanja je obavezan (Client ili Business).");
                case CancellationInitiator.System:
                    throw new ValidationAppException("Initiator System postavlja isključivo sustav — zahtjev smije biti Client ili Business.");
                case CancellationInitiator.Business:
                    // K1-4: razlog = slobodni tekst ILI šifra razloga.
                    if (string.IsNullOrWhiteSpace(request.CancellationReason) && request.CancellationReasonCodeId == null)
                        throw new ValidationAppException("Poslovno (Business) otkazivanje zahtijeva razlog.");
                    grants = await _grantResolver.Resolve(organizationId, userId);
                    if (!grants.Has(Grants.AppointmentsWriteAll))
                        throw new ForbiddenAppException("Poslovno (Business) otkazivanje zahtijeva ovlast appointments.write.all.");
                    break;
            }
        }

        if (request.WaivePolicyConsequence)
        {
            if (target != ParticipationStatus.Cancelled && target != ParticipationStatus.NoShow)
                throw new ValidationAppException("Otpis posljedice politike postoji samo uz otkazivanje ili izostanak.");
            await PolicyOverride.EnsureWaiverRequestAllowed(_grantResolver, organizationId, userId, request.WaiverReason);
        }
    }

    /// <summary>K2 (ADR-0032) — zatvoren termin: korekcija IZ terminalnog statusa traži grant korekcije po izvornom statusu i
    /// razlog (audit "StatusCorrectedAfterClose"); označavanje Confirmed sudjelovanja je dopušteno bez granta uz audit
    /// "MarkedAfterClose". Otvoren termin: ništa (normalno označavanje; poništene posljedice bilježi njihov ledger).</summary>
    private async Task EnsureClosedAppointmentRules(
        IUnitOfWork uow, Guid organizationId, Guid userId, Appointment appointment, Booking booking,
        BookingSegmentParticipation participation, ParticipationStatus oldStatus, ParticipationStatus target,
        BookingSetStatusRequest request, DateTimeOffset eventAt)
    {
        OrganizationCalendar calendar = await _organizationCalendarService.GetCompanyCalendar(organizationId, appointment.CompanyId);
        if (!AppointmentClosure.IsClosed(appointment, calendar, eventAt))
            return;

        bool isCorrection = AppointmentClosure.CorrectionGrantFor(oldStatus) != null;
        if (isCorrection)
            AppointmentClosure.EnsureCorrectionAllowed(await _grantResolver.Resolve(organizationId, userId), oldStatus, request.CorrectionReason);

        string reason = isCorrection ? request.CorrectionReason.Trim() : null;
        string newValue = reason == null ? target.ToString() : $"{target}|{reason}";
        await _auditLogHandler.Add(uow, new AppointmentAuditLog
        {
            Id = Guid.NewGuid(),
            AppointmentId = appointment.Id.GetValueOrDefault(),
            BookingId = booking.Id,
            BookingSegmentParticipationId = participation.Id,
            ChangeType = isCorrection ? "StatusCorrectedAfterClose" : "MarkedAfterClose",
            OldValue = oldStatus.ToString(),
            // audit_log.new_value je varchar(255); puni razlog je na zapisu posljedice / reverzije.
            NewValue = newValue.Length > 255 ? newValue[..255] : newValue,
            ChangedAt = eventAt,
            ChangedBy = userId
        });
    }

    /// <summary>P1 (D3) — guardovi CILJNOG događaja (jedan serverski timestamp, bez backdatinga): klijentsko otkazivanje samo
    /// prije početka segmenta (CANCELLATION_AFTER_START); izostanak tek od početka segmenta (ATTENDANCE_BEFORE_START).
    /// Business i System otkazivanje smiju i nakon početka.</summary>
    private static void EnsureTargetEventGuards(
        ParticipationStatus target, BookingSetStatusRequest request, DateTimeOffset segmentStartsAt, DateTimeOffset eventAt)
    {
        if (target == ParticipationStatus.Cancelled && request.CancellationInitiator == CancellationInitiator.Client && eventAt >= segmentStartsAt)
            throw new BusinessRuleException(ErrorCodes.CancellationAfterStart,
                "Klijentsko otkazivanje moguće je samo prije početka termina — nakon početka koristite izostanak ili poslovno otkazivanje.");
        if (target == ParticipationStatus.NoShow && eventAt < segmentStartsAt)
            throw new BusinessRuleException(ErrorCodes.AttendanceBeforeStart, "Izostanak se može evidentirati tek nakon početka termina.");
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
            // korekcije completiona) — ne izvršno stanje pojedinog sudjelovanja. Prijelaz koristi ZAKLJUČANI okvir termina:
            // segmenti (PlannedStart, usluga) svježi pod lockom — klasifikacija, guardovi i politika po stvarnom stanju.
            Appointment lockedAppointment = await _appointmentHandler.GetForUpdate(uow, organizationId, appointmentId)
                ?? throw new NotFoundAppException("Appointment", appointmentId);

            // Phase M0: zaključava SUDJELOVANJE (ne Booking redak) i od ovog trenutka koristi njegovo stanje POD LOCKOM —
            // serijalizira ovaj prijelaz s konkurentnim prijelazom/plaćanjem/potrošnjom ISTOG sudjelovanja i s
            // BookingNoShow/BookingCancelled notification handlerima koji zaključavaju isti redak. Drugo sudjelovanje
            // istog Bookinga se NE blokira.
            Booking booking = await _participationHandler.GetBookingWithLockedParticipation(uow, organizationId, participationId);
            if (booking == null)
                throw new NotFoundAppException("Participation", participationId);

            BookingSegmentParticipation participation = BookingParticipations.ById(booking, participationId);
            // Stanje se promijenilo od pred-transakcijskog čitanja tako da je prijelaz POSTAO reaktivacija bez provjere
            // rasporeda — ne nastavlja se naslijepo.
            if (targetOccupies && !claimsSchedule && !ParticipationOccupancy.Occupies(participation.Status))
                throw new BusinessRuleException(
                    ErrorCodes.ConcurrencyConflict, "Podaci su upravo promijenjeni od strane drugog zahtjeva — pokušajte ponovno.");

            await ApplyTransitionInTransaction(uow, organizationId, userId, lockedAppointment, booking, participation, request,
                new TransitionOptions(IsNewGuestBooking: false, IsCascade: false, DateTimeOffset.UtcNow));

            await uow.CommitAsync();
        }
        catch (DbUpdateConcurrencyException)
        {
            throw new BusinessRuleException(
                ErrorCodes.ConcurrencyConflict, "Podaci su upravo promijenjeni od strane drugog zahtjeva — pokušajte ponovno.");
        }

        Booking refreshed = await _appointmentHandler.GetBooking(organizationId, appointmentId, preloaded.ClientId);
        BookingDto dto = ToDto(refreshed, appointment.Form);
        // K1-9: odrada sesije s neplaćenim dugom (nije pokrivena ni paketom ni članarinom) upozorava recepciju.
        if (request.Status == BookingStatus.Completed)
            dto.Warnings.AddRange(await ParticipationCoverageWarnings.For(
                _clientPackageService, organizationId, appointment, preloaded.ClientId, dto.Participations.Single(p => p.Id == participationId)));
        return dto;
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

            // Redoslijed lockova: subjekti (ClaimActivation) → Appointment. Prijelaz koristi ZAKLJUČANI okvir termina (P1:
            // klasifikacija, guardovi i politika po svježem segmentu).
            Appointment lockedAppointment = await _appointmentHandler.GetForUpdate(uow, organizationId, appointmentId)
                ?? throw new NotFoundAppException("Appointment", appointmentId);

            BookingSegmentParticipation participation;
            DateTimeOffset eventAt = DateTimeOffset.UtcNow;
            if (existingBooking == null)
            {
                participation = booking.Participations.Single();
                await ApplyTransitionInTransaction(uow, organizationId, userId, lockedAppointment, booking, participation, request,
                    new TransitionOptions(IsNewGuestBooking: true, IsCascade: false, eventAt));
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
                // P2 (2D): claim na nastanku (kao i svaka rezervacija); prijelaz zatim odlučuje (no-op bez članarine).
                await _membershipCoverage.SyncParticipation(uow, organizationId, userId, lockedAppointment, tracked, participation,
                    MembershipCoverageEvent.Booking, MembershipCoverageMode.Interactive);
                await ApplyTransitionInTransaction(uow, organizationId, userId, lockedAppointment, tracked, participation, request,
                    new TransitionOptions(IsNewGuestBooking: false, IsCascade: false, eventAt));
                // Novo Confirmed sudjelovanje je isti status (pravi no-op prijelaza, D12) — termin se ipak ponovno izvodi.
                if (request.Status == BookingStatus.Confirmed)
                    await AppointmentLifecycle.Refresh(_appointmentHandler, _auditLogHandler, uow, organizationId, appointmentId, userId);
            }

            await uow.CommitAsync();
        }
        catch (DbUpdateConcurrencyException)
        {
            throw new BusinessRuleException(
                ErrorCodes.ConcurrencyConflict, "Podaci su upravo promijenjeni od strane drugog zahtjeva — pokušajte ponovno.");
        }

        Booking refreshed = await _appointmentHandler.GetBooking(organizationId, appointmentId, clientId);
        BookingDto dto = ToDto(refreshed, appointment.Form);
        // K1-9: isto upozorenje kao odrada postojećeg sudjelovanja.
        if (request.Status == BookingStatus.Completed)
            foreach (BookingParticipationDto participation in dto.Participations.Where(p => p.AppointmentSegmentId == segment.Id))
                dto.Warnings.AddRange(await ParticipationCoverageWarnings.For(
                    _clientPackageService, organizationId, appointment, clientId, participation));
        return dto;
    }

    /// <summary>P1 — opcije jednog prijelaza: jedan serverski timestamp događaja (klasifikacija, metapodaci, posljedica);
    /// IsNewGuestBooking = Booking gosta još nije persistiran; IsCascade = appointment-wide kaskada (pozivatelj sam radi
    /// istek/promociju liste čekanja i izvođenje statusa termina nakon svih sudjelovanja).</summary>
    private sealed record TransitionOptions(bool IsNewGuestBooking, bool IsCascade, DateTimeOffset EventAt);

    /// <summary>Phase M0 — prijelaz JEDNOG sudjelovanja unutar pozivateljeve transakcije (pozivatelj je već zaključao
    /// Appointment i to sudjelovanje). Sve nuspojave (kapacitet, paket, plaćanje, provizija, posljedica politike, audit,
    /// Outbox, Notification, lista čekanja) su po SUDJELOVANJU: StatusVersion, audit StatusVersion i identitet pojave su
    /// verzija OVOG sudjelovanja.</summary>
    public Task ApplyTransitionInTransaction(
        IUnitOfWork uow, Guid organizationId, Guid userId, Appointment appointment, Booking booking,
        BookingSegmentParticipation participation, BookingSetStatusRequest request) =>
        ApplyTransitionInTransaction(uow, organizationId, userId, appointment, booking, participation, request,
            new TransitionOptions(IsNewGuestBooking: false, IsCascade: false, DateTimeOffset.UtcNow));

    /// <summary>P1 — appointment-wide kaskada (otkazivanje/izostanak cijelog termina): isti prijelaz, isti timestamp događaja
    /// za sva sudjelovanja; pozivatelj nakon svih sudjelovanja izvodi status termina i istječe listu čekanja.</summary>
    public Task ApplyCascadeTransitionInTransaction(
        IUnitOfWork uow, Guid organizationId, Guid userId, Appointment appointment, Booking booking,
        BookingSegmentParticipation participation, BookingSetStatusRequest request, DateTimeOffset eventAt) =>
        ApplyTransitionInTransaction(uow, organizationId, userId, appointment, booking, participation, request,
            new TransitionOptions(IsNewGuestBooking: false, IsCascade: true, eventAt));

    /// <summary>
    /// P1 (ADR-0018, D12) — JEDNA tranzicijska matrica za Individual i Group: svaki prijelaz u DRUGI status je dopušten uz
    /// guardove ciljnog događaja i autorizaciju; isti status je pravi no-op (bez StatusVersiona, audita, Outboxa i efekata).
    /// Terminal → terminal je jedna atomarna korekcija: (1) reverzija svih aktivnih efekata prethodnog stanja (check-in
    /// plaćanja, potrošnja paketa i provizija completiona; aktivna posljedica politike s kaznom u paketu), (2) brisanje starih
    /// metapodataka, (3) StatusVersion + 1 jednom, (4) novi događaj u potpunosti, (5) novi efekti s novim SourceVersionom.
    /// Ručna novčana plaćanja se nikad ne diraju i ne blokiraju korekciju (ostaju kao namirenje — D7). Cijena (i grupna)
    /// se ne nulira pri izlasku iz Completed.
    /// </summary>
    private async Task ApplyTransitionInTransaction(
        IUnitOfWork uow, Guid organizationId, Guid userId, Appointment appointment, Booking booking,
        BookingSegmentParticipation participation, BookingSetStatusRequest request, TransitionOptions options)
    {
        Guid appointmentId = appointment.Id.GetValueOrDefault();
        bool isGroup = appointment.Form == AppointmentForm.Group;
        ParticipationStatus oldStatus = participation.Status;
        ParticipationStatus target = BookingParticipations.ToParticipationStatus(request.Status);
        int oldStatusVersion = participation.StatusVersion;
        DateTimeOffset eventAt = options.EventAt;

        // Phase M0: "buduće" = početak SEGMENTA ovog sudjelovanja (ne okvir termina).
        DateTimeOffset participationStartsAt = ExecutionContextResolver.ForParticipation(appointment, booking, participation).StartsAt;

        if (options.IsNewGuestBooking)
        {
            // Novi gost-Booking: meki kapacitet za novo Confirmed mjesto, pa se Booking persistira PRIJE efekata (posljedica
            // politike, potrošnja paketa i plaćanje imaju FK na sudjelovanje).
            if (isGroup && target == ParticipationStatus.Confirmed)
                await GroupCapacityGuard.EnsureAvailable(
                    _appointmentHandler, uow, organizationId, appointmentId, participation.AppointmentSegmentId, request.OverrideCapacity);
            EnsureTargetEventGuards(target, request, participationStartsAt, eventAt);
            booking.Note = request.Note ?? booking.Note;
            booking.UpdatedAt = DateTimeOffset.UtcNow;
            booking.UpdatedBy = userId;
            await _appointmentHandler.AddBooking(uow, booking);
            // P2 (2D): claim na nastanku novog gosta (no-op bez članarine); prijelaz zatim odlučuje kao za svaku rezervaciju.
            await _membershipCoverage.SyncParticipation(uow, organizationId, userId, appointment, booking, participation,
                MembershipCoverageEvent.Booking, MembershipCoverageMode.Interactive);
            if (target == ParticipationStatus.Confirmed)
            {
                await AppointmentLifecycle.Refresh(_appointmentHandler, _auditLogHandler, uow, organizationId, appointmentId, userId);
                return;
            }
            // K2: gost evidentiran nakon zatvaranja termina — označavanje, audit "MarkedAfterClose".
            await EnsureClosedAppointmentRules(uow, organizationId, userId, appointment, booking, participation,
                ParticipationStatus.Confirmed, target, request, eventAt);
        }
        else if (oldStatus == target)
        {
            // D12: isti status je pravi no-op za oba oblika (bez StatusVersiona, audita, Outboxa i efekata). Napomena Bookinga
            // nije stanje prijelaza — eksplicitno poslana nova napomena se i dalje sprema.
            if (request.Note != null && request.Note != booking.Note)
            {
                booking.Note = request.Note;
                booking.UpdatedAt = DateTimeOffset.UtcNow;
                booking.UpdatedBy = userId;
                await _appointmentHandler.UpdateBooking(uow, booking);
            }
            return;
        }
        else
        {
            EnsureTargetEventGuards(target, request, participationStartsAt, eventAt);
            // K2 (ADR-0032): zatvoren termin — korekcija iz terminalnog statusa traži grant korekcije i razlog.
            await EnsureClosedAppointmentRules(uow, organizationId, userId, appointment, booking, participation, oldStatus, target, request, eventAt);

            // Kapacitet se provjerava kad POSTOJEĆE sudjelovanje administrativno vraća na Confirmed (novo zauzeto mjesto) —
            // namjerno future-only (početak segmenta u budućnosti); nakon početka nominalni kapacitet više ne ograničava
            // korekciju povijesne prisutnosti. Phase M1F: meki kapacitet SEGMENTA; prekoračenje samo eksplicitno.
            if (isGroup && target == ParticipationStatus.Confirmed && participationStartsAt > DateTimeOffset.UtcNow)
                await GroupCapacityGuard.EnsureAvailable(
                    _appointmentHandler, uow, organizationId, appointmentId, participation.AppointmentSegmentId, request.OverrideCapacity);

            // (1) Reverzija aktivnih efekata prethodnog stanja.
            await ReversePreviousStateEffects(uow, organizationId, userId, appointment, booking, participation, oldStatus, target, request, eventAt);
        }

        // (2) Metapodaci uvijek odgovaraju statusu — stari se brišu prije novog događaja. (3) StatusVersion + 1 jednom.
        ParticipationEventMetadata.Clear(participation);
        // K1-2: dolazak postoji samo uz Confirmed/Completed (DB CHECK); izostanak/otkaz ga briše, a povijest trajno bilježi tko ga
        // je i kada označio i da je obrisan zbog prijelaza (trag za prigovore na naknadu).
        if (!ParticipationOccupancy.Occupies(target) && participation.ArrivedAt.HasValue)
            await ClearArrivalInTransaction(uow, appointmentId, booking, participation, userId, $"Cleared:{target}");
        ParticipationLifecycle.TrySetStatus(participation, target);

        // P2 (2D): ulazak u zauzimajuće stanje (ponovna aktivacija, check-in) — claim PRIJE efekata completiona, da članarina
        // ima prednost pred paketom i novcem. Korekcije i check-in nikad ne odbijaju (postavka "odbij" vrijedi za rezervaciju).
        if (ParticipationOccupancy.Occupies(target))
            await _membershipCoverage.SyncParticipation(uow, organizationId, userId, appointment, booking, participation,
                MembershipCoverageEvent.Booking, MembershipCoverageMode.Automatic);

        // (4) Novi događaj u potpunosti + (5) novi efekti s novim SourceVersionom.
        (PaymentMethod Method, decimal Amount)? pendingPayment = null;
        // K2 (12.2): otpis u trenutku događaja — grant po učinku provjerava servis politike kad je posljedica izračunata.
        PolicyEventOptions policyOptions = new PolicyEventOptions(request.ClientPackageId, request.WaivePolicyConsequence, request.WaiverReason,
            request.WaivePolicyConsequence ? await _grantResolver.Resolve(organizationId, userId) : null);
        switch (target)
        {
            case ParticipationStatus.Completed:
                pendingPayment = isGroup
                    ? await ResolveCoverage(uow, organizationId, userId, appointment, booking, participation, request)
                    : await ApplyIndividualCompletion(uow, organizationId, userId, appointment, booking, participation, request);
                break;
            case ParticipationStatus.Cancelled:
                CancellationInitiator initiator = request.CancellationInitiator
                    ?? throw new ValidationAppException("Initiator otkazivanja je obavezan (Client ili Business).");
                ParticipationEventMetadata.SetCancelled(participation, initiator, eventAt, userId, request.CancellationReason);
                // K1-4: šifra razloga (System je postavlja samo sustav — bez šifre i bez obaveznosti).
                if (initiator != CancellationInitiator.System)
                    ParticipationEventMetadata.SetCancellationReasonCode(participation, await _cancellationReasonService.ResolveForEvent(
                        organizationId, request.CancellationReasonCodeId,
                        initiator == CancellationInitiator.Client ? CancellationReasonEvent.ClientCancellation : CancellationReasonEvent.BusinessCancellation));
                // D2: politika se evaluira SAMO za klijentsko otkazivanje.
                if (initiator == CancellationInitiator.Client)
                    await _participationPolicyService.ApplyClientCancellation(
                        uow, organizationId, userId, appointment, booking, participation, eventAt, policyOptions);
                break;
            case ParticipationStatus.NoShow:
                // D2: izostanak nema initiator — valjani izostanak JEST klijentovo nedolaženje i uvijek evaluira politiku.
                ParticipationEventMetadata.SetNoShow(participation, eventAt, userId, request.NoShowReason);
                ParticipationEventMetadata.SetNoShowReasonCode(participation, await _cancellationReasonService.ResolveForEvent(
                    organizationId, request.NoShowReasonCodeId, CancellationReasonEvent.NoShow));
                await _participationPolicyService.ApplyNoShow(
                    uow, organizationId, userId, appointment, booking, participation, eventAt, policyOptions);
                break;
        }

        // P2 (2D): otkazano/izostalo sudjelovanje vraća claim, osim kad ga posljedica politike zadržava (ForfeitCredit, Q26/Q31);
        // vraćeno mjesto pokriva najraniji nepokriveni budući termin istog članstva. Bez članarine no-op.
        if (!ParticipationOccupancy.Occupies(target))
            await _membershipCoverage.SyncParticipation(uow, organizationId, userId, appointment, booking, participation,
                MembershipCoverageEvent.ParticipationCancelled, MembershipCoverageMode.Automatic);

        booking.Note = request.Note ?? booking.Note;
        booking.UpdatedAt = DateTimeOffset.UtcNow;
        booking.UpdatedBy = userId;
        await _appointmentHandler.UpdateBooking(uow, booking);

        // P2 (2F, Q38): ulaz u ili izlaz iz Cancelled/NoShow mijenja posljedicu politike (unaprijed plaćena naknada → provizija;
        // reverzirana posljedica → storno provizije na naknadu). Grupe i bez posljedice: no-op.
        if (!isGroup && (!ParticipationOccupancy.Occupies(oldStatus) || !ParticipationOccupancy.Occupies(target)))
            await _commissionLedgerService.SyncPolicyFeeCommission(uow, organizationId, userId, participation.Id.GetValueOrDefault());

        // Payment mora ići TEK nakon što je redak sudjelovanja stvarno persistiran (FK checkout_items -> sudjelovanje).
        if (pendingPayment.HasValue)
            await _paymentLedgerService.RecordPayment(
                uow, organizationId, userId, appointment.CompanyId, booking, participation, pendingPayment.Value.Method, pendingPayment.Value.Amount,
                note: null, isCheckInGenerated: true);

        // Phase M1E: individualno sudjelovanje odrađeno participation-native naredbom zarađuje proviziju u ISTOJ transakciji
        // (isti put koristi i CompleteNow — po sudjelovanju; grupna provizija ostaje po sesiji kod close-outa). P1 (D8):
        // događaji politike nikad ne stvaraju proviziju.
        if (!isGroup && target == ParticipationStatus.Completed)
            await _commissionLedgerService.GenerateForIndividualServiceCompletion(
                uow, organizationId, ExecutionContextResolver.ForParticipation(appointment, booking, participation), participation);

        await _auditLogHandler.Add(uow, new AppointmentAuditLog
        {
            Id = Guid.NewGuid(),
            AppointmentId = appointmentId,
            BookingId = booking.Id,
            BookingSegmentParticipationId = participation.Id,
            ChangeType = "BookingStatus",
            OldValue = oldStatus.ToString(),
            NewValue = target.ToString(),
            StatusVersion = participation.StatusVersion,
            ChangedAt = DateTimeOffset.UtcNow,
            ChangedBy = userId
        });

        // Notification-producing Outbox događaj za SVAKI stvarni ulaz u Cancelled/NoShow (P1 odluka 2026-10-06: i za terminal →
        // terminal korekciju); identitet pojave je (sudjelovanje, StatusVersion). Ista uow transakcija kao domenska mutacija.
        if (target == ParticipationStatus.Cancelled)
            await ParticipationEvents.WriteCancelled(_outboxWriter, uow, organizationId, appointment, booking, participation);
        else if (target == ParticipationStatus.NoShow)
            await ParticipationEvents.WriteNoShow(_outboxWriter, uow, organizationId, appointment, booking, participation);

        // Izlazak iz Cancelled/NoShow cilja TOČNO pojavu koja se korigira (oldStatusVersion, pročitan PRIJE inkrementa): ako je
        // njezin Notification već obrađen kao Pending, markira ga Cancelled u ISTOJ transakciji; ako Outbox još nije stigao
        // obraditi izvorni događaj, to pokriva occurrence-svjesna re-provjera u notification handleru (isti lock sudjelovanja).
        if (oldStatus == ParticipationStatus.NoShow && !options.IsNewGuestBooking)
            await _notificationHandler.CancelIfPending(
                uow, organizationId, NotificationType.BookingNoShow, NotificationSourceType.Participation,
                participation.Id.GetValueOrDefault(), oldStatusVersion);
        else if (oldStatus == ParticipationStatus.Cancelled && !options.IsNewGuestBooking)
            await _notificationHandler.CancelIfPending(
                uow, organizationId, NotificationType.BookingCancelled, NotificationSourceType.Participation,
                participation.Id.GetValueOrDefault(), oldStatusVersion);

        if (options.IsCascade)
            return;

        // Oslobođeno mjesto na grupnom terminu -> pokušaj promocije liste čekanja (D9) — samo za otkazivanje sudjelovanja koje
        // je stvarno zauzimalo mjesto; izostanak ne promovira.
        if (isGroup && ParticipationOccupancy.Occupies(oldStatus) && target == ParticipationStatus.Cancelled)
            await _waitlistPromotionService.PromoteEligibleWaiters(uow, organizationId, appointmentId, userId);

        // Phase M1A: životni ciklus termina se izvodi iz SVIH sudjelovanja tek NAKON prijelaza i svih nuspojava (uključujući
        // promociju liste čekanja, koja može dodati Confirmed sudjelovanje — zato i dolazi prije izvođenja). Korekcija koja
        // vrati sudjelovanje na Confirmed time automatski vraća Closed/Cancelled termin u Scheduled.
        await AppointmentLifecycle.Refresh(_appointmentHandler, _auditLogHandler, uow, organizationId, appointmentId, userId);
    }

    /// <summary>P1 (D12) — korak (1) korekcije: reverzija SVIH aktivnih efekata prethodnog stanja, u istoj transakciji.
    /// Iz Completed: check-in plaćanja se voidaju (ručna plaćanja ostaju kao namirenje), potrošnja paketa izvršenja se vraća,
    /// individualna provizija completiona prelazi u Reversed. Iz Cancelled/NoShow: aktivna posljedica politike prelazi u
    /// Reversed i vraća jedinicu potrošenu kao kaznu (sa stvarnim učinkom traži override i razlog korekcije). Waived i
    /// Reversed zapisi se nikad ne mijenjaju.</summary>
    private async Task ReversePreviousStateEffects(
        IUnitOfWork uow, Guid organizationId, Guid userId, Appointment appointment, Booking booking,
        BookingSegmentParticipation participation, ParticipationStatus oldStatus, ParticipationStatus target,
        BookingSetStatusRequest request, DateTimeOffset eventAt)
    {
        if (oldStatus == ParticipationStatus.Completed)
        {
            await _paymentLedgerService.VoidCheckInGeneratedPayments(
                uow, organizationId, userId, booking, participation, $"Poništen check-in (korekcija Completed -> {target})");

            if (await _packageConsumptionLedgerService.ReverseActive(
                    uow, organizationId, userId, participation, ReversalReasonFor(BookingParticipations.ToBookingStatus(target))))
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

            // K2: storno nosi razlog s vezom na korekciju (StatusVersion sudjelovanja koji korekcija ostavlja).
            if (appointment.Form != AppointmentForm.Group)
                await _commissionLedgerService.ReverseForIndividualServiceCorrection(uow, organizationId, userId, participation,
                    CorrectionReasonText(request, oldStatus, target, participation.StatusVersion + 1));
        }
        else if (!ParticipationOccupancy.Occupies(oldStatus))
        {
            // K2 (ADR-0032, izmjena D12): korekcija poništava posljedicu kao ispravak činjenice (Reversed, nikad Waived) i ne traži
            // grant otpisa; na zatvorenom terminu grant korekcije i razlog je već provjerio EnsureClosedAppointmentRules.
            string reason = string.IsNullOrWhiteSpace(request.CorrectionReason)
                ? $"Korekcija statusa {oldStatus} -> {target}"
                : request.CorrectionReason.Trim();
            await _participationPolicyService.ReverseActive(uow, organizationId, userId, participation, reason, eventAt);
        }
    }

    /// <summary>K2 — razlog storna provizije: korekcija (izvorni → ciljni status, StatusVersion korekcije) i razlog korekcije
    /// ako je zadan.</summary>
    private static string CorrectionReasonText(
        BookingSetStatusRequest request, ParticipationStatus oldStatus, ParticipationStatus target, int correctionStatusVersion)
    {
        string text = $"Korekcija statusa {oldStatus} -> {target} (StatusVersion {correctionStatusVersion})";
        return string.IsNullOrWhiteSpace(request.CorrectionReason) ? text : $"{text}: {request.CorrectionReason.Trim()}";
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

    /// <summary>Phase M1E — Form=Individual, novi događaj Completed JEDNOG sudjelovanja (status je već postavljen; prethodno
    /// stanje je već reverzirano — P1 dopušta i Cancelled/NoShow -&gt; Completed): cijena se mijenja samo uz eksplicitni
    /// ručni iznos (inače ostaje cijena sudjelovanja), paket se primjenjuje SAMO uz eksplicitni ClientPackageId (bez
    /// automatskog odabira — isto kao CompleteNow; valjanost na datum izvođenja SEGMENTA), inače se uz PaymentMethod + IsPaid
    /// stvara Payment za iznos tog sudjelovanja. Paket i novac su isključivi SAMO unutar ovog sudjelovanja
    /// (ledger/SettlementExclusivityPolicy) — druga sudjelovanja istog Bookinga se namiruju neovisno.</summary>
    private async Task<(PaymentMethod Method, decimal Amount)?> ApplyIndividualCompletion(
        IUnitOfWork uow, Guid organizationId, Guid userId, Appointment appointment, Booking booking,
        BookingSegmentParticipation participation, BookingSetStatusRequest request)
    {
        ParticipationExecutionContext execution = ExecutionContextResolver.ForParticipation(appointment, booking, participation);
        if (request.Amount.HasValue)
            ParticipationPrice.Apply(participation, BookingPricing.FromResolution(
                await ResolveServicePrice(organizationId, execution.ServiceId, execution.CompanyId, execution.PricingEmployeeId, execution.StartsAt), request.Amount));

        // P2 (2D): usluga pokrivena članarinom — članarina ima prednost (pravilo pokrića 1): paket se ne može primijeniti, a novac
        // se ne naplaćuje (dug je 0). Bez članarine no-op.
        if (MembershipCoverages.CoversService(participation))
        {
            if (request.ClientPackageId.HasValue)
                SettlementExclusivityPolicy.EnsureNotMembershipCovered(participation);
            return null;
        }

        if (request.ClientPackageId.HasValue)
        {
            List<ClientPackageDto> eligible = await _clientPackageService.GetEligibleForService(
                organizationId, execution.ClientId, execution.ServiceId, execution.StartsAt, execution.CompanyId);
            if (eligible.All(p => p.Id != request.ClientPackageId.Value))
                throw new BusinessRuleException(ErrorCodes.PackageNotEligible, "Odabrani paket nije valjan za klijenta ili ne pokriva ovu uslugu.");

            await _packageConsumptionLedgerService.Consume(
                uow, organizationId, userId, participation, execution, request.ClientPackageId.Value, BookingStatus.Completed);
            // P2 (2E, Q2): sesija pokrivena paketom nema pogodnost članarine — cijena se vraća na cjenik (osim ručnog iznosa).
            await _membershipCoverage.PriceOnCompletion(uow, organizationId, userId, appointment, booking, participation, packageCovered: true);
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

        // P2 (2D): check-in sesije pokrivene članarinom — cijena (retail) se snapshotira kao i inače, ali paket se ne bira ni
        // automatski (članarina ima prednost, pravilo pokrića 1) i novac se ne naplaćuje (dug 0). Bez članarine no-op.
        if (MembershipCoverages.CoversService(participation))
        {
            if (request.ClientPackageId.HasValue)
                SettlementExclusivityPolicy.EnsureNotMembershipCovered(participation);
            ParticipationPrice.Apply(participation, BookingPricing.FromResolution(
                await ResolveServicePrice(organizationId, execution.ServiceId, execution.CompanyId, execution.PricingEmployeeId, execution.StartsAt), request.Amount));
            return null;
        }

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
        ParticipationPrice.Apply(participation, pricing);
        // P2 (2E): pogodnost članarine na nepokrivenu sesiju (paket ili pokriće članarinom = cjenik, Q2); bez članarine no-op, pa je
        // iznos jednak razriješenoj cijeni kao i prije.
        await _membershipCoverage.PriceOnCompletion(uow, organizationId, userId, appointment, booking, participation, packageCovered: selected != null);
        decimal amount = participation.Amount;

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
