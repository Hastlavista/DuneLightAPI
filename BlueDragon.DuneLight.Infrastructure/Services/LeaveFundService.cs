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
using BlueDragon.DuneLight.Infrastructure.UnitOfWork;
using BlueDragon.DuneLight.Infrastructure.Utils;

namespace BlueDragon.DuneLight.Infrastructure.Services;

public class LeaveFundService : ILeaveFundService
{
    private readonly ILeaveFundHandler _leaveFundHandler;
    private readonly IEmployeeLeaveSettingsHandler _settingsHandler;
    private readonly IEmployeeHandler _employeeHandler;
    private readonly IUnitOfWorkFactory _unitOfWorkFactory;
    private readonly TimeProvider _timeProvider;
    private readonly IOrganizationCalendarService _calendars;

    public LeaveFundService(
        ILeaveFundHandler leaveFundHandler, IEmployeeLeaveSettingsHandler settingsHandler,
        IEmployeeHandler employeeHandler, IUnitOfWorkFactory unitOfWorkFactory, TimeProvider timeProvider,
        IOrganizationCalendarService calendars)
    {
        _calendars = calendars;
        _leaveFundHandler = leaveFundHandler;
        _settingsHandler = settingsHandler;
        _employeeHandler = employeeHandler;
        _unitOfWorkFactory = unitOfWorkFactory;
        _timeProvider = timeProvider;
    }

    public async Task<EmployeeLeaveFundsDto> GetForEmployee(Guid organizationId, Guid userId, bool hasFullScope, Guid employeeId)
    {
        await ValidateOwnership(organizationId, userId, hasFullScope, employeeId);

        Employee employee = await _employeeHandler.GetById(organizationId, employeeId);
        if (employee == null)
            throw new NotFoundAppException("Employee", employeeId);

        EmployeeLeaveSettings settings = await _settingsHandler.GetForEmployee(organizationId, employeeId);

        DateOnly today = await Today(organizationId);
        if (settings != null)
        {
            int currentYear = LeaveFundYearCalculator.ResolveFundYear(settings, today);
            await using IUnitOfWork uow = await _unitOfWorkFactory.Begin();
            await _leaveFundHandler.GetOrCreateForYear(uow, organizationId, employeeId, settings, currentYear, userId);
            await uow.CommitAsync();
        }

        List<LeaveFund> funds = await _leaveFundHandler.GetForEmployee(organizationId, employeeId);

        return new EmployeeLeaveFundsDto
        {
            EmployeeId = employeeId,
            EmployeeName = $"{employee.FirstName} {employee.LastName}",
            Settings = settings != null ? ToDto(settings) : null,
            Funds = funds.Select(f => ToDto(f, today)).ToList()
        };
    }

    public async Task<LeaveFundDto> ManualUpsert(Guid organizationId, Guid userId, Guid employeeId, LeaveFundManualUpsertRequest request)
    {
        Employee employee = await _employeeHandler.GetByIdLight(organizationId, employeeId);
        if (employee == null)
            throw new NotFoundAppException("Employee", employeeId);

        EmployeeLeaveSettings settings = await _settingsHandler.GetForEmployee(organizationId, employeeId);
        if (settings == null)
            throw new BusinessRuleException(
                ErrorCodes.LeaveSettingsNotConfigured,
                "Zaposlenik nema podešen fond godišnjeg odmora — postavite postavke prije otvaranja/korekcije fonda.");

        List<LeaveFund> existingFunds = await _leaveFundHandler.GetForEmployee(organizationId, employeeId);
        LeaveFund existingFund = existingFunds.SingleOrDefault(f => f.FundYear == request.FundYear);
        if (existingFund != null && request.AllocatedDays < existingFund.UsedDays)
            throw new BusinessRuleException(
                ErrorCodes.LeaveFundAllocatedBelowUsed,
                $"Novi broj dodijeljenih dana ({request.AllocatedDays}) je manji od već potrošenih ({existingFund.UsedDays}) — prvo stornirajte/izmijenite povezane roster zapise.");

        LeaveFund saved = await _leaveFundHandler.ManualUpsert(organizationId, employeeId, settings, request.FundYear, request.AllocatedDays, userId);
        return ToDto(saved, await Today(organizationId));
    }

    /// <summary>T1-7: današnji poslovni dan u zoni organizacije (poslovni sat) — fond nije vezan uz poslovnicu.</summary>
    private async Task<DateOnly> Today(Guid organizationId) =>
        (await _calendars.GetCalendar(organizationId)).LocalDate(_timeProvider.GetUtcNow());

    private async Task ValidateOwnership(Guid organizationId, Guid userId, bool hasFullScope, Guid employeeId)
    {
        if (hasFullScope)
            return;

        Employee employee = await _employeeHandler.GetByUserId(organizationId, userId);
        if (employee == null || employee.Id != employeeId)
            throw ForbiddenAppException.OutOfScope("Smijete pregledavati samo vlastiti fond godišnjeg odmora.", Grants.RosterLeaveFundViewAll);
    }

    private static EmployeeLeaveSettingsDto ToDto(EmployeeLeaveSettings s)
    {
        return new EmployeeLeaveSettingsDto
        {
            EmployeeId = s.EmployeeId,
            AnnualDays = s.AnnualDays,
            RenewalMonth = s.RenewalMonth,
            RenewalDay = s.RenewalDay,
            CarryoverExpiryMonth = s.CarryoverExpiryMonth,
            CarryoverExpiryDay = s.CarryoverExpiryDay,
            CreatedAt = s.CreatedAt,
            CreatedBy = s.CreatedBy,
            UpdatedAt = s.UpdatedAt,
            UpdatedBy = s.UpdatedBy
        };
    }

    private static LeaveFundDto ToDto(LeaveFund f, DateOnly today)
    {
        return new LeaveFundDto
        {
            Id = f.Id.GetValueOrDefault(),
            FundYear = f.FundYear,
            OpenedAt = f.OpenedAt,
            ExpiresAt = f.ExpiresAt,
            AllocatedDays = f.AllocatedDays,
            UsedDays = f.UsedDays,
            IsExpired = LeaveFundYearCalculator.IsExpired(f, today)
        };
    }
}
