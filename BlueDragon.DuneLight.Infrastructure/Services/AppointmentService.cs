using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using BlueDragon.DuneLight.Core.DTOs.Appointments;
using BlueDragon.DuneLight.Core.DTOs.Catalog;
using BlueDragon.DuneLight.Core.DTOs.Clients;
using BlueDragon.DuneLight.Core.Enums;
using BlueDragon.DuneLight.Core.Events;
using BlueDragon.DuneLight.Core.Interfaces;
using BlueDragon.DuneLight.Core.Interfaces.Appointments;
using BlueDragon.DuneLight.Core.Interfaces.Catalog;
using BlueDragon.DuneLight.Core.Interfaces.Clients;
using BlueDragon.DuneLight.Core.Interfaces.Organization;
using BlueDragon.DuneLight.Core.Shared;
using BlueDragon.DuneLight.Core.Shared.Exceptions;
using BlueDragon.DuneLight.Infrastructure.Domain.Models.Appointments;
using BlueDragon.DuneLight.Infrastructure.Domain.Models.Catalog;
using BlueDragon.DuneLight.Infrastructure.Domain.Models.Checkouts;
using BlueDragon.DuneLight.Infrastructure.Domain.Models.Clients;
using BlueDragon.DuneLight.Infrastructure.Domain.Models.Employees;
using BlueDragon.DuneLight.Infrastructure.Domain.Models.Roster;
using BlueDragon.DuneLight.Infrastructure.Handlers.Interfaces;
using BlueDragon.DuneLight.Infrastructure.Outbox;
using BlueDragon.DuneLight.Infrastructure.UnitOfWork;
using BlueDragon.DuneLight.Infrastructure.Utils;
using Microsoft.EntityFrameworkCore;
using ServiceEntity = BlueDragon.DuneLight.Infrastructure.Domain.Models.Catalog.Service;

namespace BlueDragon.DuneLight.Infrastructure.Services;

public partial class AppointmentService : IAppointmentService
{
    private const string NotOwnerMessage = "Trener smije upravljati samo svojim vlastitim terminima.";

    private readonly IAppointmentHandler _appointmentHandler;
    private readonly ISchedulingOccupancyHandler _schedulingOccupancyHandler;
    private readonly IAppointmentAuditLogHandler _auditLogHandler;
    private readonly IClientPackageService _clientPackageService;
    private readonly IPackageConsumptionLedgerService _packageConsumptionLedgerService;
    private readonly IPricingService _pricingService;
    private readonly IServiceHandler _serviceHandler;
    private readonly IServiceAvailabilityService _serviceAvailabilityService;
    private readonly IEmployeeHandler _employeeHandler;
    private readonly ICompanyHandler _companyHandler;
    private readonly IRoomHandler _roomHandler;
    private readonly IResourceHandler _resourceHandler;
    private readonly IAppointmentSegmentHandler _appointmentSegmentHandler;
    private readonly IClientHandler _clientHandler;
    private readonly IRosterEntryHandler _rosterEntryHandler;
    private readonly IWorkingHoursTemplateHandler _workingHoursTemplateHandler;
    private readonly ICompanyHolidayHandler _companyHolidayHandler;
    private readonly IScheduleBreakHandler _scheduleBreakHandler;
    private readonly IWaitlistPromotionService _waitlistPromotionService;
    private readonly IPaymentLedgerService _paymentLedgerService;
    private readonly ICheckoutHandler _checkoutHandler;
    private readonly IBookingSegmentParticipationHandler _participationHandler;
    private readonly ICommissionLedgerService _commissionLedgerService;
    private readonly IParticipationLifecycleService _participationLifecycleService;
    private readonly IOutboxWriter _outboxWriter;
    private readonly IUnitOfWorkFactory _unitOfWorkFactory;

    private readonly IOrganizationCalendarService _organizationCalendarService;

    private readonly IGrantResolver _grantResolver;
    private readonly IMembershipCoverageService _membershipCoverage;

    public AppointmentService(
        IAppointmentHandler appointmentHandler,
        ISchedulingOccupancyHandler schedulingOccupancyHandler,
        IAppointmentAuditLogHandler auditLogHandler,
        IClientPackageService clientPackageService,
        IPackageConsumptionLedgerService packageConsumptionLedgerService,
        IPricingService pricingService,
        IServiceHandler serviceHandler,
        IServiceAvailabilityService serviceAvailabilityService,
        IEmployeeHandler employeeHandler,
        ICompanyHandler companyHandler,
        IRoomHandler roomHandler,
        IResourceHandler resourceHandler,
        IAppointmentSegmentHandler appointmentSegmentHandler,
        IClientHandler clientHandler,
        IRosterEntryHandler rosterEntryHandler,
        IWorkingHoursTemplateHandler workingHoursTemplateHandler,
        ICompanyHolidayHandler companyHolidayHandler,
        IScheduleBreakHandler scheduleBreakHandler,
        IWaitlistPromotionService waitlistPromotionService,
        IPaymentLedgerService paymentLedgerService,
        ICheckoutHandler checkoutHandler,
        IBookingSegmentParticipationHandler participationHandler,
        ICommissionLedgerService commissionLedgerService,
        IParticipationLifecycleService participationLifecycleService,
        IOutboxWriter outboxWriter,
        IUnitOfWorkFactory unitOfWorkFactory,
        IOrganizationCalendarService organizationCalendarService,
        IGrantResolver grantResolver,
        IMembershipCoverageService membershipCoverage)
    {
        _organizationCalendarService = organizationCalendarService;
        _grantResolver = grantResolver;
        _membershipCoverage = membershipCoverage;
        _appointmentHandler = appointmentHandler;
        _schedulingOccupancyHandler = schedulingOccupancyHandler;
        _auditLogHandler = auditLogHandler;
        _clientPackageService = clientPackageService;
        _packageConsumptionLedgerService = packageConsumptionLedgerService;
        _pricingService = pricingService;
        _serviceHandler = serviceHandler;
        _serviceAvailabilityService = serviceAvailabilityService;
        _employeeHandler = employeeHandler;
        _companyHandler = companyHandler;
        _roomHandler = roomHandler;
        _resourceHandler = resourceHandler;
        _appointmentSegmentHandler = appointmentSegmentHandler;
        _clientHandler = clientHandler;
        _rosterEntryHandler = rosterEntryHandler;
        _workingHoursTemplateHandler = workingHoursTemplateHandler;
        _companyHolidayHandler = companyHolidayHandler;
        _scheduleBreakHandler = scheduleBreakHandler;
        _waitlistPromotionService = waitlistPromotionService;
        _paymentLedgerService = paymentLedgerService;
        _checkoutHandler = checkoutHandler;
        _participationHandler = participationHandler;
        _commissionLedgerService = commissionLedgerService;
        _participationLifecycleService = participationLifecycleService;
        _outboxWriter = outboxWriter;
        _unitOfWorkFactory = unitOfWorkFactory;
    }

    /// <summary>Phase M1B — CILJNI ugovor kreiranja (segmenti + sudionici). Phase M1E: višesegmentno kreiranje je omogućeno —
    /// atomično (svi segmenti ili nijedan), jedan Booking po klijentu i jedno sudjelovanje po odabranom segmentu, cijelo
    /// ciljno stanje (i sestrinski segmenti međusobno) validirano pod zaključanim subjektima.</summary>
    public Task<AppointmentDto> Create(Guid organizationId, Guid userId, bool hasFullScope, AppointmentCreateRequest request)
    {
        return CreateInternal(organizationId, userId, hasFullScope, request, recurrenceGroupId: null);
    }

    /// <summary>Validiran segment ciljnog zahtjeva: plan za konstrukcijsku jezgru + ono što trebaju provjere zauzetosti.</summary>
    private sealed record ValidatedSegment(SegmentPlan Plan, ServiceEntity Service, Room Room, List<Client> Clients);

    /// <summary>Phase M1E — ograničenja proizvoda nad ciljnim ugovorom: 1..N segmenata (višesegmentni termini su omogućeni;
    /// MULTI_SEGMENT_NOT_ENABLED je uklonjen), svaki segment ograničen kao u <see cref="EnsureSegmentProductLimits"/>.</summary>
    private static void EnsureCurrentProductLimits(AppointmentCreateRequest request)
    {
        if (request.Segments == null || request.Segments.Count == 0)
            throw new ValidationAppException("Termin mora imati barem jedan segment.");

        foreach (AppointmentSegmentCreateRequest segment in request.Segments)
            EnsureSegmentProductLimits(segment, requireParticipants: true);
    }

    /// <summary>Po segmentu: barem jedan zaposlenik (Phase M1G: 1..N ravnopravnih zaposlenika; individualni segment bez
    /// zaposlenika i dalje nije podržan), bez duplikata zaposlenika (skup — ne spaja se tiho), izvor cijene valjan za broj
    /// zaposlenika (<see cref="SegmentPricingSource"/>), ispravni resursi (količina &gt; 0, bez duplikata), sudionici bez
    /// duplikata. Kod kreiranja termina svaki segment ima barem jednog sudionika; segment dodan postojećem terminu smije biti
    /// bez sudionika (sesija bez klijenata je valjana).</summary>
    private static void EnsureSegmentProductLimits(AppointmentSegmentCreateRequest segment, bool requireParticipants)
    {
        if (segment == null)
            throw new ValidationAppException("Segment je obavezan.");
        EnsureEmployeeSet(segment.EmployeeIds, allowEmpty: false);
        SegmentPricingSource.Normalize(segment.EmployeeIds, segment.PricingMode, segment.PricingEmployeeId);
        if (segment.Resources is { Count: > 0 })
        {
            if (segment.Resources.Any(r => r.QuantityRequired <= 0))
                throw new ValidationAppException("Količina resursa mora biti veća od 0.");
            if (segment.Resources.Select(r => r.ResourceId).Distinct().Count() != segment.Resources.Count)
                throw new ValidationAppException("Isti resurs se na segmentu smije navesti samo jednom.");
        }
        segment.Participants ??= new List<AppointmentParticipantCreateRequest>();
        if (requireParticipants && segment.Participants.Count == 0)
            throw new ValidationAppException("Segment mora imati barem jednog sudionika.");
        if (segment.Participants.Select(p => p.ClientId).Distinct().Count() != segment.Participants.Count)
            throw new ValidationAppException("Klijent se u istom segmentu smije pojaviti samo jednom.");
    }

