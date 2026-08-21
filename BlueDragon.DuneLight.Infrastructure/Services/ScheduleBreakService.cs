using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using BlueDragon.DuneLight.Core.DTOs.Appointments;
using BlueDragon.DuneLight.Core.DTOs.ScheduleBreaks;
using BlueDragon.DuneLight.Core.Enums;
using BlueDragon.DuneLight.Core.Interfaces.ScheduleBreaks;
using BlueDragon.DuneLight.Core.Shared;
using BlueDragon.DuneLight.Core.Shared.Exceptions;
using BlueDragon.DuneLight.Infrastructure.Domain.Models.Appointments;
using BlueDragon.DuneLight.Infrastructure.Domain.Models.Catalog;
using BlueDragon.DuneLight.Infrastructure.Domain.Models.Employees;
using BlueDragon.DuneLight.Infrastructure.Handlers.Interfaces;

namespace BlueDragon.DuneLight.Infrastructure.Services;

public class ScheduleBreakService : IScheduleBreakService
{
    private readonly IScheduleBreakHandler _scheduleBreakHandler;
    private readonly IAppointmentHandler _appointmentHandler;
    private readonly IEmployeeHandler _employeeHandler;
    private readonly ICompanyHandler _companyHandler;

    public ScheduleBreakService(
        IScheduleBreakHandler scheduleBreakHandler,
        IAppointmentHandler appointmentHandler,
        IEmployeeHandler employeeHandler,
        ICompanyHandler companyHandler)
    {
        _scheduleBreakHandler = scheduleBreakHandler;
        _appointmentHandler = appointmentHandler;
        _employeeHandler = employeeHandler;
        _companyHandler = companyHandler;
    }

    public Task<ScheduleBreakDto> Create(Guid organizationId, Guid userId, bool hasFullScope, ScheduleBreakCreateRequest request)
    {
        return CreateInternal(organizationId, userId, hasFullScope, request, recurrenceGroupId: null);
    }

    public async Task<ScheduleBreakDto> Update(Guid organizationId, Guid userId, bool hasFullScope, Guid id, ScheduleBreakUpdateRequest request)
    {
        ScheduleBreak scheduleBreak = await _scheduleBreakHandler.GetByIdLight(organizationId, id);
        if (scheduleBreak == null)
            throw new NotFoundAppException("ScheduleBreak", id);

        await ValidateOwnership(organizationId, userId, hasFullScope, scheduleBreak.EmployeeId);
        if (request.EmployeeId != scheduleBreak.EmployeeId)
            await ValidateOwnership(organizationId, userId, hasFullScope, request.EmployeeId);

        await EnsureEmployeeExists(organizationId, request.EmployeeId);
        await EnsureCompanyExists(organizationId, request.CompanyId);

        scheduleBreak.EmployeeId = request.EmployeeId;
        scheduleBreak.CompanyId = request.CompanyId;
        scheduleBreak.StartsAt = request.StartsAt;
        scheduleBreak.DurationMinutes = request.DurationMinutes;
        scheduleBreak.Note = request.Note;
        scheduleBreak.UpdatedAt = DateTimeOffset.UtcNow;
        scheduleBreak.UpdatedBy = userId;

        await EnsureNoOverlap(organizationId, request.EmployeeId, request.StartsAt, request.DurationMinutes, excludeId: id);

        await _scheduleBreakHandler.UpdateScalar(scheduleBreak);

        return await GetByIdInternal(organizationId, id);
    }

    public async Task Delete(Guid organizationId, Guid userId, bool hasFullScope, Guid id)
    {
        ScheduleBreak scheduleBreak = await _scheduleBreakHandler.GetByIdLight(organizationId, id);
        if (scheduleBreak == null)
            throw new NotFoundAppException("ScheduleBreak", id);

        await ValidateOwnership(organizationId, userId, hasFullScope, scheduleBreak.EmployeeId);

        await _scheduleBreakHandler.Delete(scheduleBreak);
    }

