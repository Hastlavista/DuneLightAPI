using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using BlueDragon.DuneLight.Core.DTOs.Appointments;
using BlueDragon.DuneLight.Core.Enums;
using BlueDragon.DuneLight.Core.Shared;
using BlueDragon.DuneLight.Infrastructure.Domain.Contexts;
using BlueDragon.DuneLight.Infrastructure.Domain.Models.Appointments;
using BlueDragon.DuneLight.Infrastructure.Domain.Settings;
using BlueDragon.DuneLight.Infrastructure.Handlers.Interfaces;
using BlueDragon.DuneLight.Infrastructure.UnitOfWork;
using Microsoft.EntityFrameworkCore;

namespace BlueDragon.DuneLight.Infrastructure.Handlers.Implementations;

public class AppointmentHandler : IAppointmentHandler
{
    private readonly DatabaseSettings _databaseSettings;

    public AppointmentHandler(DatabaseSettings databaseSettings)
    {
        _databaseSettings = databaseSettings;
    }

    private static IQueryable<Appointment> IncludeGraph(IQueryable<Appointment> query)
    {
        return query
            .Include(a => a.Service)
            .Include(a => a.Employee)
            .Include(a => a.Company)
            .Include(a => a.Clients).ThenInclude(ac => ac.Client);
    }

    public async Task Add(Appointment appointment)
    {
        await using DatabaseContext context = DatabaseContext.GenerateContext(_databaseSettings.ConnectionString);
        context.Appointments.Add(appointment);
        await context.SaveChangesAsync();
    }

    public async Task Add(IUnitOfWork uow, Appointment appointment)
    {
        uow.Context.Appointments.Add(appointment);
        await uow.Context.SaveChangesAsync();
    }

    public async Task<Appointment> GetById(Guid organizationId, Guid id)
    {
        await using DatabaseContext context = DatabaseContext.GenerateContext(_databaseSettings.ConnectionString);
        return await IncludeGraph(context.Appointments)
            .SingleOrDefaultAsync(a => a.OrganizationId == organizationId && a.Id == id);
    }

    public async Task<Appointment> GetByIdLight(Guid organizationId, Guid id)
    {
        await using DatabaseContext context = DatabaseContext.GenerateContext(_databaseSettings.ConnectionString);
        return await context.Appointments
            .SingleOrDefaultAsync(a => a.OrganizationId == organizationId && a.Id == id);
    }

    public Task<List<AppointmentClient>> GetAppointmentClients(IUnitOfWork uow, Guid organizationId, Guid appointmentId, List<Guid> clientIds)
    {
        DatabaseContext context = uow.Context;
        return context.AppointmentClients
            .Where(ac => ac.AppointmentId == appointmentId && clientIds.Contains(ac.ClientId) &&
                context.Appointments.Any(a => a.Id == appointmentId && a.OrganizationId == organizationId))
            .ToListAsync();
    }

    public async Task UpdateScalar(Appointment appointment)
    {
        await using DatabaseContext context = DatabaseContext.GenerateContext(_databaseSettings.ConnectionString);
        context.Appointments.Update(appointment);
        await context.SaveChangesAsync();
    }

    public async Task UpdateScalar(IUnitOfWork uow, Appointment appointment)
    {
        uow.Context.Appointments.Update(appointment);
        await uow.Context.SaveChangesAsync();
    }

    public async Task UpdateWithClients(Appointment appointment, List<Guid> clientIds)
    {
        await using DatabaseContext context = DatabaseContext.GenerateContext(_databaseSettings.ConnectionString);
        await UpdateWithClientsCore(context, appointment, clientIds);
        await context.SaveChangesAsync();
    }

    public async Task UpdateWithClients(IUnitOfWork uow, Appointment appointment, List<Guid> clientIds)
    {
        await UpdateWithClientsCore(uow.Context, appointment, clientIds);
        await uow.Context.SaveChangesAsync();
    }

    private static async Task UpdateWithClientsCore(DatabaseContext context, Appointment appointment, List<Guid> clientIds)
    {
        List<AppointmentClient> existing = await context.AppointmentClients
            .Where(ac => ac.AppointmentId == appointment.Id)
            .ToListAsync();

        List<AppointmentClient> toRemove = existing.Where(ac => !clientIds.Contains(ac.ClientId)).ToList();
        context.AppointmentClients.RemoveRange(toRemove);

        List<Guid> existingClientIds = existing.Select(ac => ac.ClientId).ToList();
        foreach (Guid clientId in clientIds.Where(id => !existingClientIds.Contains(id)))
        {
            context.AppointmentClients.Add(new AppointmentClient
            {
                Id = Guid.NewGuid(),
                AppointmentId = appointment.Id.GetValueOrDefault(),
                ClientId = clientId,
                CreatedAt = DateTimeOffset.UtcNow
            });
        }

        context.Appointments.Update(appointment);
    }

