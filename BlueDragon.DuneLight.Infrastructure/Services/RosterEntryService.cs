using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using BlueDragon.DuneLight.Core.DTOs.Roster;
using BlueDragon.DuneLight.Core.Enums;
using BlueDragon.DuneLight.Core.Interfaces.Roster;
using BlueDragon.DuneLight.Core.Shared;
using BlueDragon.DuneLight.Core.Shared.Exceptions;
using BlueDragon.DuneLight.Infrastructure.Domain.Models.Employees;
using BlueDragon.DuneLight.Infrastructure.Domain.Models.Roster;
using BlueDragon.DuneLight.Infrastructure.Handlers.Interfaces;
using BlueDragon.DuneLight.Infrastructure.UnitOfWork;
using BlueDragon.DuneLight.Infrastructure.Utils;

namespace BlueDragon.DuneLight.Infrastructure.Services;

public class RosterEntryService : IRosterEntryService
{
    private readonly IRosterEntryHandler _rosterEntryHandler;
    private readonly IRosterTypeHandler _rosterTypeHandler;
    private readonly IEmployeeHandler _employeeHandler;
    private readonly IRosterAuditLogHandler _auditLogHandler;
    private readonly IWorkingHoursTemplateHandler _workingHoursTemplateHandler;
    private readonly ICompanyHolidayHandler _companyHolidayHandler;
    private readonly ILeaveFundHandler _leaveFundHandler;
    private readonly IEmployeeLeaveSettingsHandler _employeeLeaveSettingsHandler;
    private readonly IUnitOfWorkFactory _unitOfWorkFactory;

    public RosterEntryService(
        IRosterEntryHandler rosterEntryHandler,
        IRosterTypeHandler rosterTypeHandler,
        IEmployeeHandler employeeHandler,
        IRosterAuditLogHandler auditLogHandler,
        IWorkingHoursTemplateHandler workingHoursTemplateHandler,
        ICompanyHolidayHandler companyHolidayHandler,
        ILeaveFundHandler leaveFundHandler,
        IEmployeeLeaveSettingsHandler employeeLeaveSettingsHandler,
        IUnitOfWorkFactory unitOfWorkFactory)
    {
        _rosterEntryHandler = rosterEntryHandler;
        _rosterTypeHandler = rosterTypeHandler;
        _employeeHandler = employeeHandler;
        _auditLogHandler = auditLogHandler;
        _workingHoursTemplateHandler = workingHoursTemplateHandler;
        _companyHolidayHandler = companyHolidayHandler;
        _leaveFundHandler = leaveFundHandler;
        _employeeLeaveSettingsHandler = employeeLeaveSettingsHandler;
        _unitOfWorkFactory = unitOfWorkFactory;
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

    public async Task<RosterEntryDto> Create(Guid organizationId, Guid userId, bool hasFullScope, RosterEntryCreateRequest request)
    {
        await ValidateOwnership(organizationId, userId, hasFullScope, request.EmployeeId);

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

        EnsureLeaveFundEntryHasEndDate(type, dateTo);

        bool isOverride = !type.IsAbsence && request.IsOverride;
        if (!type.IsAbsence)
            await ValidateIsOverrideConsistency(organizationId, request.EmployeeId, dateFrom, isOverride, excludeId: null);

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
            IsOverride = isOverride,
            CreatedAt = DateTimeOffset.UtcNow,
            CreatedBy = userId
        };

        await using (IUnitOfWork uow = await _unitOfWorkFactory.Begin())
        {
            await _rosterEntryHandler.Add(uow, entry);

            await _auditLogHandler.Add(uow, new RosterAuditLog
            {
                Id = Guid.NewGuid(),
                RosterEntryId = entryId,
                ChangeType = "Created",
                OldValue = null,
                NewValue = BuildAuditSummary(entry, employee, type),
                ChangedAt = DateTimeOffset.UtcNow,
                ChangedBy = userId
            });

            if (type.DeductsFromLeaveFund)
                await AllocateLeaveFund(uow, organizationId, request.EmployeeId, entryId, dateFrom, dateTo!.Value, userId);

            await uow.CommitAsync();
        }

        RosterEntry refreshed = await _rosterEntryHandler.GetById(organizationId, entryId);
        RosterEntryDto dto = ToDto(refreshed);
        dto.Warnings = warnings;
        return dto;
    }

