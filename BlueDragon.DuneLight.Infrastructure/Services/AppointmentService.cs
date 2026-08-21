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
    private readonly IEmployeeHandler _employeeHandler;
    private readonly ICompanyHandler _companyHandler;
    private readonly IClientHandler _clientHandler;
    private readonly IRosterEntryHandler _rosterEntryHandler;
    private readonly IWorkingHoursTemplateHandler _workingHoursTemplateHandler;
    private readonly IScheduleBreakHandler _scheduleBreakHandler;
    private readonly IUnitOfWorkFactory _unitOfWorkFactory;

    public AppointmentService(
        IAppointmentHandler appointmentHandler,
        IAppointmentAuditLogHandler auditLogHandler,
        IClientPackageService clientPackageService,
        IClientPackageHandler clientPackageHandler,
        IPricingService pricingService,
        IServiceHandler serviceHandler,
        IEmployeeHandler employeeHandler,
        ICompanyHandler companyHandler,
        IClientHandler clientHandler,
        IRosterEntryHandler rosterEntryHandler,
        IWorkingHoursTemplateHandler workingHoursTemplateHandler,
        IScheduleBreakHandler scheduleBreakHandler,
        IUnitOfWorkFactory unitOfWorkFactory)
    {
        _appointmentHandler = appointmentHandler;
        _auditLogHandler = auditLogHandler;
        _clientPackageService = clientPackageService;
        _clientPackageHandler = clientPackageHandler;
        _pricingService = pricingService;
        _serviceHandler = serviceHandler;
        _employeeHandler = employeeHandler;
        _companyHandler = companyHandler;
        _clientHandler = clientHandler;
        _rosterEntryHandler = rosterEntryHandler;
        _workingHoursTemplateHandler = workingHoursTemplateHandler;
        _scheduleBreakHandler = scheduleBreakHandler;
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
        await EnsureEmployeeExists(organizationId, request.EmployeeId);
        await EnsureCompanyExists(organizationId, request.CompanyId);
        List<Client> clients = await EnsureClientsExist(organizationId, request.ClientIds);

        decimal suggestedAmount = await ResolveSuggestedAmount(organizationId, request.ServiceId, request.CompanyId, request.StartsAt);
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
            CompanyId = request.CompanyId,
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

        // CompleteNew loguje odrađeno — provjera radnog vremena vrijedi samo ako je StartsAt u budućnosti
        // (zakazuje se i odmah naplaćuje); za prošlost je ovo evidentiranje stvarnosti, ne planiranje (vidi FAZA 2).
        if (request.StartsAt > DateTimeOffset.UtcNow)
            await EnsureWithinWorkingHours(organizationId, request.EmployeeId, request.CompanyId, request.StartsAt, service.DefaultDurationMinutes);

        await EnsureNoOverlap(
            organizationId, request.EmployeeId, clients, request.StartsAt, appointment.DurationMinutes, excludeId: null);

        try
        {
            await using IUnitOfWork uow = await _unitOfWorkFactory.Begin();

            await _appointmentHandler.Add(uow, appointment);

            foreach (KeyValuePair<Guid, Guid> kvp in packageByClient)
                await DeductPackageEntryInTransaction(uow, organizationId, kvp.Value, request.ServiceId, userId);

            await uow.CommitAsync();
        }
        catch (DbUpdateConcurrencyException)
        {
            throw new BusinessRuleException(
                ErrorCodes.ConcurrencyConflict, "Paket je upravo promijenjen od strane drugog zahtjeva — pokušajte ponovno.");
        }

        AppointmentDto dto = await GetByIdInternal(organizationId, appointmentId);
        return dto;
    }

    public async Task<AppointmentDto> CompleteExisting(Guid organizationId, Guid userId, bool hasFullScope, Guid id, AppointmentCompleteRequest request)
    {
        Appointment appointment = await _appointmentHandler.GetByIdLight(organizationId, id);
        if (appointment == null)
            throw new NotFoundAppException("Appointment", id);

        if (appointment.Status == AppointmentStatus.Completed)
            throw new BusinessRuleException(ErrorCodes.AlreadyCompleted, "Termin je već označen kao odrađen.");

        await ValidateOwnership(organizationId, userId, hasFullScope, appointment.EmployeeId.GetValueOrDefault());

        ServiceEntity service = await LoadServiceOrThrow(organizationId, request.ServiceId);
        await EnsureEmployeeExists(organizationId, request.EmployeeId);
        await EnsureCompanyExists(organizationId, request.CompanyId);
        List<Client> clients = await EnsureClientsExist(organizationId, request.ClientIds);

        decimal suggestedAmount = await ResolveSuggestedAmount(organizationId, request.ServiceId, request.CompanyId, request.StartsAt);
        decimal amount = request.Amount ?? suggestedAmount;
        bool overridden = request.Amount.HasValue && request.Amount.Value != suggestedAmount;

        Dictionary<Guid, Guid> packageByClient = await ValidatePackageSelections(
            organizationId, clients.Select(c => c.Id.GetValueOrDefault()).ToList(),
            request.ServiceId, request.StartsAt, request.PaymentMethod, request.PackageSelections);

        bool amountChanged = amount != appointment.Amount;
        decimal previousAmount = appointment.Amount;

        appointment.StartsAt = request.StartsAt;
        appointment.DurationMinutes = service.DefaultDurationMinutes;
        appointment.ServiceId = request.ServiceId;
        appointment.EmployeeId = request.EmployeeId;
        appointment.CompanyId = request.CompanyId;
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

        try
        {
            await using IUnitOfWork uow = await _unitOfWorkFactory.Begin();

            if (amountChanged)
                await LogAmountChangeInTransaction(uow, id, previousAmount, amount, userId);

            await _appointmentHandler.UpdateWithClients(uow, appointment, request.ClientIds.Distinct().ToList());

            Dictionary<Guid, AppointmentClient> clientRowsByClientId = (await _appointmentHandler.GetAppointmentClients(
                    uow, organizationId, id, packageByClient.Keys.ToList()))
                .ToDictionary(ac => ac.ClientId);

            foreach (KeyValuePair<Guid, Guid> kvp in packageByClient)
            {
                if (!clientRowsByClientId.TryGetValue(kvp.Key, out AppointmentClient clientRow))
                    continue;

                await DeductPackageEntryInTransaction(uow, organizationId, kvp.Value, request.ServiceId, userId);

                clientRow.ClientPackageId = kvp.Value;
                clientRow.PackageEntryDeducted = true;
                await _appointmentHandler.UpdateAppointmentClient(uow, clientRow);
            }

            await uow.CommitAsync();
        }
        catch (DbUpdateConcurrencyException)
        {
            throw new BusinessRuleException(
                ErrorCodes.ConcurrencyConflict, "Podaci su upravo promijenjeni od strane drugog zahtjeva — pokušajte ponovno.");
        }

        AppointmentDto dto = await GetByIdInternal(organizationId, id);
        return dto;
    }

    public async Task<AppointmentDto> Update(Guid organizationId, Guid userId, bool hasFullScope, Guid id, AppointmentUpdateRequest request)
    {
        Appointment appointment = await _appointmentHandler.GetByIdLight(organizationId, id);
        if (appointment == null)
            throw new NotFoundAppException("Appointment", id);

        await ValidateOwnership(organizationId, userId, hasFullScope, appointment.EmployeeId.GetValueOrDefault());

        ServiceEntity service = await LoadServiceOrThrow(organizationId, request.ServiceId);
        await EnsureEmployeeExists(organizationId, request.EmployeeId);
        await EnsureCompanyExists(organizationId, request.CompanyId);
        List<Client> clients = await EnsureClientsExist(organizationId, request.ClientIds);

        decimal suggestedAmount = await ResolveSuggestedAmount(organizationId, request.ServiceId, request.CompanyId, request.StartsAt);
        decimal amount = request.Amount ?? suggestedAmount;
        bool overridden = request.Amount.HasValue && request.Amount.Value != suggestedAmount;

        if (amount != appointment.Amount)
            await LogAmountChange(id, appointment.Amount, amount, userId);

        appointment.StartsAt = request.StartsAt;
        appointment.DurationMinutes = service.DefaultDurationMinutes;
        appointment.ServiceId = request.ServiceId;
        appointment.EmployeeId = request.EmployeeId;
        appointment.CompanyId = request.CompanyId;
        appointment.Amount = amount;
        appointment.SuggestedAmount = suggestedAmount;
        appointment.IsAmountManuallyOverridden = overridden;
        appointment.Note = request.Note;
        appointment.UpdatedAt = DateTimeOffset.UtcNow;
        appointment.UpdatedBy = userId;

        await EnsureWithinWorkingHours(organizationId, request.EmployeeId, request.CompanyId, request.StartsAt, appointment.DurationMinutes);

        await EnsureNoOverlap(
            organizationId, request.EmployeeId, clients, request.StartsAt, appointment.DurationMinutes, excludeId: id);

        await _appointmentHandler.UpdateWithClients(appointment, request.ClientIds.Distinct().ToList());

        AppointmentDto dto = await GetByIdInternal(organizationId, id);
        return dto;
    }

    public async Task<AppointmentDto> Move(Guid organizationId, Guid userId, bool hasFullScope, Guid id, AppointmentMoveRequest request)
    {
        Appointment appointment = await _appointmentHandler.GetByIdLight(organizationId, id);
        if (appointment == null)
            throw new NotFoundAppException("Appointment", id);

        if (appointment.Status == AppointmentStatus.Cancelled || appointment.Status == AppointmentStatus.NoShow)
            throw new BusinessRuleException(ErrorCodes.AppointmentNotMovable, "Otkazan ili izostao termin se ne može pomicati.");

        await ValidateOwnership(organizationId, userId, hasFullScope, appointment.EmployeeId.GetValueOrDefault());

        if (request.EmployeeId.HasValue)
            await EnsureEmployeeExists(organizationId, request.EmployeeId.Value);

        if (request.CompanyId.HasValue)
            await EnsureCompanyExists(organizationId, request.CompanyId.Value);

        Guid effectiveEmployeeId = request.EmployeeId ?? appointment.EmployeeId.GetValueOrDefault();

        Appointment full = await _appointmentHandler.GetById(organizationId, id);
        List<Client> clients = full.Clients.Select(ac => ac.Client).ToList();

        appointment.StartsAt = request.StartsAt;
        if (request.EmployeeId.HasValue)
            appointment.EmployeeId = request.EmployeeId.Value;
        if (request.CompanyId.HasValue)
            appointment.CompanyId = request.CompanyId.Value;
        appointment.UpdatedAt = DateTimeOffset.UtcNow;
        appointment.UpdatedBy = userId;

        await EnsureWithinWorkingHours(organizationId, effectiveEmployeeId, appointment.CompanyId, request.StartsAt, appointment.DurationMinutes);

        await EnsureNoOverlap(
            organizationId, effectiveEmployeeId, clients, request.StartsAt, appointment.DurationMinutes, excludeId: id);

        await _appointmentHandler.UpdateScalar(appointment);

        AppointmentDto dto = await GetByIdInternal(organizationId, id);
        return dto;
    }

    public Task<AppointmentDto> Cancel(Guid organizationId, Guid userId, bool hasFullScope, Guid id, AppointmentCancelRequest request)
    {
        return ChangeToTerminalStatus(organizationId, userId, hasFullScope, id, request, AppointmentStatus.Cancelled);
    }

    public Task<AppointmentDto> MarkNoShow(Guid organizationId, Guid userId, bool hasFullScope, Guid id, AppointmentCancelRequest request)
    {
        return ChangeToTerminalStatus(organizationId, userId, hasFullScope, id, request, AppointmentStatus.NoShow);
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
        List<DateTimeOffset> occurrences = BuildOccurrenceDates(request.RecurrenceType, request.FirstOccurrenceStartsAt, request.EndDate);

        await EnsureNoRecurringConflicts(organizationId, request.EmployeeId, request.CompanyId, occurrences, service.DefaultDurationMinutes);

        await ValidateOwnership(organizationId, userId, hasFullScope, request.EmployeeId);
        await EnsureEmployeeExists(organizationId, request.EmployeeId);
        await EnsureCompanyExists(organizationId, request.CompanyId);
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
    /// terminom trenera (jednokratnim ili ponavljajućim), pauzom trenera, ili s roster odsutnošću, baca
    /// RECURRING_CONFLICT (409) prije nego se bilo što spremi. Pojedinačni endpointi umjesto ovoga koriste
    /// EnsureNoOverlap (isto tvrda blokada, ali baca AppointmentOverlap za prvi sudar bez liste svih konflikata).
    /// Kandidati (termini trenera + roster odsutnosti) dohvaćaju se JEDNOM za cijeli raspon niza, precizna
    /// provjera po occurrenceu radi se u memoriji — izbjegava upit po occurrenceu za duge nizove.</summary>
    private async Task EnsureNoRecurringConflicts(
        Guid organizationId, Guid employeeId, Guid companyId, List<DateTimeOffset> occurrences, int durationMinutes)
    {
        DateTimeOffset rangeFrom = occurrences[0].AddDays(-1);
        DateTimeOffset rangeTo = occurrences[^1].AddDays(1);

        List<Appointment> candidateAppointments = await _appointmentHandler.GetForEmployeeInRange(
            organizationId, employeeId, rangeFrom, rangeTo);

        List<ScheduleBreak> candidateBreaks = await _scheduleBreakHandler.GetForEmployeeInRange(
            organizationId, employeeId, rangeFrom, rangeTo);

        // Učitano JEDNOM za cijeli raspon niza (apsencije + eventualni work-override redovi) — dijeli se između
        // absenceHit provjere i working-hours provjere ispod, isti obrazac kao candidateAppointments.
        List<RosterEntry> rosterEntriesInRange = await _rosterEntryHandler.GetForPeriod(
            organizationId, new List<Guid> { employeeId }, occurrences[0], occurrences[^1]);

        List<RosterEntry> absences = rosterEntriesInRange.Where(e => e.RosterType.IsAbsence).ToList();

        WorkingHoursTemplate employeeTemplate = await _workingHoursTemplateHandler.GetForEmployee(organizationId, employeeId);
        WorkingHoursTemplate companyTemplate = await _workingHoursTemplateHandler.GetForCompany(organizationId, companyId);

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

            bool breakHit = candidateBreaks.Any(b =>
                b.StartsAt < occurrenceEnd && occurrence < b.StartsAt.AddMinutes(b.DurationMinutes));

            if (breakHit)
            {
                conflicts.Add(new RecurringConflictDetail { Date = occurrence, Reason = ErrorCodes.RecurringConflictReasonScheduleBreak });
                continue;
            }

            bool absenceHit = absences.Any(a =>
                a.DateFrom.Date <= occurrence.Date && (a.DateTo == null || occurrence.Date <= a.DateTo.Value.Date));

            if (absenceHit)
            {
                conflicts.Add(new RecurringConflictDetail { Date = occurrence, Reason = ErrorCodes.RecurringConflictReasonRosterAbsence });
                continue;
            }

            List<RosterEntry> rosterEntriesForOccurrence = rosterEntriesInRange
                .Where(e => !e.RosterType.IsAbsence && e.DateFrom.Date == occurrence.Date)
                .ToList();

            if (!IsWithinWorkingHours(employeeTemplate, companyTemplate, rosterEntriesForOccurrence, occurrence, durationMinutes))
                conflicts.Add(new RecurringConflictDetail { Date = occurrence, Reason = ErrorCodes.RecurringConflictReasonOutsideWorkingHours });
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

    public async Task<List<EmployeeAvailableSlotsDto>> GetAvailableSlots(Guid organizationId, AvailableSlotsQuery query)
    {
        ServiceEntity service = await LoadServiceOrThrow(organizationId, query.ServiceId);
        await EnsureCompanyExists(organizationId, query.CompanyId);

        List<Employee> employees = await _employeeHandler.GetForCompany(organizationId, query.CompanyId);

        if (query.EmployeeId.HasValue)
            employees = employees.Where(e => e.Id == query.EmployeeId.Value).ToList();

        employees = employees
            .Where(e => e.Services.Count == 0 || e.Services.Any(s => s.ServiceId == query.ServiceId))
            .ToList();

        if (employees.Count == 0)
            return new List<EmployeeAvailableSlotsDto>();

        List<Guid> employeeIds = employees.Select(e => e.Id.GetValueOrDefault()).ToList();

        DateTimeOffset dayStart = query.Date.Date;
        DateTimeOffset dayEnd = dayStart.AddDays(1).AddTicks(-1);

        List<WorkingHoursTemplate> employeeTemplates = await _workingHoursTemplateHandler.GetForEmployees(organizationId, employeeIds);
        WorkingHoursTemplate companyTemplate = await _workingHoursTemplateHandler.GetForCompany(organizationId, query.CompanyId);
        List<RosterEntry> rosterEntries = await _rosterEntryHandler.GetForPeriod(organizationId, employeeIds, dayStart, dayStart);
        List<Appointment> appointments = await _appointmentHandler.GetForEmployeesInRange(organizationId, employeeIds, dayStart, dayEnd);
        List<ScheduleBreak> breaks = await _scheduleBreakHandler.GetForEmployeesInRange(organizationId, employeeIds, dayStart, dayEnd);

        (List<WorkingHoursCalculator.Interval> companyIntervals, _) =
            WorkingHoursCalculator.GetEffectiveCompanyIntervals(companyTemplate, dayStart);

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
                Slots = GenerateSlots(effectiveIntervals, busy, service.DefaultDurationMinutes)
            });
        }

        return result;
    }

    private async Task<AppointmentDto> CreateInternal(
        Guid organizationId, Guid userId, bool hasFullScope, AppointmentCreateRequest request, Guid? recurrenceGroupId)
    {
        await ValidateOwnership(organizationId, userId, hasFullScope, request.EmployeeId);
        ServiceEntity service = await LoadServiceOrThrow(organizationId, request.ServiceId);
        await EnsureEmployeeExists(organizationId, request.EmployeeId);
        await EnsureCompanyExists(organizationId, request.CompanyId);
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

        await EnsureWithinWorkingHours(organizationId, request.EmployeeId, request.CompanyId, request.StartsAt, appointment.DurationMinutes);

        await EnsureNoOverlap(
            organizationId, request.EmployeeId, clients, request.StartsAt, appointment.DurationMinutes, excludeId: null);

        await _appointmentHandler.Add(appointment);

        AppointmentDto dto = await GetByIdInternal(organizationId, appointmentId);
        return dto;
    }

    private async Task<AppointmentDto> ChangeToTerminalStatus(
        Guid organizationId, Guid userId, bool hasFullScope, Guid id, AppointmentCancelRequest request, AppointmentStatus newStatus)
    {
        Appointment appointment = await _appointmentHandler.GetByIdLight(organizationId, id);
        if (appointment == null)
            throw new NotFoundAppException("Appointment", id);

        await ValidateOwnership(organizationId, userId, hasFullScope, appointment.EmployeeId.GetValueOrDefault());

        AppointmentStatus oldStatus = appointment.Status;
        appointment.Status = newStatus;
        appointment.UpdatedAt = DateTimeOffset.UtcNow;
        appointment.UpdatedBy = userId;

        try
        {
            await using IUnitOfWork uow = await _unitOfWorkFactory.Begin();

            await _appointmentHandler.UpdateScalar(uow, appointment);

            await _auditLogHandler.Add(uow, new AppointmentAuditLog
            {
                Id = Guid.NewGuid(),
                AppointmentId = id,
                ChangeType = "Status",
                OldValue = oldStatus.ToString(),
                NewValue = newStatus.ToString(),
                ChangedAt = DateTimeOffset.UtcNow,
                ChangedBy = userId
            });

            List<Guid> returnClientIds = (request.ReturnEntryForClientIds ?? new List<Guid>()).Distinct().ToList();
            Dictionary<Guid, AppointmentClient> clientRowsByClientId = returnClientIds.Count == 0
                ? new Dictionary<Guid, AppointmentClient>()
                : (await _appointmentHandler.GetAppointmentClients(uow, organizationId, id, returnClientIds)).ToDictionary(ac => ac.ClientId);

            foreach (Guid clientId in returnClientIds)
            {
                if (!clientRowsByClientId.TryGetValue(clientId, out AppointmentClient clientRow) ||
                    !clientRow.PackageEntryDeducted || clientRow.PackageEntryReturned || clientRow.ClientPackageId == null)
                    continue;

                await ReturnPackageEntryInTransaction(uow, organizationId, clientRow.ClientPackageId.Value, appointment.ServiceId, userId);

                clientRow.PackageEntryReturned = true;
                clientRow.PackageEntryReturnedAt = DateTimeOffset.UtcNow;
                clientRow.PackageEntryReturnedBy = userId;
                await _appointmentHandler.UpdateAppointmentClient(uow, clientRow);

                await _auditLogHandler.Add(uow, new AppointmentAuditLog
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
    /// (trener, pauza trenera, ili bilo koji od klijenata) — trener se provjerava prvi, zatim pauza, zatim klijenti redom.</summary>
    private async Task EnsureNoOverlap(
        Guid organizationId, Guid employeeId, List<Client> clients, DateTimeOffset startsAt, int durationMinutes, Guid? excludeId)
    {
        List<Appointment> employeeOverlaps = await _appointmentHandler.GetOverlappingForEmployee(
            organizationId, employeeId, startsAt, durationMinutes, excludeId);
        if (employeeOverlaps.Count > 0)
            throw new BusinessRuleException(ErrorCodes.AppointmentOverlap, "Trener već ima termin u ovom vremenskom razdoblju.");

        List<ScheduleBreak> breakOverlaps = await _scheduleBreakHandler.GetOverlappingForEmployee(
            organizationId, employeeId, startsAt, durationMinutes, excludeId: null);
        if (breakOverlaps.Count > 0)
            throw new BusinessRuleException(ErrorCodes.AppointmentOverlap, "Trener ima pauzu u ovom vremenu.");

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

    /// <summary>Baca OUTSIDE_WORKING_HOURS (409) prije spremanja ako termin pada izvan radnog vremena zaposlenika ILI
    /// lokacije — tvrda blokada bez iznimke (vidi WorkingHoursCalculator, FAZA 1 dizajn). Ne primjenjuje se retroaktivno
    /// (CompleteExisting, ili CompleteNew za StartsAt u prošlosti) — vidi pozivna mjesta.</summary>
    private async Task EnsureWithinWorkingHours(
        Guid organizationId, Guid employeeId, Guid companyId, DateTimeOffset startsAt, int durationMinutes)
    {
        WorkingHoursTemplate employeeTemplate = await _workingHoursTemplateHandler.GetForEmployee(organizationId, employeeId);
        WorkingHoursTemplate companyTemplate = await _workingHoursTemplateHandler.GetForCompany(organizationId, companyId);
        List<RosterEntry> rosterEntriesForDate = await _rosterEntryHandler.GetForPeriod(
            organizationId, new List<Guid> { employeeId }, startsAt.Date, startsAt.Date);

        if (!IsWithinWorkingHours(employeeTemplate, companyTemplate, rosterEntriesForDate, startsAt, durationMinutes))
            throw new BusinessRuleException(ErrorCodes.OutsideWorkingHours, "Termin je izvan radnog vremena zaposlenika ili lokacije.");
    }

    /// <summary>Čista provjera dijeljena s EnsureNoRecurringConflicts (batch grana) — rosterEntriesForDate mora
    /// sadržavati SAMO redove relevantne za TOČNO taj datum (apsencija čiji raspon ga pokriva, ili work-redovi
    /// s DateFrom==taj datum), ne cijeli raspon niza.</summary>
    private static bool IsWithinWorkingHours(
        WorkingHoursTemplate employeeTemplate, WorkingHoursTemplate companyTemplate, List<RosterEntry> rosterEntriesForDate,
        DateTimeOffset startsAt, int durationMinutes)
    {
        TimeSpan start = startsAt.TimeOfDay;
        TimeSpan end = start + TimeSpan.FromMinutes(durationMinutes);

        (List<WorkingHoursCalculator.Interval> employeeIntervals, _) =
            WorkingHoursCalculator.GetEffectiveEmployeeIntervals(employeeTemplate, rosterEntriesForDate, startsAt);
        (List<WorkingHoursCalculator.Interval> companyIntervals, _) =
            WorkingHoursCalculator.GetEffectiveCompanyIntervals(companyTemplate, startsAt);

        return WorkingHoursCalculator.IsWithinIntervals(employeeIntervals, start, end)
            && WorkingHoursCalculator.IsWithinIntervals(companyIntervals, start, end);
    }

    private static readonly TimeSpan AvailableSlotStep = TimeSpan.FromMinutes(15);

    /// <summary>Kandidati kreću na apsolutnom gridu :00/:15/:30/:45 (dosljedno vizualnom snapu na rasporedu), unutar
    /// svakog slobodnog prozora, izbacujući svaki koji preklapa nešto zauzeto (termin ili pauza istog trenera).</summary>
    private static List<AvailableSlotDto> GenerateSlots(
        List<WorkingHoursCalculator.Interval> freeIntervals, List<(TimeSpan Start, TimeSpan End)> busy, int durationMinutes)
    {
        List<AvailableSlotDto> slots = new List<AvailableSlotDto>();
        TimeSpan duration = TimeSpan.FromMinutes(durationMinutes);

        foreach (WorkingHoursCalculator.Interval interval in freeIntervals)
        {
            TimeSpan candidateStart = RoundUpToStep(interval.Start, AvailableSlotStep);

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

    private async Task LogAmountChangeInTransaction(IUnitOfWork uow, Guid appointmentId, decimal oldAmount, decimal newAmount, Guid userId)
    {
        await _auditLogHandler.Add(uow, new AppointmentAuditLog
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

    /// <summary>Odbija ulazak iz paketa unutar zajedničke transakcije s terminom (vidi IUnitOfWork) — bez ovoga bi
    /// spremanje termina i odbijanje ulaska bila dva neovisna SaveChanges-a, pa bi pad usred niza mogao ostaviti
    /// termin spremljen a ulazak neodbjen (ili obrnuto).</summary>
    private async Task DeductPackageEntryInTransaction(IUnitOfWork uow, Guid organizationId, Guid clientPackageId, Guid serviceId, Guid userId)
    {
        ClientPackage clientPackage = await _clientPackageHandler.GetById(uow, organizationId, clientPackageId);
        if (clientPackage == null)
            throw new NotFoundAppException("ClientPackage", clientPackageId);

        ClientPackageEntryMutator.Deduct(clientPackage, serviceId);
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

        ClientPackageEntryMutator.Return(clientPackage, serviceId);
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
        List<Client> clients = a.Clients.Where(c => c.Client != null).Select(c => c.Client).ToList();

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
            ClientNames = clients.Select(c => $"{c.FirstName} {c.LastName}").ToList(),
            ClientIds = clients.Select(c => c.Id.GetValueOrDefault()).ToList(),
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
            ServiceCategoryColorHex = a.Service?.ColorHex,
            EmployeeId = a.EmployeeId,
            EmployeeName = a.Employee != null ? $"{a.Employee.FirstName} {a.Employee.LastName}" : null,
            CompanyId = a.CompanyId,
            CompanyName = a.Company?.Name,
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
