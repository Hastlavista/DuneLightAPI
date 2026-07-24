using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using BlueDragon.DuneLight.Core.DTOs.Roster;
using BlueDragon.DuneLight.Core.Interfaces.Roster;
using BlueDragon.DuneLight.Core.Shared;
using BlueDragon.DuneLight.Core.Shared.Exceptions;
using BlueDragon.DuneLight.Infrastructure.Domain.Models.Employees;
using BlueDragon.DuneLight.Infrastructure.Domain.Models.Roster;
using BlueDragon.DuneLight.Infrastructure.Handlers.Interfaces;
using BlueDragon.DuneLight.Infrastructure.Utils;

namespace BlueDragon.DuneLight.Infrastructure.Services;

public class RosterEntryService : IRosterEntryService
{
    private readonly IRosterEntryHandler _rosterEntryHandler;
    private readonly IRosterTypeHandler _rosterTypeHandler;
    private readonly IEmployeeHandler _employeeHandler;
    private readonly IRosterAuditLogHandler _auditLogHandler;

    public RosterEntryService(
        IRosterEntryHandler rosterEntryHandler,
        IRosterTypeHandler rosterTypeHandler,
        IEmployeeHandler employeeHandler,
        IRosterAuditLogHandler auditLogHandler)
    {
        _rosterEntryHandler = rosterEntryHandler;
        _rosterTypeHandler = rosterTypeHandler;
        _employeeHandler = employeeHandler;
        _auditLogHandler = auditLogHandler;
    }

    public async Task<PagedResult<RosterEntryDto>> GetPaged(
        Guid organizationId, PagedRequest request, Guid? employeeId, Guid? rosterTypeId, DateTimeOffset? from, DateTimeOffset? to)
    {
        (List<RosterEntry> items, int totalCount) = await _rosterEntryHandler.GetPaged(organizationId, request, employeeId, rosterTypeId, from, to);
        return PagedResult<RosterEntryDto>.Create(items.Select(ToDto).ToList(), totalCount, request.Page, request.PageSize);
    }

    public async Task<RosterEntryDto> GetById(Guid organizationId, Guid id)
    {
        RosterEntry entry = await _rosterEntryHandler.GetById(organizationId, id);
        if (entry == null)
            throw new NotFoundAppException("RosterEntry", id);

        return ToDto(entry);
    }

    public async Task<RosterEntryDto> Create(Guid organizationId, Guid userId, bool isAdmin, RosterEntryCreateRequest request)
    {
        await ValidateOwnership(organizationId, userId, isAdmin, request.EmployeeId);

        Employee employee = await _employeeHandler.GetById(organizationId, request.EmployeeId);
        if (employee == null)
            throw new NotFoundAppException("Employee", request.EmployeeId);
        if (!employee.IsActive)
            throw new BusinessRuleException(ErrorCodes.InactiveEmployee, "Zaposlenik nije aktivan.");

        RosterType type = await _rosterTypeHandler.GetById(organizationId, request.RosterTypeId);
        if (type == null)
            throw new NotFoundAppException("RosterType", request.RosterTypeId);
        if (!type.IsActive)
            throw new BusinessRuleException(ErrorCodes.InactiveType, "Vrsta rostera nije aktivna.");

        (DateTimeOffset dateFrom, DateTimeOffset? dateTo, TimeSpan? startTime, TimeSpan? endTime, decimal? durationHours) =
            ValidateAndCompute(type, request.DateFrom, request.DateTo, request.StartTime, request.EndTime);

        List<string> warnings = await ComputeOverlapWarnings(
            organizationId, request.EmployeeId, type, dateFrom, dateTo, startTime, endTime, excludeId: null);

        Guid entryId = Guid.NewGuid();
        RosterEntry entry = new RosterEntry
        {
            Id = entryId,
            OrganizationId = organizationId,
            EmployeeId = request.EmployeeId,
            RosterTypeId = request.RosterTypeId,
            DateFrom = dateFrom,
            DateTo = dateTo,
            StartTime = startTime,
            EndTime = endTime,
            DurationHours = durationHours,
            Note = request.Note,
            CreatedAt = DateTimeOffset.UtcNow,
            CreatedBy = userId
        };

        await _rosterEntryHandler.Add(entry);

        await _auditLogHandler.Add(new RosterAuditLog
        {
            Id = Guid.NewGuid(),
            RosterEntryId = entryId,
            ChangeType = "Created",
            OldValue = null,
            NewValue = BuildAuditSummary(entry, employee, type),
            ChangedAt = DateTimeOffset.UtcNow,
            ChangedBy = userId
        });

        RosterEntry refreshed = await _rosterEntryHandler.GetById(organizationId, entryId);
        RosterEntryDto dto = ToDto(refreshed);
        dto.Warnings = warnings;
        return dto;
    }