    public async Task<RosterEntryDto> Update(Guid organizationId, Guid userId, bool hasFullScope, Guid id, RosterEntryUpdateRequest request)
    {
        RosterEntry existing = await _rosterEntryHandler.GetById(organizationId, id);
        if (existing == null)
            throw new NotFoundAppException("RosterEntry", id);

        await ValidateOwnership(organizationId, userId, hasFullScope, existing.EmployeeId);
        if (!hasFullScope && request.EmployeeId != existing.EmployeeId)
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

        EnsureLeaveFundEntryHasEndDate(type, dateTo);

        bool isOverride = !type.IsAbsence && request.IsOverride;
        if (!type.IsAbsence)
            await ValidateIsOverrideConsistency(organizationId, request.EmployeeId, dateFrom, isOverride, excludeId: id);

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
        entry.IsOverride = isOverride;
        entry.UpdatedAt = DateTimeOffset.UtcNow;
        entry.UpdatedBy = userId;

        await using (IUnitOfWork uow = await _unitOfWorkFactory.Begin())
        {
            // Puni reversal PRIJE realokacije (bez obzira je li stari tip trošio fond) — jednostavnije i sigurnije
            // od diff-anja starog/novog raspona, vidi LeaveFundAllocator.
            await ReturnLeaveFundUsages(uow, organizationId, id);

            await _rosterEntryHandler.Update(uow, entry);

            await _auditLogHandler.Add(uow, new RosterAuditLog
            {
                Id = Guid.NewGuid(),
                RosterEntryId = id,
                ChangeType = "Updated",
                OldValue = oldSummary,
                NewValue = BuildAuditSummary(entry, employee, type),
                ChangedAt = DateTimeOffset.UtcNow,
                ChangedBy = userId
            });

            if (type.DeductsFromLeaveFund)
                await AllocateLeaveFund(uow, organizationId, request.EmployeeId, id, dateFrom, dateTo!.Value, userId);

            await uow.CommitAsync();
        }

        RosterEntry refreshed = await _rosterEntryHandler.GetById(organizationId, id);
        RosterEntryDto dto = ToDto(refreshed);
        dto.Warnings = warnings;
        return dto;
    }

    public async Task Delete(Guid organizationId, Guid userId, bool hasFullScope, Guid id)
    {
        RosterEntry existing = await _rosterEntryHandler.GetById(organizationId, id);
        if (existing == null)
            throw new NotFoundAppException("RosterEntry", id);

        await ValidateOwnership(organizationId, userId, hasFullScope, existing.EmployeeId);

        RosterEntry entry = await _rosterEntryHandler.GetByIdLight(organizationId, id);

        await using IUnitOfWork uow = await _unitOfWorkFactory.Begin();

        // Povrat dana fonda PRIJE brisanja retka — LeaveFundUsage FK je CASCADE (sigurnosna mreža), ne poslovna logika.
        await ReturnLeaveFundUsages(uow, organizationId, id);

        // Audit se piše prije fizičkog brisanja — mora preživjeti brisanje retka (vidi RosterAuditLog).
        await _auditLogHandler.Add(uow, new RosterAuditLog
        {
            Id = Guid.NewGuid(),
            RosterEntryId = id,
            ChangeType = "Deleted",
            OldValue = BuildAuditSummary(existing, existing.Employee, existing.RosterType),
            NewValue = null,
            ChangedAt = DateTimeOffset.UtcNow,
            ChangedBy = userId
        });

        await _rosterEntryHandler.Delete(uow, entry);

        await uow.CommitAsync();
    }

