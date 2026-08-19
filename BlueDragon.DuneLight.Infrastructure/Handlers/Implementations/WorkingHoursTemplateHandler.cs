using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using BlueDragon.DuneLight.Core.Enums;
using BlueDragon.DuneLight.Infrastructure.Domain.Contexts;
using BlueDragon.DuneLight.Infrastructure.Domain.Models.Roster;
using BlueDragon.DuneLight.Infrastructure.Domain.Settings;
using BlueDragon.DuneLight.Infrastructure.Handlers.Interfaces;
using Microsoft.EntityFrameworkCore;

namespace BlueDragon.DuneLight.Infrastructure.Handlers.Implementations;

public class WorkingHoursTemplateHandler : IWorkingHoursTemplateHandler
{
    private readonly DatabaseSettings _databaseSettings;

    public WorkingHoursTemplateHandler(DatabaseSettings databaseSettings)
    {
        _databaseSettings = databaseSettings;
    }

    public async Task<WorkingHoursTemplate> GetForEmployee(Guid organizationId, Guid employeeId)
    {
        await using DatabaseContext context = DatabaseContext.GenerateContext(_databaseSettings.ConnectionString);
        return await context.WorkingHoursTemplates
            .Include(t => t.Employee)
            .Include(t => t.Intervals)
            .SingleOrDefaultAsync(t => t.OrganizationId == organizationId && t.EmployeeId == employeeId);
    }

    public async Task<List<WorkingHoursTemplate>> GetForEmployees(Guid organizationId, List<Guid> employeeIds)
    {
        await using DatabaseContext context = DatabaseContext.GenerateContext(_databaseSettings.ConnectionString);
        return await context.WorkingHoursTemplates
            .Include(t => t.Intervals)
            .Where(t => t.OrganizationId == organizationId && t.EmployeeId != null && employeeIds.Contains(t.EmployeeId.Value))
            .ToListAsync();
    }

    public async Task<WorkingHoursTemplate> GetForCompany(Guid organizationId, Guid companyId)
    {
        await using DatabaseContext context = DatabaseContext.GenerateContext(_databaseSettings.ConnectionString);
        return await context.WorkingHoursTemplates
            .Include(t => t.Company)
            .Include(t => t.Intervals)
            .SingleOrDefaultAsync(t => t.OrganizationId == organizationId && t.CompanyId == companyId);
    }

    public Task<WorkingHoursTemplate> UpsertForEmployee(
        Guid organizationId, Guid employeeId, WorkingHoursCycleType cycleType, DateTimeOffset anchorDate,
        List<WorkingHoursInterval> intervals, Guid userId)
    {
        return UpsertInternal(organizationId, employeeId, null, cycleType, anchorDate, intervals, userId);
    }

    public Task<WorkingHoursTemplate> UpsertForCompany(
        Guid organizationId, Guid companyId, WorkingHoursCycleType cycleType, DateTimeOffset anchorDate,
        List<WorkingHoursInterval> intervals, Guid userId)
    {
        return UpsertInternal(organizationId, null, companyId, cycleType, anchorDate, intervals, userId);
    }

    private async Task<WorkingHoursTemplate> UpsertInternal(
        Guid organizationId, Guid? employeeId, Guid? companyId, WorkingHoursCycleType cycleType, DateTimeOffset anchorDate,
        List<WorkingHoursInterval> intervals, Guid userId)
    {
        await using DatabaseContext context = DatabaseContext.GenerateContext(_databaseSettings.ConnectionString);

        WorkingHoursTemplate existing = await context.WorkingHoursTemplates
            .Include(t => t.Intervals)
            .SingleOrDefaultAsync(t => t.OrganizationId == organizationId && t.EmployeeId == employeeId && t.CompanyId == companyId);

        Guid templateId = existing?.Id ?? Guid.NewGuid();

        if (existing == null)
        {
            context.WorkingHoursTemplates.Add(new WorkingHoursTemplate
            {
                Id = templateId,
                OrganizationId = organizationId,
                EmployeeId = employeeId,
                CompanyId = companyId,
                CycleType = cycleType,
                AnchorDate = anchorDate,
                CreatedAt = DateTimeOffset.UtcNow,
                CreatedBy = userId
            });
        }
        else
        {
            existing.CycleType = cycleType;
            existing.AnchorDate = anchorDate;
            existing.UpdatedAt = DateTimeOffset.UtcNow;
            existing.UpdatedBy = userId;
            context.WorkingHoursIntervals.RemoveRange(existing.Intervals);
        }

        foreach (WorkingHoursInterval interval in intervals)
        {
            interval.Id = Guid.NewGuid();
            interval.WorkingHoursTemplateId = templateId;
            context.WorkingHoursIntervals.Add(interval);
        }

        await context.SaveChangesAsync();

        return employeeId.HasValue
            ? await GetForEmployee(organizationId, employeeId.Value)
            : await GetForCompany(organizationId, companyId!.Value);
    }
}