    public async Task<RosterEntryDto> Update(Guid organizationId, Guid userId, bool isAdmin, Guid id, RosterEntryUpdateRequest request)
    {
        RosterEntry existing = await _rosterEntryHandler.GetById(organizationId, id);
        if (existing == null)
            throw new NotFoundAppException("RosterEntry", id);

        await ValidateOwnership(organizationId, userId, isAdmin, existing.EmployeeId);
        if (!isAdmin && request.EmployeeId != existing.EmployeeId)
            throw new BusinessRuleException(ErrorCodes.NotOwner, "Smijete upravljati samo svojim vlastitim zapisima rostera.");

        Employee employee = await _employeeHandler.GetById(organizationId, request.EmployeeId);
        if (employee == null)
            throw new NotFoundAppException("Employee", request.EmployeeId);
        if (!employee.IsActive)
            throw new BusinessRuleException(ErrorCodes.InactiveEmployee, "Zaposlenik nije aktivan.");

        RosterType type = await _rosterTypeHandler.GetById(organizationId, request.RosterTypeId);
        if (type == null)
            throw new NotFoundAppException("RosterType", request.RosterTypeId);
        if (!type.IsActive)
            throw new BusinessRuleException(ErrorCodes.InactiveType, "Vrsta rostera nije aktivna.");

        string oldSummary = BuildAuditSummary(existing, existing.Employee, existing.RosterType);

        (DateTimeOffset dateFrom, DateTimeOffset? dateTo, TimeSpan? startTime, TimeSpan? endTime, decimal? durationHours) =
            ValidateAndCompute(type, request.DateFrom, request.DateTo, request.StartTime, request.EndTime);

        List<string> warnings = await ComputeOverlapWarnings(
            organizationId, request.EmployeeId, type, dateFrom, dateTo, startTime, endTime, excludeId: id);

        RosterEntry entry = await _rosterEntryHandler.GetByIdLight(organizationId, id);
        entry.EmployeeId = request.EmployeeId;
        entry.RosterTypeId = request.RosterTypeId;
        entry.DateFrom = dateFrom;
        entry.DateTo = dateTo;
        entry.StartTime = startTime;
        entry.EndTime = endTime;
        entry.DurationHours = durationHours;
        entry.Note = request.Note;
        entry.UpdatedAt = DateTimeOffset.UtcNow;
        entry.UpdatedBy = userId;

        await _rosterEntryHandler.Update(entry);

        await _auditLogHandler.Add(new RosterAuditLog
        {
            Id = Guid.NewGuid(),
            RosterEntryId = id,
            ChangeType = "Updated",
            OldValue = oldSummary,
            NewValue = BuildAuditSummary(entry, employee, type),
            ChangedAt = DateTimeOffset.UtcNow,
            ChangedBy = userId
        });

        RosterEntry refreshed = await _rosterEntryHandler.GetById(organizationId, id);
        RosterEntryDto dto = ToDto(refreshed);
        dto.Warnings = warnings;
        return dto;
    }

    public async Task Delete(Guid organizationId, Guid userId, bool isAdmin, Guid id)
    {
        RosterEntry existing = await _rosterEntryHandler.GetById(organizationId, id);
        if (existing == null)
            throw new NotFoundAppException("RosterEntry", id);

        await ValidateOwnership(organizationId, userId, isAdmin, existing.EmployeeId);

        // Audit se piše prije fizičkog brisanja — mora preživjeti brisanje retka (vidi RosterAuditLog).
        await _auditLogHandler.Add(new RosterAuditLog
        {
            Id = Guid.NewGuid(),
            RosterEntryId = id,
            ChangeType = "Deleted",
            OldValue = BuildAuditSummary(existing, existing.Employee, existing.RosterType),
            NewValue = null,
            ChangedAt = DateTimeOffset.UtcNow,
            ChangedBy = userId
        });

        RosterEntry entry = await _rosterEntryHandler.GetByIdLight(organizationId, id);
        await _rosterEntryHandler.Delete(entry);
    }