    public async Task UpdateAppointmentClient(AppointmentClient appointmentClient)
    {
        await using DatabaseContext context = DatabaseContext.GenerateContext(_databaseSettings.ConnectionString);
        context.AppointmentClients.Update(appointmentClient);
        await context.SaveChangesAsync();
    }

    public async Task UpdateAppointmentClient(IUnitOfWork uow, AppointmentClient appointmentClient)
    {
        uow.Context.AppointmentClients.Update(appointmentClient);
        await uow.Context.SaveChangesAsync();
    }

    public async Task Delete(Appointment appointment)
    {
        await using DatabaseContext context = DatabaseContext.GenerateContext(_databaseSettings.ConnectionString);
        context.Appointments.Remove(appointment);
        await context.SaveChangesAsync();
    }

    public async Task<List<Appointment>> GetOverlappingForEmployee(
        Guid organizationId, Guid employeeId, DateTimeOffset startsAt, int durationMinutes, Guid? excludeId)
    {
        DateTimeOffset newEnd = startsAt.AddMinutes(durationMinutes);
        DateTimeOffset windowStart = startsAt.AddDays(-1);
        DateTimeOffset windowEnd = startsAt.AddDays(1);

        await using DatabaseContext context = DatabaseContext.GenerateContext(_databaseSettings.ConnectionString);
        List<Appointment> candidates = await context.Appointments
            .Where(a =>
                a.OrganizationId == organizationId &&
                a.EmployeeId == employeeId &&
                a.Status != AppointmentStatus.Cancelled && a.Status != AppointmentStatus.NoShow &&
                a.StartsAt >= windowStart && a.StartsAt <= windowEnd &&
                (excludeId == null || a.Id != excludeId))
            .ToListAsync();

        return candidates.Where(a => a.StartsAt < newEnd && startsAt < a.StartsAt.AddMinutes(a.DurationMinutes)).ToList();
    }

    public async Task<List<Appointment>> GetOverlappingForClients(
        Guid organizationId, List<Guid> clientIds, DateTimeOffset startsAt, int durationMinutes, Guid? excludeId)
    {
        DateTimeOffset newEnd = startsAt.AddMinutes(durationMinutes);
        DateTimeOffset windowStart = startsAt.AddDays(-1);
        DateTimeOffset windowEnd = startsAt.AddDays(1);

        await using DatabaseContext context = DatabaseContext.GenerateContext(_databaseSettings.ConnectionString);
        List<Appointment> candidates = await context.Appointments
            .Include(a => a.Clients)
            .Where(a =>
                a.OrganizationId == organizationId &&
                a.Status != AppointmentStatus.Cancelled && a.Status != AppointmentStatus.NoShow &&
                a.StartsAt >= windowStart && a.StartsAt <= windowEnd &&
                a.Clients.Any(ac => clientIds.Contains(ac.ClientId)) &&
                (excludeId == null || a.Id != excludeId))
            .ToListAsync();

        return candidates.Where(a => a.StartsAt < newEnd && startsAt < a.StartsAt.AddMinutes(a.DurationMinutes)).ToList();
    }

    public async Task<List<Appointment>> GetForEmployeeInRange(
        Guid organizationId, Guid employeeId, DateTimeOffset rangeFrom, DateTimeOffset rangeTo)
    {
        await using DatabaseContext context = DatabaseContext.GenerateContext(_databaseSettings.ConnectionString);
        return await context.Appointments
            .Where(a =>
                a.OrganizationId == organizationId &&
                a.EmployeeId == employeeId &&
                a.Status != AppointmentStatus.Cancelled && a.Status != AppointmentStatus.NoShow &&
                a.StartsAt >= rangeFrom && a.StartsAt <= rangeTo)
            .ToListAsync();
    }

