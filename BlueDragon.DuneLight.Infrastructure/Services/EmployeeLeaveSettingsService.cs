using System;
using System.Threading.Tasks;
using BlueDragon.DuneLight.Core.DTOs.Roster;
using BlueDragon.DuneLight.Core.Interfaces.Roster;
using BlueDragon.DuneLight.Core.Shared.Exceptions;
using BlueDragon.DuneLight.Infrastructure.Domain.Models.Employees;
using BlueDragon.DuneLight.Infrastructure.Domain.Models.Roster;
using BlueDragon.DuneLight.Infrastructure.Handlers.Interfaces;

namespace BlueDragon.DuneLight.Infrastructure.Services;

public class EmployeeLeaveSettingsService : IEmployeeLeaveSettingsService
{
    private readonly IEmployeeLeaveSettingsHandler _settingsHandler;
    private readonly IEmployeeHandler _employeeHandler;

    public EmployeeLeaveSettingsService(IEmployeeLeaveSettingsHandler settingsHandler, IEmployeeHandler employeeHandler)
    {
        _settingsHandler = settingsHandler;
        _employeeHandler = employeeHandler;
    }

    public async Task<EmployeeLeaveSettingsDto> GetForEmployee(Guid organizationId, Guid employeeId)
    {
        EmployeeLeaveSettings settings = await _settingsHandler.GetForEmployee(organizationId, employeeId);
        if (settings == null)
            throw new NotFoundAppException("EmployeeLeaveSettings", employeeId);

        return ToDto(settings);
    }

    public async Task<EmployeeLeaveSettingsDto> Upsert(Guid organizationId, Guid userId, Guid employeeId, EmployeeLeaveSettingsUpsertRequest request)
    {
        Employee employee = await _employeeHandler.GetByIdLight(organizationId, employeeId);
        if (employee == null)
            throw new NotFoundAppException("Employee", employeeId);

        EmployeeLeaveSettings saved = await _settingsHandler.Upsert(
            organizationId, employeeId, request.AnnualDays, request.RenewalMonth, request.RenewalDay,
            request.CarryoverExpiryMonth, request.CarryoverExpiryDay, userId);

        return ToDto(saved);
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
}
