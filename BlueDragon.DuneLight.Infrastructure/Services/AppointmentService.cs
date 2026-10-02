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
    private readonly IOutboxWriter _outboxWriter;
    private readonly IUnitOfWorkFactory _unitOfWorkFactory;

    private readonly IOrganizationCalendarService _organizationCalendarService;

    private readonly IOrganizationSettingsService _organizationSettingsService;

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
        IOutboxWriter outboxWriter,
        IUnitOfWorkFactory unitOfWorkFactory,
        IOrganizationCalendarService organizationCalendarService,
        IOrganizationSettingsService organizationSettingsService)
    {
        _organizationCalendarService = organizationCalendarService;
        _organizationSettingsService = organizationSettingsService;
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

    /// <summary>PRIVREMENA KOMPATIBILNOST: plosnati (jednosegmentni) ugovor se na granici prevodi u ciljni ugovor.</summary>
    public Task<AppointmentDto> Create(Guid organizationId, Guid userId, bool hasFullScope, AppointmentSingleSegmentRequest request)
    {
        return CreateInternal(organizationId, userId, hasFullScope, ToTargetRequest(request), recurrenceGroupId: null);
    }

    /// <summary>Plosnati ugovor → ciljni: jedan segment (usluga, početak, jedan zaposlenik, prostorija), svaki klijent je
    /// sudionik tog segmenta s istim ručnim iznosom (ako je zadan). Kraj = zadano trajanje usluge.</summary>
    private static AppointmentCreateRequest ToTargetRequest(AppointmentSingleSegmentRequest request) => new()
    {
        CompanyId = request.CompanyId,
        Note = request.Note,
        OverrideAvailability = request.OverrideAvailability,
        Segments = new List<AppointmentSegmentCreateRequest>
        {
            new()
            {
                ServiceId = request.ServiceId,
                PlannedStart = request.StartsAt,
                PlannedEnd = null,
                EmployeeIds = new List<Guid> { request.EmployeeId },
                RoomId = request.RoomId,
                Participants = request.ClientIds.Distinct()
                    .Select(clientId => new AppointmentParticipantCreateRequest { ClientId = clientId, Amount = request.Amount })
                    .ToList()
            }
        }
    };

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

    /// <summary>Po segmentu: točno jedan zaposlenik (MULTI_EMPLOYEE_NOT_SUPPORTED — atribucija cijene/provizije više
    /// zaposlenika unutar segmenta je otvorena; ograničenje proizvoda, ne sheme), ispravni resursi (količina &gt; 0, bez
    /// duplikata — ne spajaju se tiho), sudionici bez duplikata. Kod kreiranja termina svaki segment ima barem jednog
    /// sudionika; segment dodan postojećem terminu smije biti bez sudionika (sesija bez klijenata je valjana).</summary>
    private static void EnsureSegmentProductLimits(AppointmentSegmentCreateRequest segment, bool requireParticipants)
    {
        if (segment == null)
            throw new ValidationAppException("Segment je obavezan.");
        if (segment.EmployeeIds == null || segment.EmployeeIds.Distinct().Count() != 1)
            throw new ValidationAppException(ErrorCodes.MultiEmployeeNotSupported,
                "Segment trenutno mora imati točno jednog zaposlenika.");
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
        List<Guid> employeeIds = segment.EmployeeIds.Distinct().ToList();
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

        ResolvePriceResponse resolvedPrice = await ResolveServicePrice(organizationId, segment.ServiceId, companyId, segment.PlannedStart);
        List<ParticipantPlan> participants = segment.Participants
            .Select(p => new ParticipantPlan(p.ClientId, pricingMode == PricingMode.SuggestedOnly
                ? BookingPricing.AtSuggested(resolvedPrice)
                : BookingPricing.FromResolution(resolvedPrice, p.Amount)))
            .ToList();

        return new ValidatedSegment(
            new SegmentPlan(segment.ServiceId, segment.PlannedStart, plannedEnd, employeeIds, segment.RoomId, participants, resources),
            service, room, clients);
    }

    /// <summary>/recurring namjerno ignorira ručni iznos (svaki occurrence po svojoj predloženoj cijeni).</summary>
    private enum PricingMode
    {
        WithManualOverride,
        SuggestedOnly
    }

    /// <summary>"Upiši odrađeno" — PRIVREMENA KOMPATIBILNOST (plosnati jednosegmentni ugovor + Settlements po klijentu).
    /// Phase M1B: termin se gradi kroz ciljnu konstrukcijsku jezgru (jedan segment, sudjelovanja odmah Completed); segment
    /// se razrješava na ovoj granici i dalje adresira eksplicitno.</summary>
    public async Task<AppointmentDto> CompleteNew(Guid organizationId, Guid userId, bool hasFullScope, AppointmentCompleteRequest request)
    {
        AppointmentCreateRequest target = ToTargetRequest(request);
        EnsureCurrentProductLimits(target);
        await AppointmentOwnership.EnsureCallerIsEmployee(_employeeHandler, organizationId, userId, hasFullScope,
            target.Segments.SelectMany(x => x.EmployeeIds), NotOwnerMessage);
        bool overrideAvailability = request.OverrideAvailability && hasFullScope;

        AppointmentSegmentCreateRequest segmentRequest = target.Segments[0];
        ValidatedSegment validated = await ValidateSegment(organizationId, request.CompanyId, segmentRequest, PricingMode.WithManualOverride);
        List<Client> clients = validated.Clients;
        Room room = validated.Room;
        ResolvePriceResponse resolvedPrice = await ResolveServicePrice(organizationId, request.ServiceId, request.CompanyId, request.StartsAt);

        Dictionary<Guid, AppointmentClientSettlement> settlementByClient = await ValidateSettlements(
            organizationId, clients.Select(c => c.Id.GetValueOrDefault()).ToList(), request.ServiceId, request.CompanyId, request.StartsAt, request.Settlements);

        // Cijena svakog sudionika dolazi iz NJEGOVOG settlementa (ne iz plosnatog Amount).
        SegmentPlan plan = validated.Plan with
        {
            Participants = validated.Plan.Participants
                .Select(p => new ParticipantPlan(p.ClientId, BookingPricing.FromResolution(resolvedPrice, settlementByClient[p.ClientId].Amount)))
                .ToList()
        };

        Appointment appointment = AppointmentFactory.CreateIndividual(
            organizationId, request.CompanyId, request.Note, recurrenceGroupId: null, userId, DateTimeOffset.UtcNow,
            new[] { plan }, ParticipationStatus.Completed);
        Guid appointmentId = appointment.Id.GetValueOrDefault();
        AppointmentSegment executionSegment = LegacySingleSegment.Resolve(appointment);

        Dictionary<Guid, Guid> packageByClient = new Dictionary<Guid, Guid>();
        List<(Booking Booking, BookingSegmentParticipation Participation, PaymentMethod Method, decimal Amount)> pendingPayments =
            new List<(Booking, BookingSegmentParticipation, PaymentMethod, decimal)>();

        foreach (Booking booking in appointment.Bookings)
        {
            AppointmentClientSettlement settlement = settlementByClient[booking.ClientId];
            BookingSegmentParticipation participation = BookingParticipations.OnSegment(booking, executionSegment);
            bool hasPackage = settlement.ClientPackageId.HasValue;

            if (hasPackage)
                packageByClient[booking.ClientId] = settlement.ClientPackageId.GetValueOrDefault();

            // Paket namiruje obvezu bez Paymenta (vidi Payment.cs/spec section 3/40) — monetarni Payment se
            // stvara samo bez paketa, uz zatraženu metodu, IsPaid=true i stvaran pozitivan iznos.
            if (!hasPackage && settlement.PaymentMethod.HasValue && settlement.IsPaid && participation.Amount > 0m)
                pendingPayments.Add((booking, participation, settlement.PaymentMethod.Value, participation.Amount));
        }

        // Phase M1A: termin nastaje kao Scheduled, a početni status mu se IZVODI iz upravo stvorenih (Completed) sudjelovanja
        // → Closed. Nema "Completed" termina; status nikad nije zasebna odluka.
        appointment.Status = AppointmentLifecycle.Derive(appointment);

        // CompleteNew loguje odrađeno — provjera radne-snage dostupnosti vrijedi samo ako je početak u budućnosti
        // (zakazuje se i odmah naplaćuje); za prošlost je ovo evidentiranje stvarnosti, ne planiranje (vidi FAZA 2).
        List<WarningDto> warnings = new List<WarningDto>();
        if (plan.PlannedStart > DateTimeOffset.UtcNow)
            warnings.AddRange(await EnsureWorkforceAvailability(
                organizationId, plan.EmployeeIds, request.CompanyId, plan.PlannedStart, plan.PlannedEnd, overrideAvailability));

        try
        {
            await using IUnitOfWork uow = await _unitOfWorkFactory.Begin();

            // Phase M1C: tvrde invarijante pod zaključanim subjektima, PRVO u transakciji.
            await EnsureNoHardOverlap(uow, organizationId, new[]
            {
                new HardOverlapTarget(null, plan.PlannedStart, plan.PlannedEnd, plan.EmployeeIds, clients, room, ResourcesOf(plan))
            });

            await _appointmentHandler.Add(uow, appointment);

            foreach (KeyValuePair<Guid, Guid> kvp in packageByClient)
            {
                Booking packageBooking = appointment.Bookings.First(b => b.ClientId == kvp.Key);
                BookingSegmentParticipation packageParticipation = BookingParticipations.OnSegment(packageBooking, executionSegment);
                ParticipationExecutionContext packageExecution = ExecutionContextResolver.ForParticipation(appointment, packageBooking, packageParticipation);

                // Phase D3B3A: potrošnja paketa = PackageConsumption na sudjelovanju (ledger), valjanost na datum usluge.
                await _packageConsumptionLedgerService.Consume(
                    uow, organizationId, userId, packageParticipation, packageExecution, kvp.Value, BookingStatus.Completed);

                await _auditLogHandler.Add(uow, new AppointmentAuditLog
                {
                    Id = Guid.NewGuid(),
                    AppointmentId = appointmentId,
                    BookingId = packageBooking.Id,
                    BookingSegmentParticipationId = packageParticipation.Id,
                    ChangeType = "BookingPackageCoverageApplied",
                    OldValue = null,
                    NewValue = kvp.Value.ToString(),
                    ChangedAt = DateTimeOffset.UtcNow,
                    ChangedBy = userId
                });
            }

            // Payment ide TEK nakon _appointmentHandler.Add (FK payments.booking_id) — Booking.Id je već
            // poznat (dodijeljen prije Add), pa je isti in-memory objekt (sad persistiran) siguran za referencu.
            foreach ((Booking booking, BookingSegmentParticipation participation, PaymentMethod method, decimal amount) in pendingPayments)
                await _paymentLedgerService.RecordPayment(
                    uow, organizationId, userId, appointment.CompanyId, booking, participation, method, amount, note: null, isCheckInGenerated: true);

            // Provizija se zarađuje ISTOM transakcijom kao completion — svaki upravo odrađen Booking je jedan
            // izvor (vidi ICommissionLedgerService, spec section 27/28).
            foreach (Booking booking in appointment.Bookings)
            {
                BookingSegmentParticipation participation = BookingParticipations.OnSegment(booking, executionSegment);
                await _commissionLedgerService.GenerateForIndividualServiceCompletion(
                    uow, organizationId, ExecutionContextResolver.ForParticipation(appointment, booking, participation), participation);
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

    public async Task<AppointmentDto> CompleteExisting(Guid organizationId, Guid userId, bool hasFullScope, Guid id, AppointmentCompleteRequest request)
    {
        Appointment appointment = await _appointmentHandler.GetByIdLight(organizationId, id);
        if (appointment == null)
            throw new NotFoundAppException("Appointment", id);

        // Ovaj put (ClientIds/Settlements popis koji reconcilea Booking retke preko UpdateWithBookings — hard-delete
        // izbačenih, ali SAMO ako su još Confirmed bez povijesti, vidi tamo) pretpostavlja Form=Individual — za
        // Form=Group to bi netočno restrukturiralo Bookinge koji već postoje po GroupMemberima (vidi
        // GroupService.GenerateAppointments/AddMember). Grupni termin se zatvara kroz
        // IAppointmentService.CompleteGroupAppointment, koji ne dira Booking retke.
        if (appointment.Form != AppointmentForm.Individual)
            throw new ValidationAppException(
                "Grupni termin se odrađuje kroz complete-group, ne kroz complete-existing (naplata je po klijentu/Bookingu, ne po popisu klijenata termina).");

        // Phase M1A: "već odrađen" = termin je Closed (nijedno sudjelovanje nije Confirmed, a nije sve otkazano). Ponovni
        // completion ide tek nakon korekcije sudjelovanja (koja termin automatski vraća u Scheduled).
        if (appointment.Status == AppointmentStatus.Closed)
            throw new BusinessRuleException(ErrorCodes.AlreadyCompleted, "Termin je već označen kao odrađen.");

        // LEGACY (Phase M1E): plosnati ugovor completiona adresira termin — samo za jednosegmentni termin (višesegmentni:
        // SEGMENT_SELECTION_REQUIRED, ciljni put je ParticipationId); vlasništvo slijedi taj segment.
        AppointmentSegment legacySegment = LegacySingleSegment.Resolve(appointment);
        await AppointmentOwnership.EnsureCallerOwnsSegments(_employeeHandler, organizationId, userId, hasFullScope, new[] { legacySegment }, NotOwnerMessage);

        ServiceEntity service = await LoadServiceOrThrow(organizationId, request.ServiceId);
        await EnsureStructuralEligibility(organizationId, service, request.CompanyId, request.EmployeeId);
        Room room = await EnsureRoomExists(organizationId, request.CompanyId, request.RoomId);
        List<Client> clients = await EnsureClientsExist(organizationId, request.ClientIds);

        ResolvePriceResponse resolvedPrice = await ResolveServicePrice(organizationId, request.ServiceId, request.CompanyId, request.StartsAt);

        Dictionary<Guid, AppointmentClientSettlement> settlementByClient = await ValidateSettlements(
            organizationId, clients.Select(c => c.Id.GetValueOrDefault()).ToList(), request.ServiceId, request.CompanyId, request.StartsAt, request.Settlements);

        DateTimeOffset plannedEnd = request.StartsAt.AddMinutes(service.DefaultDurationMinutes);
        List<Guid> employeeIds = new List<Guid> { request.EmployeeId };
        Guid? completedSegmentId = legacySegment.Id;
        SegmentSnapshot.State validatedSegment = SegmentSnapshot.Capture(
            appointment, legacySegment, await _schedulingOccupancyHandler.GetSegmentResources(legacySegment.Id.GetValueOrDefault()));

        try
        {
            await using IUnitOfWork uow = await _unitOfWorkFactory.Begin();

            // Phase M1C: prepisuje se (jedini) segment — isključuje se SAMO on; subjekti se zaključavaju PRIJE Appointment locka.
            await EnsureNoHardOverlap(uow, organizationId, new[]
            {
                new HardOverlapTarget(completedSegmentId, request.StartsAt, plannedEnd, employeeIds, clients, room,
                    await _schedulingOccupancyHandler.GetSegmentResources(uow, completedSegmentId.GetValueOrDefault()))
            });
            // Phase M1E: segment je prepisan iz zahtjeva; iz PROČITANOG stanja potječu samo resursi — pod Appointment lockom
            // se potvrđuje da ih segmentna naredba u međuvremenu nije promijenila.
            await SegmentSnapshot.VerifyUnderLock(_appointmentHandler, uow, organizationId, id, new[] { validatedSegment }, includeParticipants: false);

            // Zaključava Appointment redak (FOR UPDATE) i ponovno čita Status prije mutacije — sprječava utrku s
            // konkurentnim drugim completion/cancel zahtjevom na ISTOM terminu (drugi zahtjev čeka na lock pa vidi
            // svježe stanje nakon commita prvog, vidi spec section 8-11). Zamjenjuje pred-transakcijski appointment
            // (GetByIdLight iznad, koji je poslužio samo za brzu Form/ownership/AlreadyCompleted provjeru).
            appointment = await _appointmentHandler.GetForUpdate(uow, organizationId, id);
            if (appointment == null)
                throw new NotFoundAppException("Appointment", id);
            if (appointment.Status == AppointmentStatus.Closed)
                throw new BusinessRuleException(ErrorCodes.AlreadyCompleted, "Termin je već označen kao odrađen.");

            // Segment se razrješava na granici ovog kompatibilnog ugovora i mijenja eksplicitnim segmentnim operacijama.
            AppointmentSegment executionSegment = LegacySingleSegment.Resolve(appointment);
            DateTimeOffset now = DateTimeOffset.UtcNow;
            SegmentMutator.ChangeService(executionSegment, request.ServiceId, now);
            SegmentMutator.ChangeTime(executionSegment, request.StartsAt, plannedEnd, now);
            SegmentMutator.ChangeRoom(executionSegment, request.RoomId, now);
            SegmentMutator.AssignEmployees(executionSegment, employeeIds, now);
            appointment.CompanyId = request.CompanyId;
            appointment.Note = request.Note;
            appointment.UpdatedAt = DateTimeOffset.UtcNow;
            appointment.UpdatedBy = userId;

            // Novi/preživjeli retci dobivaju privremeno BookingPricing.Zero (isto kao prije: 0/0/false) — stvarna cijena
            // svakog zatraženog klijenta se postavlja niže, po njegovom settlementu.
            await _appointmentHandler.UpdateWithBookings(uow, appointment, executionSegment, request.ClientIds.Distinct().ToList(), BookingPricing.Zero);

            List<Booking> bookingRows = await _appointmentHandler.GetBookings(uow, organizationId, id, request.ClientIds.Distinct().ToList());

            // Phase M0: completion adresira sudjelovanje svakog Bookinga NA IZVRŠNOM SEGMENTU (razriješenom na granici gore)
            // — ne "jedino sudjelovanje Bookinga". Sudjelovanja se zaključavaju nakon termina, stabilnim redoslijedom
            // (statusi su svježi: svaki prijelaz statusa prvo zaključava ovaj termin).
            List<(Booking Booking, BookingSegmentParticipation Participation)> rows = bookingRows
                .Select(b => (b, BookingParticipations.OnSegment(b, executionSegment)))
                .ToList();
            await _participationHandler.LockForUpdate(uow, organizationId, rows.Select(r => r.Participation.Id.GetValueOrDefault()));

            foreach ((Booking bookingRow, BookingSegmentParticipation participation) in rows)
            {
                // appointment je gore već postavljen na zatraženu uslugu/trenera/vrijeme — kontekst čita te vrijednosti.
                ParticipationExecutionContext execution = ExecutionContextResolver.ForParticipation(appointment, bookingRow, participation);
                AppointmentClientSettlement settlement = settlementByClient[bookingRow.ClientId];
                bool hasPackage = settlement.ClientPackageId.HasValue;
                BookingPricing pricing = BookingPricing.FromResolution(resolvedPrice, settlement.Amount);
                decimal bookingAmount = pricing.Amount;
                decimal currentAmount = participation.Amount;

                if (currentAmount != bookingAmount)
                    await LogAmountChangeInTransaction(uow, id, bookingRow.Id, currentAmount, bookingAmount, userId);

                ParticipationStatus bookingOldStatus = participation.Status;
                bool bookingStatusChanged = ParticipationLifecycle.TrySetStatus(participation, ParticipationStatus.Completed);
                if (bookingStatusChanged)
                {
                    // Isti "BookingStatus" audit obrazac kao BookingService.SetStatus — bez ovoga bi individualni
                    // Confirmed -> Completed prijelaz kroz complete-existing bio jedini status prijelaz koji ne
                    // ostavlja trag tko/kada ga je proveo (vidi audit-cleanup spec section 22-24).
                    await _auditLogHandler.Add(uow, new AppointmentAuditLog
                    {
                        Id = Guid.NewGuid(),
                        AppointmentId = id,
                        BookingId = bookingRow.Id,
                        BookingSegmentParticipationId = participation.Id,
                        ChangeType = "BookingStatus",
                        OldValue = bookingOldStatus.ToString(),
                        NewValue = participation.Status.ToString(),
                        StatusVersion = participation.StatusVersion,
                        ChangedAt = DateTimeOffset.UtcNow,
                        ChangedBy = userId
                    });
                }
                ParticipationPrice.Apply(participation, pricing);

                // Phase D3B3A: "već pokriveno" = AKTIVNA potrošnja paketa (poništena potrošnja nakon korekcije više ne
                // sprječava ponovno pokriće — prije je zaostala zastavica PackageCoverageApplied tiho preskakala skidanje).
                if (hasPackage && PackageConsumptions.ActiveOf(participation) == null)
                {
                    // Paket-namirenje i novčano namirenje su međusobno isključivi (bookingRow je POSTOJEĆI redak, mogao je
                    // već primiti uplatu preko POS Checkouta) — Phase D3B3B: provjeru provodi ledger potrošnje kroz
                    // jedino pravilo SettlementExclusivityPolicy, na granici sudjelovanja.
                    await _packageConsumptionLedgerService.Consume(
                        uow, organizationId, userId, participation, execution, settlement.ClientPackageId.GetValueOrDefault(), BookingStatus.Completed);
                }

                await _appointmentHandler.UpdateBooking(uow, bookingRow);

                // Payment/provizija se generiraju SAMO za booking koji je OVIM pozivom STVARNO tek prešao u
                // Completed (bookingStatusChanged) — bez ovog uvjeta bi ponovni CompleteExisting poziv koji u
                // request.ClientIds ponovno šalje VEĆ Completed sestrinski Booking (npr. nekorigirani klijent B
                // na multi-klijent terminu, dok se korigirani klijent A re-completa nakon P1 korekcije — vidi
                // BookingService.ApplyIndividualCompletionCorrection) pokušao stvoriti DRUGI Payment za B i pao
                // na ux_commission_entries_booking_id_source_version (B.StatusVersion se ovdje ne mijenja jer
                // TrySetStatus no-opira, pa bi SourceVersion bio identičan već postojećem Earned zapisu, vidi
                // CommissionEntry.cs). Prije P1 korekcije ovaj put je bio nedostižan — appointment.Status==Completed
                // je uvijek blokirao ponovni ulaz na vrhu ove metode (ALREADY_COMPLETED) dok god je bilo koji
                // Booking na terminu ostajao Completed, pa je ovaj uvjet čisto zatvaranje NOVO dosegnute putanje,
                // bez promjene ponašanja za prvi/jedini completion (gdje je bookingStatusChanged uvijek true za
                // svaki redak u bookingRows).
                if (bookingStatusChanged)
                {
                    // Booking je već persistiran (postojeći redak, samo ažuriran) — Payment sigurno može odmah nakon.
                    if (!hasPackage && settlement.PaymentMethod.HasValue && settlement.IsPaid && bookingAmount > 0m)
                        await _paymentLedgerService.RecordPayment(
                            uow, organizationId, userId, appointment.CompanyId, bookingRow, participation, settlement.PaymentMethod.Value, bookingAmount,
                            note: null, isCheckInGenerated: true);

                    // Provizija se zarađuje ISTOM transakcijom kao completion — vidi CompleteNew.
                    await _commissionLedgerService.GenerateForIndividualServiceCompletion(uow, organizationId, execution, participation);
                }
            }

            // Phase M1A: status termina se IZVODI iz svih sudjelovanja (zatraženi klijenti su sad Completed; sestrinsko
            // sudjelovanje koje je još Confirmed drži termin Scheduled — prije je termin bezuvjetno postajao Completed).
            await AppointmentLifecycle.Refresh(_appointmentHandler, _auditLogHandler, uow, organizationId, id, userId);

            await uow.CommitAsync();
        }
        catch (DbUpdateConcurrencyException)
        {
            throw new BusinessRuleException(
                ErrorCodes.ConcurrencyConflict, "Podaci su upravo promijenjeni od strane drugog zahtjeva — pokušajte ponovno.");
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
        bool commissionSkipped = false;

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
                    "Individualni termin se odrađuje kroz complete/complete-existing, ne kroz complete-group.");

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

                // Provizija se zarađuje PO CIJELOM odrađenom terminu (sesiji), ne po sudioniku: korisnik = zaposlenik segmenta,
                // pravilo = (zaposlenik, USLUGA sesije), Fixed. Jednom: zaštićena close-out činjenicom iznad (pod lockom termina).
                // Phase M1F: postojeće pravilo je jednoznačno SAMO za jednosegmentni occurrence (jedna usluga). Za višesegmentni
                // occurrence (više usluga) NE izmišlja se atribucija (ni "prvi" segment, ni zbroj, ni prosjek) — provizija se
                // ne stvara i close-out vraća upozorenje (dug faze provizija).
                if (appointment.Segments.Count == 1)
                    await _commissionLedgerService.GenerateForGroupServiceCompletion(
                        uow, organizationId, ExecutionContextResolver.ForSegment(appointment, appointment.Segments[0]));
                else
                    commissionSkipped = true;
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
        if (commissionSkipped)
            dto.Warnings.Add(new WarningDto(WarningCodes.GroupCommissionNotSupportedForMultiSegment, null));

        return dto;
    }

    /// <summary>Statusi koji ZAKLJUČUJU komercijalnu evidenciju bookinga — Update ih nikad ne repricinga
    /// (historijski Amount se ne smije mijenjati naknadno, vidi spec section 18/20).</summary>
    private static bool IsTerminal(ParticipationStatus status) => status != ParticipationStatus.Confirmed;

    public async Task<AppointmentDto> Update(Guid organizationId, Guid userId, bool hasFullScope, Guid id, AppointmentUpdateRequest request)
    {
        Appointment appointment = await _appointmentHandler.GetWithBookingsForMutation(organizationId, id);
        if (appointment == null)
            throw new NotFoundAppException("Appointment", id);

        // LEGACY (Phase M1E): plosnati Update adresira termin — samo jednosegmentni termin (višesegmentni:
        // SEGMENT_SELECTION_REQUIRED; nikad "prvi" segment ni "svi"); mijenja taj segment eksplicitnim segmentnim operacijama.
        AppointmentSegment executionSegment = LegacySingleSegment.Resolve(appointment);
        await AppointmentOwnership.EnsureCallerOwnsSegments(_employeeHandler, organizationId, userId, hasFullScope, new[] { executionSegment }, NotOwnerMessage);

        ServiceEntity service = await LoadServiceOrThrow(organizationId, request.ServiceId);
        bool overrideAvailability = request.OverrideAvailability && hasFullScope;
        await EnsureStructuralEligibility(organizationId, service, request.CompanyId, request.EmployeeId);
        Room room = await EnsureRoomExists(organizationId, request.CompanyId, request.RoomId);
        List<Client> clients = await EnsureClientsExist(organizationId, request.ClientIds);

        BookingPricing pricing = BookingPricing.FromResolution(
            await ResolveServicePrice(organizationId, request.ServiceId, request.CompanyId, request.StartsAt), request.Amount);
        decimal amount = pricing.Amount;

        // Re-cijenjenje (persistira ga AppointmentHandler.UpdateWithBookings niže) se primjenjuje samo na
        // Bookinge koji NISU terminalni — historijski Amount na već odrađenom/otkazanom/izostalom Bookingu
        // se ne dira (vidi IsTerminal). Ovdje samo audit-logiramo promjenu za te retke. Phase M0: cijena se mijenja na
        // sudjelovanju Bookinga NA IZVRŠNOM SEGMENTU termina (segmentno adresiranje, vidi AppointmentHandler.UpdateWithBookings).
        List<Guid> requestedClientIds = request.ClientIds.Distinct().ToList();
        // Phase M1D: osobe u prostoriji nakon Update-a = zatraženi klijenti + klijenti izvan popisa čije sudjelovanje na
        // segmentu ostaje odrađeno (Completed — UpdateWithBookings ga ne uklanja).
        List<Client> retainedCompletedClients = appointment.Bookings
            .Where(b => !requestedClientIds.Contains(b.ClientId) && b.Client != null &&
                        b.Participations.Any(p => p.AppointmentSegmentId == executionSegment.Id && p.Status == ParticipationStatus.Completed))
            .Select(b => b.Client)
            .ToList();
        foreach (Booking booking in appointment.Bookings.Where(b => requestedClientIds.Contains(b.ClientId)))
        {
            BookingSegmentParticipation participation = BookingParticipations.OnSegment(booking, executionSegment);
            if (!IsTerminal(participation.Status) && amount != participation.Amount)
                await LogAmountChange(id, booking.Id, participation.Amount, amount, userId);
        }

        Guid? currentEmployeeId = AppointmentSegments.GetSingleEmployeeId(executionSegment);
        if (currentEmployeeId != request.EmployeeId)
            await LogEmployeeChange(id, currentEmployeeId, request.EmployeeId, userId);

        SegmentSnapshot.State validatedSegment = SegmentSnapshot.Capture(
            appointment, executionSegment, await _schedulingOccupancyHandler.GetSegmentResources(executionSegment.Id.GetValueOrDefault()));

        // F-01 ISPRAVLJEN (Phase M1E, namjerno): Update je validirao i auditao NOVU uslugu/trenera i iz nove usluge izveo
        // trajanje i cijenu, ali ih nije spremao (zastarjeli upis). Sada se zatraženo ciljno stanje segmenta stvarno sprema —
        // usluga, zaposlenik, vrijeme/trajanje i prostorija — istim segmentnim operacijama kao segmentne naredbe.
        DateTimeOffset plannedEnd = request.StartsAt.AddMinutes(service.DefaultDurationMinutes);
        DateTimeOffset now = DateTimeOffset.UtcNow;
        SegmentMutator.ChangeService(executionSegment, request.ServiceId, now);
        SegmentMutator.AssignEmployees(executionSegment, new[] { request.EmployeeId }, now);
        SegmentMutator.ChangeTime(executionSegment, request.StartsAt, plannedEnd, now);
        SegmentMutator.ChangeRoom(executionSegment, request.RoomId, now);
        appointment.CompanyId = request.CompanyId;
        appointment.Note = request.Note;
        appointment.UpdatedAt = DateTimeOffset.UtcNow;
        appointment.UpdatedBy = userId;

        List<WarningDto> warnings = new List<WarningDto>();
        List<Guid> requestedEmployeeIds = new List<Guid> { request.EmployeeId };
        warnings.AddRange(await EnsureWorkforceAvailability(
            organizationId, requestedEmployeeIds, request.CompanyId, request.StartsAt, plannedEnd, overrideAvailability));

        // Phase M1A: Update može dodati Confirmed sudjelovanja (novi klijenti) ili ukloniti netaknuta — status termina se
        // zatim IZVODI u istoj transakciji (npr. dodan klijent na Closed termin → Scheduled).
        await using (IUnitOfWork uow = await _unitOfWorkFactory.Begin())
        {
            // Phase M1C: isključuje se SAMO segment koji se mijenja — sestrinski segmenti istog termina ostaju vidljivi.
            await EnsureNoHardOverlap(uow, organizationId, new[]
            {
                new HardOverlapTarget(executionSegment.Id, request.StartsAt, plannedEnd, requestedEmployeeIds,
                    clients.Concat(retainedCompletedClients).ToList(), room,
                    await _schedulingOccupancyHandler.GetSegmentResources(uow, executionSegment.Id.GetValueOrDefault()))
            });
            // Phase M1E: validirano je PROČITANO stanje segmenta (sudionici, resursi) — pod Appointment lockom se potvrđuje da ga
            // segmentna/sudionička naredba u međuvremenu nije promijenila (inače CONCURRENCY_CONFLICT).
            await SegmentSnapshot.VerifyUnderLock(_appointmentHandler, uow, organizationId, id, new[] { validatedSegment });
            await _appointmentHandler.UpdateWithBookings(uow, appointment, executionSegment, requestedClientIds, pricing);
            await AppointmentLifecycle.Refresh(_appointmentHandler, _auditLogHandler, uow, organizationId, id, userId);
            await uow.CommitAsync();
        }

        AppointmentDto dto = await GetByIdInternal(organizationId, id);
        dto.Warnings = warnings;
        return dto;
    }

    public async Task<AppointmentDto> Move(Guid organizationId, Guid userId, bool hasFullScope, Guid id, AppointmentMoveRequest request)
    {
        Appointment appointment = await _appointmentHandler.GetByIdLight(organizationId, id);
        if (appointment == null)
            throw new NotFoundAppException("Appointment", id);

        if (appointment.Status == AppointmentStatus.Cancelled)
            throw new BusinessRuleException(ErrorCodes.AppointmentNotMovable, "Otkazan termin se ne može pomicati.");

        // LEGACY (Phase M1E): Move pomiče segment jednosegmentnog termina (višesegmentni: SEGMENT_SELECTION_REQUIRED —
        // ciljni put je ChangeSegmentTime).
        AppointmentSegment movedSegment = LegacySingleSegment.Resolve(appointment);
        await AppointmentOwnership.EnsureCallerOwnsSegments(_employeeHandler, organizationId, userId, hasFullScope, new[] { movedSegment }, NotOwnerMessage);

        bool overrideAvailability = request.OverrideAvailability && hasFullScope;

        if (request.EmployeeId.HasValue)
            await EnsureEmployeeExists(organizationId, request.EmployeeId.Value);

        if (request.CompanyId.HasValue)
            await EnsureCompanyExists(organizationId, request.CompanyId.Value);

        Guid? currentEmployeeId = AppointmentSegments.GetSingleEmployeeId(movedSegment);
        Guid effectiveEmployeeId = request.EmployeeId ?? currentEmployeeId.GetValueOrDefault();
        Guid effectiveCompanyId = request.CompanyId ?? appointment.CompanyId;

        Appointment full = await _appointmentHandler.GetById(organizationId, id);
        // Phase M0: klijenti čije sudjelovanje NA SEGMENTU koji se pomiče zauzima raspored — centralno pravilo.
        List<Client> clients = full.Bookings
            .Where(b => b.Participations.Any(p => p.AppointmentSegmentId == movedSegment.Id && ParticipationOccupancy.Occupies(p.Status)))
            .Select(b => b.Client).ToList();

        ServiceEntity service = await LoadServiceOrThrow(organizationId, movedSegment.ServiceId);
        await EnsureStructuralEligibility(organizationId, service, effectiveCompanyId, effectiveEmployeeId);

        // Djelomična izmjena: null u zahtjevu = "bez promjene" (pinned F-03 — prostorija se ne može očistiti),
        // trajanje se ne mijenja.
        if (request.EmployeeId.HasValue && request.EmployeeId.Value != currentEmployeeId)
            await LogEmployeeChange(id, currentEmployeeId, request.EmployeeId.Value, userId);

        SegmentSnapshot.State validatedSegment = SegmentSnapshot.Capture(
            full, movedSegment, await _schedulingOccupancyHandler.GetSegmentResources(movedSegment.Id.GetValueOrDefault()));

        DateTimeOffset now = DateTimeOffset.UtcNow;
        TimeSpan duration = movedSegment.PlannedEnd - movedSegment.PlannedStart;
        SegmentMutator.ChangeTime(movedSegment, request.StartsAt, request.StartsAt + duration, now);
        if (request.EmployeeId.HasValue)
            SegmentMutator.AssignEmployees(movedSegment, new[] { request.EmployeeId.Value }, now);
        if (request.RoomId.HasValue)
            SegmentMutator.ChangeRoom(movedSegment, request.RoomId, now);
        if (request.CompanyId.HasValue)
            appointment.CompanyId = request.CompanyId.Value;
        appointment.UpdatedAt = DateTimeOffset.UtcNow;
        appointment.UpdatedBy = userId;

        // Efektivna prostorija se revalidira i kad nije eksplicitno poslana u zahtjevu — pomicanje termina u drugu
        // poslovnicu bez zadanog RoomId inače bi ostavilo prostoriju iz stare poslovnice na terminu nove.
        Room room = await EnsureRoomExists(organizationId, appointment.CompanyId, movedSegment.RoomId);

        List<Guid> effectiveEmployeeIds = new List<Guid> { effectiveEmployeeId };
        List<WarningDto> warnings = new List<WarningDto>();
        warnings.AddRange(await EnsureWorkforceAvailability(
            organizationId, effectiveEmployeeIds, appointment.CompanyId, movedSegment.PlannedStart, movedSegment.PlannedEnd, overrideAvailability));

        await using (IUnitOfWork uow = await _unitOfWorkFactory.Begin())
        {
            // Phase M1C: isključuje se SAMO pomaknuti segment — sestrinski segmenti istog termina ostaju vidljivi.
            await EnsureNoHardOverlap(uow, organizationId, new[]
            {
                new HardOverlapTarget(movedSegment.Id, movedSegment.PlannedStart, movedSegment.PlannedEnd, effectiveEmployeeIds, clients, room,
                    await _schedulingOccupancyHandler.GetSegmentResources(uow, movedSegment.Id.GetValueOrDefault()))
            });
            // Phase M1E: pod Appointment lockom — pročitani segment (trajanje, sudionici, resursi) se nije promijenio.
            await SegmentSnapshot.VerifyUnderLock(_appointmentHandler, uow, organizationId, id, new[] { validatedSegment });
            await _appointmentHandler.UpdateScalar(uow, appointment);
            await uow.CommitAsync();
        }

        AppointmentDto dto = await GetByIdInternal(organizationId, id);
        dto.Warnings = warnings;
        return dto;
    }

    /// <summary>Otkazuje CIJELI termin — svi aktivni (Confirmed) Bookinzi prelaze u Cancelled zajedno s
    /// Appointment.Status. Za otkazivanje SAMO jednog klijenta (npr. duo/grupa) koristi se
    /// IBookingService.SetStatus umjesto ovoga (vidi Booking.cs section 44).</summary>
    public Task<AppointmentDto> Cancel(Guid organizationId, Guid userId, bool hasFullScope, Guid id, AppointmentCancelRequest request)
    {
        return ChangeToTerminalStatus(organizationId, userId, hasFullScope, id, request, BookingStatus.Cancelled);
    }

    /// <summary>Bulk no-show — svi aktivni Bookinzi prelaze u NoShow, Appointment.Status ipak završava na
    /// Cancelled (termin kao okvir NIKAD nije NoShow — vidi AppointmentStatus.cs). Za pojedinačni no-show na
    /// terminu s više klijenata koristi se IBookingService.SetStatus.</summary>
    public Task<AppointmentDto> MarkNoShow(Guid organizationId, Guid userId, bool hasFullScope, Guid id, AppointmentCancelRequest request)
    {
        return ChangeToTerminalStatus(organizationId, userId, hasFullScope, id, request, BookingStatus.NoShow);
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
        // AppointmentHandler.Delete); sudjelovanje s poviješću → REFERENCED_CANNOT_DELETE.
        await _appointmentHandler.Delete(appointment);
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
            ResolvePriceResponse resolvedPrice = await ResolveServicePrice(organizationId, request.ServiceId, request.CompanyId, occurrence);

            // Phase M1B: svaki occurrence kroz konstrukcijsku jezgru (jedan segment; /recurring namjerno ignorira ručni iznos
            // — request nema Amount — svaki occurrence po svojoj predloženoj cijeni).
            SegmentPlan plan = new SegmentPlan(
                request.ServiceId, occurrence, occurrence.AddMinutes(service.DefaultDurationMinutes), new[] { request.EmployeeId }, request.RoomId,
                clients.Select(c => new ParticipantPlan(c.Id.GetValueOrDefault(), BookingPricing.AtSuggested(resolvedPrice))).ToList());
            Appointment appointment = AppointmentFactory.CreateIndividual(
                organizationId, request.CompanyId, request.Note, recurrenceGroupId, userId, DateTimeOffset.UtcNow,
                new[] { plan }, ParticipationStatus.Confirmed);

            toCreate.Add(appointment);
        }

        await _appointmentHandler.AddRange(uow, toCreate);
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

        // Capability je isključivo eksplicitna (vidi EmployeeServiceAssignment) — prazan popis znači
        // zaposlenik ne smije nijednu uslugu, ne "smije sve".
        employees = employees
            .Where(e => e.IsActive && e.Services.Any(s => s.ServiceId == query.ServiceId))
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
            segments.Select(s => s.Plan).ToList(), ParticipationStatus.Confirmed);
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
            await uow.CommitAsync();
        }

        AppointmentDto dto = await GetByIdInternal(organizationId, appointmentId);
        dto.Warnings = warnings;
        return dto;
    }

    /// <summary>Zajednička implementacija za Cancel/MarkNoShow — cijeli termin završava na
    /// AppointmentStatus.Cancelled (termin kao okvir nikad nije NoShow), svi Bookinzi koji još nisu u
    /// terminalnom stanju prelaze na targetBookingStatus (Cancelled ili NoShow).</summary>
    private async Task<AppointmentDto> ChangeToTerminalStatus(
        Guid organizationId, Guid userId, bool hasFullScope, Guid id, AppointmentCancelRequest request, BookingStatus targetBookingStatus)
    {
        List<Guid> returnClientIds = (request.ReturnEntryForClientIds ?? new List<Guid>()).Distinct().ToList();

        try
        {
            await using IUnitOfWork uow = await _unitOfWorkFactory.Begin();

            // Zaključava Appointment redak (FOR UPDATE) i čita Status/EmployeeId pod lockom PRIJE bilo kakve
            // provjere/mutacije — sprječava utrku s konkurentnim CompleteExisting/CompleteGroupAppointment na
            // ISTOM terminu (drugi zahtjev čeka na lock pa vidi svježe stanje nakon commita prvog, vidi spec
            // section 8-11). Ownership se namjerno provjerava OVDJE (ne pred-transakcijski) — jeftina provjera,
            // nema razloga za dodatan round-trip prije zaključavanja.
            Appointment appointment = await _appointmentHandler.GetForUpdateWithBookings(uow, organizationId, id);
            if (appointment == null)
                throw new NotFoundAppException("Appointment", id);

            // Phase M1E (zaključano): otkazivanje/bulk no-show TERMINA je operacija nad cijelim agregatom (sva aktivna
            // sudjelovanja svih segmenata) — zahtijeva appointments.write.all, own-opseg nikad.
            AppointmentOwnership.EnsureWholeAppointmentScope(hasFullScope, NotOwnerMessage);

            // Phase M1B (zaključano): bulk no-show je operacija nad AKTIVNIM sudjelovanjima — bez ijednog aktivnog (Confirmed)
            // sudjelovanja (npr. prazan termin) se odbija; status se ne mijenja i ne izmišlja se "NoShow" termina.
            if (targetBookingStatus == BookingStatus.NoShow && appointment.Status != AppointmentStatus.Closed &&
                !appointment.Bookings.SelectMany(b => b.Participations).Any(p => p.Status == ParticipationStatus.Confirmed))
                throw new BusinessRuleException(
                    ErrorCodes.NoActiveParticipations, "Termin nema aktivnih sudjelovanja koja bi se mogla označiti kao izostanak.");

            // Phase M1A: termin bez ijednog Confirmed sudjelovanja koji NIJE "sve otkazano" (Closed — razriješen) nema što
            // otkazati/označiti izostankom; isto pravilo kao prije za Completed (odrađen i eventualno proviziran termin se
            // ne smije naknadno "otkazati"). Djelomično izvršen termin (npr. Completed + Confirmed) JEST dopušten: otkazuju
            // se samo aktivna sudjelovanja, a termin se izvodi (→ Closed).
            if (appointment.Status == AppointmentStatus.Closed)
                throw new BusinessRuleException(
                    ErrorCodes.AlreadyCompleted,
                    "Termin je već razriješen (Closed) i ne može se otkazati niti označiti kao izostanak.");

            // Phase M1A.1: otkazivanje TERMINA je zasebna, eksplicitna činjenica (CancelledAt/By + "AppointmentCancelled"
            // audit) — ulaz u izvođenje statusa, nikad izveden iz sudjelovanja. Bulk no-show NIJE otkazivanje termina: samo
            // bilježi razlog (metapodatak, kao i prije). STATUS se ne postavlja ovdje nego izvodi niže.
            if (targetBookingStatus == BookingStatus.Cancelled)
            {
                await AppointmentLifecycle.MarkExplicitlyCancelled(_auditLogHandler, uow, appointment, request.CancellationReason, userId);
            }
            else
            {
                appointment.CancellationReason = request.CancellationReason;
                appointment.UpdatedAt = DateTimeOffset.UtcNow;
                appointment.UpdatedBy = userId;
            }

            await _appointmentHandler.UpdateScalar(uow, appointment);

            // Phase M0: appointment-wide prijelaz (A) — Booking nema status, pa se cijeli termin zatvara kontroliranim
            // prijelazom SVAKOG aktivnog (Confirmed) sudjelovanja svih Bookinga. Sudjelovanja se zaključavaju nakon
            // termina, u stabilnom redoslijedu (po Id-u); statusi su već svježi jer svaki prijelaz statusa prvo zaključava
            // ovaj isti termin.
            ParticipationStatus target = BookingParticipations.ToParticipationStatus(targetBookingStatus);
            // Phase M1E.1: otkazivanje termina klasificira SVAKO otkazano sudjelovanje zasebno (isti cutoff organizacije, isti
            // trenutak, početak NJEGOVOG segmenta) — kao otkazivanje jednog sudjelovanja ili cijelog Bookinga.
            int cutoffMinutes = targetBookingStatus == BookingStatus.Cancelled
                ? await _organizationSettingsService.GetCancellationCutoffMinutes(organizationId)
                : 0;
            DateTimeOffset cancelledAt = DateTimeOffset.UtcNow;
            await _participationHandler.LockForUpdate(
                uow, organizationId, appointment.Bookings.SelectMany(b => b.Participations).Select(p => p.Id.GetValueOrDefault()));

            foreach ((Booking booking, BookingSegmentParticipation participation) in appointment.Bookings
                         .SelectMany(b => b.Participations.Select(p => (Booking: b, Participation: p)))
                         .Where(x => x.Participation.Status == ParticipationStatus.Confirmed)
                         .OrderBy(x => x.Participation.Id)
                         .ToList())
            {
                ParticipationStatus oldParticipationStatus = participation.Status;
                ParticipationLifecycle.TrySetStatus(participation, target);
                ParticipationLifecycle.SetCancellationReason(participation, request.CancellationReason);
                if (targetBookingStatus == BookingStatus.Cancelled)
                    ParticipationLifecycle.SetLateCancellation(participation, BookingCancellationPolicy.IsLateCancellation(
                        ExecutionContextResolver.ForParticipation(appointment, booking, participation), cancelledAt, cutoffMinutes));
                booking.UpdatedAt = DateTimeOffset.UtcNow;
                booking.UpdatedBy = userId;

                // Phase D3B3A: povrat ulaska = poništenje AKTIVNE potrošnje paketa sudjelovanja (ledger; no-op ako je nema).
                bool shouldReturn = returnClientIds.Contains(booking.ClientId) &&
                    await _packageConsumptionLedgerService.ReverseActive(
                        uow, organizationId, userId, participation,
                        targetBookingStatus == BookingStatus.NoShow ? PackageConsumptionReversalReason.NoShow : PackageConsumptionReversalReason.Cancellation);

                await _appointmentHandler.UpdateBooking(uow, booking);

                await _auditLogHandler.Add(uow, new AppointmentAuditLog
                {
                    Id = Guid.NewGuid(),
                    AppointmentId = id,
                    BookingId = booking.Id,
                    BookingSegmentParticipationId = participation.Id,
                    ChangeType = "BookingStatus",
                    OldValue = oldParticipationStatus.ToString(),
                    NewValue = participation.Status.ToString(),
                    StatusVersion = participation.StatusVersion,
                    ChangedAt = DateTimeOffset.UtcNow,
                    ChangedBy = userId
                });

                // Jedan booking.cancelled.v1/booking.no-show.v1 po STVARNO otkazanom/izostalom SUDJELOVANJU (ne jedan
                // generički Appointment event) — vidi spec section 33. Ista uow transakcija kao mutacija iznad.
                if (targetBookingStatus == BookingStatus.Cancelled)
                    await ParticipationEvents.WriteCancelled(_outboxWriter, uow, organizationId, appointment, booking, participation);
                else if (targetBookingStatus == BookingStatus.NoShow)
                    await ParticipationEvents.WriteNoShow(_outboxWriter, uow, organizationId, appointment, booking, participation);

                if (shouldReturn)
                {
                    await _auditLogHandler.Add(uow, new AppointmentAuditLog
                    {
                        Id = Guid.NewGuid(),
                        AppointmentId = id,
                        BookingId = booking.Id,
                        BookingSegmentParticipationId = participation.Id,
                        ChangeType = "BookingPackageCoverageReturned",
                        OldValue = "Applied",
                        NewValue = "Returned",
                        ChangedAt = DateTimeOffset.UtcNow,
                        ChangedBy = userId
                    });
                }
            }

            // Phase M1A.1: status se IZVODI iz sudjelovanja + eksplicitne otkazanosti: otkazan termin bez izvršenog rada
            // (uključujući prazan termin) → Cancelled; bilo koji Completed/NoShow (uključujući bulk no-show) → Closed.
            // Terminalna sudjelovanja (Completed/Cancelled/NoShow) nisu dirana.
            await AppointmentLifecycle.Refresh(_appointmentHandler, _auditLogHandler, uow, organizationId, id, userId);

            // Aktivni rad termina je otkazan/izostao — preostali Waiting retci više nisu smisleni, ne promovira se
            // (spec section 19/41/42).
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

    private async Task EnsureEmployeeExists(Guid organizationId, Guid employeeId)
    {
        Employee employee = await _employeeHandler.GetById(organizationId, employeeId);
        if (employee == null)
            throw new NotFoundAppException("Employee", employeeId);
    }

    private async Task EnsureCompanyExists(Guid organizationId, Guid companyId)
    {
        Company company = await _companyHandler.GetById(organizationId, companyId);
        if (company == null)
            throw new NotFoundAppException("Company", companyId);
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

    /// <summary>Kad je ClientPackageId popunjen, svaki klijent na terminu mora imati odabran svoj vlastiti
    /// valjani paket (npr. duo/par usluga: svaki klijent skida ulazak iz svog profila, neovisno o ostalima).
    /// Validira da Settlements pokriva SVAKI klijent termina TOČNO JEDNOM (mješovito plaćanje po klijentu —
    /// vidi spec section 10/12). Zamjenjuje staru ValidatePackageSelections (koja je pokrivala samo
    /// paket-granu uz jedan zajednički PaymentMethod za sve — mješovito plaćanje strukturno nije bilo moguće).</summary>
    private async Task<Dictionary<Guid, AppointmentClientSettlement>> ValidateSettlements(
        Guid organizationId, List<Guid> clientIds, Guid serviceId, Guid companyId, DateTimeOffset date, List<AppointmentClientSettlement> settlements)
    {
        settlements ??= new List<AppointmentClientSettlement>();
        List<Guid> settledClientIds = settlements.Select(s => s.ClientId).ToList();

        bool coversAllClientsExactlyOnce =
            settlements.Count == clientIds.Count &&
            settledClientIds.Distinct().Count() == settledClientIds.Count &&
            clientIds.All(id => settledClientIds.Contains(id));

        if (!coversAllClientsExactlyOnce)
            throw new ValidationAppException("Potrebno je odabrati točno jedno plaćanje (Settlements) za svakog klijenta na terminu.");

        Dictionary<Guid, AppointmentClientSettlement> result = new Dictionary<Guid, AppointmentClientSettlement>();

        foreach (AppointmentClientSettlement settlement in settlements)
        {
            if (settlement.ClientPackageId.HasValue)
            {
                List<ClientPackageDto> eligible = await _clientPackageService.GetEligibleForService(
                    organizationId, settlement.ClientId, serviceId, date, companyId);

                if (eligible.All(p => p.Id != settlement.ClientPackageId.Value))
                    throw new BusinessRuleException(ErrorCodes.PackageNotEligible, "Odabrani paket nije valjan za klijenta ili ne pokriva ovu uslugu.");
            }

            result[settlement.ClientId] = settlement;
        }

        return result;
    }

    /// <summary>Ciljno stanje jednog segmenta za tvrdu provjeru: <paramref name="SegmentId"/> = postojeći segment koji se
    /// prepisuje (isključuje se SAMO on; zahtjev je njegovo CIJELO ciljno stanje) ili null za novi. Osobe u prostoriji = svi
    /// zaposlenici + svi klijenti koji će segment zauzimati; resursi = sve dodjele segmenta.</summary>
    private sealed record HardOverlapTarget(
        Guid? SegmentId, DateTimeOffset PlannedStart, DateTimeOffset PlannedEnd, IReadOnlyList<Guid> EmployeeIds, IReadOnlyList<Client> Clients,
        Room Room, IReadOnlyList<ResourceClaim> Resources = null);

    /// <summary>Koriste svi write endpointi (Create/CompleteNew/CompleteExisting/Update/Move/recurring) — tvrde invarijante
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
    /// lista (vidljivost bez blokade — isto ponašanje kao prije ovog zahvata). Koriste svi write endpointi osim
    /// CompleteExisting (retroaktivno evidentiranje odrađenog, ne planiranje unaprijed) i CompleteNew za StartsAt
    /// u prošlosti (isti razlog, provjereno kod pozivatelja).</summary>
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

    private async Task LogAmountChange(Guid appointmentId, Guid? bookingId, decimal oldAmount, decimal newAmount, Guid userId)
    {
        await _auditLogHandler.Add(new AppointmentAuditLog
        {
            Id = Guid.NewGuid(),
            AppointmentId = appointmentId,
            BookingId = bookingId,
            ChangeType = "Amount",
            OldValue = oldAmount.ToString(CultureInfo.InvariantCulture),
            NewValue = newAmount.ToString(CultureInfo.InvariantCulture),
            ChangedAt = DateTimeOffset.UtcNow,
            ChangedBy = userId
        });
    }

    private async Task LogAmountChangeInTransaction(
        IUnitOfWork uow, Guid appointmentId, Guid? bookingId, decimal oldAmount, decimal newAmount, Guid userId)
    {
        await _auditLogHandler.Add(uow, new AppointmentAuditLog
        {
            Id = Guid.NewGuid(),
            AppointmentId = appointmentId,
            BookingId = bookingId,
            ChangeType = "Amount",
            OldValue = oldAmount.ToString(CultureInfo.InvariantCulture),
            NewValue = newAmount.ToString(CultureInfo.InvariantCulture),
            ChangedAt = DateTimeOffset.UtcNow,
            ChangedBy = userId
        });
    }

    /// <summary>Bilježi zamjenu trenera na terminu (npr. netko drugi uskoči na grupu umjesto zadanog/planiranog
    /// trenera) — bez ovoga bi se EmployeeId tiho prepisao i izgubio bi se trag tko je prije bio raspoređen.</summary>
    private async Task LogEmployeeChange(Guid appointmentId, Guid? oldEmployeeId, Guid? newEmployeeId, Guid userId)
    {
        await _auditLogHandler.Add(new AppointmentAuditLog
        {
            Id = Guid.NewGuid(),
            AppointmentId = appointmentId,
            ChangeType = "EmployeeId",
            OldValue = oldEmployeeId?.ToString(),
            NewValue = newEmployeeId?.ToString(),
            ChangedAt = DateTimeOffset.UtcNow,
            ChangedBy = userId
        });
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
        SingleSegmentProjection compat = SingleSegmentProjection.Of(a);

        return new AppointmentScheduleCellDto
        {
            Id = a.Id.GetValueOrDefault(),
            PlannedStart = range.PlannedStart,
            PlannedEnd = range.PlannedEnd,
            Segments = AppointmentSegmentReadModel.ToDtos(a),
            StartsAt = range.PlannedStart,
            DurationMinutes = range.SpanMinutes,
            ServiceId = compat.ServiceId,
            ServiceName = compat.ServiceName,
            ServiceCategoryColorHex = compat.ServiceColorHex,
            EmployeeId = compat.EmployeeId,
            EmployeeName = compat.EmployeeName,
            CompanyId = a.CompanyId,
            CompanyName = a.Company?.Name,
            RoomId = compat.RoomId,
            RoomName = compat.RoomName,
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
        SingleSegmentProjection compat = SingleSegmentProjection.Of(a);
        PackageCoverageView coverage = PackageConsumptions.CoverageOfBooking(booking, a.Form);

        return new ClientAppointmentHistoryDto
        {
            Id = a.Id.GetValueOrDefault(),
            Form = a.Form,
            PlannedStart = range.PlannedStart,
            PlannedEnd = range.PlannedEnd,
            Segments = AppointmentSegmentReadModel.ToDtos(a),
            StartsAt = range.PlannedStart,
            DurationMinutes = range.SpanMinutes,
            ServiceId = compat.ServiceId,
            ServiceName = compat.ServiceName,
            ServiceCategoryColorHex = compat.ServiceColorHex,
            EmployeeId = compat.EmployeeId,
            EmployeeName = compat.EmployeeName,
            CompanyId = a.CompanyId,
            CompanyName = a.Company?.Name,
            Status = a.Status,
            GroupId = a.GroupId,
            GroupName = a.Group?.Name,
            Amount = commercial.FinalPrice,
            PaidAmount = commercial.MonetarySettled,
            OutstandingAmount = commercial.Outstanding,
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
        SingleSegmentProjection compat = SingleSegmentProjection.Of(a);
        return new AppointmentDto
        {
            Id = a.Id.GetValueOrDefault(),
            Form = a.Form,
            PlannedStart = range.PlannedStart,
            PlannedEnd = range.PlannedEnd,
            Segments = AppointmentSegmentReadModel.ToDtos(a),
            StartsAt = range.PlannedStart,
            DurationMinutes = range.SpanMinutes,
            ServiceId = compat.ServiceId,
            ServiceName = compat.ServiceName,
            ServiceCategoryColorHex = compat.ServiceColorHex,
            EmployeeId = compat.EmployeeId,
            EmployeeName = compat.EmployeeName,
            CompanyId = a.CompanyId,
            CompanyName = a.Company?.Name,
            RoomId = compat.RoomId,
            RoomName = compat.RoomName,
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
