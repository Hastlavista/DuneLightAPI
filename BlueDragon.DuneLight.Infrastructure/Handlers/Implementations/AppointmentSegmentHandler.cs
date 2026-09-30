using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using BlueDragon.DuneLight.Infrastructure.Domain.Contexts;
using BlueDragon.DuneLight.Infrastructure.Domain.Models.Appointments;
using BlueDragon.DuneLight.Infrastructure.Domain.Settings;
using BlueDragon.DuneLight.Infrastructure.Handlers.Interfaces;
using Microsoft.EntityFrameworkCore;

namespace BlueDragon.DuneLight.Infrastructure.Handlers.Implementations;

public class AppointmentSegmentHandler : IAppointmentSegmentHandler
{
    private readonly DatabaseSettings _databaseSettings;

    public AppointmentSegmentHandler(DatabaseSettings databaseSettings)
    {
        _databaseSettings = databaseSettings;
    }

    private static IQueryable<AppointmentSegment> IncludeGraph(IQueryable<AppointmentSegment> query)
    {
        return query
            .Include(s => s.Service)
            .Include(s => s.Room)
            .Include(s => s.Employees)
            .Include(s => s.Resources)
            .AsSplitQuery();
    }

    public async Task Add(AppointmentSegment segment)
    {
        await using DatabaseContext context = DatabaseContext.GenerateContext(_databaseSettings.ConnectionString);
        context.AppointmentSegments.Add(segment);
        await context.SaveChangesAsync();
    }

    public async Task<AppointmentSegment> GetById(Guid organizationId, Guid id)
    {
        await using DatabaseContext context = DatabaseContext.GenerateContext(_databaseSettings.ConnectionString);
        return await IncludeGraph(context.AppointmentSegments.AsNoTracking())
            .SingleOrDefaultAsync(s => s.OrganizationId == organizationId && s.Id == id);
    }

    public async Task<List<AppointmentSegment>> GetForAppointment(Guid organizationId, Guid appointmentId)
    {
        await using DatabaseContext context = DatabaseContext.GenerateContext(_databaseSettings.ConnectionString);
        return await IncludeGraph(context.AppointmentSegments.AsNoTracking())
            .Where(s => s.OrganizationId == organizationId && s.AppointmentId == appointmentId)
            .OrderBy(s => s.PlannedStart)
            .ToListAsync();
    }
}
