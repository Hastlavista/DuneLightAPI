using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using BlueDragon.DuneLight.Core.DTOs.ScheduleBreaks;
using BlueDragon.DuneLight.Infrastructure.Domain.Contexts;
using BlueDragon.DuneLight.Infrastructure.Domain.Models.Appointments;
using BlueDragon.DuneLight.Infrastructure.Domain.Settings;
using BlueDragon.DuneLight.Infrastructure.Handlers.Interfaces;
using Microsoft.EntityFrameworkCore;

namespace BlueDragon.DuneLight.Infrastructure.Handlers.Implementations;

public class ScheduleBreakHandler : IScheduleBreakHandler
{
    private readonly DatabaseSettings _databaseSettings;

    public ScheduleBreakHandler(DatabaseSettings databaseSettings)
    {
        _databaseSettings = databaseSettings;
    }

    private static IQueryable<ScheduleBreak> IncludeGraph(IQueryable<ScheduleBreak> query)
    {
        return query
            .Include(b => b.Employee)
            .Include(b => b.Company);
    }

    public async Task Add(ScheduleBreak scheduleBreak)
    {
        await using DatabaseContext context = DatabaseContext.GenerateContext(_databaseSettings.ConnectionString);
        context.ScheduleBreaks.Add(scheduleBreak);
        await context.SaveChangesAsync();
    }

    public async Task<ScheduleBreak> GetById(Guid organizationId, Guid id)
    {
        await using DatabaseContext context = DatabaseContext.GenerateContext(_databaseSettings.ConnectionString);
        return await IncludeGraph(context.ScheduleBreaks)
            .SingleOrDefaultAsync(b => b.OrganizationId == organizationId && b.Id == id);
    }

    public async Task<ScheduleBreak> GetByIdLight(Guid organizationId, Guid id)
    {
        await using DatabaseContext context = DatabaseContext.GenerateContext(_databaseSettings.ConnectionString);
        return await context.ScheduleBreaks
            .SingleOrDefaultAsync(b => b.OrganizationId == organizationId && b.Id == id);
    }

    public async Task UpdateScalar(ScheduleBreak scheduleBreak)
    {
        await using DatabaseContext context = DatabaseContext.GenerateContext(_databaseSettings.ConnectionString);
        context.ScheduleBreaks.Update(scheduleBreak);
        await context.SaveChangesAsync();
    }

    public async Task Delete(ScheduleBreak scheduleBreak)
    {
        await using DatabaseContext context = DatabaseContext.GenerateContext(_databaseSettings.ConnectionString);
        context.ScheduleBreaks.Remove(scheduleBreak);
        await context.SaveChangesAsync();
    }

    public async Task<List<ScheduleBreak>> GetOverlappingForEmployee(
        Guid organizationId, Guid employeeId, DateTimeOffset startsAt, int durationMinutes, Guid? excludeId)
    {
        DateTimeOffset newEnd = startsAt.AddMinutes(durationMinutes);
        DateTimeOffset windowStart = startsAt.AddDays(-1);
        DateTimeOffset windowEnd = startsAt.AddDays(1);

        await using DatabaseContext context = DatabaseContext.GenerateContext(_databaseSettings.ConnectionString);
        List<ScheduleBreak> candidates = await context.ScheduleBreaks
            .Where(b =>
                b.OrganizationId == organizationId &&
                b.EmployeeId == employeeId &&
                b.StartsAt >= windowStart && b.StartsAt <= windowEnd &&
                (excludeId == null || b.Id != excludeId))
            .ToListAsync();

        return candidates.Where(b => b.StartsAt < newEnd && startsAt < b.StartsAt.AddMinutes(b.DurationMinutes)).ToList();
    }

    public async Task<List<ScheduleBreak>> GetForEmployeeInRange(
        Guid organizationId, Guid employeeId, DateTimeOffset rangeFrom, DateTimeOffset rangeTo)
    {
        await using DatabaseContext context = DatabaseContext.GenerateContext(_databaseSettings.ConnectionString);
        return await context.ScheduleBreaks
            .Where(b =>
                b.OrganizationId == organizationId &&
                b.EmployeeId == employeeId &&
                b.StartsAt >= rangeFrom && b.StartsAt <= rangeTo)
            .ToListAsync();
    }

    public async Task<List<ScheduleBreak>> GetForEmployeesInRange(
        Guid organizationId, List<Guid> employeeIds, DateTimeOffset rangeFrom, DateTimeOffset rangeTo)
    {
        await using DatabaseContext context = DatabaseContext.GenerateContext(_databaseSettings.ConnectionString);
        return await context.ScheduleBreaks
            .Where(b =>
                b.OrganizationId == organizationId &&
                employeeIds.Contains(b.EmployeeId) &&
                b.StartsAt >= rangeFrom && b.StartsAt <= rangeTo)
            .ToListAsync();
    }

    public async Task<List<ScheduleBreak>> GetForSchedule(Guid organizationId, ScheduleBreakQuery query)
    {
        await using DatabaseContext context = DatabaseContext.GenerateContext(_databaseSettings.ConnectionString);
        IQueryable<ScheduleBreak> q = IncludeGraph(context.ScheduleBreaks)
            .Where(b => b.OrganizationId == organizationId && b.StartsAt >= query.From && b.StartsAt <= query.To);

        if (query.EmployeeId.HasValue)
            q = q.Where(b => b.EmployeeId == query.EmployeeId.Value);

        if (query.CompanyId.HasValue)
            q = q.Where(b => b.CompanyId == query.CompanyId.Value);

        return await q.OrderBy(b => b.StartsAt).ToListAsync();
    }

    public async Task AddRange(List<ScheduleBreak> scheduleBreaks)
    {
        if (scheduleBreaks.Count == 0)
            return;

        await using DatabaseContext context = DatabaseContext.GenerateContext(_databaseSettings.ConnectionString);
        context.ScheduleBreaks.AddRange(scheduleBreaks);
        await context.SaveChangesAsync();
    }
}
