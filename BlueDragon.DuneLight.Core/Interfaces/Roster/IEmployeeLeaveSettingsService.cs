using System;
using System.Threading.Tasks;
using BlueDragon.DuneLight.Core.DTOs.Roster;

namespace BlueDragon.DuneLight.Core.Interfaces.Roster;

public interface IEmployeeLeaveSettingsService
{
    Task<EmployeeLeaveSettingsDto> GetForEmployee(Guid organizationId, Guid employeeId);

    Task<EmployeeLeaveSettingsDto> Upsert(Guid organizationId, Guid userId, Guid employeeId, EmployeeLeaveSettingsUpsertRequest request);
}