    /// <summary>Strukturna validacija i cijena JEDNOG segmenta (usluga, poslovnica, zaposlenici, prostorija, klijenti);
    /// cijena se razrješava po usluzi segmenta na početak segmenta, uz ručni iznos sudionika.</summary>
    private async Task<ValidatedSegment> ValidateSegment(
        Guid organizationId, Guid companyId, AppointmentSegmentCreateRequest segment, PricingMode pricingMode)
    {
        ServiceEntity service = await LoadServiceOrThrow(organizationId, segment.ServiceId);
        List<Guid> employeeIds = segment.EmployeeIds.ToList();
        PricingSourceValue pricingSource = SegmentPricingSource.Normalize(employeeIds, segment.PricingMode, segment.PricingEmployeeId);
        foreach (Guid employeeId in employeeIds)
            await EnsureStructuralEligibility(organizationId, service, companyId, employeeId);
        Room room = await EnsureRoomExists(organizationId, companyId, segment.RoomId);
        List<SegmentResourcePlan> resources = new List<SegmentResourcePlan>();
        foreach (AppointmentSegmentResourceRequest resource in segment.Resources ?? new List<AppointmentSegmentResourceRequest>())
        {
            await EnsureResourceUsable(organizationId, companyId, resource.ResourceId);
            resources.Add(new SegmentResourcePlan(resource.ResourceId, resource.QuantityRequired));
        }
        List<Client> clients = await EnsureClientsExist(organizationId, segment.Participants.Select(p => p.ClientId).ToList());

        DateTimeOffset plannedEnd = segment.PlannedEnd ?? segment.PlannedStart.AddMinutes(service.DefaultDurationMinutes);
        if (plannedEnd <= segment.PlannedStart)
            throw new ValidationAppException("Kraj segmenta mora biti nakon početka.");

        ResolvePriceResponse resolvedPrice = await ResolveServicePrice(
            organizationId, segment.ServiceId, companyId, pricingSource.PricingEmployeeId, segment.PlannedStart);
        List<ParticipantPlan> participants = segment.Participants
            .Select(p => new ParticipantPlan(p.ClientId, pricingMode == PricingMode.SuggestedOnly
                ? BookingPricing.AtSuggested(resolvedPrice)
                : BookingPricing.FromResolution(resolvedPrice, p.Amount)))
            .ToList();

        return new ValidatedSegment(
            new SegmentPlan(segment.ServiceId, segment.PlannedStart, plannedEnd, employeeIds, segment.RoomId, participants, resources,
                PricingSource: pricingSource),
            service, room, clients);
    }

    /// <summary>Phase M1G — skup zaposlenika segmenta: bez duplikata (odbija se, ne spaja tiho); prazan samo gdje tok to
    /// dopušta (grupni predložak bez trenera) — individualni segment ima barem jednog zaposlenika.</summary>
    internal static void EnsureEmployeeSet(IReadOnlyCollection<Guid> employeeIds, bool allowEmpty)
    {
        if (employeeIds == null || (!allowEmpty && employeeIds.Count == 0))
            throw new ValidationAppException("Segment mora imati barem jednog zaposlenika.");
        if (employeeIds.Distinct().Count() != employeeIds.Count)
            throw new ValidationAppException("Isti zaposlenik se na segmentu smije navesti samo jednom.");
    }

    /// <summary>/recurring namjerno ignorira ručni iznos (svaki occurrence po svojoj predloženoj cijeni).</summary>
    private enum PricingMode
    {
        WithManualOverride,
        SuggestedOnly
    }

    /// <summary>
    /// Phase M1H — "upiši odrađeno" (vidi <see cref="AppointmentCompleteNowRequest"/>): JEDAN eksplicitni segment (doseg
    /// naredbe), validiran istom ciljnom validacijom kao kreiranje (struktura, podobnost i radna snaga svih zaposlenika,
    /// izvor cijene, preklapanja, kapacitet prostorije/resursa); sudjelovanja nastaju Confirmed i zatim se ODRAĐUJU kroz
    /// jedinu jezgru prijelaza sudjelovanja (IParticipationLifecycleService — StatusVersion, cijena, paket ILI novac,
    /// provizija po zaposleniku, audit, izvođenje statusa termina) — sve u JEDNOJ transakciji (sve ili ništa).
    /// Prošlost je dopuštena (evidentiranje stvarnosti): radna snaga se provjerava samo za budući početak, tvrde invarijante uvijek.
    /// </summary>
    public async Task<AppointmentDto> CompleteNow(Guid organizationId, Guid userId, bool hasFullScope, AppointmentCompleteNowRequest request)
    {
        if (request?.Segment == null)
            throw new ValidationAppException("Segment je obavezan.");
        List<AppointmentCompletedClientRequest> clientRequests = request.Clients ?? new List<AppointmentCompletedClientRequest>();
        if (clientRequests.Count == 0)
            throw new ValidationAppException("Potreban je barem jedan klijent.");
        if (clientRequests.Select(c => c.ClientId).Distinct().Count() != clientRequests.Count)
            throw new ValidationAppException("Klijent se smije navesti samo jednom.");

        AppointmentSegmentCreateRequest segmentRequest = new()
        {
            ServiceId = request.Segment.ServiceId,
            PlannedStart = request.Segment.PlannedStart,
            PlannedEnd = request.Segment.PlannedEnd,
            EmployeeIds = request.Segment.EmployeeIds,
            PricingMode = request.Segment.PricingMode,
            PricingEmployeeId = request.Segment.PricingEmployeeId,
            RoomId = request.Segment.RoomId,
            Resources = request.Segment.Resources,
            Participants = clientRequests
                .Select(c => new AppointmentParticipantCreateRequest { ClientId = c.ClientId, Amount = c.Amount })
                .ToList()
        };
        EnsureSegmentProductLimits(segmentRequest, requireParticipants: true);
        await AppointmentOwnership.EnsureCallerIsEmployee(_employeeHandler, organizationId, userId, hasFullScope,
            segmentRequest.EmployeeIds, NotOwnerMessage);
        bool overrideAvailability = request.OverrideAvailability && hasFullScope;

        ValidatedSegment validated = await ValidateSegment(organizationId, request.CompanyId, segmentRequest, PricingMode.WithManualOverride);
        SegmentPlan plan = validated.Plan;

        List<WarningDto> warnings = new List<WarningDto>();
        if (plan.PlannedStart > DateTimeOffset.UtcNow)
            warnings.AddRange(await EnsureWorkforceAvailability(
                organizationId, plan.EmployeeIds, request.CompanyId, plan.PlannedStart, plan.PlannedEnd, overrideAvailability));

        Appointment appointment = AppointmentFactory.CreateIndividual(
            organizationId, request.CompanyId, request.Note, recurrenceGroupId: null, userId, DateTimeOffset.UtcNow,
            new[] { plan });
        Guid appointmentId = appointment.Id.GetValueOrDefault();
        AppointmentSegment segment = appointment.Segments.Single();

        try
        {
            await using IUnitOfWork uow = await _unitOfWorkFactory.Begin();

            // Tvrde invarijante pod zaključanim subjektima (zaposlenici, klijenti, prostorija, resursi) — PRVO u transakciji.
            await EnsureNoHardOverlap(uow, organizationId, new[]
            {
                new HardOverlapTarget(null, plan.PlannedStart, plan.PlannedEnd, plan.EmployeeIds, validated.Clients, validated.Room, ResourcesOf(plan))
            });

            await _appointmentHandler.Add(uow, appointment);

            // Odrađivanje kroz jedinu jezgru prijelaza sudjelovanja, stabilnim redoslijedom (po klijentu).
            foreach (AppointmentCompletedClientRequest client in clientRequests.OrderBy(c => c.ClientId))
            {
                Booking booking = appointment.Bookings.Single(b => b.ClientId == client.ClientId);
                BookingSegmentParticipation participation = BookingParticipations.OnSegment(booking, segment);
                await _participationLifecycleService.ApplyTransitionInTransaction(uow, organizationId, userId, appointment, booking, participation,
                    new BookingSetStatusRequest
                    {
                        Status = BookingStatus.Completed,
                        ClientPackageId = client.ClientPackageId,
                        PaymentMethod = client.ClientPackageId.HasValue ? null : client.PaymentMethod,
                        IsPaid = client.IsPaid
                    });
            }

            await uow.CommitAsync();
        }
        catch (DbUpdateConcurrencyException)
        {
            throw new BusinessRuleException(
                ErrorCodes.ConcurrencyConflict, "Paket je upravo promijenjen od strane drugog zahtjeva — pokušajte ponovno.");
        }

        AppointmentDto dto = await GetByIdInternal(organizationId, appointmentId);
        dto.Warnings = warnings;
        return dto;
    }

    /// <summary>Phase M1H — napomena termina (metapodatak agregata): ne dira segmente, cijene, raspored ni životni ciklus.
    /// Vlasništvo kao i ostale izmjene termina: own-opseg mora biti dodijeljen SVAKOM segmentu termina.</summary>
    public async Task<AppointmentDto> ChangeNote(Guid organizationId, Guid userId, bool hasFullScope, Guid id, AppointmentNoteChangeRequest request)
    {
        Appointment appointment = await _appointmentHandler.GetByIdLight(organizationId, id)
            ?? throw new NotFoundAppException("Appointment", id);
        await AppointmentOwnership.EnsureCallerOwnsSegments(_employeeHandler, organizationId, userId, hasFullScope, appointment.Segments, NotOwnerMessage);

        await using (IUnitOfWork uow = await _unitOfWorkFactory.Begin())
        {
            Appointment locked = await _appointmentHandler.GetForUpdate(uow, organizationId, id)
                ?? throw new NotFoundAppException("Appointment", id);
            locked.Note = request?.Note;
            locked.UpdatedAt = DateTimeOffset.UtcNow;
            locked.UpdatedBy = userId;
            await _appointmentHandler.UpdateScalar(uow, locked);
            await uow.CommitAsync();
        }

        return await GetByIdInternal(organizationId, id);
    }