    public async Task<RosterTeamMonthlyDto> GetTeamMonthly(Guid organizationId, int year, int month, Guid? locationId)
    {
        DateTimeOffset monthStart = new DateTimeOffset(year, month, 1, 0, 0, 0, TimeSpan.Zero);
        DateTimeOffset monthEndInclusive = monthStart.AddMonths(1).AddTicks(-1);
        int daysInMonth = DateTime.DaysInMonth(year, month);

        (List<Employee> employees, int _) = await _employeeHandler.GetPaged(
            organizationId, new PagedRequest { Page = 1, PageSize = 200, IsActive = true }, locationId, null, null);

        List<Guid> employeeIds = employees.Select(e => e.Id.GetValueOrDefault()).ToList();
        List<RosterEntry> entries = employeeIds.Count == 0
            ? new List<RosterEntry>()
            : await _rosterEntryHandler.GetForPeriod(organizationId, employeeIds, monthStart, monthEndInclusive);

        ILookup<Guid, RosterEntry> byEmployee = entries.ToLookup(e => e.EmployeeId);

        RosterTeamMonthlyDto result = new RosterTeamMonthlyDto { Year = year, Month = month };

        foreach (Employee employee in employees.OrderBy(e => e.SortOrder).ThenBy(e => e.LastName))
        {
            List<RosterEntry> employeeEntries = byEmployee[employee.Id.GetValueOrDefault()].ToList();

            RosterEmployeeMonthDto employeeDto = new RosterEmployeeMonthDto
            {
                EmployeeId = employee.Id.GetValueOrDefault(),
                EmployeeName = $"{employee.FirstName} {employee.LastName}"
            };

            for (int day = 1; day <= daysInMonth; day++)
            {
                DateTime dayDate = monthStart.AddDays(day - 1).Date;
                RosterDayCellDto cell = new RosterDayCellDto { Day = day };

                foreach (RosterEntry entry in employeeEntries)
                {
                    DateTime entryFrom = entry.DateFrom.Date;
                    DateTime entryTo = entry.RosterType.IsAbsence
                        ? (entry.DateTo?.Date ?? monthEndInclusive.Date)
                        : entryFrom;

                    if (dayDate < entryFrom || dayDate > entryTo)
                        continue;

                    cell.Entries.Add(new RosterDayCellEntryDto
                    {
                        RosterEntryId = entry.Id.GetValueOrDefault(),
                        RosterTypeId = entry.RosterTypeId,
                        RosterTypeName = entry.RosterType.Name,
                        RosterTypeColorHex = entry.RosterType.ColorHex,
                        IsAbsence = entry.RosterType.IsAbsence,
                        Hours = entry.DurationHours,
                        TimeRange = entry.RosterType.IsAbsence ? null : $"{entry.StartTime:hh\\:mm}-{entry.EndTime:hh\\:mm}"
                    });
                }

                employeeDto.Days.Add(cell);
            }

            (employeeDto.WorkHoursByType, employeeDto.TotalWorkHours, employeeDto.AbsenceDaysByType) =
                BuildSums(employeeEntries, monthStart, monthEndInclusive);

            result.Employees.Add(employeeDto);
        }

        return result;
    }

    public async Task<RosterPersonalReviewDto> GetPersonal(
        Guid organizationId, Guid userId, bool isAdmin, Guid employeeId, DateTimeOffset from, DateTimeOffset to)
    {
        await ValidateOwnership(organizationId, userId, isAdmin, employeeId);

        Employee employee = await _employeeHandler.GetById(organizationId, employeeId);
        if (employee == null)
            throw new NotFoundAppException("Employee", employeeId);

        List<RosterEntry> entries = await _rosterEntryHandler.GetForPeriod(organizationId, new List<Guid> { employeeId }, from, to);

        RosterPersonalReviewDto dto = new RosterPersonalReviewDto
        {
            EmployeeId = employeeId,
            EmployeeName = $"{employee.FirstName} {employee.LastName}",
            From = from,
            To = to,
            Entries = entries.Select(ToDto).ToList()
        };

        (dto.WorkHoursByType, dto.TotalWorkHours, dto.AbsenceDaysByType) = BuildSums(entries, from, to);
        return dto;
    }

