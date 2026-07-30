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
using BlueDragon.DuneLight.Infrastructure.Domain.Models.Clients;
using BlueDragon.DuneLight.Infrastructure.Domain.Models.Employees;
using BlueDragon.DuneLight.Infrastructure.Domain.Models.Roster;
using BlueDragon.DuneLight.Infrastructure.Handlers.Interfaces;
using ServiceEntity = BlueDragon.DuneLight.Infrastructure.Domain.Models.Catalog.Service;

namespace BlueDragon.DuneLight.Infrastructure.Services;

public class AppointmentService : IAppointmentService
{
    private readonly IAppointmentHandler _appointmentHandler;
    private readonly IAppointmentAuditLogHandler _auditLogHandler;
    private readonly IClientPackageService _clientPackageService;
    private readonly IPricingService _pricingService;
    private readonly IServiceHandler _serviceHandler;
    private readonly IEmployeeHandler _employeeHandler;
    private readonly ILocationHandler _locationHandler;
    private readonly IClientHandler _clientHandler;
    private readonly IRosterEntryHandler _rosterEntryHandler;

    public AppointmentService(
        IAppointmentHandler appointmentHandler,
        IAppointmentAuditLogHandler auditLogHandler,
        IClientPackageService clientPackageService,
        IPricingService pricingService,
        IServiceHandler serviceHandler,
        IEmployeeHandler employeeHandler,
        ILocationHandler locationHandler,
        IClientHandler clientHandler,
        IRosterEntryHandler rosterEntryHandler)
    {
        _appointmentHandler = appointmentHandler;
        _auditLogHandler = auditLogHandler;
        _clientPackageService = clientPackageService;
        _pricingService = pricingService;
        _serviceHandler = serviceHandler;
        _employeeHandler = employeeHandler;
        _locationHandler = locationHandler;
        _clientHandler = clientHandler;
        _rosterEntryHandler = rosterEntryHandler;
    }

    public Task<AppointmentDto> Create(Guid organizationId, Guid userId, bool isAdmin, AppointmentCreateRequest request)
    {
        return CreateInternal(organizationId, userId, isAdmin, request, recurrenceGroupId: null);
    }

    public async Task<AppointmentDto> CompleteNew(Guid organizationId, Guid userId, bool isAdmin, AppointmentCompleteRequest request)
    {
        await ValidateOwnership(organizationId, userId, isAdmin, request.EmployeeId);
        ServiceEntity service = await LoadServiceOrThrow(organizationId, request.ServiceId);
        await EnsureEmployeeExists(organizationId, request.EmployeeId);
        await EnsureLocationExists(organizationId, request.LocationId);
        List<Client> clients = await EnsureClientsExist(organizationId, request.ClientIds);

        decimal suggestedAmount = await ResolveSuggestedAmount(organizationId, request.ServiceId, request.LocationId, request.StartsAt);
        decimal amount = request.Amount ?? suggestedAmount;
        bool overridden = request.Amount.HasValue && request.Amount.Value != suggestedAmount;

        Dictionary<Guid, Guid> packageByClient = await ValidatePackageSelections(
            organizationId, clients.Select(c => c.Id.GetValueOrDefault()).ToList(),
            request.ServiceId, request.StartsAt, request.PaymentMethod, request.PackageSelections);

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
            LocationId = request.LocationId,
            Amount = amount,
            SuggestedAmount = suggestedAmount,
            IsAmountManuallyOverridden = overridden,
            PaymentMethod = request.PaymentMethod,
            IsPaid = true,
            Status = AppointmentStatus.Completed,
            Note = request.Note,
            CreatedAt = DateTimeOffset.UtcNow,
            CreatedBy = userId
        };

        foreach (Client client in clients)
        {
            Guid clientId = client.Id.GetValueOrDefault();
            bool hasPackage = packageByClient.TryGetValue(clientId, out Guid clientPackageId);
            appointment.Clients.Add(new AppointmentClient
            {
                Id = Guid.NewGuid(),
                AppointmentId = appointmentId,
                ClientId = clientId,
                ClientPackageId = hasPackage ? clientPackageId : (Guid?)null,
                PackageEntryDeducted = hasPackage,
                CreatedAt = DateTimeOffset.UtcNow
            });
        }

        await EnsureNoOverlap(
            organizationId, request.EmployeeId, clients, request.StartsAt, appointment.DurationMinutes, excludeId: null);

