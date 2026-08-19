using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using BlueDragon.DuneLight.Core.DTOs.Roster;
using BlueDragon.DuneLight.Core.Enums;
using BlueDragon.DuneLight.Core.Interfaces.Roster;
using BlueDragon.DuneLight.Core.Shared.Exceptions;
using BlueDragon.DuneLight.Infrastructure.Domain.Models.Employees;
using BlueDragon.DuneLight.Infrastructure.Domain.Models.Catalog;
using BlueDragon.DuneLight.Infrastructure.Domain.Models.Roster;
using BlueDragon.DuneLight.Infrastructure.Handlers.Interfaces;
using BlueDragon.DuneLight.Infrastructure.Utils;

namespace BlueDragon.DuneLight.Infrastructure.Services;

public class WorkingHoursTemplateService : IWorkingHoursTemplateService
{
    private readonly IWorkingHoursTemplateHandler _templateHandler;
    private readonly IEmployeeHandler _employeeHandler;
    private readonly ICompanyHandler _companyHandler;
    private readonly IRosterEntryHandler _rosterEntryHandler;

    public WorkingHoursTemplateService(
        IWorkingHoursTemplateHandler templateHandler,
        IEmployeeHandler employeeHandler,
        ICompanyHandler companyHandler,
        IRosterEntryHandler rosterEntryHandler)
    {
        _templateHandler = templateHandler;
        _employeeHandler = employeeHandler;
        _companyHandler = companyHandler;
        _rosterEntryHandler = rosterEntryHandler;
    }

    public async Task<WorkingHoursTemplateDto> GetForEmployee(Guid organizationId, Guid employeeId)
    {
        WorkingHoursTemplate template = await _templateHandler.GetForEmployee(organizationId, employeeId);
        if (template == null)
            throw new NotFoundAppException("WorkingHoursTemplate", employeeId);

        return ToDto(template);
    }

    public async Task<WorkingHoursTemplateDto> UpsertForEmployee(
        Guid organizationId, Guid userId, Guid employeeId, WorkingHoursTemplateUpsertRequest request)
    {
        Employee employee = await _employeeHandler.GetByIdLight(organizationId, employeeId);
        if (employee == null)
            throw new NotFoundAppException("Employee", employeeId);

        List<WorkingHoursInterval> intervals = ValidateAndBuildIntervals(request);
        DateTimeOffset anchorMonday = NormalizeToMonday(request.AnchorDate);

        WorkingHoursTemplate saved = await _templateHandler.UpsertForEmployee(
            organizationId, employeeId, request.CycleType, anchorMonday, intervals, userId);

        return ToDto(saved);
    }

    public async Task<WorkingHoursTemplateDto> GetForCompany(Guid organizationId, Guid companyId)
    {
        WorkingHoursTemplate template = await _templateHandler.GetForCompany(organizationId, companyId);
        if (template == null)
            throw new NotFoundAppException("WorkingHoursTemplate", companyId);

        return ToDto(template);
    }

    public async Task<WorkingHoursTemplateDto> UpsertForCompany(
        Guid organizationId, Guid userId, Guid companyId, WorkingHoursTemplateUpsertRequest request)
    {
        Company company = await _companyHandler.GetById(organizationId, companyId);
        if (company == null)
            throw new NotFoundAppException("Company", companyId);

        List<WorkingHoursInterval> intervals = ValidateAndBuildIntervals(request);
        DateTimeOffset anchorMonday = NormalizeToMonday(request.AnchorDate);

        WorkingHoursTemplate saved = await _templateHandler.UpsertForCompany(
            organizationId, companyId, request.CycleType, anchorMonday, intervals, userId);

        return ToDto(saved);
    }

    public async Task<AvailabilityDto> GetAvailability(Guid organizationId, Guid employeeId, Guid companyId, DateTimeOffset date)
    {
        WorkingHoursTemplate employeeTemplate = await _templateHandler.GetForEmployee(organizationId, employeeId);
        WorkingHoursTemplate companyTemplate = await _templateHandler.GetForCompany(organizationId, companyId);

        List<RosterEntry> rosterEntriesForDate = await _rosterEntryHandler.GetForPeriod(
            organizationId, new List<Guid> { employeeId }, date.Date, date.Date);

        (List<WorkingHoursCalculator.Interval> employeeIntervals, AvailabilitySource employeeSource) =
            WorkingHoursCalculator.GetEffectiveEmployeeIntervals(employeeTemplate, rosterEntriesForDate, date);

        (List<WorkingHoursCalculator.Interval> companyIntervals, AvailabilitySource companySource) =
            WorkingHoursCalculator.GetEffectiveCompanyIntervals(companyTemplate, date);

        List<WorkingHoursCalculator.Interval> effective = WorkingHoursCalculator.IntersectIntervals(employeeIntervals, companyIntervals);

        return new AvailabilityDto
        {
            Date = date.Date,
            EmployeeIntervals = employeeIntervals.Select(ToDto).ToList(),
            CompanyIntervals = companyIntervals.Select(ToDto).ToList(),
            EffectiveIntervals = effective.Select(ToDto).ToList(),
            EmployeeSource = employeeSource,
            CompanySource = companySource
        };
    }

