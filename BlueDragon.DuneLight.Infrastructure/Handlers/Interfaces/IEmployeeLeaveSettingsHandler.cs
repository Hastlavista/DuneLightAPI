using System;
using System.Threading.Tasks;
using BlueDragon.DuneLight.Infrastructure.Domain.Models.Roster;

namespace BlueDragon.DuneLight.Infrastructure.Handlers.Interfaces;

public interface IEmployeeLeaveSettingsHandler
{
    Task<EmployeeLeaveSettings> GetForEmployee(Guid organizationId, Guid employeeId);

    Task<EmployeeLeaveSettings> Upsert(
        Guid organizationId, Guid employeeId, int annualDays, int renewalMonth, int renewalDay,
        int carryoverExpiryMonth, int carryoverExpiryDay, Guid userId);
}