    /// <summary>Phase M1A.1 — "zatvaranje grupne sesije" (close-out) je POSLOVNA ČINJENICA (Appointment.ClosedOutAt/By),
    /// ne životni ciklus termina: namjerno NE dira nijedno sudjelovanje i NE postavlja status (status se izvodi iz
    /// sudjelovanja — sesija s još Confirmed članom ostaje Scheduled uz GROUP_APPOINTMENT_UNRESOLVED_BOOKINGS upozorenje).
    /// Prvi close-out bilježi činjenicu i izvodi nuspojave TOČNO JEDNOM: istek liste čekanja i provizija po sesiji (prema
    /// pravilu primjenjivom u tom trenutku). Ponovljeni close-out je idempotentan: ne bilježi novi close-out, ne zarađuje
    /// proviziju (ni ako je pravilo dodano naknadno) i ne ponavlja istek liste čekanja — vraća stanje i upozorenje.
    /// Identitet close-outa je ClosedOutAt, NIKAD postojanje CommissionEntry. Otkazan termin se ne može zatvoriti.</summary>
    public async Task<AppointmentDto> CompleteGroupAppointment(Guid organizationId, Guid userId, bool hasFullScope, Guid id)
    {
        Appointment appointment;
        List<WarningDto> commissionWarnings = new();

        await using (IUnitOfWork uow = await _unitOfWorkFactory.Begin())
        {
            // Zaključava Appointment redak (FOR UPDATE) i čita Form/Status/EmployeeId pod lockom PRIJE bilo kakve
            // provjere/mutacije — serijalizira close-out s konkurentnim cancel/close-out zahtjevom na ISTOM terminu.
            // Bookings su uključeni jer se čitaju i nakon commita (unresolvedClientIds upozorenje niže).
            appointment = await _appointmentHandler.GetForUpdateWithBookings(uow, organizationId, id);
            if (appointment == null)
                throw new NotFoundAppException("Appointment", id);

            if (appointment.Form != AppointmentForm.Group)
                throw new ValidationAppException(
                    "Individualni termin se odrađuje po sudjelovanju (participations/{id}/status) ili kroz complete, ne kroz complete-group.");

            if (appointment.Status == AppointmentStatus.Cancelled)
                throw new BusinessRuleException(ErrorCodes.AppointmentNotMovable, "Otkazan termin se ne može označiti kao odrađen.");

            // Phase M1F: close-out zatvara CIJELU sesiju (sve segmente occurrencea) — own-opseg mora posjedovati svaki segment
            // (grupni trener je dodijeljen svim segmentima occurrencea).
            await AppointmentOwnership.EnsureCallerOwnsSegments(
                _employeeHandler, organizationId, userId, hasFullScope, appointment.Segments, NotOwnerMessage);

            if (appointment.ClosedOutAt == null)
            {
                DateTimeOffset now = DateTimeOffset.UtcNow;
                appointment.ClosedOutAt = now;
                appointment.ClosedOutBy = userId;
                appointment.UpdatedAt = now;
                appointment.UpdatedBy = userId;
                await _appointmentHandler.UpdateScalar(uow, appointment);

                await _auditLogHandler.Add(uow, new AppointmentAuditLog
                {
                    Id = Guid.NewGuid(),
                    AppointmentId = id,
                    ChangeType = "GroupClosedOut",
                    OldValue = null,
                    NewValue = now.ToString("O"),
                    ChangedAt = now,
                    ChangedBy = userId
                });

                // Occurrence je zatvoren — preostali Waiting retci više nisu smisleni (spec section 20), ne promovira se.
                await _waitlistPromotionService.ExpireWaitingForAppointment(
                    uow, organizationId, id, userId, WaitlistExpiredReasons.AppointmentCompleted);

                // Provizija se zarađuje PO SESIJI, ne po sudioniku. Phase M1G: izvor je SVAKI SEGMENT occurrencea (njegova usluga
                // i njegovi zaposlenici) — za svakog zaposlenika segmenta jedan Fixed zapis (pravilo zaposlenik × usluga
                // segmenta). Nema "prvog" segmenta ni usluge termina. Jednom: zaštićeno close-out činjenicom iznad (pod lockom
                // termina) i jedinstvenošću (segment, zaposlenik) u bazi.
                foreach (AppointmentSegment segment in appointment.Segments.OrderBy(s => s.PlannedStart).ThenBy(s => s.Id))
                    commissionWarnings.AddRange(await _commissionLedgerService.GenerateForGroupServiceCompletion(
                        uow, organizationId, ExecutionContextResolver.ForSegment(appointment, segment)));
            }

            // Status se ne postavlja — izvodi se (no-op kad je već usklađen).
            await AppointmentLifecycle.Refresh(_appointmentHandler, _auditLogHandler, uow, organizationId, id, userId);

            await uow.CommitAsync();
        }

        // Phase M0: nerazriješen = klijent s barem jednim još Confirmed sudjelovanjem (A, read-only upozorenje).
        List<Guid> unresolvedClientIds = appointment.Bookings
            .Where(b => b.Participations.Any(p => p.Status == ParticipationStatus.Confirmed))
            .Select(b => b.ClientId)
            .ToList();

        AppointmentDto dto = await GetByIdInternal(organizationId, id);
        if (unresolvedClientIds.Count > 0)
            dto.Warnings.Add(new WarningDto(
                WarningCodes.GroupAppointmentUnresolvedBookings, new WarningUnresolvedBookingsDetails { ClientIds = unresolvedClientIds }));
        dto.Warnings.AddRange(commissionWarnings);

        return dto;
    }

    /// <summary>Otkazuje CIJELI termin — svi aktivni (Confirmed) sudionici prelaze u Cancelled, a termin se eksplicitno
    /// otkazuje. P1 (D2): otkazivanje termina je uvijek poslovno (initiator Business obavezan, Client se odbija) uz razlog;
    /// politika se ne evaluira. Za otkazivanje SAMO jednog klijenta koristi se Booking-wide ili participation naredba.</summary>
    public Task<AppointmentDto> Cancel(Guid organizationId, Guid userId, bool hasFullScope, Guid id, AppointmentCancelRequest request)
    {
        if (request?.CancellationInitiator == null)
            throw new ValidationAppException("Initiator otkazivanja je obavezan (otkazivanje termina je uvijek Business).");
        if (request.CancellationInitiator != CancellationInitiator.Business)
            throw new ValidationAppException(
                "Otkazivanje cijelog termina je uvijek poslovno (Business) — klijentsko otkazivanje ide po Bookingu ili sudjelovanju.");
        if (string.IsNullOrWhiteSpace(request.CancellationReason))
            throw new ValidationAppException("Poslovno (Business) otkazivanje zahtijeva razlog.");

        return ChangeToTerminalStatus(organizationId, userId, hasFullScope, id, new BookingSetStatusRequest
        {
            Status = BookingStatus.Cancelled,
            CancellationInitiator = CancellationInitiator.Business,
            CancellationReason = request.CancellationReason
        }, request.CancellationReason);
    }

    /// <summary>Bulk no-show — svi aktivni sudionici prelaze u NoShow (termin se izvodi u Closed; termin kao okvir NIKAD nije
    /// NoShow). P1: politika izostanka se evaluira po sudjelovanju; otpis u trenutku događaja traži razlog i
    /// appointments.policy.override. Za pojedinačni no-show koristi se participation naredba.</summary>
    public async Task<AppointmentDto> MarkNoShow(Guid organizationId, Guid userId, bool hasFullScope, Guid id, NoShowRequest request)
    {
        if (request?.WaivePolicyConsequence == true)
            await PolicyOverride.EnsureWaiverAllowed(_grantResolver, organizationId, userId, request.WaiverReason);

        return await ChangeToTerminalStatus(organizationId, userId, hasFullScope, id, new BookingSetStatusRequest
        {
            Status = BookingStatus.NoShow,
            NoShowReason = request?.NoShowReason,
            WaivePolicyConsequence = request?.WaivePolicyConsequence ?? false,
            WaiverReason = request?.WaiverReason
        }, appointmentCancellationReason: null, request?.ClientPackageId, request?.PackageSelections);
    }

    public async Task Delete(Guid organizationId, Guid userId, Guid id)
    {
        Appointment appointment = await _appointmentHandler.GetByIdLight(organizationId, id);
        if (appointment == null)
            throw new NotFoundAppException("Appointment", id);

        // "Isti dan" je kalendarski dan poslovnice termina (efektivna zona), ne UTC dan (F-19 / timezone foundation).
        OrganizationCalendar calendar = await _organizationCalendarService.GetCompanyCalendar(organizationId, appointment.CompanyId);
        if (calendar.LocalDate(appointment.CreatedAt) != calendar.LocalDate(DateTimeOffset.UtcNow))
            throw new BusinessRuleException(ErrorCodes.SameDayOnly, "Termin se može trajno obrisati samo istog dana kad je unesen — u suprotnom ga otkažite.");

        // Phase D3B1: fizičko brisanje samo ako su sva sudjelovanja netaknuta (pravilo i brisanje: ParticipationHistory kroz
        // AppointmentHandler.Delete); sudjelovanje s poviješću → REFERENCED_CANNOT_DELETE. P2 (2D): claim netaknutog
        // sudjelovanja se prije brisanja vraća (oslobođeno mjesto se prerasporedi), u istoj transakciji; bez članarine no-op.
        await using IUnitOfWork uow = await _unitOfWorkFactory.Begin();
        List<BookingSegmentParticipation> participations = await uow.Context.BookingSegmentParticipations
            .Where(p => p.OrganizationId == organizationId && p.Booking.AppointmentId == id)
            .ToListAsync();
        await _membershipCoverage.ReleaseForRemoval(uow, organizationId, userId, participations);
        await _appointmentHandler.Delete(uow, appointment);
        await uow.CommitAsync();
    }

