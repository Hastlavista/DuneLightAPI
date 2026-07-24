using System;
using System.Linq;
using System.Threading.Tasks;
using BlueDragon.DuneLight.Core.Enums;
using BlueDragon.DuneLight.Infrastructure.Domain.Contexts;
using BlueDragon.DuneLight.Infrastructure.Domain.Models.Appointments;
using BlueDragon.DuneLight.Infrastructure.Domain.Settings;
using BlueDragon.DuneLight.Infrastructure.Handlers.Interfaces;
using Microsoft.EntityFrameworkCore;

namespace BlueDragon.DuneLight.Infrastructure.Handlers.Implementations;

public class GroupAttendanceHandler : IGroupAttendanceHandler
{
    private readonly DatabaseSettings _databaseSettings;

    public GroupAttendanceHandler(DatabaseSettings databaseSettings)
    {
        _databaseSettings = databaseSettings;
    }

    public async Task<Appointment> GetGroupAppointment(Guid organizationId, Guid appointmentId)
    {
        await using DatabaseContext context = DatabaseContext.GenerateContext(_databaseSettings.ConnectionString);
        return await context.Appointments
            .Include(a => a.Service)
            .Include(a => a.Employee)
            .Include(a => a.Group).ThenInclude(g => g.Members.Where(m => m.IsActive)).ThenInclude(m => m.Client)
            .Include(a => a.Attendances).ThenInclude(at => at.Client)
            .SingleOrDefaultAsync(a =>
                a.OrganizationId == organizationId && a.Id == appointmentId && a.Form == AppointmentForm.Group);
    }

    public async Task<AppointmentAttendance> GetAttendanceRow(Guid appointmentId, Guid clientId)
    {
        await using DatabaseContext context = DatabaseContext.GenerateContext(_databaseSettings.ConnectionString);
        return await context.AppointmentAttendances
            .SingleOrDefaultAsync(a => a.AppointmentId == appointmentId && a.ClientId == clientId);
    }

    public async Task AddAttendance(AppointmentAttendance attendance)
    {
        await using DatabaseContext context = DatabaseContext.GenerateContext(_databaseSettings.ConnectionString);
        context.AppointmentAttendances.Add(attendance);
        await context.SaveChangesAsync();
    }

    public async Task UpdateAttendance(AppointmentAttendance attendance)
    {
        await using DatabaseContext context = DatabaseContext.GenerateContext(_databaseSettings.ConnectionString);
        context.AppointmentAttendances.Update(attendance);
        await context.SaveChangesAsync();
    }
}