    /// <summary>Monday istog tjedna kao anchorDate (ne "kronološki najbliži") — poravnava sve predloške na predvidljivu tjednu granicu za admin UI.</summary>
    private static DateTimeOffset NormalizeToMonday(DateTimeOffset anchorDate)
    {
        int diff = (7 + (int)anchorDate.DayOfWeek - (int)DayOfWeek.Monday) % 7;
        return anchorDate.Date.AddDays(-diff);
    }

    private static List<WorkingHoursInterval> ValidateAndBuildIntervals(WorkingHoursTemplateUpsertRequest request)
    {
        int cycleLength = request.CycleType switch
        {
            WorkingHoursCycleType.Weekly => 1,
            WorkingHoursCycleType.Fortnightly => 2,
            WorkingHoursCycleType.FourWeekly => 4,
            _ => throw new ArgumentOutOfRangeException()
        };

        List<WorkingHoursIntervalRequest> intervals = request.Intervals ?? new List<WorkingHoursIntervalRequest>();

        foreach (WorkingHoursIntervalRequest interval in intervals)
        {
            if (interval.CycleWeekIndex < 0 || interval.CycleWeekIndex >= cycleLength)
                throw new ValidationAppException($"Tjedan ciklusa {interval.CycleWeekIndex} nije valjan za {request.CycleType} (dopušteno 0-{cycleLength - 1}).");

            if (interval.EndTime <= interval.StartTime)
                throw new ValidationAppException("Vrijeme do mora biti nakon vremena od.");
        }

        var groups = intervals.GroupBy(i => new { i.CycleWeekIndex, i.DayOfWeek });
        foreach (var group in groups)
        {
            List<WorkingHoursIntervalRequest> ordered = group.OrderBy(i => i.StartTime).ToList();
            for (int i = 1; i < ordered.Count; i++)
                if (ordered[i].StartTime < ordered[i - 1].EndTime)
                    throw new ValidationAppException($"Intervali se preklapaju za {group.Key.DayOfWeek} (tjedan ciklusa {group.Key.CycleWeekIndex}).");
        }

        return intervals.Select(i => new WorkingHoursInterval
        {
            CycleWeekIndex = i.CycleWeekIndex,
            DayOfWeek = i.DayOfWeek,
            StartTime = i.StartTime,
            EndTime = i.EndTime
        }).ToList();
    }

    private static WorkingHoursTemplateDto ToDto(WorkingHoursTemplate template)
    {
        return new WorkingHoursTemplateDto
        {
            Id = template.Id.GetValueOrDefault(),
            EmployeeId = template.EmployeeId,
            EmployeeName = template.Employee != null ? $"{template.Employee.FirstName} {template.Employee.LastName}" : null,
            CompanyId = template.CompanyId,
            CompanyName = template.Company?.Name,
            CycleType = template.CycleType,
            AnchorDate = template.AnchorDate,
            Intervals = template.Intervals
                .OrderBy(i => i.CycleWeekIndex).ThenBy(i => i.DayOfWeek).ThenBy(i => i.StartTime)
                .Select(i => new WorkingHoursIntervalDto
                {
                    CycleWeekIndex = i.CycleWeekIndex,
                    DayOfWeek = i.DayOfWeek,
                    StartTime = i.StartTime,
                    EndTime = i.EndTime
                }).ToList(),
            CreatedAt = template.CreatedAt,
            CreatedBy = template.CreatedBy,
            UpdatedAt = template.UpdatedAt,
            UpdatedBy = template.UpdatedBy
        };
    }

    private static AvailabilityIntervalDto ToDto(WorkingHoursCalculator.Interval interval)
    {
        return new AvailabilityIntervalDto { Start = interval.Start, End = interval.End };
    }
}