    public async Task<List<AppointmentDto>> CreateRecurring(Guid organizationId, Guid userId, bool hasFullScope, RecurringAppointmentCreateRequest request)
    {
        if (request.EndDate < request.FirstOccurrenceStartsAt)
            throw new ValidationAppException("Datum kraja ne smije biti prije prvog termina.");

        ServiceEntity service = await LoadServiceOrThrow(organizationId, request.ServiceId);
        bool overrideAvailability = request.OverrideAvailability && hasFullScope;
        // Isto lokalno vrijeme u efektivnoj zoni poslovnice svaki dan/tjedan, i preko DST prijelaza.
        OrganizationCalendar calendar = await _organizationCalendarService.GetCompanyCalendar(organizationId, request.CompanyId);
        List<DateTimeOffset> occurrences = calendar.RepeatAtLocalTime(
            request.FirstOccurrenceStartsAt, request.EndDate, request.RecurrenceType == RecurrenceType.Daily ? 1 : 7);

        await EnsureStructuralEligibility(organizationId, service, request.CompanyId, request.EmployeeId);
        Room room = await EnsureRoomExists(organizationId, request.CompanyId, request.RoomId);

        // Phase M1C: cijeli niz se validira i upisuje u JEDNOJ transakciji koja najprije zaključa subjekte rasporeda
        // (zaposlenik + klijenti) — batch provjere niže tada vide sve što je konkurentno commitano prije njih.
        await using IUnitOfWork uow = await _unitOfWorkFactory.Begin();
        List<Guid> requestedClientIds = (request.ClientIds ?? new List<Guid>()).Distinct().ToList();
        await _schedulingOccupancyHandler.LockSchedulingSubjects(
            uow, new[] { request.EmployeeId }, requestedClientIds, room?.Id is Guid lockedRoomId ? new[] { lockedRoomId } : null);

        Dictionary<DateTimeOffset, List<WarningDto>> warningsByOccurrence = await EnsureNoRecurringConflicts(
            uow, organizationId, request.EmployeeId, request.CompanyId, occurrences, service.DefaultDurationMinutes, overrideAvailability,
            room, roomPeople: RoomPeopleCount.Of(1, requestedClientIds.Count));

        await AppointmentOwnership.EnsureCallerIsEmployee(_employeeHandler, organizationId, userId, hasFullScope, new[] { request.EmployeeId }, NotOwnerMessage);
        List<Client> clients = await EnsureClientsExist(organizationId, request.ClientIds);

        await EnsureNoRecurringClientOverlap(organizationId, clients, occurrences, service.DefaultDurationMinutes);

        Guid recurrenceGroupId = Guid.NewGuid();
        List<Appointment> toCreate = new List<Appointment>();

        foreach (DateTimeOffset occurrence in occurrences)
        {
            ResolvePriceResponse resolvedPrice = await ResolveServicePrice(organizationId, request.ServiceId, request.CompanyId, request.EmployeeId, occurrence);

            // Phase M1B: svaki occurrence kroz konstrukcijsku jezgru (jedan segment; /recurring namjerno ignorira ručni iznos
            // — request nema Amount — svaki occurrence po svojoj predloženoj cijeni).
            SegmentPlan plan = new SegmentPlan(
                request.ServiceId, occurrence, occurrence.AddMinutes(service.DefaultDurationMinutes), new[] { request.EmployeeId }, request.RoomId,
                clients.Select(c => new ParticipantPlan(c.Id.GetValueOrDefault(), BookingPricing.AtSuggested(resolvedPrice))).ToList());
            Appointment appointment = AppointmentFactory.CreateIndividual(
                organizationId, request.CompanyId, request.Note, recurrenceGroupId, userId, DateTimeOffset.UtcNow,
                new[] { plan });

            toCreate.Add(appointment);
        }

        await _appointmentHandler.AddRange(uow, toCreate);
        // P2 (2D, §11.4): pokriće occurrence po occurrence, redom po vremenu (limit koji presuši usred serije → ostatak na
        // sljedeći izvor; uz postavku "odbij" odbija se cijela serija).
        foreach (Appointment appointment in toCreate)
            await SyncMembershipCoverage(uow, organizationId, userId, appointment);
        await uow.CommitAsync();

        List<AppointmentDto> created = new List<AppointmentDto>();
        foreach (Appointment appointment in toCreate)
        {
            AppointmentDto dto = await GetByIdInternal(organizationId, appointment.Id.GetValueOrDefault());
            if (warningsByOccurrence.TryGetValue(AppointmentRange.Of(appointment).PlannedStart, out List<WarningDto> occurrenceWarnings))
                dto.Warnings = occurrenceWarnings;
            created.Add(dto);
        }

        return created;
    }

    /// <summary>SAMO za /recurring. STVARNI sudari (trener već ima termin/grupu u to vrijeme, ili soba zauzeta)
    /// i dalje abortiraju CIJELI niz s RECURRING_CONFLICT (409) prije nego se bilo što spremi — pojedinačni
    /// endpointi umjesto ovoga koriste EnsureNoHardOverlap (isto tvrda blokada za iste razloge, ali baca
    /// APPOINTMENT_OVERLAP za prvi sudar bez liste svih konflikata). Radno vrijeme/praznik/odsutnost/pauza trenera
    /// su TAKOĐER tvrda blokada za cijeli niz OSIM kad je overrideAvailability=true — tad se vraćaju kao
    /// upozorenje po occurrenceu koje pozivatelj upisuje u AppointmentDto.Warnings nakon što se niz stvarno
    /// kreira (isto ponašanje kao prije uvođenja tvrde blokade). Kandidati (termini trenera + roster odsutnosti)
    /// dohvaćaju se JEDNOM za cijeli raspon niza, precizna provjera po occurrenceu radi se u memoriji.</summary>
    private async Task<Dictionary<DateTimeOffset, List<WarningDto>>> EnsureNoRecurringConflicts(
        IUnitOfWork uow, Guid organizationId, Guid employeeId, Guid companyId, List<DateTimeOffset> occurrences, int durationMinutes,
        bool overrideAvailability, Room room, int roomPeople)
    {
        DateTimeOffset rangeFrom = occurrences[0].AddDays(-1);
        DateTimeOffset rangeTo = occurrences[^1].AddDays(1);

        List<OccupancySlot> candidateAppointments = await _schedulingOccupancyHandler.GetForEmployeeInRange(
            organizationId, employeeId, rangeFrom, rangeTo);

        // Phase M1D: prostorija je kapacitet u OSOBAMA (zaposlenik + klijenti svakog occurrencea), vremenski raslojeno uz
        // postojeće segmente; pozivatelj je zaključao prostoriju.
        HashSet<DateTimeOffset> roomOverCapacity = new HashSet<DateTimeOffset>();
        if (room != null)
        {
            Dictionary<SegmentClaim, DateTimeOffset> byClaim = occurrences.ToDictionary<DateTimeOffset, SegmentClaim, DateTimeOffset>(
                occurrence => new SegmentClaim(null, occurrence, occurrence.AddMinutes(durationMinutes), Array.Empty<Guid>(), Array.Empty<Guid>())
                {
                    RoomId = room.Id,
                    RoomPeople = roomPeople
                },
                occurrence => occurrence,
                ReferenceEqualityComparer.Instance);
            foreach (CapacityViolation violation in await SchedulingConflictGuard.FindCapacityViolations(
                         _schedulingOccupancyHandler, uow, organizationId, byClaim.Keys.ToList()))
                foreach (SegmentClaim claim in violation.Claims)
                    roomOverCapacity.Add(byClaim[claim]);
        }

        List<ScheduleBreak> candidateBreaks = await _scheduleBreakHandler.GetForEmployeeInRange(
            organizationId, employeeId, rangeFrom, rangeTo);

        // Učitano JEDNOM za cijeli raspon niza (apsencije + eventualni work-override redovi) — dijeli se između
        // absenceHit provjere i working-hours provjere ispod, isti obrazac kao candidateAppointments.
        // Kalendarski datumi i lokalna vremena occurrencea u efektivnoj zoni poslovnice — nikad offset zahtjeva ni hosta.
        OrganizationCalendar calendar = await _organizationCalendarService.GetCompanyCalendar(organizationId, companyId);
        DateOnly firstLocalDate = calendar.LocalDate(occurrences[0]);
        DateOnly lastLocalDate = calendar.LocalDate(occurrences[^1]);

        List<RosterEntry> rosterEntriesInRange = await _rosterEntryHandler.GetForPeriod(
            organizationId, new List<Guid> { employeeId }, firstLocalDate, lastLocalDate);

        List<RosterEntry> absences = rosterEntriesInRange.Where(e => e.RosterType.IsAbsence).ToList();

        WorkingHoursTemplate employeeTemplate = await _workingHoursTemplateHandler.GetForEmployee(organizationId, employeeId);
        WorkingHoursTemplate companyTemplate = await _workingHoursTemplateHandler.GetForCompany(organizationId, companyId);

        // Učitano JEDNOM za cijeli raspon niza — isti obrazac kao rosterEntriesInRange iznad.
        List<CompanyHoliday> companyHolidaysInRange = await _companyHolidayHandler.GetForCompaniesInRange(
            organizationId, new List<Guid> { companyId }, firstLocalDate, lastLocalDate);

        List<RecurringConflictDetail> hardConflicts = new List<RecurringConflictDetail>();
        Dictionary<DateTimeOffset, List<WarningDto>> warningsByOccurrence = new Dictionary<DateTimeOffset, List<WarningDto>>();

        foreach (DateTimeOffset occurrence in occurrences)
        {
            DateTimeOffset occurrenceEnd = occurrence.AddMinutes(durationMinutes);
            List<WarningDto> warnings = new List<WarningDto>();

            bool appointmentHit = candidateAppointments.Any(a => a.Overlaps(occurrence, occurrenceEnd));
            if (appointmentHit)
                hardConflicts.Add(new RecurringConflictDetail { Date = occurrence, Reason = ErrorCodes.RecurringConflictReasonAppointment });

            bool roomHit = roomOverCapacity.Contains(occurrence);
            if (roomHit)
                hardConflicts.Add(new RecurringConflictDetail { Date = occurrence, Reason = ErrorCodes.RecurringConflictReasonRoom });

            bool breakHit = candidateBreaks.Any(b =>
                SchedulingInterval.Overlaps(b.StartsAt, b.StartsAt.AddMinutes(b.DurationMinutes), occurrence, occurrenceEnd));

            DateOnly localDate = calendar.LocalDate(occurrence);

            bool absenceHit = absences.Any(a =>
                a.DateFrom <= localDate && (a.DateTo == null || localDate <= a.DateTo.Value));

            List<RosterEntry> rosterEntriesForOccurrence = rosterEntriesInRange
                .Where(e => !e.RosterType.IsAbsence && e.DateFrom == localDate)
                .ToList();

            List<CompanyHoliday> companyHolidaysForOccurrence = companyHolidaysInRange
                .Where(h => h.Date == localDate)
                .ToList();

            bool withinHours = absenceHit || IsWithinWorkingHours(
                employeeTemplate, companyTemplate, rosterEntriesForOccurrence, localDate, calendar.LocalTimeOfDay(occurrence),
                durationMinutes, companyHolidaysForOccurrence);

            AppointmentEligibilityHelper.WorkforceViolation violation = AppointmentEligibilityHelper.Classify(
                absenceHit, breakHit, companyHolidaysForOccurrence.Count > 0, withinHours);

            if (violation != AppointmentEligibilityHelper.WorkforceViolation.None)
            {
                if (!overrideAvailability)
                {
                    hardConflicts.Add(new RecurringConflictDetail { Date = occurrence, Reason = ToRecurringConflictReason(violation) });
                }
                else
                {
                    AppointmentEligibilityHelper.ThrowOrWarn(violation, overrideAvailability: true, warnings);
                }
            }

            if (warnings.Count > 0)
                warningsByOccurrence[occurrence] = warnings;
        }

        if (hardConflicts.Count > 0)
            throw new BusinessRuleException(
                ErrorCodes.RecurringConflict,
                "Neki termini u nizu se sudaraju s postojećim obavezama.",
                new { conflicts = hardConflicts });

        return warningsByOccurrence;
    }

