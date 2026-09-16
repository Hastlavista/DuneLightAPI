using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using BlueDragon.DuneLight.Core.Enums;
using BlueDragon.DuneLight.Infrastructure.Domain.Contexts;
using BlueDragon.DuneLight.Infrastructure.Domain.Models.Groups;
using BlueDragon.DuneLight.Infrastructure.Domain.Settings;
using BlueDragon.DuneLight.Infrastructure.Handlers.Interfaces;
using BlueDragon.DuneLight.Infrastructure.UnitOfWork;
using Microsoft.EntityFrameworkCore;

namespace BlueDragon.DuneLight.Infrastructure.Handlers.Implementations;

public class WaitlistHandler : IWaitlistHandler
{
    private readonly DatabaseSettings _databaseSettings;

    public WaitlistHandler(DatabaseSettings databaseSettings)
    {
        _databaseSettings = databaseSettings;
    }

    public async Task Add(IUnitOfWork uow, WaitlistEntry entry)
    {
        uow.Context.WaitlistEntries.Add(entry);
        await uow.Context.SaveChangesAsync();
    }

    public async Task Update(IUnitOfWork uow, WaitlistEntry entry)
    {
        uow.Context.WaitlistEntries.Update(entry);
        await uow.Context.SaveChangesAsync();
    }

    public async Task<List<WaitlistEntry>> GetForAppointment(Guid organizationId, Guid appointmentId)
    {
        await using DatabaseContext context = DatabaseContext.GenerateContext(_databaseSettings.ConnectionString);
        return await context.WaitlistEntries
            .Include(w => w.Client)
            .Where(w => w.OrganizationId == organizationId && w.AppointmentId == appointmentId)
            .ToListAsync();
    }

    public async Task<WaitlistEntry> GetActiveForClient(Guid organizationId, Guid appointmentId, Guid clientId)
    {
        await using DatabaseContext context = DatabaseContext.GenerateContext(_databaseSettings.ConnectionString);
        return await context.WaitlistEntries
            .Include(w => w.Client)
            .Where(w => w.OrganizationId == organizationId && w.AppointmentId == appointmentId &&
                w.ClientId == clientId && w.Status == WaitlistEntryStatus.Waiting)
            .SingleOrDefaultAsync();
    }

    public async Task<WaitlistEntry> GetMostRecentForClient(Guid organizationId, Guid appointmentId, Guid clientId)
    {
        await using DatabaseContext context = DatabaseContext.GenerateContext(_databaseSettings.ConnectionString);
        return await context.WaitlistEntries
            .Include(w => w.Client)
            .Where(w => w.OrganizationId == organizationId && w.AppointmentId == appointmentId && w.ClientId == clientId)
            .OrderByDescending(w => w.CreatedAt)
            .FirstOrDefaultAsync();
    }

    public async Task<bool> HasActiveWaitingForClient(Guid organizationId, Guid clientId)
    {
        await using DatabaseContext context = DatabaseContext.GenerateContext(_databaseSettings.ConnectionString);
        return await context.WaitlistEntries.AnyAsync(w =>
            w.OrganizationId == organizationId && w.ClientId == clientId && w.Status == WaitlistEntryStatus.Waiting);
    }

    public async Task<List<WaitlistEntry>> GetWaitingForAppointments(Guid organizationId, List<Guid> appointmentIds)
    {
        if (appointmentIds.Count == 0)
            return new List<WaitlistEntry>();

        await using DatabaseContext context = DatabaseContext.GenerateContext(_databaseSettings.ConnectionString);
        return await context.WaitlistEntries
            .Where(w => w.OrganizationId == organizationId && w.Status == WaitlistEntryStatus.Waiting &&
                appointmentIds.Contains(w.AppointmentId))
            .ToListAsync();
    }
}
