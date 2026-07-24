using System;
using System.Threading.Tasks;
using BlueDragon.DuneLight.Infrastructure.Domain.Contexts;
using BlueDragon.DuneLight.Infrastructure.Domain.Models.Employees;
using BlueDragon.DuneLight.Infrastructure.Domain.Settings;
using BlueDragon.DuneLight.Infrastructure.Handlers.Interfaces;
using Microsoft.EntityFrameworkCore;

namespace BlueDragon.DuneLight.Infrastructure.Handlers.Implementations;

public class EmployeeAuditLogHandler : IEmployeeAuditLogHandler
{
    private readonly DatabaseSettings _databaseSettings;

    public EmployeeAuditLogHandler(DatabaseSettings databaseSettings)
    {
        _databaseSettings = databaseSettings;
    }

    public async Task Add(EmployeeAuditLog entry)
    {
        await using DatabaseContext context = DatabaseContext.GenerateContext(_databaseSettings.ConnectionString);
        context.EmployeeAuditLog.Add(entry);
        await context.SaveChangesAsync();
    }

    public async Task<bool> HasEntries(Guid employeeId)
    {
        await using DatabaseContext context = DatabaseContext.GenerateContext(_databaseSettings.ConnectionString);
        return await context.EmployeeAuditLog.AnyAsync(a => a.EmployeeId == employeeId);
    }
}