    private static string ToRecurringConflictReason(AppointmentEligibilityHelper.WorkforceViolation violation) => violation switch
    {
        AppointmentEligibilityHelper.WorkforceViolation.EmployeeAbsent => ErrorCodes.RecurringConflictReasonRosterAbsence,
        AppointmentEligibilityHelper.WorkforceViolation.EmployeeOnBreak => ErrorCodes.RecurringConflictReasonScheduleBreak,
        AppointmentEligibilityHelper.WorkforceViolation.CompanyClosedHoliday => ErrorCodes.RecurringConflictReasonHoliday,
        _ => ErrorCodes.RecurringConflictReasonOutsideWorkingHours
    };

    /// <summary>Provjera preklapanja klijenata za /recurring — odgovara klijentskoj grani EnsureNoHardOverlap,
    /// ali nad cijelim nizom odjednom: kandidati se dohvaćaju JEDNOM za cijeli raspon, a za prvi occurrence (kronološki)
    /// s pogođenim klijentom baca se APPOINTMENT_OVERLAP (409), isto ponašanje/kod kao i za pojedinačne termine.</summary>
    private async Task EnsureNoRecurringClientOverlap(
        Guid organizationId, List<Client> clients, List<DateTimeOffset> occurrences, int durationMinutes)
    {
        List<Guid> clientIds = clients.Select(c => c.Id.GetValueOrDefault()).ToList();

        DateTimeOffset rangeFrom = occurrences[0].AddDays(-1);
        DateTimeOffset rangeTo = occurrences[^1].AddDays(1);

        List<OccupancySlot> candidateAppointments = await _schedulingOccupancyHandler.GetForClientsInRange(
            organizationId, clientIds, rangeFrom, rangeTo);

        foreach (DateTimeOffset occurrence in occurrences)
        {
            DateTimeOffset occurrenceEnd = occurrence.AddMinutes(durationMinutes);

            List<OccupancySlot> overlapping = candidateAppointments
                .Where(a => a.Overlaps(occurrence, occurrenceEnd))
                .ToList();

            foreach (Client client in clients)
            {
                bool hasOverlap = overlapping.Any(a => a.ActiveClientIds.Contains(client.Id.GetValueOrDefault()));
                if (hasOverlap)
                    throw new BusinessRuleException(ErrorCodes.AppointmentOverlap, $"Klijent {client.FirstName} {client.LastName} je već zakazan u ovom vremenskom razdoblju.");
            }
        }
    }

    public async Task<List<AppointmentScheduleCellDto>> GetSchedule(Guid organizationId, AppointmentScheduleQuery query)
    {
        List<Appointment> appointments = await _appointmentHandler.GetForSchedule(organizationId, query);
        return appointments.Select(ToScheduleCellDto).ToList();
    }

    public Task<AppointmentDto> GetById(Guid organizationId, Guid id)
    {
        return GetByIdInternal(organizationId, id);
    }

    public async Task<PagedResult<ClientAppointmentHistoryDto>> GetByClient(Guid organizationId, Guid clientId, PagedRequest request)
    {
        (List<Appointment> items, int totalCount) = await _appointmentHandler.GetByClient(organizationId, clientId, request);
        return PagedResult<ClientAppointmentHistoryDto>.Create(
            items.Select(a => ToClientHistoryDto(a, clientId)).ToList(), totalCount, request.Page, request.PageSize);
    }

    public async Task<PagedResult<AppointmentDto>> GetByEmployee(Guid organizationId, Guid employeeId, PagedRequest request)
    {
        (List<Appointment> items, int totalCount) = await _appointmentHandler.GetByEmployee(organizationId, employeeId, request);
        return PagedResult<AppointmentDto>.Create(items.Select(a => ToDto(a)).ToList(), totalCount, request.Page, request.PageSize);
    }

    public async Task<List<EmployeeAvailableSlotsDto>> GetAvailableSlots(Guid organizationId, AvailableSlotsQuery query)
    {
        // Traženi dan je kalendarski datum kako ga je klijent napisao; "danas", granice dana i sva lokalna vremena
        // (radno vrijeme, zauzeti intervali) su u efektivnoj zoni poslovnice — ne ovise o hostu ni o offsetu učitanih vrijednosti.
        OrganizationCalendar calendar = await _organizationCalendarService.GetCompanyCalendar(organizationId, query.CompanyId);
        DateTimeOffset now = DateTimeOffset.UtcNow;
        DateOnly requestedDay = CalendarDates.FromWallDate(query.Date);
        DateOnly today = calendar.LocalDate(now);

        if (requestedDay < today)
            return new List<EmployeeAvailableSlotsDto>();

        TimeSpan? minimumStart = requestedDay == today ? calendar.LocalTimeOfDay(now) + AvailableSlotLeadTime : null;

        ServiceEntity service = await LoadServiceOrThrow(organizationId, query.ServiceId);
        Company company = await _companyHandler.GetById(organizationId, query.CompanyId);
        if (company == null)
            throw new NotFoundAppException("Company", query.CompanyId);

        // Slot se ne smije ponuditi ako Create ne bi mogao proći strukturne provjere — vidi EnsureStructuralEligibility.
        if (!service.IsActive || !company.IsActive ||
            !await _serviceAvailabilityService.IsServiceAvailableAtCompany(organizationId, query.ServiceId, query.CompanyId))
            return new List<EmployeeAvailableSlotsDto>();

        List<Employee> employees = await _employeeHandler.GetForCompany(organizationId, query.CompanyId);

        if (query.EmployeeId.HasValue)
            employees = employees.Where(e => e.Id == query.EmployeeId.Value).ToList();

        // Phase M1G: isto pravilo podobnosti kao Create (EmployeeServiceEligibility — bez dodjela = sve usluge).
        employees = employees
            .Where(e => e.IsActive && EmployeeServiceEligibility.CanPerform(e.Services.Select(s => s.ServiceId).ToList(), query.ServiceId))
            .ToList();

        if (employees.Count == 0)
            return new List<EmployeeAvailableSlotsDto>();

        List<Guid> employeeIds = employees.Select(e => e.Id.GetValueOrDefault()).ToList();

        DateTimeOffset dayStart = calendar.StartOfDay(requestedDay);
        DateTimeOffset dayEnd = calendar.StartOfDay(requestedDay.AddDays(1)).AddTicks(-1);

        List<WorkingHoursTemplate> employeeTemplates = await _workingHoursTemplateHandler.GetForEmployees(organizationId, employeeIds);
        WorkingHoursTemplate companyTemplate = await _workingHoursTemplateHandler.GetForCompany(organizationId, query.CompanyId);
        List<RosterEntry> rosterEntries = await _rosterEntryHandler.GetForPeriod(organizationId, employeeIds, requestedDay, requestedDay);
        List<OccupancySlot> appointments = await _schedulingOccupancyHandler.GetForEmployeesInRange(organizationId, employeeIds, dayStart, dayEnd);
        List<ScheduleBreak> breaks = await _scheduleBreakHandler.GetForEmployeesInRange(organizationId, employeeIds, dayStart, dayEnd);
        List<CompanyHoliday> companyHolidays = await _companyHolidayHandler.GetForCompaniesInRange(
            organizationId, new List<Guid> { query.CompanyId }, requestedDay, requestedDay);

        (List<WorkingHoursCalculator.Interval> companyIntervals, _) =
            WorkingHoursCalculator.GetEffectiveCompanyIntervals(companyTemplate, companyHolidays, requestedDay);

        List<EmployeeAvailableSlotsDto> result = new List<EmployeeAvailableSlotsDto>();

        foreach (Employee employee in employees)
        {
            Guid employeeId = employee.Id.GetValueOrDefault();

            WorkingHoursTemplate employeeTemplate = employeeTemplates.FirstOrDefault(t => t.EmployeeId == employeeId);
            List<RosterEntry> rosterForEmployee = rosterEntries.Where(r => r.EmployeeId == employeeId).ToList();

            (List<WorkingHoursCalculator.Interval> employeeIntervals, _) =
                WorkingHoursCalculator.GetEffectiveEmployeeIntervals(employeeTemplate, rosterForEmployee, requestedDay);

            List<WorkingHoursCalculator.Interval> effectiveIntervals =
                WorkingHoursCalculator.IntersectIntervals(employeeIntervals, companyIntervals);

            List<(TimeSpan Start, TimeSpan End)> busy = new List<(TimeSpan Start, TimeSpan End)>();
            // Lokalni početak u zoni organizacije + trajanje (ne lokalni kraj) — interval koji prelazi ponoć ne smije se
            // "zamotati" na sljedeći dan.
            busy.AddRange(appointments
                .Where(a => a.EmployeeIds.Contains(employeeId))
                .Select(a => (calendar.LocalTimeOfDay(a.Start), calendar.LocalTimeOfDay(a.Start) + (a.End - a.Start))));
            busy.AddRange(breaks
                .Where(b => b.EmployeeId == employeeId)
                .Select(b => (calendar.LocalTimeOfDay(b.StartsAt), calendar.LocalTimeOfDay(b.StartsAt) + TimeSpan.FromMinutes(b.DurationMinutes))));

            result.Add(new EmployeeAvailableSlotsDto
            {
                EmployeeId = employeeId,
                EmployeeName = $"{employee.FirstName} {employee.LastName}",
                ColorHex = employee.ColorHex,
                Slots = GenerateSlots(effectiveIntervals, busy, service.DefaultDurationMinutes, minimumStart)
            });
        }

        return result;
    }

