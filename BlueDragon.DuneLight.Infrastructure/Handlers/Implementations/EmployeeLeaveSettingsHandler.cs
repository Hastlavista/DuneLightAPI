using System;
using System.Threading.Tasks;
using BlueDragon.DuneLight.Infrastructure.Domain.Contexts;
using BlueDragon.DuneLight.Infrastructure.Domain.Models.Roster;
using BlueDragon.DuneLight.Infrastructure.Domain.Settings;
using BlueDragon.DuneLight.Infrastructure.Handlers.Interfaces;
using Microsoft.EntityFrameworkCore;

namespace BlueDragon.DuneLight.Infrastructure.Handlers.Implementations;

public class EmployeeLeaveSettingsHandler : IEmployeeLeaveSettingsHandler
{
    private readonly DatabaseSettings _databaseSettings;

    public EmployeeLeaveSettingsHandler(DatabaseSettings databaseSettings)
    {
        _databaseSettings = databaseSettings;
    }

    public async Task<EmployeeLeaveSettings> GetForEmployee(Guid organizationId, Guid employeeId)
    {
        await using DatabaseContext context = DatabaseContext.GenerateContext(_databaseSettings.ConnectionString);
        return await context.EmployeeLeaveSettings
            .SingleOrDefaultAsync(s => s.OrganizationId == organizationId && s.EmployeeId == employeeId);
    }

    public async Task<EmployeeLeaveSettings> Upsert(
        Guid organizationId, Guid employeeId, int annualDays, int renewalMonth, int renewalDay,
        int carryoverExpiryMonth, int carryoverExpiryDay, Guid userId)
    {
        await using DatabaseContext context = DatabaseContext.GenerateContext(_databaseSettings.ConnectionString);

        EmployeeLeaveSettings existing = await context.EmployeeLeaveSettings
            .SingleOrDefaultAsync(s => s.OrganizationId == organizationId && s.EmployeeId == employeeId);

        if (existing == null)
        {
            existing = new EmployeeLeaveSettings
            {
                Id = Guid.NewGuid(),
                OrganizationId = organizationId,
                EmployeeId = employeeId,
                AnnualDays = annualDays,
                RenewalMonth = renewalMonth,
                RenewalDay = renewalDay,
                CarryoverExpiryMonth = carryoverExpiryMonth,
                CarryoverExpiryDay = carryoverExpiryDay,
                CreatedAt = DateTimeOffset.UtcNow,
                CreatedBy = userId
            };
            context.EmployeeLeaveSettings.Add(existing);
        }
        else
        {
            existing.AnnualDays = annualDays;
            existing.RenewalMonth = renewalMonth;
            existing.RenewalDay = renewalDay;
            existing.CarryoverExpiryMonth = carryoverExpiryMonth;
            existing.CarryoverExpiryDay = carryoverExpiryDay;
            existing.UpdatedAt = DateTimeOffset.UtcNow;
            existing.UpdatedBy = userId;
        }

        await context.SaveChangesAsync();
        return existing;
    }
}