    public async Task<RosterTeamMonthlyDto> GetTeamMonthly(Guid organizationId, int year, int month, Guid? companyId)
    {
        DateTimeOffset monthStart = new DateTimeOffset(year, month, 1, 0, 0, 0, TimeSpan.Zero);
        DateTimeOffset monthEndInclusive = monthStart.AddMonths(1).AddTicks(-1);
        int daysInMonth = DateTime.DaysInMonth(year, month);

        (List<Employee> employees, int _) = await _employeeHandler.GetPaged(
            organizationId, new PagedRequest { Page = 1, PageSize = 200, IsActive = true }, companyId, null, null);

        List<Guid> employeeIds = employees.Select(e => e.Id.GetValueOrDefault()).ToList();
        List<RosterEntry> entries = employeeIds.Count == 0
            ? new List<RosterEntry>()
            : await _rosterEntryHandler.GetForPeriod(organizationId, employeeIds, monthStart, monthEndInclusive);

        List<WorkingHoursTemplate> templates = employeeIds.Count == 0
            ? new List<WorkingHoursTemplate>()
            : await _workingHoursTemplateHandler.GetForEmployees(organizationId, employeeIds);

        ILookup<Guid, RosterEntry> byEmployee = entries.ToLookup(e => e.EmployeeId);
        Dictionary<Guid, WorkingHoursTemplate> templateByEmployee = templates.ToDictionary(t => t.EmployeeId!.Value);

        Dictionary<Guid, Guid?> primaryCompanyByEmployee = employees.ToDictionary(
            e => e.Id.GetValueOrDefault(), e => e.Companies.FirstOrDefault(c => c.IsPrimary)?.CompanyId);
        List<Guid> primaryCompanyIds = primaryCompanyByEmployee.Values.Where(c => c.HasValue).Select(c => c!.Value).Distinct().ToList();
        List<CompanyHoliday> holidays = await _companyHolidayHandler.GetForCompaniesInRange(
            organizationId, primaryCompanyIds, monthStart, monthEndInclusive);

        DateTime today = DateTimeOffset.UtcNow.Date;
        RosterTeamMonthlyDto result = new RosterTeamMonthlyDto { Year = year, Month = month };

        foreach (Employee employee in employees.OrderBy(e => e.SortOrder).ThenBy(e => e.LastName))
        {
            List<RosterEntry> employeeEntries = byEmployee[employee.Id.GetValueOrDefault()].ToList();
            templateByEmployee.TryGetValue(employee.Id.GetValueOrDefault(), out WorkingHoursTemplate employeeTemplate);
            Guid? employeeCompanyId = primaryCompanyByEmployee[employee.Id.GetValueOrDefault()];

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

                ApplyPlannedSource(cell, employeeTemplate, dayDate, today);

                employeeDto.Days.Add(cell);
            }

            (employeeDto.WorkHoursByType, employeeDto.TotalWorkHours, employeeDto.AbsenceDaysByType) =
                BuildSums(employeeEntries, employeeTemplate, monthStart, monthEndInclusive, holidays, employeeCompanyId);

            result.Employees.Add(employeeDto);
        }