    /// <summary>Phase M1B — kreiranje kroz CILJNI ugovor: validacija po segmentu, konstrukcijska jezgra (N segmenata,
    /// jedinstveni Booking po klijentu, sudjelovanje po odabranom segmentu), radna snaga i preklapanja po segmentu.</summary>
    private async Task<AppointmentDto> CreateInternal(
        Guid organizationId, Guid userId, bool hasFullScope, AppointmentCreateRequest request, Guid? recurrenceGroupId)
    {
        EnsureCurrentProductLimits(request);
        await AppointmentOwnership.EnsureCallerIsEmployee(_employeeHandler, organizationId, userId, hasFullScope,
            request.Segments.SelectMany(s => s.EmployeeIds), NotOwnerMessage);
        bool overrideAvailability = request.OverrideAvailability && hasFullScope;

        List<ValidatedSegment> segments = new List<ValidatedSegment>();
        foreach (AppointmentSegmentCreateRequest segment in request.Segments)
            segments.Add(await ValidateSegment(organizationId, request.CompanyId, segment, PricingMode.WithManualOverride));

        Appointment appointment = AppointmentFactory.CreateIndividual(
            organizationId, request.CompanyId, request.Note, recurrenceGroupId, userId, DateTimeOffset.UtcNow,
            segments.Select(s => s.Plan).ToList());
        Guid appointmentId = appointment.Id.GetValueOrDefault();

        List<WarningDto> warnings = new List<WarningDto>();
        foreach (ValidatedSegment segment in segments)
        {
            warnings.AddRange(await EnsureWorkforceAvailability(
                organizationId, segment.Plan.EmployeeIds, request.CompanyId, segment.Plan.PlannedStart, segment.Plan.PlannedEnd, overrideAvailability));
        }

        // Phase M1C: ciljno stanje SVIH segmenata (i međusobni sudari) + postojeće stanje pod zaključanim subjektima, u istoj
        // transakciji kao upis — dva konkurentna Create-a za istog zaposlenika/klijenta ne mogu oba proći.
        await using (IUnitOfWork uow = await _unitOfWorkFactory.Begin())
        {
            await EnsureNoHardOverlap(uow, organizationId, segments
                .Select(segment => new HardOverlapTarget(
                    null, segment.Plan.PlannedStart, segment.Plan.PlannedEnd, segment.Plan.EmployeeIds,
                    segment.Clients, segment.Room, ResourcesOf(segment.Plan)))
                .ToList());
            await _appointmentHandler.Add(uow, appointment);
            await SyncMembershipCoverage(uow, organizationId, userId, appointment);
            await uow.CommitAsync();
        }

        AppointmentDto dto = await GetByIdInternal(organizationId, appointmentId);
        dto.Warnings = warnings;
        return dto;
    }

    /// <summary>P2 (2D) — claim na rezervaciji za SVA nova sudjelovanja termina, redom po početku segmenta (pa klijentu):
    /// interaktivna naredba, pa postavka "odbij" kod iskorištenog limita odbija cijelu naredbu. Bez članarine no-op.</summary>
    private async Task SyncMembershipCoverage(IUnitOfWork uow, Guid organizationId, Guid userId, Appointment appointment)
    {
        foreach ((Booking booking, BookingSegmentParticipation participation) in appointment.Bookings
                     .SelectMany(b => b.Participations.Select(p => (Booking: b, Participation: p)))
                     .OrderBy(x => appointment.Segments.Single(s => s.Id == x.Participation.AppointmentSegmentId).PlannedStart)
                     .ThenBy(x => x.Booking.ClientId))
        {
            await _membershipCoverage.SyncParticipation(uow, organizationId, userId, appointment, booking, participation,
                MembershipCoverageEvent.Booking, MembershipCoverageMode.Interactive);
        }
    }

    /// <summary>Zajednička implementacija za Cancel/MarkNoShow — svi AKTIVNI (Confirmed) sudionici termina prelaze u ciljni
    /// status kroz jedinu jezgru prijelaza sudjelovanja (IParticipationLifecycleService, kaskada) s JEDNIM serverskim
    /// timestampom događaja; terminalna sudjelovanja se ne diraju (D12). P1: otkazivanje termina je uvijek Business (bez
    /// politike, razlog obavezan); izostanak termina evaluira politiku po sudjelovanju i atomaran je (ako ijedan aktivni segment
    /// nije počeo → ATTENDANCE_BEFORE_START, ništa se ne mijenja). Status termina se zatim izvodi.</summary>
    private async Task<AppointmentDto> ChangeToTerminalStatus(
        Guid organizationId, Guid userId, bool hasFullScope, Guid id, BookingSetStatusRequest transition, string appointmentCancellationReason,
        Guid? singleClientPackageId = null, IReadOnlyCollection<ParticipationPackageSelection> packageSelections = null)
    {
        try
        {
            await using IUnitOfWork uow = await _unitOfWorkFactory.Begin();

            // Zaključava Appointment redak (FOR UPDATE) i čita Status pod lockom PRIJE bilo kakve provjere/mutacije —
            // sprječava utrku s konkurentnim prijelazom sudjelovanja/CompleteGroupAppointment na ISTOM terminu.
            Appointment appointment = await _appointmentHandler.GetForUpdateWithBookings(uow, organizationId, id);
            if (appointment == null)
                throw new NotFoundAppException("Appointment", id);

            // Phase M1E (zaključano): otkazivanje/bulk no-show TERMINA je operacija nad cijelim agregatom (sva aktivna
            // sudjelovanja svih segmenata) — zahtijeva appointments.write.all, own-opseg nikad.
            AppointmentOwnership.EnsureWholeAppointmentScope(hasFullScope, NotOwnerMessage);

            List<(Booking Booking, BookingSegmentParticipation Participation)> active = appointment.Bookings
                .SelectMany(b => b.Participations.Select(p => (Booking: b, Participation: p)))
                .Where(x => x.Participation.Status == ParticipationStatus.Confirmed)
                .OrderBy(x => x.Participation.Id)
                .ToList();
            bool isNoShow = transition.Status == BookingStatus.NoShow;

            // Phase M1A: termin bez ijednog Confirmed sudjelovanja koji je razriješen (Closed) nema što otkazati/označiti
            // izostankom. Djelomično izvršen termin (npr. Completed + Confirmed) JEST dopušten.
            if (appointment.Status == AppointmentStatus.Closed)
                throw new BusinessRuleException(
                    ErrorCodes.AlreadyCompleted,
                    "Termin je već razriješen (Closed) i ne može se otkazati niti označiti kao izostanak.");

            // Phase M1B/P1 (D12): bulk no-show je operacija nad AKTIVNIM sudjelovanjima — bez ijednog se odbija.
            if (isNoShow && active.Count == 0)
                throw new BusinessRuleException(
                    ErrorCodes.NoActiveParticipations, "Termin nema aktivnih sudjelovanja koja bi se mogla označiti kao izostanak.");

            DateTimeOffset eventAt = DateTimeOffset.UtcNow;

            // P1 (D3): appointment-wide izostanak je atomaran — ako ijedan aktivni segment još nije počeo, ništa se ne mijenja.
            if (isNoShow && active.Any(x => ExecutionContextResolver.ForParticipation(appointment, x.Booking, x.Participation).StartsAt > eventAt))
                throw new BusinessRuleException(
                    ErrorCodes.AttendanceBeforeStart, "Izostanak se može evidentirati tek nakon početka svih aktivnih segmenata termina.");

            // Phase M1A.1: otkazivanje TERMINA je zasebna, eksplicitna činjenica (CancelledAt/By + "AppointmentCancelled"
            // audit) — ulaz u izvođenje statusa, nikad izveden iz sudjelovanja. Bulk no-show NIJE otkazivanje termina; razlog
            // izostanka (P1) je na sudjelovanjima (NoShowReason).
            if (!isNoShow)
            {
                await AppointmentLifecycle.MarkExplicitlyCancelled(_auditLogHandler, uow, appointment, appointmentCancellationReason, userId);
                await _appointmentHandler.UpdateScalar(uow, appointment);
            }

            // Sudjelovanja se zaključavaju nakon termina, u stabilnom redoslijedu (po Id-u).
            await _participationHandler.LockForUpdate(
                uow, organizationId, appointment.Bookings.SelectMany(b => b.Participations).Select(p => p.Id.GetValueOrDefault()));

            // Phase M1E.1/P1: svako sudjelovanje se obrađuje zasebno (vlastiti StatusVersion, audit, Outbox pojava, posljedica
            // politike po početku NJEGOVOG segmenta) — isti timestamp događaja za sve.
            // P1 (D6): paket za kaznu se bira PO SUDJELOVANJU (više klijenata, različite usluge) — samo aktivna sudjelovanja
            // ovog termina; jedan ClientPackageId za cijeli termin se odbija.
            IReadOnlyDictionary<Guid, Guid> selections = ParticipationPackageSelections.Resolve(
                singleClientPackageId, packageSelections, active.Select(x => x.Participation.Id.GetValueOrDefault()).ToList());

            foreach ((Booking booking, BookingSegmentParticipation participation) in active)
                await _participationLifecycleService.ApplyCascadeTransitionInTransaction(
                    uow, organizationId, userId, appointment, booking, participation,
                    ParticipationPackageSelections.ForParticipation(transition, selections, participation.Id.GetValueOrDefault()), eventAt);

            // Phase M1A.1: status se IZVODI iz sudjelovanja + eksplicitne otkazanosti: otkazan termin bez izvršenog rada
            // (uključujući prazan termin) → Cancelled; bilo koji Completed/NoShow (uključujući bulk no-show) → Closed.
            await AppointmentLifecycle.Refresh(_appointmentHandler, _auditLogHandler, uow, organizationId, id, userId);

            // Aktivni rad termina je otkazan/izostao — preostali Waiting retci više nisu smisleni, ne promovira se.
            if (appointment.Form == AppointmentForm.Group)
                await _waitlistPromotionService.ExpireWaitingForAppointment(
                    uow, organizationId, id, userId, WaitlistExpiredReasons.AppointmentCancelled);

            await uow.CommitAsync();
        }
        catch (DbUpdateConcurrencyException)
        {
            throw new BusinessRuleException(
                ErrorCodes.ConcurrencyConflict, "Podaci su upravo promijenjeni od strane drugog zahtjeva — pokušajte ponovno.");
        }

        return await GetByIdInternal(organizationId, id);
    }

    private async Task<ServiceEntity> LoadServiceOrThrow(Guid organizationId, Guid serviceId)
    {
        ServiceEntity service = await _serviceHandler.GetById(organizationId, serviceId);
        if (service == null)
            throw new NotFoundAppException("Service", serviceId);

        return service;
    }