    private async Task ValidateOwnership(Guid organizationId, Guid userId, bool isAdmin, Guid employeeId)
    {
        if (isAdmin)
            return;

        Employee employee = await _employeeHandler.GetByUserId(organizationId, userId);
        if (employee == null || employee.Id != employeeId)
            throw new BusinessRuleException(ErrorCodes.NotOwner, "Smijete upravljati samo svojim vlastitim zapisima rostera.");
    }

    /// <summary>
    /// Oblik zapisa (rad/odsutnost) određuje isključivo RosterType.IsAbsence. Vraća normalizirane (datum-only) vrijednosti.
    /// </summary>
    private static (DateTimeOffset DateFrom, DateTimeOffset? DateTo, TimeSpan? StartTime, TimeSpan? EndTime, decimal? DurationHours) ValidateAndCompute(
        RosterType type, DateTimeOffset dateFrom, DateTimeOffset? dateTo, TimeSpan? startTime, TimeSpan? endTime)
    {
        if (type.IsAbsence)
        {
            if (startTime.HasValue || endTime.HasValue)
                throw new ValidationAppException("Odsutnost ne koristi vrijeme od/do — ostavite prazno.");

            if (dateTo.HasValue && dateTo.Value.Date < dateFrom.Date)
                throw new ValidationAppException("Datum do ne smije biti prije datuma od.");

            return (dateFrom.Date, dateTo?.Date, null, null, null);
        }

        if (dateTo.HasValue && dateTo.Value.Date != dateFrom.Date)
            throw new ValidationAppException("Rad ima jedan datum, ne raspon — ostavite 'datum do' prazno.");

        if (!startTime.HasValue || !endTime.HasValue)
            throw new ValidationAppException("Rad zahtijeva vrijeme od i do.");

        if (endTime.Value <= startTime.Value)
            throw new ValidationAppException("Vrijeme do mora biti nakon vremena od.");

        decimal durationHours = Math.Round((decimal)(endTime.Value - startTime.Value).TotalHours, 2);
        return (dateFrom.Date, null, startTime, endTime, durationHours);
    }

    private async Task<List<string>> ComputeOverlapWarnings(
        Guid organizationId, Guid employeeId, RosterType candidateType,
        DateTimeOffset dateFrom, DateTimeOffset? dateTo, TimeSpan? startTime, TimeSpan? endTime, Guid? excludeId)
    {
        (DateTimeOffset candidateFrom, DateTimeOffset? candidateTo) = ComputeBounds(candidateType.IsAbsence, dateFrom, dateTo, startTime, endTime);

        DateTimeOffset windowFrom = dateFrom.Date;
        DateTimeOffset windowTo = dateTo ?? DateTimeOffset.MaxValue;

        List<RosterEntry> candidates = await _rosterEntryHandler.GetOverlapCandidates(organizationId, employeeId, windowFrom, windowTo, excludeId);

        List<string> warnings = new List<string>();
        foreach (RosterEntry candidate in candidates)
        {
            (DateTimeOffset otherFrom, DateTimeOffset? otherTo) = ComputeBounds(
                candidate.RosterType.IsAbsence, candidate.DateFrom, candidate.DateTo, candidate.StartTime, candidate.EndTime);

            if (DateRangeOverlap.Overlaps(candidateFrom, candidateTo, otherFrom, otherTo))
                warnings.Add($"Preklapa se s postojećim zapisom: {candidate.RosterType.Name} ({DescribeRange(candidate)})");
        }

        return warnings;
    }

    /// <summary>Odsutnost pokriva cijeli dan (00:00-24:00); rad pokriva samo svoj vremenski prozor — tako dva rad-zapisa
    /// istog dana (dvokratni) ne prijavljuju lažno preklapanje, dok rad unutar odsutnosti ispravno prijavljuje.</summary>
    private static (DateTimeOffset From, DateTimeOffset? To) ComputeBounds(
        bool isAbsence, DateTimeOffset dateFrom, DateTimeOffset? dateTo, TimeSpan? startTime, TimeSpan? endTime)
    {
        if (isAbsence)
        {
            DateTimeOffset from = dateFrom.Date;
            DateTimeOffset? to = dateTo?.Date.AddDays(1).AddTicks(-1);
            return (from, to);
        }

        return (dateFrom.Date + startTime!.Value, dateFrom.Date + endTime!.Value);
    }