    public async Task<List<ScheduleBreakDto>> CreateRecurring(Guid organizationId, Guid userId, bool hasFullScope, RecurringScheduleBreakCreateRequest request)
    {
        if (request.EndDate < request.FirstOccurrenceStartsAt)
            throw new ValidationAppException("Datum kraja ne smije biti prije prve pauze.");

        await ValidateOwnership(organizationId, userId, hasFullScope, request.EmployeeId);
        await EnsureEmployeeExists(organizationId, request.EmployeeId);
        await EnsureCompanyExists(organizationId, request.CompanyId);

        List<DateTimeOffset> occurrences = BuildOccurrenceDates(request.RecurrenceType, request.FirstOccurrenceStartsAt, request.EndDate);

        await EnsureNoRecurringConflicts(organizationId, request.EmployeeId, occurrences, request.DurationMinutes);

        Guid recurrenceGroupId = Guid.NewGuid();
        List<ScheduleBreak> toCreate = new List<ScheduleBreak>();

        foreach (DateTimeOffset occurrence in occurrences)
        {
            toCreate.Add(new ScheduleBreak
            {
                Id = Guid.NewGuid(),
                OrganizationId = organizationId,
                EmployeeId = request.EmployeeId,
                CompanyId = request.CompanyId,
                StartsAt = occurrence,
                DurationMinutes = request.DurationMinutes,
                Note = request.Note,
                RecurrenceGroupId = recurrenceGroupId,
                CreatedAt = DateTimeOffset.UtcNow,
                CreatedBy = userId
            });
        }

        await _scheduleBreakHandler.AddRange(toCreate);

        List<ScheduleBreakDto> created = new List<ScheduleBreakDto>();
        foreach (ScheduleBreak scheduleBreak in toCreate)
            created.Add(await GetByIdInternal(organizationId, scheduleBreak.Id.GetValueOrDefault()));

        return created;
    }

    public async Task<List<ScheduleBreakDto>> GetList(Guid organizationId, ScheduleBreakQuery query)
    {
        List<ScheduleBreak> breaks = await _scheduleBreakHandler.GetForSchedule(organizationId, query);
        return breaks.Select(ToDto).ToList();
    }

    public async Task<List<ScheduleBreakCellDto>> GetScheduleCells(Guid organizationId, ScheduleBreakQuery query)
    {
        List<ScheduleBreak> breaks = await _scheduleBreakHandler.GetForSchedule(organizationId, query);
        return breaks.Select(ToCellDto).ToList();
    }

    public Task<ScheduleBreakDto> GetById(Guid organizationId, Guid id)
    {
        return GetByIdInternal(organizationId, id);
    }

    private async Task<ScheduleBreakDto> CreateInternal(
        Guid organizationId, Guid userId, bool hasFullScope, ScheduleBreakCreateRequest request, Guid? recurrenceGroupId)
    {
        await ValidateOwnership(organizationId, userId, hasFullScope, request.EmployeeId);
        await EnsureEmployeeExists(organizationId, request.EmployeeId);
        await EnsureCompanyExists(organizationId, request.CompanyId);

        Guid id = Guid.NewGuid();
        ScheduleBreak scheduleBreak = new ScheduleBreak
        {
            Id = id,
            OrganizationId = organizationId,
            EmployeeId = request.EmployeeId,
            CompanyId = request.CompanyId,
            StartsAt = request.StartsAt,
            DurationMinutes = request.DurationMinutes,
            Note = request.Note,
            RecurrenceGroupId = recurrenceGroupId,
            CreatedAt = DateTimeOffset.UtcNow,
            CreatedBy = userId
        };

        await EnsureNoOverlap(organizationId, request.EmployeeId, request.StartsAt, request.DurationMinutes, excludeId: null);

        await _scheduleBreakHandler.Add(scheduleBreak);

        return await GetByIdInternal(organizationId, id);
    }

    /// <summary>Weekly = +7 dana. Daily = svaki kalendarski dan uključivo vikend, bez preskakanja.</summary>
    private static List<DateTimeOffset> BuildOccurrenceDates(RecurrenceType recurrenceType, DateTimeOffset first, DateTimeOffset end)
    {
        int stepDays = recurrenceType == RecurrenceType.Daily ? 1 : 7;

        List<DateTimeOffset> occurrences = new List<DateTimeOffset>();
        for (DateTimeOffset occurrence = first; occurrence <= end; occurrence = occurrence.AddDays(stepDays))
            occurrences.Add(occurrence);

        return occurrences;
    }