    /// <summary>Puni strukturni lanac podobnosti (FAZA 3): Company/Service aktivni, Service stvarno ponuđen u
    /// toj Company (ServiceCompany), Employee aktivan i eksplicitno dodijeljen i toj Company i toj usluzi. Tvrda
    /// blokada BEZ override-a — override smije zaobići samo MEKE radne-snage provjere (vidi EnsureWorkforceAvailability),
    /// nikad strukturne (vidi AppointmentEligibilityHelper domensku napomenu / spec section 20).</summary>
    private async Task EnsureStructuralEligibility(Guid organizationId, ServiceEntity service, Guid companyId, Guid employeeId)
    {
        if (!service.IsActive)
            throw new BusinessRuleException(ErrorCodes.InactiveService, $"Usluga '{service.Name}' nije aktivna.");

        Company company = await _companyHandler.GetById(organizationId, companyId);
        if (company == null)
            throw new NotFoundAppException("Company", companyId);
        if (!company.IsActive)
            throw new BusinessRuleException(ErrorCodes.InactiveCompany, $"Poslovnica '{company.Name}' nije aktivna.");

        if (!await _serviceAvailabilityService.IsServiceAvailableAtCompany(organizationId, service.Id.GetValueOrDefault(), companyId))
            throw new BusinessRuleException(
                ErrorCodes.ServiceNotAvailableAtCompany, $"Usluga '{service.Name}' nije dostupna u poslovnici '{company.Name}'.");

        Employee employee = await _employeeHandler.GetById(organizationId, employeeId);
        if (employee == null)
            throw new NotFoundAppException("Employee", employeeId);
        if (!employee.IsActive)
            throw new BusinessRuleException(ErrorCodes.InactiveEmployee, $"Zaposlenik '{employee.FirstName} {employee.LastName}' nije aktivan.");

        if (!await _employeeHandler.IsEmployeeAssignedToCompany(organizationId, employeeId, companyId))
            throw new BusinessRuleException(
                ErrorCodes.EmployeeNotAssignedToCompany, $"Zaposlenik nije dodijeljen poslovnici '{company.Name}'.");

        if (!await _employeeHandler.CanEmployeePerformService(organizationId, employeeId, service.Id.GetValueOrDefault()))
            throw new BusinessRuleException(
                ErrorCodes.EmployeeNotAssignedToService, $"Zaposlenik nije ovlašten izvoditi uslugu '{service.Name}'.");
    }

    /// <summary>Vraća null ako roomId nije zadan (prostorija je opcionalna). Baca NOT_FOUND ako prostorija ne
    /// postoji, INACTIVE_ROOM ako nije aktivna, ROOM_COMPANY_MISMATCH ako pripada drugoj poslovnici od one na
    /// koju se termin zakazuje.</summary>
    private async Task<Room> EnsureRoomExists(Guid organizationId, Guid companyId, Guid? roomId)
    {
        if (!roomId.HasValue)
            return null;

        Room room = await _roomHandler.GetById(organizationId, roomId.Value);
        if (room == null)
            throw new NotFoundAppException("Room", roomId.Value);

        if (!room.IsActive)
            throw new BusinessRuleException(ErrorCodes.InactiveRoom, $"Prostorija '{room.Name}' nije aktivna.");

        if (room.CompanyId != companyId)
            throw new BusinessRuleException(ErrorCodes.RoomCompanyMismatch, "Prostorija ne pripada odabranoj poslovnici.");

        return room;
    }

    /// <summary>Phase M1D — strukturna validacija dodjele resursa: postoji (u organizaciji), aktivan, pripada poslovnici termina
    /// (tvrda greška, ne upozorenje); Capacity &gt;= 1 jamči CHECK u bazi.</summary>
    private async Task EnsureResourceUsable(Guid organizationId, Guid companyId, Guid resourceId)
    {
        Resource resource = await _resourceHandler.GetById(organizationId, resourceId);
        if (resource == null)
            throw new NotFoundAppException("Resource", resourceId);
        if (!resource.IsActive)
            throw new BusinessRuleException(ErrorCodes.InactiveResource, $"Resurs '{resource.Name}' nije aktivan.");
        if (resource.CompanyId != companyId)
            throw new BusinessRuleException(ErrorCodes.ResourceCompanyMismatch, "Resurs ne pripada odabranoj poslovnici.");
    }

    /// <summary>Uz postojanje i tenant provjeru, sad dodatno zahtijeva IsActive/!IsAnonymized za svakog klijenta
    /// (FAZA 3) — prije ovoga se anonimizirani/neaktivni klijent tiho mogao dodati na novi termin.</summary>
    private async Task<List<Client>> EnsureClientsExist(Guid organizationId, List<Guid> clientIds)
    {
        List<Guid> distinctIds = clientIds.Distinct().ToList();
        List<Client> clients = await _clientHandler.GetByIds(organizationId, distinctIds);

        if (clients.Count != distinctIds.Count)
        {
            HashSet<Guid> foundIds = clients.Select(c => c.Id.GetValueOrDefault()).ToHashSet();
            Guid missingId = distinctIds.First(id => !foundIds.Contains(id));
            throw new NotFoundAppException("Client", missingId);
        }

        foreach (Client client in clients)
        {
            if (client.IsAnonymized)
                throw new BusinessRuleException(ErrorCodes.ClientAnonymized, $"Klijent {client.FirstName} {client.LastName} je anonimiziran.");
            if (!client.IsActive)
                throw new BusinessRuleException(ErrorCodes.InactiveClient, $"Klijent {client.FirstName} {client.LastName} nije aktivan.");
        }

        return clients;
    }

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

    /// <summary>Ciljno stanje jednog segmenta za tvrdu provjeru: <paramref name="SegmentId"/> = postojeći segment koji se
    /// prepisuje (isključuje se SAMO on; zahtjev je njegovo CIJELO ciljno stanje) ili null za novi. Osobe u prostoriji = svi
    /// zaposlenici + svi klijenti koji će segment zauzimati; resursi = sve dodjele segmenta.</summary>
    private sealed record HardOverlapTarget(
        Guid? SegmentId, DateTimeOffset PlannedStart, DateTimeOffset PlannedEnd, IReadOnlyList<Guid> EmployeeIds, IReadOnlyList<Client> Clients,
        Room Room, IReadOnlyList<ResourceClaim> Resources = null);

    /// <summary>Koriste svi upisi rasporeda (Create, CompleteNow, segmentne naredbe, recurring) — tvrde invarijante
    /// (zaposlenik, klijent: APPOINTMENT_OVERLAP; prostorija: ROOM_CAPACITY_EXCEEDED; resurs: RESOURCE_CAPACITY_EXCEEDED) se
    /// NIKAD ne mogu zaobići s OverrideAvailability.</summary>
    /// <remarks>Phase M1C/M1D: segmentno i konkurentno sigurno — poziva se UNUTAR transakcije upisa kao PRVI korak (zaključava
    /// subjekte rasporeda, vidi SchedulingConflictGuard/SchedulingLockOrder), provjerava ciljno stanje u memoriji pa postojeće
    /// segmente u bazi, isključujući samo segmente koji se prepisuju. Prostorija je kapacitet u osobama (vremenski raslojen),
    /// ne "bilo koji preklapajući termin".</remarks>
    private Task EnsureNoHardOverlap(IUnitOfWork uow, Guid organizationId, IReadOnlyList<HardOverlapTarget> targets)
    {
        Dictionary<Guid, string> clientNames = targets.SelectMany(t => t.Clients)
            .GroupBy(c => c.Id.GetValueOrDefault())
            .ToDictionary(g => g.Key, g => $"{g.First().FirstName} {g.First().LastName}");
        List<SegmentClaim> claims = targets
            .Select(target =>
            {
                List<Guid> employees = target.EmployeeIds.Distinct().ToList();
                List<Guid> clients = target.Clients.Select(c => c.Id.GetValueOrDefault()).Distinct().ToList();
                return new SegmentClaim(target.SegmentId, target.PlannedStart, target.PlannedEnd, employees, clients)
                {
                    RoomId = target.Room?.Id,
                    RoomPeople = RoomPeopleCount.Of(employees.Count, clients.Count),
                    Resources = target.Resources ?? Array.Empty<ResourceClaim>()
                };
            })
            .ToList();

        return SchedulingConflictGuard.Claim(
            _schedulingOccupancyHandler, uow, organizationId, claims,
            clientId => clientNames.TryGetValue(clientId, out string name) ? name : null);
    }

    private static List<ResourceClaim> ResourcesOf(SegmentPlan plan) =>
        (plan.Resources ?? Array.Empty<SegmentResourcePlan>()).Select(r => new ResourceClaim(r.ResourceId, r.QuantityRequired)).ToList();

    /// <summary>Zamjenjuje staro BuildWorkingHoursWarning — sada TVRDA blokada (throw) za sve četiri "meke"
    /// radne-snage kategorije (odsutnost/pauza/praznik/izvan-radnog-vremena) OSIM kad je overrideAvailability=true
    /// (već provjereno kod pozivatelja da ima appointments.write.all), kad se umjesto bacanja vraća WarningDto
    /// lista (vidljivost bez blokade — isto ponašanje kao prije ovog zahvata). Koriste svi upisi rasporeda osim
    /// CompleteNow za početak u prošlosti (retroaktivno evidentiranje odrađenog, ne planiranje unaprijed; provjereno kod
    /// pozivatelja). Prijelaz sudjelovanja u Completed ništa ne raspoređuje pa ne provjerava radnu snagu.</summary>
    /// <remarks>Phase M1B: po SEGMENTU (njegov raspon) i za svakog zaposlenika segmenta.</remarks>
    private async Task<List<WarningDto>> EnsureWorkforceAvailability(
        Guid organizationId, IReadOnlyList<Guid> employeeIds, Guid companyId, DateTimeOffset plannedStart, DateTimeOffset plannedEnd,
        bool overrideAvailability)
    {
        List<WarningDto> warnings = new List<WarningDto>();
        foreach (Guid employeeId in employeeIds)
            warnings.AddRange(await EnsureEmployeeWorkforceAvailability(
                organizationId, employeeId, companyId, plannedStart, (int)(plannedEnd - plannedStart).TotalMinutes, overrideAvailability));
        return warnings;
    }