    public async Task<List<Appointment>> GetForEmployeesInRange(
        Guid organizationId, List<Guid> employeeIds, DateTimeOffset rangeFrom, DateTimeOffset rangeTo)
    {
        await using DatabaseContext context = DatabaseContext.GenerateContext(_databaseSettings.ConnectionString);
        return await context.Appointments
            .Where(a =>
                a.OrganizationId == organizationId &&
                a.EmployeeId != null && employeeIds.Contains(a.EmployeeId.Value) &&
                a.Status != AppointmentStatus.Cancelled && a.Status != AppointmentStatus.NoShow &&
                a.StartsAt >= rangeFrom && a.StartsAt <= rangeTo)
            .ToListAsync();
    }

    public async Task<List<Appointment>> GetForClientsInRange(
        Guid organizationId, List<Guid> clientIds, DateTimeOffset rangeFrom, DateTimeOffset rangeTo)
    {
        await using DatabaseContext context = DatabaseContext.GenerateContext(_databaseSettings.ConnectionString);
        return await context.Appointments
            .Include(a => a.Clients)
            .Where(a =>
                a.OrganizationId == organizationId &&
                a.Status != AppointmentStatus.Cancelled && a.Status != AppointmentStatus.NoShow &&
                a.StartsAt >= rangeFrom && a.StartsAt <= rangeTo &&
                a.Clients.Any(ac => clientIds.Contains(ac.ClientId)))
            .ToListAsync();
    }

    public async Task<List<Appointment>> GetForSchedule(Guid organizationId, AppointmentScheduleQuery query)
    {
        await using DatabaseContext context = DatabaseContext.GenerateContext(_databaseSettings.ConnectionString);
        IQueryable<Appointment> q = IncludeGraph(context.Appointments)
            .Include(a => a.Group).ThenInclude(g => g.Members.Where(m => m.IsActive))
            .Include(a => a.Attendances)
            .AsSplitQuery()
            .Where(a => a.OrganizationId == organizationId && a.StartsAt >= query.From && a.StartsAt <= query.To);

        if (query.CompanyId.HasValue)
            q = q.Where(a => a.CompanyId == query.CompanyId.Value);

        if (query.EmployeeId.HasValue)
            q = q.Where(a => a.EmployeeId == query.EmployeeId.Value);

        if (query.ServiceId.HasValue)
            q = q.Where(a => a.ServiceId == query.ServiceId.Value);

        if (query.ExecutionMode.HasValue)
            q = q.Where(a => a.Service.ExecutionMode == query.ExecutionMode.Value);

        if (query.Status.HasValue)
            q = q.Where(a => a.Status == query.Status.Value);

        return await q.OrderBy(a => a.StartsAt).ToListAsync();
    }

    public async Task<(List<Appointment> Items, int TotalCount)> GetByClient(Guid organizationId, Guid clientId, PagedRequest request)
    {
        await using DatabaseContext context = DatabaseContext.GenerateContext(_databaseSettings.ConnectionString);
        IQueryable<Appointment> query = IncludeGraph(context.Appointments)
            .Where(a => a.OrganizationId == organizationId && a.Clients.Any(ac => ac.ClientId == clientId));

        int totalCount = await query.CountAsync();

        List<Appointment> items = await query
            .OrderByDescending(a => a.StartsAt)
            .Skip((request.Page - 1) * request.PageSize)
            .Take(request.PageSize)
            .ToListAsync();

        return (items, totalCount);
    }

    public async Task<bool> HasFutureScheduledForEmployee(Guid organizationId, Guid employeeId)
    {
        await using DatabaseContext context = DatabaseContext.GenerateContext(_databaseSettings.ConnectionString);
        DateTimeOffset now = DateTimeOffset.UtcNow;
        return await context.Appointments.AnyAsync(a =>
            a.OrganizationId == organizationId &&
            a.EmployeeId == employeeId &&
            a.Status == AppointmentStatus.Scheduled &&
            a.StartsAt >= now);
    }

    public async Task<bool> HasAnyForClient(Guid organizationId, Guid clientId)
    {
        await using DatabaseContext context = DatabaseContext.GenerateContext(_databaseSettings.ConnectionString);
        return await context.AppointmentClients.AnyAsync(ac =>
            ac.ClientId == clientId &&
            context.Appointments.Any(a => a.Id == ac.AppointmentId && a.OrganizationId == organizationId));
    }

    public async Task AddRange(List<Appointment> appointments)
    {
        if (appointments.Count == 0)
            return;

        await using DatabaseContext context = DatabaseContext.GenerateContext(_databaseSettings.ConnectionString);
        context.Appointments.AddRange(appointments);
        await context.SaveChangesAsync();
    }
}