        return result;
    }

    /// <summary>Stvarni zapisi uvijek imaju prednost (Source=Actual). Bez stvarnog zapisa, kad predložak razrješava
    /// radne intervale za taj dan: Assumed za prošlost/danas (upravljanje po iznimci — tretira se kao odrađeno,
    /// vidi BuildSums/ComputeAssumedHours), Planned za budućnost (ne broji se). Inače None (predložak kaže
    /// neradni dan, ili nema predloška).</summary>
    private static void ApplyPlannedSource(RosterDayCellDto cell, WorkingHoursTemplate template, DateTime dayDate, DateTime today)
    {
        if (cell.Entries.Count > 0)
        {
            cell.Source = RosterCellSource.Actual;
            return;
        }

        (List<WorkingHoursCalculator.Interval> intervals, AvailabilitySource source) = WorkingHoursCalculator.GetEffectiveEmployeeIntervals(
            template, new List<RosterEntry>(), new DateTimeOffset(dayDate, TimeSpan.Zero));

        if (source != AvailabilitySource.Template)
            return;

        cell.Source = dayDate <= today ? RosterCellSource.Assumed : RosterCellSource.Planned;
        cell.PlannedIntervals = intervals.Select(i => new RosterPlannedIntervalDto { Start = i.Start, End = i.End }).ToList();
    }

    public async Task<RosterPersonalReviewDto> GetPersonal(
        Guid organizationId, Guid userId, bool hasFullScope, Guid employeeId, DateTimeOffset from, DateTimeOffset to)
    {
        await ValidateOwnership(organizationId, userId, hasFullScope, employeeId);

        Employee employee = await _employeeHandler.GetById(organizationId, employeeId);
        if (employee == null)
            throw new NotFoundAppException("Employee", employeeId);

        List<RosterEntry> entries = await _rosterEntryHandler.GetForPeriod(organizationId, new List<Guid> { employeeId }, from, to);
        WorkingHoursTemplate template = await _workingHoursTemplateHandler.GetForEmployee(organizationId, employeeId);

        Guid? employeeCompanyId = employee.Companies.FirstOrDefault(c => c.IsPrimary)?.CompanyId;
        List<CompanyHoliday> holidays = employeeCompanyId.HasValue
            ? await _companyHolidayHandler.GetForCompaniesInRange(organizationId, new List<Guid> { employeeCompanyId.Value }, from, to)
            : new List<CompanyHoliday>();

        RosterPersonalReviewDto dto = new RosterPersonalReviewDto
        {
            EmployeeId = employeeId,
            EmployeeName = $"{employee.FirstName} {employee.LastName}",
            From = from,
            To = to,
            Entries = entries.Select(ToDto).ToList()
        };

        (dto.WorkHoursByType, dto.TotalWorkHours, dto.AbsenceDaysByType) = BuildSums(entries, template, from, to, holidays, employeeCompanyId);
        dto.PlannedDays = BuildPlannedDays(entries, template, from, to);
        return dto;
    }

    /// <summary>Budući dani (nakon danas) u [from,to] bez stvarnog zapisa za koje postoji WorkingHoursTemplate —
    /// prošli/današnji dani bez zapisa su "Assumed" i ulaze u BuildSums umjesto ovdje, vidi ApplyPlannedSource
    /// (isto pravilo, team-monthly grana).</summary>
    private static List<RosterPlannedDayDto> BuildPlannedDays(
        List<RosterEntry> entries, WorkingHoursTemplate template, DateTimeOffset from, DateTimeOffset to)
    {
        DateTime tomorrow = DateTimeOffset.UtcNow.Date.AddDays(1);
        DateTime rangeStart = from.Date > tomorrow ? from.Date : tomorrow;
        if (rangeStart > to.Date)
            return new List<RosterPlannedDayDto>();

        List<RosterPlannedDayDto> plannedDays = new List<RosterPlannedDayDto>();

        for (DateTime date = rangeStart; date <= to.Date; date = date.AddDays(1))
        {
            if (HasActualEntryForDay(entries, date, to.Date))
                continue;

            DateTimeOffset dateOffset = new DateTimeOffset(date, TimeSpan.Zero);
            (List<WorkingHoursCalculator.Interval> intervals, AvailabilitySource source) = WorkingHoursCalculator.GetEffectiveEmployeeIntervals(
                template, new List<RosterEntry>(), dateOffset);

            if (source != AvailabilitySource.Template)
                continue;

            plannedDays.Add(new RosterPlannedDayDto
            {
                Date = dateOffset,
                Intervals = intervals.Select(i => new RosterPlannedIntervalDto { Start = i.Start, End = i.End }).ToList()
            });
        }

        return plannedDays;
    }

    private async Task ValidateOwnership(Guid organizationId, Guid userId, bool hasFullScope, Guid employeeId)
    {
        if (hasFullScope)
            return;

        Employee employee = await _employeeHandler.GetByUserId(organizationId, userId);
        if (employee == null || employee.Id != employeeId)
            throw new BusinessRuleException(ErrorCodes.NotOwner, "Smijete upravljati samo svojim vlastitim zapisima rostera.");
    }

    /// <summary>Tip koji troši fond godišnjeg odmora zahtijeva zatvoren raspon — beskonačna ("otvorena") odsutnost ne može oduzeti određen broj dana od fonda.</summary>
    private static void EnsureLeaveFundEntryHasEndDate(RosterType type, DateTimeOffset? dateTo)
    {
        if (type.DeductsFromLeaveFund && !dateTo.HasValue)
            throw new BusinessRuleException(
                ErrorCodes.LeaveFundEntryRequiresEndDate,
                "Vrsta rostera koja troši fond godišnjeg odmora zahtijeva datum do.");
    }

    /// <summary>Troši fond godišnjeg odmora za [dateFrom,dateTo] (kalendarski dani, uključivo) — lijeno otvara fond(ove)
    /// relevantne godine ako ne postoje, pa troši stariji-prvo preko LeaveFundAllocator (baca LEAVE_FUND_EXCEEDED
    /// ako zbroj svih neisteklih fondova ne pokriva traženo). Poziva se unutar iste transakcije kao upis RosterEntry-ja.</summary>
    private async Task AllocateLeaveFund(
        IUnitOfWork uow, Guid organizationId, Guid employeeId, Guid rosterEntryId,
        DateTimeOffset dateFrom, DateTimeOffset dateTo, Guid userId)
    {
        EmployeeLeaveSettings settings = await _employeeLeaveSettingsHandler.GetForEmployee(organizationId, employeeId);
        if (settings == null)
            throw new BusinessRuleException(
                ErrorCodes.LeaveSettingsNotConfigured,
                "Zaposlenik nema podešen fond godišnjeg odmora — postavite broj dana i datume prije upisa godišnjeg.");

        int requestedDays = (dateTo.Date - dateFrom.Date).Days + 1;

        int fromYear = LeaveFundYearCalculator.ResolveFundYear(settings, dateFrom);
        int toYear = LeaveFundYearCalculator.ResolveFundYear(settings, dateTo);

        await _leaveFundHandler.GetOrCreateForYear(uow, organizationId, employeeId, settings, fromYear, userId);
        if (toYear != fromYear)
            await _leaveFundHandler.GetOrCreateForYear(uow, organizationId, employeeId, settings, toYear, userId);

        List<LeaveFund> eligible = await _leaveFundHandler.GetEligible(uow, organizationId, employeeId, DateTimeOffset.UtcNow);
        List<(LeaveFund Fund, int Days)> allocation = LeaveFundAllocator.Deduct(eligible, requestedDays);

        foreach ((LeaveFund fund, int days) in allocation)
        {
            await _leaveFundHandler.Update(uow, fund);
            await _leaveFundHandler.AddUsage(uow, new LeaveFundUsage
            {
                Id = Guid.NewGuid(),
                OrganizationId = organizationId,
                RosterEntryId = rosterEntryId,
                LeaveFundId = fund.Id!.Value,
                Days = days,
                CreatedAt = DateTimeOffset.UtcNow
            });
        }
    }

    /// <summary>Vraća sve dane fonda potrošene za ovaj RosterEntry (no-op ako ih nema) — poziva se bezuvjetno na svaki Update/Delete, prije eventualne nove alokacije.</summary>
    private async Task ReturnLeaveFundUsages(IUnitOfWork uow, Guid organizationId, Guid rosterEntryId)
    {
        List<LeaveFundUsage> usages = await _leaveFundHandler.GetUsagesForEntry(uow, organizationId, rosterEntryId);
        if (usages.Count == 0)
            return;

        foreach (LeaveFundUsage usage in usages)
        {
            LeaveFundAllocator.Return(usage.LeaveFund, usage.Days);
            await _leaveFundHandler.Update(uow, usage.LeaveFund);
        }

        await _leaveFundHandler.DeleteUsages(uow, usages);
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

    /// <summary>Svi work-redovi istog EmployeeId+DateFrom (dvokratni rad) moraju dijeliti istu IsOverride vrijednost —
    /// inače je stanje "pola override, pola predložak" za isti dan dvosmisleno kod izračuna dostupnosti (vidi
    /// WorkingHoursCalculator, FAZA 2). Ne primjenjuje se na odsutnost (uvijek IsOverride=false, irelevantno).</summary>
    private async Task ValidateIsOverrideConsistency(
        Guid organizationId, Guid employeeId, DateTimeOffset dateFrom, bool isOverride, Guid? excludeId)
    {
        List<RosterEntry> candidates = await _rosterEntryHandler.GetOverlapCandidates(
            organizationId, employeeId, dateFrom.Date, dateFrom.Date, excludeId);

        bool mismatch = candidates.Any(c => !c.RosterType.IsAbsence && c.DateFrom.Date == dateFrom.Date && c.IsOverride != isOverride);
        if (mismatch)
            throw new ValidationAppException(
                "Svi zapisi rada istog zaposlenika i datuma (dvokratni rad) moraju imati istu vrijednost \"override\" — uredite ih zajedno.");
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

    /// <summary>Sentinel RosterTypeId za sintetički "Pretpostavljeno" redak u WorkHoursByType — nema stvaran
    /// RosterType jer WorkingHoursTemplate ne nosi tip, a organizacija može imati više tipova koji broje kao rad
    /// (Smjena/Bowen/Rec-dvok...), pa nema jedan "točan" tip kojem bi se pretpostavljeni sati mogli pripisati.</summary>
    private static readonly Guid AssumedRosterTypeId = Guid.Empty;
    private const string AssumedRosterTypeName = "Pretpostavljeno (predložak)";

    private static (List<RosterWorkHoursSumDto> WorkSums, decimal TotalHours, List<RosterAbsenceDaysSumDto> AbsenceSums) BuildSums(
        List<RosterEntry> entries, WorkingHoursTemplate template, DateTimeOffset periodFrom, DateTimeOffset periodTo,
        List<CompanyHoliday> companyHolidays, Guid? employeeCompanyId)
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

        decimal assumedHours = ComputeAssumedHours(entries, template, periodFrom, periodTo, companyHolidays, employeeCompanyId);
        if (assumedHours > 0)
        {
            workSums.Add(new RosterWorkHoursSumDto
            {
                RosterTypeId = AssumedRosterTypeId,
                RosterTypeName = AssumedRosterTypeName,
                Hours = assumedHours
            });
        }

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

    /// <summary>Upravljanje po iznimci: za svaki dan u [periodFrom, min(today,periodTo)] bez stvarnog zapisa,
    /// kad predložak razrješava radne intervale za taj dan, zbraja te sate kao "odrađeno" (vidi ApplyPlannedSource,
    /// isto pravilo). Budući dani se namjerno preskaču — posao se još nije dogodio.</summary>
    private static decimal ComputeAssumedHours(
        List<RosterEntry> entries, WorkingHoursTemplate template, DateTimeOffset periodFrom, DateTimeOffset periodTo,
        List<CompanyHoliday> companyHolidays, Guid? employeeCompanyId)
    {
        if (template == null)
            return 0m;

        DateTime today = DateTimeOffset.UtcNow.Date;
        DateTime rangeEnd = periodTo.Date < today ? periodTo.Date : today;
        if (rangeEnd < periodFrom.Date)
            return 0m;

        decimal total = 0m;
        for (DateTime date = periodFrom.Date; date <= rangeEnd; date = date.AddDays(1))
        {
            if (HasActualEntryForDay(entries, date, periodTo.Date))
                continue;

            (List<WorkingHoursCalculator.Interval> intervals, AvailabilitySource source) = WorkingHoursCalculator.GetEffectiveEmployeeIntervals(
                template, new List<RosterEntry>(), new DateTimeOffset(date, TimeSpan.Zero));

            if (source != AvailabilitySource.Template)
                continue;

            // Poslovnica ne radi taj dan (praznik) — ne broji se kao pretpostavljeno odrađeno.
            if (employeeCompanyId.HasValue && companyHolidays.Any(h => h.CompanyId == employeeCompanyId.Value && h.Date.Date == date))
                continue;

            total += intervals.Sum(i => (decimal)(i.End - i.Start).TotalHours);
        }

        return total;
    }

    /// <summary>Ima li zaposlenik stvaran zapis (rad ili odsutnost) koji pokriva zadani dan — otvorena odsutnost
    /// (DateTo=null) se za ovu provjeru klipa na openEndedFallback.</summary>
    private static bool HasActualEntryForDay(List<RosterEntry> entries, DateTime date, DateTime openEndedFallback)
    {
        return entries.Any(e =>
        {
            DateTime entryFrom = e.DateFrom.Date;
            DateTime entryTo = e.RosterType.IsAbsence ? (e.DateTo?.Date ?? openEndedFallback) : entryFrom;
            return date >= entryFrom && date <= entryTo;
        });
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
            IsOverride = entry.IsOverride,
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