    private async Task<List<WarningDto>> EnsureEmployeeWorkforceAvailability(
        Guid organizationId, Guid employeeId, Guid companyId, DateTimeOffset startsAt, int durationMinutes, bool overrideAvailability)
    {
        // Datum i lokalno vrijeme termina u efektivnoj zoni poslovnice (F-19: prije je DateTime -> DateTimeOffset
        // konverzija koristila offset hosta pa je prvi dan odsutnosti "nestajao" na ne-UTC hostu).
        OrganizationCalendar calendar = await _organizationCalendarService.GetCompanyCalendar(organizationId, companyId);
        DateOnly localDate = calendar.LocalDate(startsAt);

        WorkingHoursTemplate employeeTemplate = await _workingHoursTemplateHandler.GetForEmployee(organizationId, employeeId);
        WorkingHoursTemplate companyTemplate = await _workingHoursTemplateHandler.GetForCompany(organizationId, companyId);
        List<RosterEntry> rosterEntriesForDate = await _rosterEntryHandler.GetForPeriod(
            organizationId, new List<Guid> { employeeId }, localDate, localDate);
        List<CompanyHoliday> companyHolidaysForDate = await _companyHolidayHandler.GetForCompaniesInRange(
            organizationId, new List<Guid> { companyId }, localDate, localDate);
        List<ScheduleBreak> breakOverlaps = await _scheduleBreakHandler.GetOverlappingForEmployee(
            organizationId, employeeId, startsAt, durationMinutes, excludeId: null);

        bool absenceHit = rosterEntriesForDate.Any(e =>
            e.RosterType.IsAbsence && e.DateFrom <= localDate && (e.DateTo == null || localDate <= e.DateTo.Value));
        bool breakHit = breakOverlaps.Count > 0;

        List<RosterEntry> nonAbsenceEntries = rosterEntriesForDate.Where(e => !e.RosterType.IsAbsence).ToList();
        bool withinHours = absenceHit || IsWithinWorkingHours(
            employeeTemplate, companyTemplate, nonAbsenceEntries, localDate, calendar.LocalTimeOfDay(startsAt), durationMinutes, companyHolidaysForDate);

        AppointmentEligibilityHelper.WorkforceViolation violation = AppointmentEligibilityHelper.Classify(
            absenceHit, breakHit, companyHolidaysForDate.Count > 0, withinHours);

        List<WarningDto> warnings = new List<WarningDto>();
        AppointmentEligibilityHelper.ThrowOrWarn(violation, overrideAvailability, warnings);
        return warnings;
    }

    /// <summary>Čista provjera dijeljena s EnsureNoRecurringConflicts (batch grana) — rosterEntriesForDate/
    /// companyHolidaysForDate moraju sadržavati SAMO redove relevantne za TOČNO taj datum (apsencija čiji raspon
    /// ga pokriva, work-redovi s DateFrom==taj datum, praznik čiji Date==taj datum), ne cijeli raspon niza.
    /// localDate/localStart su u zoni organizacije (vidi OrganizationCalendar); kraj = početak + trajanje, pa termin koji
    /// prelazi ponoć nikad nije "unutar" radnog vremena jednog dana.</summary>
    private static bool IsWithinWorkingHours(
        WorkingHoursTemplate employeeTemplate, WorkingHoursTemplate companyTemplate, List<RosterEntry> rosterEntriesForDate,
        DateOnly localDate, TimeSpan localStart, int durationMinutes, List<CompanyHoliday> companyHolidaysForDate)
    {
        TimeSpan start = localStart;
        TimeSpan end = start + TimeSpan.FromMinutes(durationMinutes);

        (List<WorkingHoursCalculator.Interval> employeeIntervals, _) =
            WorkingHoursCalculator.GetEffectiveEmployeeIntervals(employeeTemplate, rosterEntriesForDate, localDate);
        (List<WorkingHoursCalculator.Interval> companyIntervals, _) =
            WorkingHoursCalculator.GetEffectiveCompanyIntervals(companyTemplate, companyHolidaysForDate, localDate);

        return WorkingHoursCalculator.IsWithinIntervals(employeeIntervals, start, end)
            && WorkingHoursCalculator.IsWithinIntervals(companyIntervals, start, end);
    }

    private static readonly TimeSpan AvailableSlotStep = TimeSpan.FromMinutes(15);
    private static readonly TimeSpan AvailableSlotLeadTime = TimeSpan.FromMinutes(5);

    /// <summary>Kandidati kreću na apsolutnom gridu :00/:15/:30/:45 (dosljedno vizualnom snapu na rasporedu), unutar
    /// svakog slobodnog prozora, izbacujući svaki koji preklapa nešto zauzeto (termin ili pauza istog trenera).</summary>
    private static List<AvailableSlotDto> GenerateSlots(
        List<WorkingHoursCalculator.Interval> freeIntervals, List<(TimeSpan Start, TimeSpan End)> busy, int durationMinutes,
        TimeSpan? minimumStart)
    {
        List<AvailableSlotDto> slots = new List<AvailableSlotDto>();
        TimeSpan duration = TimeSpan.FromMinutes(durationMinutes);

        foreach (WorkingHoursCalculator.Interval interval in freeIntervals)
        {
            TimeSpan candidateStart = RoundUpToStep(interval.Start, AvailableSlotStep);

            if (minimumStart.HasValue)
            {
                TimeSpan roundedMinimumStart = RoundUpToStep(minimumStart.Value, AvailableSlotStep);
                if (roundedMinimumStart > candidateStart)
                    candidateStart = roundedMinimumStart;
            }

            while (candidateStart + duration <= interval.End)
            {
                TimeSpan candidateEnd = candidateStart + duration;
                bool overlapsBusy = busy.Any(b => SchedulingInterval.Overlaps(candidateStart, candidateEnd, b.Start, b.End));

                if (!overlapsBusy)
                    slots.Add(new AvailableSlotDto { Start = candidateStart, End = candidateEnd });

                candidateStart += AvailableSlotStep;
            }
        }

        return slots;
    }

    private static TimeSpan RoundUpToStep(TimeSpan value, TimeSpan step)
    {
        long remainder = value.Ticks % step.Ticks;
        return remainder == 0 ? value : TimeSpan.FromTicks(value.Ticks + (step.Ticks - remainder));
    }

    private async Task<AppointmentDto> GetByIdInternal(Guid organizationId, Guid id)
    {
        Appointment appointment = await _appointmentHandler.GetById(organizationId, id);
        if (appointment == null)
            throw new NotFoundAppException("Appointment", id);

        return ToDto(appointment);
    }

    private static AppointmentScheduleCellDto ToScheduleCellDto(Appointment a)
    {
        bool isGroup = a.Form == AppointmentForm.Group;
        List<Client> clients = a.Bookings.Where(b => b.Client != null).Select(b => b.Client).ToList();
        AppointmentRange range = AppointmentRange.Of(a);
        return new AppointmentScheduleCellDto
        {
            Id = a.Id.GetValueOrDefault(),
            PlannedStart = range.PlannedStart,
            PlannedEnd = range.PlannedEnd,
            Segments = AppointmentSegmentReadModel.ToDtos(a),
            CompanyId = a.CompanyId,
            CompanyName = a.Company?.Name,
            ClientNames = clients.Select(c => $"{c.FirstName} {c.LastName}").ToList(),
            ClientIds = clients.Select(c => c.Id.GetValueOrDefault()).ToList(),
            Status = a.Status,
            IsCancelled = a.Status == AppointmentStatus.Cancelled,
            Form = a.Form,
            GroupId = a.GroupId,
            GroupName = isGroup ? a.Group?.Name : null,
            AttendanceCount = isGroup ? a.Bookings.SelectMany(b => b.Participations).Count(p => p.Status == ParticipationStatus.Completed) : (int?)null,
            ExpectedCount = isGroup ? a.Group?.Members.Count(m => m.IsActive) : (int?)null
        };
    }

    /// <summary>Klijent-povijest projekcija (GetByClient) — namjerno NE vraća a.Bookings (ostali klijenti na
    /// istom Appointmentu, npr. grupni/duo termin). Klijent koji gleda vlastitu povijest smije vidjeti samo
    /// vlastiti Booking; roster/admin prikaz cijelog termina i dalje ide preko ToDto/GetById. Amount/PaymentMethod/
    /// IsPaid ostaju Appointment-razina (vidi domensku napomenu na Booking.cs) — dijele ih svi Bookinzi termina,
    /// mixed plaćanje po klijentu trenutno nije podržano.</summary>
    private static ClientAppointmentHistoryDto ToClientHistoryDto(Appointment a, Guid clientId)
    {
        Booking booking = a.Bookings.First(b => b.ClientId == clientId);
        BookingCommercialSummary commercial = BookingCommercialSummary.Of(booking);
        AppointmentRange range = AppointmentRange.Of(a);
        PackageCoverageView coverage = PackageConsumptions.CoverageOfBooking(booking, a.Form);

        return new ClientAppointmentHistoryDto
        {
            Id = a.Id.GetValueOrDefault(),
            Form = a.Form,
            PlannedStart = range.PlannedStart,
            PlannedEnd = range.PlannedEnd,
            Segments = AppointmentSegmentReadModel.ToDtos(a),
            CompanyId = a.CompanyId,
            CompanyName = a.Company?.Name,
            Status = a.Status,
            GroupId = a.GroupId,
            GroupName = a.Group?.Name,
            Amount = commercial.FinalPrice,
            PaidAmount = commercial.MonetarySettled,
            OutstandingAmount = commercial.Outstanding,
            SurplusAmount = commercial.Surplus,
            IsPaid = commercial.FullySettled,
            BookingId = booking.Id.GetValueOrDefault(),
            BookingStatus = BookingSummary.StatusOf(booking),
            ClientPackageId = coverage.ClientPackageId,
            CoverageType = coverage.CoverageType,
            PackageCoverageApplied = coverage.PackageCoverageApplied,
            PackageCoverageReturned = coverage.PackageCoverageReturned,
            BookingNote = booking.Note,
            BookingCancellationReason = BookingSummary.Agreed(booking, p => p.CancellationReason)
        };
    }

    private static AppointmentDto ToDto(Appointment a)
    {
        // Phase M1B: termin = izvedeni raspon + segmenti + Bookinzi; plosnata polja su privremena jednosegmentna projekcija.
        AppointmentRange range = AppointmentRange.Of(a);
        return new AppointmentDto
        {
            Id = a.Id.GetValueOrDefault(),
            Form = a.Form,
            PlannedStart = range.PlannedStart,
            PlannedEnd = range.PlannedEnd,
            Segments = AppointmentSegmentReadModel.ToDtos(a),
            CompanyId = a.CompanyId,
            CompanyName = a.Company?.Name,
            Status = a.Status,
            Note = a.Note,
            CancellationReason = a.CancellationReason,
            CancelledAt = a.CancelledAt,
            ClosedOutAt = a.ClosedOutAt,
            GroupId = a.GroupId,
            GroupName = a.Group?.Name,
            RecurrenceGroupId = a.RecurrenceGroupId,
            Bookings = a.Bookings
                .Select(b => BookingReadModel.ToDto(b, a.Form, b.Client != null ? $"{b.Client.FirstName} {b.Client.LastName}" : null))
                .ToList(),
            CreatedAt = a.CreatedAt,
            CreatedBy = a.CreatedBy,
            UpdatedAt = a.UpdatedAt,
            UpdatedBy = a.UpdatedBy
        };
    }
}