    private static string DescribeRange(RosterEntry entry)
    {
        if (entry.RosterType.IsAbsence)
        {
            string to = entry.DateTo.HasValue ? entry.DateTo.Value.ToString("dd.MM.yyyy.") : "otvoreno";
            return $"{entry.DateFrom:dd.MM.yyyy.} - {to}";
        }

        return $"{entry.DateFrom:dd.MM.yyyy.} {entry.StartTime:hh\\:mm}-{entry.EndTime:hh\\:mm}";
    }

    private static (List<RosterWorkHoursSumDto> WorkSums, decimal TotalHours, List<RosterAbsenceDaysSumDto> AbsenceSums) BuildSums(
        List<RosterEntry> entries, DateTimeOffset periodFrom, DateTimeOffset periodTo)
    {
        List<RosterWorkHoursSumDto> workSums = entries
            .Where(e => e.RosterType.CountsAsWork && e.DurationHours.HasValue)
            .GroupBy(e => new { e.RosterTypeId, e.RosterType.Name })
            .Select(g => new RosterWorkHoursSumDto
            {
                RosterTypeId = g.Key.RosterTypeId,
                RosterTypeName = g.Key.Name,
                Hours = g.Sum(e => e.DurationHours!.Value)
            })
            .OrderBy(s => s.RosterTypeName)
            .ToList();

        decimal totalHours = workSums.Sum(s => s.Hours);

        List<RosterAbsenceDaysSumDto> absenceSums = entries
            .Where(e => e.RosterType.IsAbsence)
            .GroupBy(e => new { e.RosterTypeId, e.RosterType.Name })
            .Select(g => new RosterAbsenceDaysSumDto
            {
                RosterTypeId = g.Key.RosterTypeId,
                RosterTypeName = g.Key.Name,
                Days = g.Sum(e => ClipAbsenceDays(e, periodFrom, periodTo))
            })
            .OrderBy(s => s.RosterTypeName)
            .ToList();

        return (workSums, totalHours, absenceSums);
    }

    /// <summary>Broji kalendarske dane preklapanja odsutnosti s traženim razdobljem, klipano na granice razdoblja
    /// (otvorena odsutnost bez datuma do se za potrebe zbroja klipa na kraj razdoblja, ne broji se unedogled).</summary>
    private static int ClipAbsenceDays(RosterEntry entry, DateTimeOffset periodFrom, DateTimeOffset periodTo)
    {
        DateTime from = entry.DateFrom.Date > periodFrom.Date ? entry.DateFrom.Date : periodFrom.Date;
        DateTime entryTo = entry.DateTo?.Date ?? periodTo.Date;
        DateTime to = entryTo < periodTo.Date ? entryTo : periodTo.Date;
        return to >= from ? (to - from).Days + 1 : 0;
    }

    private static RosterEntryDto ToDto(RosterEntry entry)
    {
        return new RosterEntryDto
        {
            Id = entry.Id.GetValueOrDefault(),
            EmployeeId = entry.EmployeeId,
            EmployeeName = $"{entry.Employee.FirstName} {entry.Employee.LastName}",
            RosterTypeId = entry.RosterTypeId,
            RosterTypeName = entry.RosterType.Name,
            RosterTypeColorHex = entry.RosterType.ColorHex,
            IsAbsence = entry.RosterType.IsAbsence,
            CountsAsWork = entry.RosterType.CountsAsWork,
            DateFrom = entry.DateFrom,
            DateTo = entry.DateTo,
            StartTime = entry.StartTime,
            EndTime = entry.EndTime,
            DurationHours = entry.DurationHours,
            Note = entry.Note,
            CreatedAt = entry.CreatedAt,
            CreatedBy = entry.CreatedBy,
            UpdatedAt = entry.UpdatedAt,
            UpdatedBy = entry.UpdatedBy
        };
    }

    private static string BuildAuditSummary(RosterEntry entry, Employee employee, RosterType type)
    {
        string employeeName = employee != null ? $"{employee.FirstName} {employee.LastName}" : entry.EmployeeId.ToString();
        string range = type.IsAbsence
            ? $"{entry.DateFrom:dd.MM.yyyy.} - {(entry.DateTo.HasValue ? entry.DateTo.Value.ToString("dd.MM.yyyy.") : "otvoreno")}"
            : $"{entry.DateFrom:dd.MM.yyyy.} {entry.StartTime:hh\\:mm}-{entry.EndTime:hh\\:mm}";

        return $"Zaposlenik={employeeName}; Vrsta={type.Name}; {range}; Napomena={entry.Note ?? "-"}";
    }
}
