using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using BlueDragon.DuneLight.Core.DTOs.Appointments;
using BlueDragon.DuneLight.Core.DTOs.Catalog;
using BlueDragon.DuneLight.Core.DTOs.Clients;
using BlueDragon.DuneLight.Core.Enums;
using BlueDragon.DuneLight.Core.Interfaces.Appointments;
using BlueDragon.DuneLight.Core.Interfaces.Catalog;
using BlueDragon.DuneLight.Core.Interfaces.Clients;
using BlueDragon.DuneLight.Core.Shared;
using BlueDragon.DuneLight.Core.Shared.Exceptions;
using BlueDragon.DuneLight.Infrastructure.Domain.Models.Appointments;
using BlueDragon.DuneLight.Infrastructure.Domain.Models.Catalog;
using BlueDragon.DuneLight.Infrastructure.Domain.Models.Checkouts;
using BlueDragon.DuneLight.Infrastructure.Domain.Models.Clients;
using BlueDragon.DuneLight.Infrastructure.Domain.Models.Employees;
using BlueDragon.DuneLight.Infrastructure.Domain.Models.Roster;
using BlueDragon.DuneLight.Infrastructure.Handlers.Interfaces;
using BlueDragon.DuneLight.Infrastructure.UnitOfWork;
using BlueDragon.DuneLight.Infrastructure.Utils;
using Microsoft.EntityFrameworkCore;
using ServiceEntity = BlueDragon.DuneLight.Infrastructure.Domain.Models.Catalog.Service;

namespace BlueDragon.DuneLight.Infrastructure.Services;

public class AppointmentService : IAppointmentService
{
    private readonly IAppointmentHandler _appointmentHandler;
    private readonly IAppointmentAuditLogHandler _auditLogHandler;
    private readonly IClientPackageService _clientPackageService;
    private readonly IClientPackageHandler _clientPackageHandler;
    private readonly IPricingService _pricingService;
    private readonly IServiceHandler _serviceHandler;
    private readonly IServiceAvailabilityService _serviceAvailabilityService;
    private readonly IEmployeeHandler _employeeHandler;
    private readonly ICompanyHandler _companyHandler;
    private readonly IRoomHandler _roomHandler;
    private readonly IClientHandler _clientHandler;
    private readonly IRosterEntryHandler _rosterEntryHandler;
    private readonly IWorkingHoursTemplateHandler _workingHoursTemplateHandler;
    private readonly ICompanyHolidayHandler _companyHolidayHandler;
    private readonly IScheduleBreakHandler _scheduleBreakHandler;
    private readonly IWaitlistPromotionService _waitlistPromotionService;
    private readonly IPaymentLedgerService _paymentLedgerService;
    private readonly ICheckoutHandler _checkoutHandler;
    private readonly ICommissionLedgerService _commissionLedgerService;
    private readonly IUnitOfWorkFactory _unitOfWorkFactory;

    public AppointmentService(
        IAppointmentHandler appointmentHandler,
        IAppointmentAuditLogHandler auditLogHandler,
        IClientPackageService clientPackageService,
        IClientPackageHandler clientPackageHandler,
        IPricingService pricingService,
        IServiceHandler serviceHandler,
        IServiceAvailabilityService serviceAvailabilityService,
        IEmployeeHandler employeeHandler,
        ICompanyHandler companyHandler,
        IRoomHandler roomHandler,
        IClientHandler clientHandler,
        IRosterEntryHandler rosterEntryHandler,
        IWorkingHoursTemplateHandler workingHoursTemplateHandler,
        ICompanyHolidayHandler companyHolidayHandler,
        IScheduleBreakHandler scheduleBreakHandler,
        IWaitlistPromotionService waitlistPromotionService,
        IPaymentLedgerService paymentLedgerService,
        ICheckoutHandler checkoutHandler,
        ICommissionLedgerService commissionLedgerService,
        IUnitOfWorkFactory unitOfWorkFactory)
    {
        _appointmentHandler = appointmentHandler;
        _auditLogHandler = auditLogHandler;
        _clientPackageService = clientPackageService;
        _clientPackageHandler = clientPackageHandler;
        _pricingService = pricingService;
        _serviceHandler = serviceHandler;
        _serviceAvailabilityService = serviceAvailabilityService;
        _employeeHandler = employeeHandler;
        _companyHandler = companyHandler;
        _roomHandler = roomHandler;
        _clientHandler = clientHandler;
        _rosterEntryHandler = rosterEntryHandler;
        _workingHoursTemplateHandler = workingHoursTemplateHandler;
        _companyHolidayHandler = companyHolidayHandler;
        _scheduleBreakHandler = scheduleBreakHandler;
        _waitlistPromotionService = waitlistPromotionService;
        _paymentLedgerService = paymentLedgerService;
        _checkoutHandler = checkoutHandler;
        _commissionLedgerService = commissionLedgerService;
        _unitOfWorkFactory = unitOfWorkFactory;
    }

    public Task<AppointmentDto> Create(Guid organizationId, Guid userId, bool hasFullScope, AppointmentCreateRequest request)
    {
        return CreateInternal(organizationId, userId, hasFullScope, request, recurrenceGroupId: null);
    }