    /// <summary>Tvrda unaprijedna provjera za /recurring — svaki datum u nizu provjerava se protiv postojećih
    /// termina I postojećih pauza istog trenera (BEZ radnog vremena — pauza smije biti postavljena bilo kad).
    /// Cijeli niz pada ako ijedan datum sudara, RECURRING_CONFLICT (409) s popisom svih sudara — isti obrazac
    /// kao AppointmentService.EnsureNoRecurringConflicts.</summary>
    private async Task EnsureNoRecurringConflicts(Guid organizationId, Guid employeeId, List<DateTimeOffset> occurrences, int durationMinutes)
    {
        DateTimeOffset rangeFrom = occurrences[0].AddDays(-1);
        DateTimeOffset rangeTo = occurrences[^1].AddDays(1);

        List<Appointment> candidateAppointments = await _appointmentHandler.GetForEmployeeInRange(organizationId, employeeId, rangeFrom, rangeTo);
        List<ScheduleBreak> candidateBreaks = await _scheduleBreakHandler.GetForEmployeeInRange(organizationId, employeeId, rangeFrom, rangeTo);

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
                conflicts.Add(new RecurringConflictDetail { Date = occurrence, Reason = ErrorCodes.RecurringConflictReasonScheduleBreak });
        }

        if (conflicts.Count > 0)
            throw new BusinessRuleException(
                ErrorCodes.RecurringConflict, "Neke pauze u nizu se sudaraju s postojećim obavezama.", new { conflicts });
    }

    /// <summary>Baca APPOINTMENT_OVERLAP (409) prije spremanja ako se pauza preklapa s postojećim terminom ili
    /// drugom pauzom istog trenera. NE provjerava radno vrijeme — pauza smije biti postavljena bilo kad.</summary>
    private async Task EnsureNoOverlap(Guid organizationId, Guid employeeId, DateTimeOffset startsAt, int durationMinutes, Guid? excludeId)
    {
        List<Appointment> appointmentOverlaps = await _appointmentHandler.GetOverlappingForEmployee(
            organizationId, employeeId, startsAt, durationMinutes, excludeId: null);
        if (appointmentOverlaps.Count > 0)
            throw new BusinessRuleException(ErrorCodes.AppointmentOverlap, "Trener već ima termin u ovom vremenskom razdoblju.");

        List<ScheduleBreak> breakOverlaps = await _scheduleBreakHandler.GetOverlappingForEmployee(
            organizationId, employeeId, startsAt, durationMinutes, excludeId);
        if (breakOverlaps.Count > 0)
            throw new BusinessRuleException(ErrorCodes.AppointmentOverlap, "Trener već ima pauzu u ovom vremenskom razdoblju.");
    }

    private async Task ValidateOwnership(Guid organizationId, Guid userId, bool hasFullScope, Guid employeeId)
    {
        if (hasFullScope)
            return;

        Employee employee = await _employeeHandler.GetByUserId(organizationId, userId);
        if (employee == null || employee.Id != employeeId)
            throw new BusinessRuleException(ErrorCodes.NotOwner, "Trener smije upravljati samo svojim vlastitim pauzama.");
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

    private async Task<ScheduleBreakDto> GetByIdInternal(Guid organizationId, Guid id)
    {
        ScheduleBreak scheduleBreak = await _scheduleBreakHandler.GetById(organizationId, id);
        if (scheduleBreak == null)
            throw new NotFoundAppException("ScheduleBreak", id);

        return ToDto(scheduleBreak);
    }

    private static ScheduleBreakDto ToDto(ScheduleBreak b)
    {
        return new ScheduleBreakDto
        {
            Id = b.Id.GetValueOrDefault(),
            EmployeeId = b.EmployeeId,
            EmployeeName = b.Employee != null ? $"{b.Employee.FirstName} {b.Employee.LastName}" : null,
            CompanyId = b.CompanyId,
            CompanyName = b.Company?.Name,
            StartsAt = b.StartsAt,
            DurationMinutes = b.DurationMinutes,
            Note = b.Note,
            RecurrenceGroupId = b.RecurrenceGroupId,
            CreatedAt = b.CreatedAt,
            CreatedBy = b.CreatedBy,
            UpdatedAt = b.UpdatedAt,
            UpdatedBy = b.UpdatedBy
        };
    }

    private static ScheduleBreakCellDto ToCellDto(ScheduleBreak b)
    {
        return new ScheduleBreakCellDto
        {
            Id = b.Id.GetValueOrDefault(),
            StartsAt = b.StartsAt,
            DurationMinutes = b.DurationMinutes,
            EmployeeId = b.EmployeeId,
            EmployeeName = b.Employee != null ? $"{b.Employee.FirstName} {b.Employee.LastName}" : null,
            CompanyId = b.CompanyId,
            CompanyName = b.Company?.Name,
            Note = b.Note
        };
    }
}