        await _appointmentHandler.Add(appointment);

        foreach (KeyValuePair<Guid, Guid> kvp in packageByClient)
            await _clientPackageService.DeductEntry(organizationId, kvp.Value, request.ServiceId, userId);

        AppointmentDto dto = await GetByIdInternal(organizationId, appointmentId);
        return dto;
    }

    public async Task<AppointmentDto> CompleteExisting(Guid organizationId, Guid userId, bool isAdmin, Guid id, AppointmentCompleteRequest request)
    {
        Appointment appointment = await _appointmentHandler.GetByIdLight(organizationId, id);
        if (appointment == null)
            throw new NotFoundAppException("Appointment", id);

        if (appointment.Status == AppointmentStatus.Completed)
            throw new BusinessRuleException(ErrorCodes.AlreadyCompleted, "Termin je već označen kao odrađen.");

        await ValidateOwnership(organizationId, userId, isAdmin, appointment.EmployeeId.GetValueOrDefault());

        ServiceEntity service = await LoadServiceOrThrow(organizationId, request.ServiceId);
        await EnsureEmployeeExists(organizationId, request.EmployeeId);
        await EnsureLocationExists(organizationId, request.LocationId);
        List<Client> clients = await EnsureClientsExist(organizationId, request.ClientIds);

        decimal suggestedAmount = await ResolveSuggestedAmount(organizationId, request.ServiceId, request.LocationId, request.StartsAt);
        decimal amount = request.Amount ?? suggestedAmount;
        bool overridden = request.Amount.HasValue && request.Amount.Value != suggestedAmount;

        Dictionary<Guid, Guid> packageByClient = await ValidatePackageSelections(
            organizationId, clients.Select(c => c.Id.GetValueOrDefault()).ToList(),
            request.ServiceId, request.StartsAt, request.PaymentMethod, request.PackageSelections);

        if (amount != appointment.Amount)
            await LogAmountChange(id, appointment.Amount, amount, userId);

        appointment.StartsAt = request.StartsAt;
        appointment.DurationMinutes = service.DefaultDurationMinutes;
        appointment.ServiceId = request.ServiceId;
        appointment.EmployeeId = request.EmployeeId;
        appointment.LocationId = request.LocationId;
        appointment.Amount = amount;
        appointment.SuggestedAmount = suggestedAmount;
        appointment.IsAmountManuallyOverridden = overridden;
        appointment.PaymentMethod = request.PaymentMethod;
        appointment.IsPaid = true;
        appointment.Status = AppointmentStatus.Completed;
        appointment.Note = request.Note;
        appointment.UpdatedAt = DateTimeOffset.UtcNow;
        appointment.UpdatedBy = userId;

        await EnsureNoOverlap(
            organizationId, request.EmployeeId, clients, request.StartsAt, appointment.DurationMinutes, excludeId: id);

        await _appointmentHandler.UpdateWithClients(appointment, request.ClientIds.Distinct().ToList());

        foreach (KeyValuePair<Guid, Guid> kvp in packageByClient)
        {
            AppointmentClient clientRow = await _appointmentHandler.GetAppointmentClient(organizationId, id, kvp.Key);
            if (clientRow == null)
                continue;

            await _clientPackageService.DeductEntry(organizationId, kvp.Value, request.ServiceId, userId);

            clientRow.ClientPackageId = kvp.Value;
            clientRow.PackageEntryDeducted = true;
            await _appointmentHandler.UpdateAppointmentClient(clientRow);
        }

        AppointmentDto dto = await GetByIdInternal(organizationId, id);
        return dto;
    }

    public async Task<AppointmentDto> Update(Guid organizationId, Guid userId, bool isAdmin, Guid id, AppointmentUpdateRequest request)
    {
        Appointment appointment = await _appointmentHandler.GetByIdLight(organizationId, id);
        if (appointment == null)
            throw new NotFoundAppException("Appointment", id);

        await ValidateOwnership(organizationId, userId, isAdmin, appointment.EmployeeId.GetValueOrDefault());

        ServiceEntity service = await LoadServiceOrThrow(organizationId, request.ServiceId);
        await EnsureEmployeeExists(organizationId, request.EmployeeId);
        await EnsureLocationExists(organizationId, request.LocationId);
        List<Client> clients = await EnsureClientsExist(organizationId, request.ClientIds);

        decimal suggestedAmount = await ResolveSuggestedAmount(organizationId, request.ServiceId, request.LocationId, request.StartsAt);
        decimal amount = request.Amount ?? suggestedAmount;
        bool overridden = request.Amount.HasValue && request.Amount.Value != suggestedAmount;

        if (amount != appointment.Amount)
            await LogAmountChange(id, appointment.Amount, amount, userId);

        appointment.StartsAt = request.StartsAt;
        appointment.DurationMinutes = service.DefaultDurationMinutes;
        appointment.ServiceId = request.ServiceId;
        appointment.EmployeeId = request.EmployeeId;
        appointment.LocationId = request.LocationId;
        appointment.Amount = amount;
        appointment.SuggestedAmount = suggestedAmount;
        appointment.IsAmountManuallyOverridden = overridden;
        appointment.Note = request.Note;
        appointment.UpdatedAt = DateTimeOffset.UtcNow;
        appointment.UpdatedBy = userId;

        await EnsureNoOverlap(
            organizationId, request.EmployeeId, clients, request.StartsAt, appointment.DurationMinutes, excludeId: id);

        await _appointmentHandler.UpdateWithClients(appointment, request.ClientIds.Distinct().ToList());

        AppointmentDto dto = await GetByIdInternal(organizationId, id);
        return dto;
    }

    public async Task<AppointmentDto> Move(Guid organizationId, Guid userId, bool isAdmin, Guid id, AppointmentMoveRequest request)
    {
        Appointment appointment = await _appointmentHandler.GetByIdLight(organizationId, id);
        if (appointment == null)
            throw new NotFoundAppException("Appointment", id);

        if (appointment.Status == AppointmentStatus.Cancelled || appointment.Status == AppointmentStatus.NoShow)
            throw new BusinessRuleException(ErrorCodes.AppointmentNotMovable, "Otkazan ili izostao termin se ne može pomicati.");

        await ValidateOwnership(organizationId, userId, isAdmin, appointment.EmployeeId.GetValueOrDefault());

        if (request.EmployeeId.HasValue)
            await EnsureEmployeeExists(organizationId, request.EmployeeId.Value);

        if (request.LocationId.HasValue)
            await EnsureLocationExists(organizationId, request.LocationId.Value);

        Guid effectiveEmployeeId = request.EmployeeId ?? appointment.EmployeeId.GetValueOrDefault();

        Appointment full = await _appointmentHandler.GetById(organizationId, id);
        List<Client> clients = full.Clients.Select(ac => ac.Client).ToList();

        appointment.StartsAt = request.StartsAt;
        if (request.EmployeeId.HasValue)
            appointment.EmployeeId = request.EmployeeId.Value;
        if (request.LocationId.HasValue)
            appointment.LocationId = request.LocationId.Value;
        appointment.UpdatedAt = DateTimeOffset.UtcNow;
        appointment.UpdatedBy = userId;

        await EnsureNoOverlap(
            organizationId, effectiveEmployeeId, clients, request.StartsAt, appointment.DurationMinutes, excludeId: id);

        await _appointmentHandler.UpdateScalar(appointment);

        AppointmentDto dto = await GetByIdInternal(organizationId, id);
        return dto;
    }

    public Task<AppointmentDto> Cancel(Guid organizationId, Guid userId, bool isAdmin, Guid id, AppointmentCancelRequest request)
    {
        return ChangeToTerminalStatus(organizationId, userId, isAdmin, id, request, AppointmentStatus.Cancelled);
    }

    public Task<AppointmentDto> MarkNoShow(Guid organizationId, Guid userId, bool isAdmin, Guid id, AppointmentCancelRequest request)
    {
        return ChangeToTerminalStatus(organizationId, userId, isAdmin, id, request, AppointmentStatus.NoShow);
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

    public async Task<List<AppointmentDto>> CreateRecurring(Guid organizationId, Guid userId, bool isAdmin, RecurringAppointmentCreateRequest request)
    {
        if (request.EndDate < request.FirstOccurrenceStartsAt)
            throw new ValidationAppException("Datum kraja ne smije biti prije prvog termina.");

        ServiceEntity service = await LoadServiceOrThrow(organizationId, request.ServiceId);
        List<DateTimeOffset> occurrences = BuildOccurrenceDates(request.RecurrenceType, request.FirstOccurrenceStartsAt, request.EndDate);

        await EnsureNoRecurringConflicts(organizationId, request.EmployeeId, occurrences, service.DefaultDurationMinutes);

        await ValidateOwnership(organizationId, userId, isAdmin, request.EmployeeId);
        await EnsureEmployeeExists(organizationId, request.EmployeeId);
        await EnsureLocationExists(organizationId, request.LocationId);
        List<Client> clients = await EnsureClientsExist(organizationId, request.ClientIds);

        await EnsureNoRecurringClientOverlap(organizationId, clients, occurrences, service.DefaultDurationMinutes);

        Guid recurrenceGroupId = Guid.NewGuid();
        List<Appointment> toCreate = new List<Appointment>();

        foreach (DateTimeOffset occurrence in occurrences)
        {
            decimal suggestedAmount = await ResolveSuggestedAmount(organizationId, request.ServiceId, request.LocationId, occurrence);

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
                LocationId = request.LocationId,
                Amount = suggestedAmount,
                SuggestedAmount = suggestedAmount,
                IsAmountManuallyOverridden = false,
                PaymentMethod = null,
                IsPaid = false,
                Status = AppointmentStatus.Scheduled,
                Note = request.Note,
                RecurrenceGroupId = recurrenceGroupId,
                CreatedAt = DateTimeOffset.UtcNow,
                CreatedBy = userId
            };

            foreach (Client client in clients)
            {
                appointment.Clients.Add(new AppointmentClient
                {
                    Id = Guid.NewGuid(),
                    AppointmentId = appointmentId,
                    ClientId = client.Id.GetValueOrDefault(),
                    CreatedAt = DateTimeOffset.UtcNow
                });
            }

            toCreate.Add(appointment);
        }

        await _appointmentHandler.AddRange(toCreate);

        List<AppointmentDto> created = new List<AppointmentDto>();
        foreach (Appointment appointment in toCreate)
            created.Add(await GetByIdInternal(organizationId, appointment.Id.GetValueOrDefault()));

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

    /// <summary>Tvrda, unaprijedna provjera SAMO za /recurring — ako bilo koji datum u nizu sudara s postojećim
    /// terminom trenera (jednokratnim ili ponavljajućim) ili s roster odsutnošću, baca RECURRING_CONFLICT (409)
    /// prije nego se bilo što spremi. Pojedinačni endpointi umjesto ovoga koriste EnsureNoOverlap (isto tvrda
    /// blokada, ali baca AppointmentOverlap za prvi sudar bez liste svih konflikata).
    /// Kandidati (termini trenera + roster odsutnosti) dohvaćaju se JEDNOM za cijeli raspon niza, precizna
    /// provjera po occurrenceu radi se u memoriji — izbjegava upit po occurrenceu za duge nizove.</summary>
    private async Task EnsureNoRecurringConflicts(
        Guid organizationId, Guid employeeId, List<DateTimeOffset> occurrences, int durationMinutes)
    {
        DateTimeOffset rangeFrom = occurrences[0].AddDays(-1);
        DateTimeOffset rangeTo = occurrences[^1].AddDays(1);

        List<Appointment> candidateAppointments = await _appointmentHandler.GetForEmployeeInRange(
            organizationId, employeeId, rangeFrom, rangeTo);

        List<RosterEntry> absences = (await _rosterEntryHandler.GetForPeriod(
                organizationId, new List<Guid> { employeeId }, occurrences[0], occurrences[^1]))
            .Where(e => e.RosterType.IsAbsence)
            .ToList();

        List<RecurringConflictDetail> conflicts = new List<RecurringConflictDetail>();

        foreach (DateTimeOffset occurrence in occurrences)
        {
            DateTimeOffset occurrenceEnd = occurrence.AddMinutes(durationMinutes);

            bool appointmentHit = candidateAppointments.Any(a =>
                a.StartsAt < occurrenceEnd && occurrence < a.StartsAt.AddMinutes(a.DurationMinutes));

            if (appointmentHit)
            {
                conflicts.Add(new RecurringConflictDetail { Date = occurrence, Reason = ErrorCodes.RecurringConflictReasonAppointment });
                continue;
            }

            bool absenceHit = absences.Any(a =>
                a.DateFrom.Date <= occurrence.Date && (a.DateTo == null || occurrence.Date <= a.DateTo.Value.Date));

            if (absenceHit)
                conflicts.Add(new RecurringConflictDetail { Date = occurrence, Reason = ErrorCodes.RecurringConflictReasonRosterAbsence });
        }

        if (conflicts.Count > 0)
            throw new BusinessRuleException(
                ErrorCodes.RecurringConflict,
                "Neki termini u nizu se sudaraju s postojećim obavezama.",
                new { conflicts });
    }

    /// <summary>Provjera preklapanja klijenata za /recurring — odgovara klijentskoj grani EnsureNoOverlap, ali
    /// nad cijelim nizom odjednom: kandidati se dohvaćaju JEDNOM za cijeli raspon, a za prvi occurrence (kronološki)
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
                bool hasOverlap = overlapping.Any(a => a.Clients.Any(ac => ac.ClientId == client.Id));
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

    public async Task<PagedResult<AppointmentDto>> GetByClient(Guid organizationId, Guid clientId, PagedRequest request)
    {
        (List<Appointment> items, int totalCount) = await _appointmentHandler.GetByClient(organizationId, clientId, request);
        return PagedResult<AppointmentDto>.Create(items.Select(ToDto).ToList(), totalCount, request.Page, request.PageSize);
    }

    private async Task<AppointmentDto> CreateInternal(
        Guid organizationId, Guid userId, bool isAdmin, AppointmentCreateRequest request, Guid? recurrenceGroupId)
    {
        await ValidateOwnership(organizationId, userId, isAdmin, request.EmployeeId);
        ServiceEntity service = await LoadServiceOrThrow(organizationId, request.ServiceId);
        await EnsureEmployeeExists(organizationId, request.EmployeeId);
        await EnsureLocationExists(organizationId, request.LocationId);
        List<Client> clients = await EnsureClientsExist(organizationId, request.ClientIds);

        decimal suggestedAmount = await ResolveSuggestedAmount(organizationId, request.ServiceId, request.LocationId, request.StartsAt);
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
            LocationId = request.LocationId,
            Amount = amount,
            SuggestedAmount = suggestedAmount,
            IsAmountManuallyOverridden = overridden,
            PaymentMethod = null,
            IsPaid = false,
            Status = AppointmentStatus.Scheduled,
            Note = request.Note,
            RecurrenceGroupId = recurrenceGroupId,
            CreatedAt = DateTimeOffset.UtcNow,
            CreatedBy = userId
        };

        foreach (Client client in clients)
        {
            appointment.Clients.Add(new AppointmentClient
            {
                Id = Guid.NewGuid(),
                AppointmentId = appointmentId,
                ClientId = client.Id.GetValueOrDefault(),
                CreatedAt = DateTimeOffset.UtcNow
            });
        }

        await EnsureNoOverlap(
            organizationId, request.EmployeeId, clients, request.StartsAt, appointment.DurationMinutes, excludeId: null);

        await _appointmentHandler.Add(appointment);

        AppointmentDto dto = await GetByIdInternal(organizationId, appointmentId);
        return dto;
    }

    private async Task<AppointmentDto> ChangeToTerminalStatus(
        Guid organizationId, Guid userId, bool isAdmin, Guid id, AppointmentCancelRequest request, AppointmentStatus newStatus)
    {
        Appointment appointment = await _appointmentHandler.GetByIdLight(organizationId, id);
        if (appointment == null)
            throw new NotFoundAppException("Appointment", id);

        await ValidateOwnership(organizationId, userId, isAdmin, appointment.EmployeeId.GetValueOrDefault());

        AppointmentStatus oldStatus = appointment.Status;
        appointment.Status = newStatus;
        appointment.UpdatedAt = DateTimeOffset.UtcNow;
        appointment.UpdatedBy = userId;

        await _appointmentHandler.UpdateScalar(appointment);

        await _auditLogHandler.Add(new AppointmentAuditLog
        {
            Id = Guid.NewGuid(),
            AppointmentId = id,
            ChangeType = "Status",
            OldValue = oldStatus.ToString(),
            NewValue = newStatus.ToString(),
            ChangedAt = DateTimeOffset.UtcNow,
            ChangedBy = userId
        });

        foreach (Guid clientId in (request.ReturnEntryForClientIds ?? new List<Guid>()).Distinct())
        {
            AppointmentClient clientRow = await _appointmentHandler.GetAppointmentClient(organizationId, id, clientId);
            if (clientRow == null || !clientRow.PackageEntryDeducted || clientRow.PackageEntryReturned || clientRow.ClientPackageId == null)
                continue;

            await _clientPackageService.ReturnEntry(organizationId, clientRow.ClientPackageId.Value, appointment.ServiceId, userId);

            clientRow.PackageEntryReturned = true;
            clientRow.PackageEntryReturnedAt = DateTimeOffset.UtcNow;
            clientRow.PackageEntryReturnedBy = userId;
            await _appointmentHandler.UpdateAppointmentClient(clientRow);

            await _auditLogHandler.Add(new AppointmentAuditLog
            {
                Id = Guid.NewGuid(),
                AppointmentId = id,
                ChangeType = "PackageEntryReturn",
                OldValue = "Deducted",
                NewValue = "Returned",
                ChangedAt = DateTimeOffset.UtcNow,
                ChangedBy = userId
            });
        }

        return await GetByIdInternal(organizationId, id);
    }

    private async Task ValidateOwnership(Guid organizationId, Guid userId, bool isAdmin, Guid employeeId)
    {
        if (isAdmin)
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

    private async Task EnsureLocationExists(Guid organizationId, Guid locationId)
    {
        Location location = await _locationHandler.GetById(organizationId, locationId);
        if (location == null)
            throw new NotFoundAppException("Location", locationId);
    }

    private async Task<List<Client>> EnsureClientsExist(Guid organizationId, List<Guid> clientIds)
    {
        List<Client> clients = new List<Client>();
        foreach (Guid clientId in clientIds.Distinct())
        {
            Client client = await _clientHandler.GetByIdLight(organizationId, clientId);
            if (client == null)
                throw new NotFoundAppException("Client", clientId);

            clients.Add(client);
        }

        return clients;
    }

    private async Task<decimal> ResolveSuggestedAmount(Guid organizationId, Guid serviceId, Guid locationId, DateTimeOffset date)
    {
        ResolvePriceResponse resolved = await _pricingService.ResolvePrice(organizationId, new ResolvePriceRequest
        {
            SubjectType = PricingSubjectType.Service,
            SubjectId = serviceId,
            LocationId = locationId,
            Date = date
        });
        return resolved.Price;
    }

    /// <summary>Kod PaymentMethod=Package svaki klijent na terminu mora imati odabran svoj vlastiti valjani paket
    /// (npr. duo/par usluga: svaki klijent skida ulazak iz svog profila, neovisno o ostalima).</summary>
    private async Task<Dictionary<Guid, Guid>> ValidatePackageSelections(
        Guid organizationId, List<Guid> clientIds, Guid serviceId, DateTimeOffset date,
        PaymentMethod paymentMethod, List<AppointmentClientPackageSelection> selections)
    {
        Dictionary<Guid, Guid> result = new Dictionary<Guid, Guid>();
        if (paymentMethod != PaymentMethod.Package)
            return result;

        selections ??= new List<AppointmentClientPackageSelection>();
        List<Guid> selectedClientIds = selections.Select(s => s.ClientId).ToList();

        bool coversAllClientsExactlyOnce =
            selections.Count == clientIds.Count &&
            selectedClientIds.Distinct().Count() == selectedClientIds.Count &&
            clientIds.All(id => selectedClientIds.Contains(id));

        if (!coversAllClientsExactlyOnce)
            throw new ValidationAppException("Kod plaćanja iz paketa potrebno je odabrati točno jedan paket za svakog klijenta na terminu.");

        foreach (AppointmentClientPackageSelection selection in selections)
        {
            List<ClientPackageDto> eligible = await _clientPackageService.GetEligibleForService(
                organizationId, selection.ClientId, serviceId, date);

            if (eligible.All(p => p.Id != selection.ClientPackageId))
                throw new BusinessRuleException(ErrorCodes.PackageNotEligible, "Odabrani paket nije valjan za klijenta ili ne pokriva ovu uslugu.");

            result[selection.ClientId] = selection.ClientPackageId;
        }

        return result;
    }

    /// <summary>Baca APPOINTMENT_OVERLAP (409) prije spremanja ako se termin preklapa s postojećim
    /// (trener ili bilo koji od klijenata) — trener se provjerava prvi, zatim klijenti redom.</summary>
    private async Task EnsureNoOverlap(
        Guid organizationId, Guid employeeId, List<Client> clients, DateTimeOffset startsAt, int durationMinutes, Guid? excludeId)
    {
        List<Appointment> employeeOverlaps = await _appointmentHandler.GetOverlappingForEmployee(
            organizationId, employeeId, startsAt, durationMinutes, excludeId);
        if (employeeOverlaps.Count > 0)
            throw new BusinessRuleException(ErrorCodes.AppointmentOverlap, "Trener već ima termin u ovom vremenskom razdoblju.");

        List<Guid> clientIds = clients.Select(c => c.Id.GetValueOrDefault()).ToList();
        List<Appointment> clientOverlaps = await _appointmentHandler.GetOverlappingForClients(
            organizationId, clientIds, startsAt, durationMinutes, excludeId);

        foreach (Client client in clients)
        {
            bool hasOverlap = clientOverlaps.Any(a => a.Clients.Any(ac => ac.ClientId == client.Id));
            if (hasOverlap)
                throw new BusinessRuleException(ErrorCodes.AppointmentOverlap, $"Klijent {client.FirstName} {client.LastName} je već zakazan u ovom vremenskom razdoblju.");
        }
    }

    private async Task LogAmountChange(Guid appointmentId, decimal oldAmount, decimal newAmount, Guid userId)
    {
        await _auditLogHandler.Add(new AppointmentAuditLog
        {
            Id = Guid.NewGuid(),
            AppointmentId = appointmentId,
            ChangeType = "Amount",
            OldValue = oldAmount.ToString(CultureInfo.InvariantCulture),
            NewValue = newAmount.ToString(CultureInfo.InvariantCulture),
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

        return new AppointmentScheduleCellDto
        {
            Id = a.Id.GetValueOrDefault(),
            StartsAt = a.StartsAt,
            DurationMinutes = a.DurationMinutes,
            ServiceId = a.ServiceId,
            ServiceName = a.Service?.Name,
            ServiceCategoryColorHex = a.Service?.ServiceCategory?.ColorHex,
            EmployeeId = a.EmployeeId,
            EmployeeName = a.Employee != null ? $"{a.Employee.FirstName} {a.Employee.LastName}" : null,
            LocationId = a.LocationId,
            LocationName = a.Location?.Name,
            ClientNames = a.Clients
                .Where(c => c.Client != null)
                .Select(c => $"{c.Client.FirstName} {c.Client.LastName}")
                .ToList(),
            Status = a.Status,
            IsCancelled = a.Status == AppointmentStatus.Cancelled,
            Form = a.Form,
            GroupId = a.GroupId,
            GroupName = isGroup ? a.Group?.Name : null,
            AttendanceCount = isGroup ? a.Attendances.Count(x => x.Attended == true) : (int?)null,
            ExpectedCount = isGroup ? a.Group?.Members.Count(m => m.IsActive) : (int?)null
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
            ServiceCategoryColorHex = a.Service?.ServiceCategory?.ColorHex,
            EmployeeId = a.EmployeeId,
            EmployeeName = a.Employee != null ? $"{a.Employee.FirstName} {a.Employee.LastName}" : null,
            LocationId = a.LocationId,
            LocationName = a.Location?.Name,
            Amount = a.Amount,
            SuggestedAmount = a.SuggestedAmount,
            IsAmountManuallyOverridden = a.IsAmountManuallyOverridden,
            PaymentMethod = a.PaymentMethod,
            IsPaid = a.IsPaid,
            Status = a.Status,
            Note = a.Note,
            GroupId = a.GroupId,
            RecurrenceGroupId = a.RecurrenceGroupId,
            Clients = a.Clients.Select(c => new AppointmentClientDto
            {
                ClientId = c.ClientId,
                ClientName = c.Client != null ? $"{c.Client.FirstName} {c.Client.LastName}" : null,
                ClientPackageId = c.ClientPackageId,
                PackageEntryDeducted = c.PackageEntryDeducted,
                PackageEntryReturned = c.PackageEntryReturned
            }).ToList(),
            CreatedAt = a.CreatedAt,
            CreatedBy = a.CreatedBy,
            UpdatedAt = a.UpdatedAt,
            UpdatedBy = a.UpdatedBy
        };
    }
}