    public async Task<AppointmentDto> CompleteNew(Guid organizationId, Guid userId, bool hasFullScope, AppointmentCompleteRequest request)
    {
        await ValidateOwnership(organizationId, userId, hasFullScope, request.EmployeeId);
        ServiceEntity service = await LoadServiceOrThrow(organizationId, request.ServiceId);
        bool overrideAvailability = request.OverrideAvailability && hasFullScope;
        await EnsureStructuralEligibility(organizationId, service, request.CompanyId, request.EmployeeId);
        Room room = await EnsureRoomExists(organizationId, request.CompanyId, request.RoomId);
        List<Client> clients = await EnsureClientsExist(organizationId, request.ClientIds);

        decimal suggestedAmount = await ResolveSuggestedAmount(organizationId, request.ServiceId, request.CompanyId, request.StartsAt);

        Dictionary<Guid, AppointmentClientSettlement> settlementByClient = await ValidateSettlements(
            organizationId, clients.Select(c => c.Id.GetValueOrDefault()).ToList(), request.ServiceId, request.StartsAt, request.Settlements);

        Guid appointmentId = Guid.NewGuid();
        Appointment appointment = new Appointment
        {
            Id = appointmentId,
            OrganizationId = organizationId,
            Form = AppointmentForm.Individual,
            StartsAt = request.StartsAt,
            DurationMinutes = service.DefaultDurationMinutes,
            ServiceId = request.ServiceId,
            EmployeeId = request.EmployeeId,
            CompanyId = request.CompanyId,
            RoomId = request.RoomId,
            Status = AppointmentStatus.Completed,
            Note = request.Note,
            CreatedAt = DateTimeOffset.UtcNow,
            CreatedBy = userId
        };

        Dictionary<Guid, Guid> packageByClient = new Dictionary<Guid, Guid>();
        List<(Booking Booking, PaymentMethod Method, decimal Amount)> pendingPayments = new List<(Booking, PaymentMethod, decimal)>();

        foreach (Client client in clients)
        {
            Guid clientId = client.Id.GetValueOrDefault();
            AppointmentClientSettlement settlement = settlementByClient[clientId];
            bool hasPackage = settlement.ClientPackageId.HasValue;
            decimal bookingAmount = settlement.Amount ?? suggestedAmount;

            if (hasPackage)
                packageByClient[clientId] = settlement.ClientPackageId.GetValueOrDefault();

            Booking booking = new Booking
            {
                Id = Guid.NewGuid(),
                OrganizationId = organizationId,
                AppointmentId = appointmentId,
                ClientId = clientId,
                Status = BookingStatus.Completed,
                Amount = bookingAmount,
                SuggestedAmount = suggestedAmount,
                IsAmountManuallyOverridden = settlement.Amount.HasValue && settlement.Amount.Value != suggestedAmount,
                ClientPackageId = hasPackage ? settlement.ClientPackageId : (Guid?)null,
                PackageCoverageApplied = hasPackage,
                CreatedAt = DateTimeOffset.UtcNow
            };
            appointment.Bookings.Add(booking);

            // Paket namiruje obvezu bez Paymenta (vidi Payment.cs/spec section 3/40) — monetarni Payment se
            // stvara samo bez paketa, uz zatraženu metodu, IsPaid=true i stvaran pozitivan iznos.
            if (!hasPackage && settlement.PaymentMethod.HasValue && settlement.IsPaid && bookingAmount > 0m)
                pendingPayments.Add((booking, settlement.PaymentMethod.Value, bookingAmount));
        }

        // CompleteNew loguje odrađeno — provjera radne-snage dostupnosti vrijedi samo ako je StartsAt u budućnosti
        // (zakazuje se i odmah naplaćuje); za prošlost je ovo evidentiranje stvarnosti, ne planiranje (vidi FAZA 2).
        List<WarningDto> warnings = new List<WarningDto>();
        if (request.StartsAt > DateTimeOffset.UtcNow)
            warnings.AddRange(await EnsureWorkforceAvailability(
                organizationId, request.EmployeeId, request.CompanyId, request.StartsAt, service.DefaultDurationMinutes, overrideAvailability));

        await EnsureNoHardOverlap(organizationId, request.EmployeeId, clients, request.StartsAt, appointment.DurationMinutes, excludeId: null, room);

        try
        {
            await using IUnitOfWork uow = await _unitOfWorkFactory.Begin();

            await _appointmentHandler.Add(uow, appointment);

            foreach (KeyValuePair<Guid, Guid> kvp in packageByClient)
                await DeductPackageEntryInTransaction(uow, organizationId, kvp.Value, request.ServiceId, userId);

            // Payment ide TEK nakon _appointmentHandler.Add (FK payments.booking_id) — Booking.Id je već
            // poznat (dodijeljen prije Add), pa je isti in-memory objekt (sad persistiran) siguran za referencu.
            foreach ((Booking booking, PaymentMethod method, decimal amount) in pendingPayments)
                await _paymentLedgerService.RecordPayment(
                    uow, organizationId, userId, appointment.CompanyId, booking, method, amount, note: null, isCheckInGenerated: true);

            // Provizija se zarađuje ISTOM transakcijom kao completion — svaki upravo odrađen Booking je jedan
            // izvor (vidi ICommissionLedgerService, spec section 27/28).
            foreach (Booking booking in appointment.Bookings)
                await _commissionLedgerService.GenerateForIndividualServiceCompletion(uow, organizationId, appointment, booking);

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

        // Ovaj put (ClientIds/Settlements popis koji reconcilea Booking retke, uklj. hard-delete izbačenih)
        // pretpostavlja Form=Individual — za Form=Group to bi netočno restrukturiralo Bookinge koji već
        // postoje po GroupMemberima (vidi GroupService.GenerateAppointments/AddMember). Grupni termin se
        // zatvara kroz IAppointmentService.CompleteGroupAppointment, koji ne dira Booking retke.
        if (appointment.Form != AppointmentForm.Individual)
            throw new ValidationAppException(
                "Grupni termin se odrađuje kroz complete-group, ne kroz complete-existing (naplata je po klijentu/Bookingu, ne po popisu klijenata termina).");

        if (appointment.Status == AppointmentStatus.Completed)
            throw new BusinessRuleException(ErrorCodes.AlreadyCompleted, "Termin je već označen kao odrađen.");

        await ValidateOwnership(organizationId, userId, hasFullScope, appointment.EmployeeId.GetValueOrDefault());

        ServiceEntity service = await LoadServiceOrThrow(organizationId, request.ServiceId);
        await EnsureStructuralEligibility(organizationId, service, request.CompanyId, request.EmployeeId);
        Room room = await EnsureRoomExists(organizationId, request.CompanyId, request.RoomId);
        List<Client> clients = await EnsureClientsExist(organizationId, request.ClientIds);

        decimal suggestedAmount = await ResolveSuggestedAmount(organizationId, request.ServiceId, request.CompanyId, request.StartsAt);

        Dictionary<Guid, AppointmentClientSettlement> settlementByClient = await ValidateSettlements(
            organizationId, clients.Select(c => c.Id.GetValueOrDefault()).ToList(), request.ServiceId, request.StartsAt, request.Settlements);

        await EnsureNoHardOverlap(organizationId, request.EmployeeId, clients, request.StartsAt, service.DefaultDurationMinutes, excludeId: id, room);

        try
        {
            await using IUnitOfWork uow = await _unitOfWorkFactory.Begin();

            // Zaključava Appointment redak (FOR UPDATE) i ponovno čita Status prije mutacije — sprječava utrku s
            // konkurentnim drugim completion/cancel zahtjevom na ISTOM terminu (drugi zahtjev čeka na lock pa vidi
            // svježe stanje nakon commita prvog, vidi spec section 8-11). Zamjenjuje pred-transakcijski appointment
            // (GetByIdLight iznad, koji je poslužio samo za brzu Form/ownership/AlreadyCompleted provjeru).
            appointment = await _appointmentHandler.GetForUpdate(uow, organizationId, id);
            if (appointment == null)
                throw new NotFoundAppException("Appointment", id);
            if (appointment.Status == AppointmentStatus.Completed)
                throw new BusinessRuleException(ErrorCodes.AlreadyCompleted, "Termin je već označen kao odrađen.");

            appointment.StartsAt = request.StartsAt;
            appointment.DurationMinutes = service.DefaultDurationMinutes;
            appointment.ServiceId = request.ServiceId;
            appointment.EmployeeId = request.EmployeeId;
            appointment.CompanyId = request.CompanyId;
            appointment.RoomId = request.RoomId;
            appointment.Status = AppointmentStatus.Completed;
            appointment.Note = request.Note;
            appointment.UpdatedAt = DateTimeOffset.UtcNow;
            appointment.UpdatedBy = userId;

            await _appointmentHandler.UpdateWithBookings(uow, appointment, request.ClientIds.Distinct().ToList());

            List<Booking> bookingRows = await _appointmentHandler.GetBookings(uow, organizationId, id, request.ClientIds.Distinct().ToList());

            foreach (Booking bookingRow in bookingRows)
            {
                AppointmentClientSettlement settlement = settlementByClient[bookingRow.ClientId];
                bool hasPackage = settlement.ClientPackageId.HasValue;
                decimal bookingAmount = settlement.Amount ?? suggestedAmount;

                if (bookingRow.Amount != bookingAmount)
                    await LogAmountChangeInTransaction(uow, id, bookingRow.Id, bookingRow.Amount, bookingAmount, userId);

                bookingRow.Status = BookingStatus.Completed;
                bookingRow.Amount = bookingAmount;
                bookingRow.SuggestedAmount = suggestedAmount;
                bookingRow.IsAmountManuallyOverridden = settlement.Amount.HasValue && settlement.Amount.Value != suggestedAmount;

                if (hasPackage && !bookingRow.PackageCoverageApplied)
                {
                    // Paket-namirenje i novčano namirenje su međusobno isključivi dok nemamo surcharge/refund/
                    // store-credit semantiku (vidi spec fix section 2) — bookingRow je POSTOJEĆI redak (Confirmed
                    // termin koji se sad zatvara), mogao je već primiti djelomičnu novčanu uplatu preko POS
                    // Checkouta prije ovog completiona. Ne "orphan-aj" taj novac tihom primjenom paketa.
                    List<CheckoutItem> existingCheckoutItems = await _checkoutHandler.GetItemsForBooking(
                        uow, organizationId, bookingRow.Id.GetValueOrDefault());
                    decimal existingMonetaryPaid = BookingFinancialsCalculator.CalculatePaidAmount(existingCheckoutItems);
                    if (existingMonetaryPaid > 0m)
                        throw new BusinessRuleException(
                            ErrorCodes.BookingAlreadyHasMonetaryPayment,
                            "Booking već ima aktivnu novčanu uplatu — pokriće paketom se ne može primijeniti dok se ne poništi ta uplata.",
                            new { bookingId = bookingRow.Id, existingMonetaryPaid });

                    Guid clientPackageId = settlement.ClientPackageId.GetValueOrDefault();
                    await DeductPackageEntryInTransaction(uow, organizationId, clientPackageId, request.ServiceId, userId);
                    bookingRow.ClientPackageId = clientPackageId;
                    bookingRow.PackageCoverageApplied = true;
                }

                await _appointmentHandler.UpdateBooking(uow, bookingRow);

                // Booking je već persistiran (postojeći redak, samo ažuriran) — Payment sigurno može odmah nakon.
                if (!hasPackage && settlement.PaymentMethod.HasValue && settlement.IsPaid && bookingAmount > 0m)
                    await _paymentLedgerService.RecordPayment(
                        uow, organizationId, userId, appointment.CompanyId, bookingRow, settlement.PaymentMethod.Value, bookingAmount,
                        note: null, isCheckInGenerated: true);

                // Provizija se zarađuje ISTOM transakcijom kao completion — vidi CompleteNew.
                await _commissionLedgerService.GenerateForIndividualServiceCompletion(uow, organizationId, appointment, bookingRow);
            }

            await uow.CommitAsync();
        }
        catch (DbUpdateConcurrencyException)
        {
            throw new BusinessRuleException(
                ErrorCodes.ConcurrencyConflict, "Podaci su upravo promijenjeni od strane drugog zahtjeva — pokušajte ponovno.");
        }

        return await GetByIdInternal(organizationId, id);
    }

    /// <summary>Appointment-razina "odrađeno" za GRUPNI termin — jedina zadaća je prijelaz okvira Scheduled →
    /// Completed. Namjerno NE dira nijedan Booking redak: svaki se već razrješava neovisno kroz
    /// BookingService.SetStatus/GroupAttendanceService (check-in po klijentu), koji ostaje jedini put za
    /// Booking.Status. Ako neki Booking ostane Confirmed (nerazrješen) u trenutku zatvaranja, zatvaranje se
    /// SVEJEDNO dopušta (isto lijenije ponašanje kao ostatak ovog API-ja — upozorenje, ne blokada) uz
    /// GROUP_APPOINTMENT_UNRESOLVED_BOOKINGS upozorenje koje nabraja pogođene ClientId-jeve.</summary>
    public async Task<AppointmentDto> CompleteGroupAppointment(Guid organizationId, Guid userId, bool hasFullScope, Guid id)
    {
        Appointment appointment;

        await using (IUnitOfWork uow = await _unitOfWorkFactory.Begin())
        {
            // Zaključava Appointment redak (FOR UPDATE) i čita Form/Status/EmployeeId pod lockom PRIJE bilo kakve
            // provjere/mutacije — sprječava utrku s konkurentnim drugim completion/cancel zahtjevom na ISTOM
            // terminu (drugi zahtjev čeka na lock pa vidi svježe stanje nakon commita prvog, vidi spec section
            // 8-11). Bookings su uključeni jer se čitaju i nakon commita (unresolvedClientIds upozorenje niže).
            appointment = await _appointmentHandler.GetForUpdateWithBookings(uow, organizationId, id);
            if (appointment == null)
                throw new NotFoundAppException("Appointment", id);

            if (appointment.Form != AppointmentForm.Group)
                throw new ValidationAppException(
                    "Individualni termin se odrađuje kroz complete/complete-existing, ne kroz complete-group.");

            if (appointment.Status == AppointmentStatus.Completed)
                throw new BusinessRuleException(ErrorCodes.AlreadyCompleted, "Termin je već označen kao odrađen.");

            if (appointment.Status == AppointmentStatus.Cancelled)
                throw new BusinessRuleException(ErrorCodes.AppointmentNotMovable, "Otkazan termin se ne može označiti kao odrađen.");

            await ValidateOwnership(organizationId, userId, hasFullScope, appointment.EmployeeId.GetValueOrDefault());

            AppointmentStatus oldStatus = appointment.Status;
            appointment.Status = AppointmentStatus.Completed;
            appointment.UpdatedAt = DateTimeOffset.UtcNow;
            appointment.UpdatedBy = userId;

            await _appointmentHandler.UpdateScalar(uow, appointment);

            await _auditLogHandler.Add(uow, new AppointmentAuditLog
            {
                Id = Guid.NewGuid(),
                AppointmentId = id,
                ChangeType = "Status",
                OldValue = oldStatus.ToString(),
                NewValue = appointment.Status.ToString(),
                ChangedAt = DateTimeOffset.UtcNow,
                ChangedBy = userId
            });

            // Occurrence je zatvoren — preostali Waiting retci više nisu smisleni (spec section 20), ne promovira se.
            await _waitlistPromotionService.ExpireWaitingForAppointment(
                uow, organizationId, id, userId, WaitlistExpiredReasons.AppointmentCompleted);

            // Provizija se zarađuje PO CIJELOM odrađenom terminu, ne po sudioniku — vidi CommissionService
            // domensku napomenu (spec section 14/27/28).
            await _commissionLedgerService.GenerateForGroupServiceCompletion(uow, organizationId, appointment);

            await uow.CommitAsync();
        }

        List<Guid> unresolvedClientIds = appointment.Bookings
            .Where(b => b.Status == BookingStatus.Confirmed)
            .Select(b => b.ClientId)
            .ToList();

        AppointmentDto dto = await GetByIdInternal(organizationId, id);
        if (unresolvedClientIds.Count > 0)
            dto.Warnings.Add(new WarningDto(
                WarningCodes.GroupAppointmentUnresolvedBookings, new WarningUnresolvedBookingsDetails { ClientIds = unresolvedClientIds }));

        return dto;
    }

    /// <summary>Statusi koji ZAKLJUČUJU komercijalnu evidenciju bookinga — Update ih nikad ne repricinga
    /// (historijski Amount se ne smije mijenjati naknadno, vidi spec section 18/20).</summary>
    private static readonly BookingStatus[] TerminalBookingStatuses =
        { BookingStatus.Completed, BookingStatus.Cancelled, BookingStatus.NoShow };

    public async Task<AppointmentDto> Update(Guid organizationId, Guid userId, bool hasFullScope, Guid id, AppointmentUpdateRequest request)
    {
        Appointment appointment = await _appointmentHandler.GetWithBookingsForMutation(organizationId, id);
        if (appointment == null)
            throw new NotFoundAppException("Appointment", id);

        await ValidateOwnership(organizationId, userId, hasFullScope, appointment.EmployeeId.GetValueOrDefault());

        ServiceEntity service = await LoadServiceOrThrow(organizationId, request.ServiceId);
        bool overrideAvailability = request.OverrideAvailability && hasFullScope;
        await EnsureStructuralEligibility(organizationId, service, request.CompanyId, request.EmployeeId);
        Room room = await EnsureRoomExists(organizationId, request.CompanyId, request.RoomId);
        List<Client> clients = await EnsureClientsExist(organizationId, request.ClientIds);

        decimal suggestedAmount = await ResolveSuggestedAmount(organizationId, request.ServiceId, request.CompanyId, request.StartsAt);
        decimal amount = request.Amount ?? suggestedAmount;
        bool overridden = request.Amount.HasValue && request.Amount.Value != suggestedAmount;

        // Re-cijenjenje (persistira ga AppointmentHandler.UpdateWithBookings niže) se primjenjuje samo na
        // Bookinge koji NISU terminalni — historijski Amount na već odrađenom/otkazanom/izostalom Bookingu
        // se ne dira (vidi TerminalBookingStatuses). Ovdje samo audit-logiramo promjenu za te retke.
        List<Guid> requestedClientIds = request.ClientIds.Distinct().ToList();
        foreach (Booking booking in appointment.Bookings.Where(b =>
            requestedClientIds.Contains(b.ClientId) && !TerminalBookingStatuses.Contains(b.Status) && amount != b.Amount))
            await LogAmountChange(id, booking.Id, booking.Amount, amount, userId);

        if (appointment.EmployeeId != request.EmployeeId)
            await LogEmployeeChange(id, appointment.EmployeeId, request.EmployeeId, userId);

        appointment.StartsAt = request.StartsAt;
        appointment.DurationMinutes = service.DefaultDurationMinutes;
        appointment.ServiceId = request.ServiceId;
        appointment.EmployeeId = request.EmployeeId;
        appointment.CompanyId = request.CompanyId;
        appointment.RoomId = request.RoomId;
        appointment.Note = request.Note;
        appointment.UpdatedAt = DateTimeOffset.UtcNow;
        appointment.UpdatedBy = userId;

        List<WarningDto> warnings = new List<WarningDto>();
        warnings.AddRange(await EnsureWorkforceAvailability(
            organizationId, request.EmployeeId, request.CompanyId, request.StartsAt, appointment.DurationMinutes, overrideAvailability));

        await EnsureNoHardOverlap(organizationId, request.EmployeeId, clients, request.StartsAt, appointment.DurationMinutes, excludeId: id, room);

        await _appointmentHandler.UpdateWithBookings(appointment, requestedClientIds, amount, suggestedAmount, overridden);

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

        await ValidateOwnership(organizationId, userId, hasFullScope, appointment.EmployeeId.GetValueOrDefault());

        bool overrideAvailability = request.OverrideAvailability && hasFullScope;

        if (request.EmployeeId.HasValue)
            await EnsureEmployeeExists(organizationId, request.EmployeeId.Value);

        if (request.CompanyId.HasValue)
            await EnsureCompanyExists(organizationId, request.CompanyId.Value);

        Guid effectiveEmployeeId = request.EmployeeId ?? appointment.EmployeeId.GetValueOrDefault();
        Guid effectiveCompanyId = request.CompanyId ?? appointment.CompanyId;

        Appointment full = await _appointmentHandler.GetById(organizationId, id);
        List<Client> clients = full.Bookings.Where(b => b.Status != BookingStatus.Cancelled && b.Status != BookingStatus.NoShow)
            .Select(b => b.Client).ToList();

        ServiceEntity service = await LoadServiceOrThrow(organizationId, appointment.ServiceId);
        await EnsureStructuralEligibility(organizationId, service, effectiveCompanyId, effectiveEmployeeId);

        appointment.StartsAt = request.StartsAt;
        if (request.EmployeeId.HasValue && request.EmployeeId.Value != appointment.EmployeeId)
        {
            await LogEmployeeChange(id, appointment.EmployeeId, request.EmployeeId.Value, userId);
            appointment.EmployeeId = request.EmployeeId.Value;
        }
        if (request.CompanyId.HasValue)
            appointment.CompanyId = request.CompanyId.Value;
        if (request.RoomId.HasValue)
            appointment.RoomId = request.RoomId.Value;
        appointment.UpdatedAt = DateTimeOffset.UtcNow;
        appointment.UpdatedBy = userId;

        // Efektivna prostorija se revalidira i kad nije eksplicitno poslana u zahtjevu — pomicanje termina u drugu
        // poslovnicu bez zadanog RoomId inače bi ostavilo prostoriju iz stare poslovnice na terminu nove.
        Room room = await EnsureRoomExists(organizationId, appointment.CompanyId, appointment.RoomId);

        List<WarningDto> warnings = new List<WarningDto>();
        warnings.AddRange(await EnsureWorkforceAvailability(
            organizationId, effectiveEmployeeId, appointment.CompanyId, request.StartsAt, appointment.DurationMinutes, overrideAvailability));

        await EnsureNoHardOverlap(organizationId, effectiveEmployeeId, clients, request.StartsAt, appointment.DurationMinutes, excludeId: id, room);

        await _appointmentHandler.UpdateScalar(appointment);

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

        if (appointment.CreatedAt.UtcDateTime.Date != DateTimeOffset.UtcNow.UtcDateTime.Date)
            throw new BusinessRuleException(ErrorCodes.SameDayOnly, "Termin se može trajno obrisati samo istog dana kad je unesen — u suprotnom ga otkažite.");

        await _appointmentHandler.Delete(appointment);
    }

    public async Task<List<AppointmentDto>> CreateRecurring(Guid organizationId, Guid userId, bool hasFullScope, RecurringAppointmentCreateRequest request)
    {
        if (request.EndDate < request.FirstOccurrenceStartsAt)
            throw new ValidationAppException("Datum kraja ne smije biti prije prvog termina.");

        ServiceEntity service = await LoadServiceOrThrow(organizationId, request.ServiceId);
        bool overrideAvailability = request.OverrideAvailability && hasFullScope;
        List<DateTimeOffset> occurrences = BuildOccurrenceDates(request.RecurrenceType, request.FirstOccurrenceStartsAt, request.EndDate);

        await EnsureStructuralEligibility(organizationId, service, request.CompanyId, request.EmployeeId);
        Room room = await EnsureRoomExists(organizationId, request.CompanyId, request.RoomId);

        Dictionary<DateTimeOffset, List<WarningDto>> warningsByOccurrence = await EnsureNoRecurringConflicts(
            organizationId, request.EmployeeId, request.CompanyId, occurrences, service.DefaultDurationMinutes, overrideAvailability, room);

        await ValidateOwnership(organizationId, userId, hasFullScope, request.EmployeeId);
        List<Client> clients = await EnsureClientsExist(organizationId, request.ClientIds);

        await EnsureNoRecurringClientOverlap(organizationId, clients, occurrences, service.DefaultDurationMinutes);

        Guid recurrenceGroupId = Guid.NewGuid();
        List<Appointment> toCreate = new List<Appointment>();

        foreach (DateTimeOffset occurrence in occurrences)
        {
            decimal suggestedAmount = await ResolveSuggestedAmount(organizationId, request.ServiceId, request.CompanyId, occurrence);

            Guid appointmentId = Guid.NewGuid();
            Appointment appointment = new Appointment
            {
                Id = appointmentId,
                OrganizationId = organizationId,
                Form = AppointmentForm.Individual,
                StartsAt = occurrence,
                DurationMinutes = service.DefaultDurationMinutes,
                ServiceId = request.ServiceId,
                EmployeeId = request.EmployeeId,
                CompanyId = request.CompanyId,
                RoomId = request.RoomId,
                Status = AppointmentStatus.Scheduled,
                Note = request.Note,
                RecurrenceGroupId = recurrenceGroupId,
                CreatedAt = DateTimeOffset.UtcNow,
                CreatedBy = userId
            };

            foreach (Client client in clients)
            {
                appointment.Bookings.Add(new Booking
                {
                    Id = Guid.NewGuid(),
                    OrganizationId = organizationId,
                    AppointmentId = appointmentId,
                    ClientId = client.Id.GetValueOrDefault(),
                    Status = BookingStatus.Confirmed,
                    Amount = suggestedAmount,
                    SuggestedAmount = suggestedAmount,
                    CreatedAt = DateTimeOffset.UtcNow
                });
            }

            toCreate.Add(appointment);
        }

        await _appointmentHandler.AddRange(toCreate);

        List<AppointmentDto> created = new List<AppointmentDto>();
        foreach (Appointment appointment in toCreate)
        {
            AppointmentDto dto = await GetByIdInternal(organizationId, appointment.Id.GetValueOrDefault());
            if (warningsByOccurrence.TryGetValue(appointment.StartsAt, out List<WarningDto> occurrenceWarnings))
                dto.Warnings = occurrenceWarnings;
            created.Add(dto);
        }

        return created;
    }

    /// <summary>Weekly = postojeće ponašanje (+7 dana). Daily = svaki kalendarski dan uključivo vikend, bez preskakanja.</summary>
    private static List<DateTimeOffset> BuildOccurrenceDates(RecurrenceType recurrenceType, DateTimeOffset first, DateTimeOffset end)
    {
        int stepDays = recurrenceType == RecurrenceType.Daily ? 1 : 7;

        List<DateTimeOffset> occurrences = new List<DateTimeOffset>();
        for (DateTimeOffset occurrence = first; occurrence <= end; occurrence = occurrence.AddDays(stepDays))
            occurrences.Add(occurrence);

        return occurrences;
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
        Guid organizationId, Guid employeeId, Guid companyId, List<DateTimeOffset> occurrences, int durationMinutes,
        bool overrideAvailability, Room room = null)
    {
        DateTimeOffset rangeFrom = occurrences[0].AddDays(-1);
        DateTimeOffset rangeTo = occurrences[^1].AddDays(1);

        List<Appointment> candidateAppointments = await _appointmentHandler.GetForEmployeeInRange(
            organizationId, employeeId, rangeFrom, rangeTo);

        bool checkRoom = room != null && !room.AllowConcurrentBookings;
        List<Appointment> candidateRoomAppointments = checkRoom
            ? await _appointmentHandler.GetForRoomInRange(organizationId, room.Id.GetValueOrDefault(), rangeFrom, rangeTo)
            : new List<Appointment>();

        List<ScheduleBreak> candidateBreaks = await _scheduleBreakHandler.GetForEmployeeInRange(
            organizationId, employeeId, rangeFrom, rangeTo);

        // Učitano JEDNOM za cijeli raspon niza (apsencije + eventualni work-override redovi) — dijeli se između
        // absenceHit provjere i working-hours provjere ispod, isti obrazac kao candidateAppointments.
        List<RosterEntry> rosterEntriesInRange = await _rosterEntryHandler.GetForPeriod(
            organizationId, new List<Guid> { employeeId }, occurrences[0], occurrences[^1]);

        List<RosterEntry> absences = rosterEntriesInRange.Where(e => e.RosterType.IsAbsence).ToList();

        WorkingHoursTemplate employeeTemplate = await _workingHoursTemplateHandler.GetForEmployee(organizationId, employeeId);
        WorkingHoursTemplate companyTemplate = await _workingHoursTemplateHandler.GetForCompany(organizationId, companyId);

        // Učitano JEDNOM za cijeli raspon niza — isti obrazac kao rosterEntriesInRange iznad.
        List<CompanyHoliday> companyHolidaysInRange = await _companyHolidayHandler.GetForCompaniesInRange(
            organizationId, new List<Guid> { companyId }, occurrences[0], occurrences[^1]);

        List<RecurringConflictDetail> hardConflicts = new List<RecurringConflictDetail>();
        Dictionary<DateTimeOffset, List<WarningDto>> warningsByOccurrence = new Dictionary<DateTimeOffset, List<WarningDto>>();

        foreach (DateTimeOffset occurrence in occurrences)
        {
            DateTimeOffset occurrenceEnd = occurrence.AddMinutes(durationMinutes);
            List<WarningDto> warnings = new List<WarningDto>();

            bool appointmentHit = candidateAppointments.Any(a =>
                a.StartsAt < occurrenceEnd && occurrence < a.StartsAt.AddMinutes(a.DurationMinutes));
            if (appointmentHit)
                hardConflicts.Add(new RecurringConflictDetail { Date = occurrence, Reason = ErrorCodes.RecurringConflictReasonAppointment });

            bool roomHit = checkRoom && candidateRoomAppointments.Any(a =>
                a.StartsAt < occurrenceEnd && occurrence < a.StartsAt.AddMinutes(a.DurationMinutes));
            if (roomHit)
                hardConflicts.Add(new RecurringConflictDetail { Date = occurrence, Reason = ErrorCodes.RecurringConflictReasonRoom });

            bool breakHit = candidateBreaks.Any(b =>
                b.StartsAt < occurrenceEnd && occurrence < b.StartsAt.AddMinutes(b.DurationMinutes));

            bool absenceHit = absences.Any(a =>
                a.DateFrom.Date <= occurrence.Date && (a.DateTo == null || occurrence.Date <= a.DateTo.Value.Date));

            List<RosterEntry> rosterEntriesForOccurrence = rosterEntriesInRange
                .Where(e => !e.RosterType.IsAbsence && e.DateFrom.Date == occurrence.Date)
                .ToList();

            List<CompanyHoliday> companyHolidaysForOccurrence = companyHolidaysInRange
                .Where(h => h.Date.Date == occurrence.Date)
                .ToList();

            bool withinHours = absenceHit || IsWithinWorkingHours(
                employeeTemplate, companyTemplate, rosterEntriesForOccurrence, occurrence, durationMinutes, companyHolidaysForOccurrence);

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

        List<Appointment> candidateAppointments = await _appointmentHandler.GetForClientsInRange(
            organizationId, clientIds, rangeFrom, rangeTo);

        foreach (DateTimeOffset occurrence in occurrences)
        {
            DateTimeOffset occurrenceEnd = occurrence.AddMinutes(durationMinutes);

            List<Appointment> overlapping = candidateAppointments
                .Where(a => a.StartsAt < occurrenceEnd && occurrence < a.StartsAt.AddMinutes(a.DurationMinutes))
                .ToList();

            foreach (Client client in clients)
            {
                bool hasOverlap = overlapping.Any(a => a.Bookings.Any(b =>
                    b.ClientId == client.Id && b.Status != BookingStatus.Cancelled && b.Status != BookingStatus.NoShow));
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
        DateTimeOffset now = DateTimeOffset.UtcNow;
        DateTimeOffset requestedDay = query.Date.Date;

        if (requestedDay < now.Date)
            return new List<EmployeeAvailableSlotsDto>();

        TimeSpan? minimumStart = requestedDay == now.Date ? now.TimeOfDay + AvailableSlotLeadTime : null;

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

        DateTimeOffset dayStart = requestedDay;
        DateTimeOffset dayEnd = dayStart.AddDays(1).AddTicks(-1);

        List<WorkingHoursTemplate> employeeTemplates = await _workingHoursTemplateHandler.GetForEmployees(organizationId, employeeIds);
        WorkingHoursTemplate companyTemplate = await _workingHoursTemplateHandler.GetForCompany(organizationId, query.CompanyId);
        List<RosterEntry> rosterEntries = await _rosterEntryHandler.GetForPeriod(organizationId, employeeIds, dayStart, dayStart);
        List<Appointment> appointments = await _appointmentHandler.GetForEmployeesInRange(organizationId, employeeIds, dayStart, dayEnd);
        List<ScheduleBreak> breaks = await _scheduleBreakHandler.GetForEmployeesInRange(organizationId, employeeIds, dayStart, dayEnd);
        List<CompanyHoliday> companyHolidays = await _companyHolidayHandler.GetForCompaniesInRange(
            organizationId, new List<Guid> { query.CompanyId }, dayStart, dayStart);

        (List<WorkingHoursCalculator.Interval> companyIntervals, _) =
            WorkingHoursCalculator.GetEffectiveCompanyIntervals(companyTemplate, companyHolidays, dayStart);

        List<EmployeeAvailableSlotsDto> result = new List<EmployeeAvailableSlotsDto>();

        foreach (Employee employee in employees)
        {
            Guid employeeId = employee.Id.GetValueOrDefault();

            WorkingHoursTemplate employeeTemplate = employeeTemplates.FirstOrDefault(t => t.EmployeeId == employeeId);
            List<RosterEntry> rosterForEmployee = rosterEntries.Where(r => r.EmployeeId == employeeId).ToList();

            (List<WorkingHoursCalculator.Interval> employeeIntervals, _) =
                WorkingHoursCalculator.GetEffectiveEmployeeIntervals(employeeTemplate, rosterForEmployee, dayStart);

            List<WorkingHoursCalculator.Interval> effectiveIntervals =
                WorkingHoursCalculator.IntersectIntervals(employeeIntervals, companyIntervals);

            List<(TimeSpan Start, TimeSpan End)> busy = new List<(TimeSpan Start, TimeSpan End)>();
            busy.AddRange(appointments
                .Where(a => a.EmployeeId == employeeId)
                .Select(a => (a.StartsAt.TimeOfDay, a.StartsAt.TimeOfDay + TimeSpan.FromMinutes(a.DurationMinutes))));
            busy.AddRange(breaks
                .Where(b => b.EmployeeId == employeeId)
                .Select(b => (b.StartsAt.TimeOfDay, b.StartsAt.TimeOfDay + TimeSpan.FromMinutes(b.DurationMinutes))));

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

    private async Task<AppointmentDto> CreateInternal(
        Guid organizationId, Guid userId, bool hasFullScope, AppointmentCreateRequest request, Guid? recurrenceGroupId)
    {
        await ValidateOwnership(organizationId, userId, hasFullScope, request.EmployeeId);
        ServiceEntity service = await LoadServiceOrThrow(organizationId, request.ServiceId);
        bool overrideAvailability = request.OverrideAvailability && hasFullScope;
        await EnsureStructuralEligibility(organizationId, service, request.CompanyId, request.EmployeeId);
        Room room = await EnsureRoomExists(organizationId, request.CompanyId, request.RoomId);
        List<Client> clients = await EnsureClientsExist(organizationId, request.ClientIds);

        decimal suggestedAmount = await ResolveSuggestedAmount(organizationId, request.ServiceId, request.CompanyId, request.StartsAt);
        decimal amount = request.Amount ?? suggestedAmount;
        bool overridden = request.Amount.HasValue && request.Amount.Value != suggestedAmount;

        Guid appointmentId = Guid.NewGuid();
        Appointment appointment = new Appointment
        {
            Id = appointmentId,
            OrganizationId = organizationId,
            Form = AppointmentForm.Individual,
            StartsAt = request.StartsAt,
            DurationMinutes = service.DefaultDurationMinutes,
            ServiceId = request.ServiceId,
            EmployeeId = request.EmployeeId,
            CompanyId = request.CompanyId,
            RoomId = request.RoomId,
            Status = AppointmentStatus.Scheduled,
            Note = request.Note,
            RecurrenceGroupId = recurrenceGroupId,
            CreatedAt = DateTimeOffset.UtcNow,
            CreatedBy = userId
        };

        foreach (Client client in clients)
        {
            appointment.Bookings.Add(new Booking
            {
                Id = Guid.NewGuid(),
                OrganizationId = organizationId,
                AppointmentId = appointmentId,
                ClientId = client.Id.GetValueOrDefault(),
                Status = BookingStatus.Confirmed,
                Amount = amount,
                SuggestedAmount = suggestedAmount,
                IsAmountManuallyOverridden = overridden,
                CreatedAt = DateTimeOffset.UtcNow
            });
        }

        List<WarningDto> warnings = new List<WarningDto>();
        warnings.AddRange(await EnsureWorkforceAvailability(
            organizationId, request.EmployeeId, request.CompanyId, request.StartsAt, appointment.DurationMinutes, overrideAvailability));

        await EnsureNoHardOverlap(organizationId, request.EmployeeId, clients, request.StartsAt, appointment.DurationMinutes, excludeId: null, room);

        await _appointmentHandler.Add(appointment);

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

            await ValidateOwnership(organizationId, userId, hasFullScope, appointment.EmployeeId.GetValueOrDefault());

            // Completed je terminalno i za Cancel/MarkNoShow — već odrađen (i eventualno proviziran) termin se
            // ne smije naknadno "otkazati" kroz ove putanje (vidi spec section 1/3, CommissionService domenska
            // napomena o "poznatoj postojećoj praznini" koju ovo zatvara).
            if (appointment.Status == AppointmentStatus.Completed)
                throw new BusinessRuleException(
                    ErrorCodes.AlreadyCompleted,
                    "Termin je već odrađen (Completed) i ne može se otkazati niti označiti kao izostanak.");

            AppointmentStatus oldStatus = appointment.Status;
            appointment.Status = AppointmentStatus.Cancelled;
            appointment.CancellationReason = request.CancellationReason;
            appointment.UpdatedAt = DateTimeOffset.UtcNow;
            appointment.UpdatedBy = userId;

            await _appointmentHandler.UpdateScalar(uow, appointment);

            if (oldStatus != appointment.Status)
            {
                await _auditLogHandler.Add(uow, new AppointmentAuditLog
                {
                    Id = Guid.NewGuid(),
                    AppointmentId = id,
                    ChangeType = "Status",
                    OldValue = oldStatus.ToString(),
                    NewValue = appointment.Status.ToString(),
                    ChangedAt = DateTimeOffset.UtcNow,
                    ChangedBy = userId
                });
            }

            foreach (Booking booking in appointment.Bookings.Where(b => b.Status == BookingStatus.Confirmed))
            {
                BookingStatus oldBookingStatus = booking.Status;
                booking.Status = targetBookingStatus;
                booking.CancellationReason = request.CancellationReason;
                booking.UpdatedAt = DateTimeOffset.UtcNow;
                booking.UpdatedBy = userId;
                // IsLateCancellation namjerno OSTAJE null ovdje (poslovno/appointment-wide otkazivanje, ne
                // klijentska inicijativa) — vidi Booking.cs domensku napomenu i spec section 38.

                bool shouldReturn = returnClientIds.Contains(booking.ClientId) &&
                    booking.PackageCoverageApplied && !booking.PackageCoverageReturned && booking.ClientPackageId.HasValue;

                if (shouldReturn)
                {
                    await ReturnPackageEntryInTransaction(uow, organizationId, booking.ClientPackageId.Value, appointment.ServiceId, userId);
                    booking.PackageCoverageReturned = true;
                    booking.PackageCoverageReturnedAt = DateTimeOffset.UtcNow;
                    booking.PackageCoverageReturnedBy = userId;
                }

                await _appointmentHandler.UpdateBooking(uow, booking);

                await _auditLogHandler.Add(uow, new AppointmentAuditLog
                {
                    Id = Guid.NewGuid(),
                    AppointmentId = id,
                    BookingId = booking.Id,
                    ChangeType = "BookingStatus",
                    OldValue = oldBookingStatus.ToString(),
                    NewValue = booking.Status.ToString(),
                    ChangedAt = DateTimeOffset.UtcNow,
                    ChangedBy = userId
                });

                if (shouldReturn)
                {
                    await _auditLogHandler.Add(uow, new AppointmentAuditLog
                    {
                        Id = Guid.NewGuid(),
                        AppointmentId = id,
                        BookingId = booking.Id,
                        ChangeType = "BookingPackageCoverageReturned",
                        OldValue = "Applied",
                        NewValue = "Returned",
                        ChangedAt = DateTimeOffset.UtcNow,
                        ChangedBy = userId
                    });
                }
            }

            // Cijeli occurrence je zatvoren (otkazan ili bulk no-show, oboje završavaju na Appointment.Status =
            // Cancelled) — preostali Waiting retci više nisu smisleni, ne promovira se (spec section 19/41/42).
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

    private async Task ValidateOwnership(Guid organizationId, Guid userId, bool hasFullScope, Guid employeeId)
    {
        if (hasFullScope)
            return;

        Employee employee = await _employeeHandler.GetByUserId(organizationId, userId);
        if (employee == null || employee.Id != employeeId)
            throw new BusinessRuleException(ErrorCodes.NotOwner, "Trener smije upravljati samo svojim vlastitim terminima.");
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

    /// <summary>Kad je ClientPackageId popunjen, svaki klijent na terminu mora imati odabran svoj vlastiti
    /// valjani paket (npr. duo/par usluga: svaki klijent skida ulazak iz svog profila, neovisno o ostalima).
    /// Validira da Settlements pokriva SVAKI klijent termina TOČNO JEDNOM (mješovito plaćanje po klijentu —
    /// vidi spec section 10/12). Zamjenjuje staru ValidatePackageSelections (koja je pokrivala samo
    /// paket-granu uz jedan zajednički PaymentMethod za sve — mješovito plaćanje strukturno nije bilo moguće).</summary>
    private async Task<Dictionary<Guid, AppointmentClientSettlement>> ValidateSettlements(
        Guid organizationId, List<Guid> clientIds, Guid serviceId, DateTimeOffset date, List<AppointmentClientSettlement> settlements)
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
                    organizationId, settlement.ClientId, serviceId, date);

                if (eligible.All(p => p.Id != settlement.ClientPackageId.Value))
                    throw new BusinessRuleException(ErrorCodes.PackageNotEligible, "Odabrani paket nije valjan za klijenta ili ne pokriva ovu uslugu.");
            }

            result[settlement.ClientId] = settlement;
        }

        return result;
    }

    /// <summary>Koriste svi write endpointi (Create/CompleteNew/CompleteExisting/Update/Move preko poziva ovdje) —
    /// STVARNI sudari (trener već ima termin/grupu, soba zauzeta, klijent već zakazan) uvijek bacaju
    /// APPOINTMENT_OVERLAP (409), NIKAD se ne mogu zaobići s OverrideAvailability (vidi spec section 20/21-23) —
    /// za razliku od EnsureWorkforceAvailability, ovdje nema override parametra.</summary>
    private async Task EnsureNoHardOverlap(
        Guid organizationId, Guid employeeId, List<Client> clients, DateTimeOffset startsAt, int durationMinutes, Guid? excludeId, Room room)
    {
        List<Appointment> employeeOverlaps = await _appointmentHandler
            .GetOverlappingForEmployee(organizationId, employeeId, startsAt, durationMinutes, excludeId);
        if (employeeOverlaps.Count > 0)
            throw new BusinessRuleException(ErrorCodes.AppointmentOverlap, "Trener već ima termin u ovom vremenskom razdoblju.");

        if (room != null && !room.AllowConcurrentBookings)
        {
            List<Appointment> roomOverlaps = await _appointmentHandler
                .GetOverlappingForRoom(organizationId, room.Id.GetValueOrDefault(), startsAt, durationMinutes, excludeId);
            if (roomOverlaps.Count > 0)
                throw new BusinessRuleException(ErrorCodes.AppointmentOverlap, "Prostorija je već zauzeta u ovom vremenskom razdoblju.");
        }

        List<Guid> clientIds = clients.Select(c => c.Id.GetValueOrDefault()).ToList();
        List<Appointment> clientOverlaps = await _appointmentHandler
            .GetOverlappingForClients(organizationId, clientIds, startsAt, durationMinutes, excludeId);

        foreach (Client client in clients)
        {
            bool hasOverlap = clientOverlaps.Any(a => a.Bookings.Any(b =>
                b.ClientId == client.Id && b.Status != BookingStatus.Cancelled && b.Status != BookingStatus.NoShow));
            if (hasOverlap)
                throw new BusinessRuleException(ErrorCodes.AppointmentOverlap, $"Klijent {client.FirstName} {client.LastName} je već zakazan u ovom vremenskom razdoblju.");
        }
    }

    /// <summary>Zamjenjuje staro BuildWorkingHoursWarning — sada TVRDA blokada (throw) za sve četiri "meke"
    /// radne-snage kategorije (odsutnost/pauza/praznik/izvan-radnog-vremena) OSIM kad je overrideAvailability=true
    /// (već provjereno kod pozivatelja da ima appointments.write.all), kad se umjesto bacanja vraća WarningDto
    /// lista (vidljivost bez blokade — isto ponašanje kao prije ovog zahvata). Koriste svi write endpointi osim
    /// CompleteExisting (retroaktivno evidentiranje odrađenog, ne planiranje unaprijed) i CompleteNew za StartsAt
    /// u prošlosti (isti razlog, provjereno kod pozivatelja).</summary>
    private async Task<List<WarningDto>> EnsureWorkforceAvailability(
        Guid organizationId, Guid employeeId, Guid companyId, DateTimeOffset startsAt, int durationMinutes, bool overrideAvailability)
    {
        WorkingHoursTemplate employeeTemplate = await _workingHoursTemplateHandler.GetForEmployee(organizationId, employeeId);
        WorkingHoursTemplate companyTemplate = await _workingHoursTemplateHandler.GetForCompany(organizationId, companyId);
        List<RosterEntry> rosterEntriesForDate = await _rosterEntryHandler.GetForPeriod(
            organizationId, new List<Guid> { employeeId }, startsAt.Date, startsAt.Date);
        List<CompanyHoliday> companyHolidaysForDate = await _companyHolidayHandler.GetForCompaniesInRange(
            organizationId, new List<Guid> { companyId }, startsAt.Date, startsAt.Date);
        List<ScheduleBreak> breakOverlaps = await _scheduleBreakHandler.GetOverlappingForEmployee(
            organizationId, employeeId, startsAt, durationMinutes, excludeId: null);

        bool absenceHit = rosterEntriesForDate.Any(e =>
            e.RosterType.IsAbsence && e.DateFrom.Date <= startsAt.Date && (e.DateTo == null || startsAt.Date <= e.DateTo.Value.Date));
        bool breakHit = breakOverlaps.Count > 0;

        List<RosterEntry> nonAbsenceEntries = rosterEntriesForDate.Where(e => !e.RosterType.IsAbsence).ToList();
        bool withinHours = absenceHit || IsWithinWorkingHours(
            employeeTemplate, companyTemplate, nonAbsenceEntries, startsAt, durationMinutes, companyHolidaysForDate);

        AppointmentEligibilityHelper.WorkforceViolation violation = AppointmentEligibilityHelper.Classify(
            absenceHit, breakHit, companyHolidaysForDate.Count > 0, withinHours);

        List<WarningDto> warnings = new List<WarningDto>();
        AppointmentEligibilityHelper.ThrowOrWarn(violation, overrideAvailability, warnings);
        return warnings;
    }

    /// <summary>Čista provjera dijeljena s EnsureNoRecurringConflicts (batch grana) — rosterEntriesForDate/
    /// companyHolidaysForDate moraju sadržavati SAMO redove relevantne za TOČNO taj datum (apsencija čiji raspon
    /// ga pokriva, work-redovi s DateFrom==taj datum, praznik čiji Date==taj datum), ne cijeli raspon niza.</summary>
    private static bool IsWithinWorkingHours(
        WorkingHoursTemplate employeeTemplate, WorkingHoursTemplate companyTemplate, List<RosterEntry> rosterEntriesForDate,
        DateTimeOffset startsAt, int durationMinutes, List<CompanyHoliday> companyHolidaysForDate)
    {
        TimeSpan start = startsAt.TimeOfDay;
        TimeSpan end = start + TimeSpan.FromMinutes(durationMinutes);

        (List<WorkingHoursCalculator.Interval> employeeIntervals, _) =
            WorkingHoursCalculator.GetEffectiveEmployeeIntervals(employeeTemplate, rosterEntriesForDate, startsAt);
        (List<WorkingHoursCalculator.Interval> companyIntervals, _) =
            WorkingHoursCalculator.GetEffectiveCompanyIntervals(companyTemplate, companyHolidaysForDate, startsAt);

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
                bool overlapsBusy = busy.Any(b => candidateStart < b.End && b.Start < candidateEnd);

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

    /// <summary>Odbija ulazak iz paketa unutar zajedničke transakcije s terminom (vidi IUnitOfWork) — bez ovoga bi
    /// spremanje termina i odbijanje ulaska bila dva neovisna SaveChanges-a, pa bi pad usred niza mogao ostaviti
    /// termin spremljen a ulazak neodbjen (ili obrnuto).</summary>
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

    /// <summary>Vraća ulazak u paket unutar zajedničke transakcije s terminom — vidi DeductPackageEntryInTransaction.</summary>
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

        return new AppointmentScheduleCellDto
        {
            Id = a.Id.GetValueOrDefault(),
            StartsAt = a.StartsAt,
            DurationMinutes = a.DurationMinutes,
            ServiceId = a.ServiceId,
            ServiceName = a.Service?.Name,
            ServiceCategoryColorHex = a.Service?.ColorHex,
            EmployeeId = a.EmployeeId,
            EmployeeName = a.Employee != null ? $"{a.Employee.FirstName} {a.Employee.LastName}" : null,
            CompanyId = a.CompanyId,
            CompanyName = a.Company?.Name,
            RoomId = a.RoomId,
            RoomName = a.Room?.Name,
            ClientNames = clients.Select(c => $"{c.FirstName} {c.LastName}").ToList(),
            ClientIds = clients.Select(c => c.Id.GetValueOrDefault()).ToList(),
            Status = a.Status,
            IsCancelled = a.Status == AppointmentStatus.Cancelled,
            Form = a.Form,
            GroupId = a.GroupId,
            GroupName = isGroup ? a.Group?.Name : null,
            AttendanceCount = isGroup ? a.Bookings.Count(b => b.Status == BookingStatus.Completed) : (int?)null,
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
        decimal outstandingAmount = BookingFinancialsCalculator.CalculateOutstanding(booking);

        return new ClientAppointmentHistoryDto
        {
            Id = a.Id.GetValueOrDefault(),
            Form = a.Form,
            StartsAt = a.StartsAt,
            DurationMinutes = a.DurationMinutes,
            ServiceId = a.ServiceId,
            ServiceName = a.Service?.Name,
            ServiceCategoryColorHex = a.Service?.ColorHex,
            EmployeeId = a.EmployeeId,
            EmployeeName = a.Employee != null ? $"{a.Employee.FirstName} {a.Employee.LastName}" : null,
            CompanyId = a.CompanyId,
            CompanyName = a.Company?.Name,
            Status = a.Status,
            GroupId = a.GroupId,
            GroupName = a.Group?.Name,
            Amount = booking.Amount,
            PaidAmount = BookingFinancialsCalculator.CalculatePaidAmount(booking),
            OutstandingAmount = outstandingAmount,
            IsPaid = outstandingAmount <= 0m,
            BookingId = booking.Id.GetValueOrDefault(),
            BookingStatus = booking.Status,
            ClientPackageId = booking.ClientPackageId,
            CoverageType = booking.CoverageType,
            PackageCoverageApplied = booking.PackageCoverageApplied,
            PackageCoverageReturned = booking.PackageCoverageReturned,
            BookingNote = booking.Note,
            BookingCancellationReason = booking.CancellationReason
        };
    }

    private static AppointmentDto ToDto(Appointment a)
    {
        return new AppointmentDto
        {
            Id = a.Id.GetValueOrDefault(),
            Form = a.Form,
            StartsAt = a.StartsAt,
            DurationMinutes = a.DurationMinutes,
            ServiceId = a.ServiceId,
            ServiceName = a.Service?.Name,
            ServiceCategoryColorHex = a.Service?.ColorHex,
            EmployeeId = a.EmployeeId,
            EmployeeName = a.Employee != null ? $"{a.Employee.FirstName} {a.Employee.LastName}" : null,
            CompanyId = a.CompanyId,
            CompanyName = a.Company?.Name,
            RoomId = a.RoomId,
            RoomName = a.Room?.Name,
            Status = a.Status,
            Note = a.Note,
            CancellationReason = a.CancellationReason,
            GroupId = a.GroupId,
            GroupName = a.Group?.Name,
            RecurrenceGroupId = a.RecurrenceGroupId,
            Bookings = a.Bookings.Select(b =>
            {
                decimal outstandingAmount = BookingFinancialsCalculator.CalculateOutstanding(b);
                return new BookingDto
                {
                    Id = b.Id.GetValueOrDefault(),
                    ClientId = b.ClientId,
                    ClientName = b.Client != null ? $"{b.Client.FirstName} {b.Client.LastName}" : null,
                    Status = b.Status,
                    Amount = b.Amount,
                    SuggestedAmount = b.SuggestedAmount,
                    IsAmountManuallyOverridden = b.IsAmountManuallyOverridden,
                    PaidAmount = BookingFinancialsCalculator.CalculatePaidAmount(b),
                    OutstandingAmount = outstandingAmount,
                    IsPaid = outstandingAmount <= 0m,
                    ClientPackageId = b.ClientPackageId,
                    CoverageType = b.CoverageType,
                    PackageCoverageApplied = b.PackageCoverageApplied,
                    PackageCoverageReturned = b.PackageCoverageReturned,
                    Payments = BookingFinancialsCalculator.GetPayments(b).Select(PaymentDtoFactory.ToDto).ToList(),
                    Note = b.Note,
                    CancellationReason = b.CancellationReason,
                    IsLateCancellation = b.IsLateCancellation
                };
            }).ToList(),
            CreatedAt = a.CreatedAt,
            CreatedBy = a.CreatedBy,
            UpdatedAt = a.UpdatedAt,
            UpdatedBy = a.UpdatedBy
        };
    }
}
