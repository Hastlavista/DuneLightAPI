using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using BlueDragon.DuneLight.Core.Enums;
using BlueDragon.DuneLight.Infrastructure.Domain.Contexts;
using BlueDragon.DuneLight.Infrastructure.Domain.Models.Appointments;
using BlueDragon.DuneLight.Infrastructure.Domain.Settings;
using BlueDragon.DuneLight.Infrastructure.Handlers.Interfaces;
using Microsoft.EntityFrameworkCore;

namespace BlueDragon.DuneLight.Infrastructure.Handlers.Implementations;

/// <summary>
/// Upiti su 1:1 preneseni iz AppointmentHandler (isti filteri, isti ±1 dan prozor, isto in-memory pravilo
/// preklapanja) — mijenja se samo oblik rezultata (<see cref="OccupancySlot"/> umjesto Appointment grafa).
/// Nova instanca DatabaseContext po pozivu, bez zaključavanja — isto ponašanje kao prije (vidi F-17).
/// </summary>
public class SchedulingOccupancyHandler : ISchedulingOccupancyHandler
{
    private readonly DatabaseSettings _databaseSettings;

    public SchedulingOccupancyHandler(DatabaseSettings databaseSettings)
    {
        _databaseSettings = databaseSettings;
    }

    public async Task<List<OccupancySlot>> GetOverlappingForEmployee(
        Guid organizationId, Guid employeeId, DateTimeOffset startsAt, int durationMinutes, Guid? excludeId)
    {
        DateTimeOffset newEnd = startsAt.AddMinutes(durationMinutes);
        DateTimeOffset windowStart = startsAt.AddDays(-1);
        DateTimeOffset windowEnd = startsAt.AddDays(1);

        await using DatabaseContext context = DatabaseContext.GenerateContext(_databaseSettings.ConnectionString);
        List<OccupancySlot> candidates = await Project(context.Appointments
            .Where(a =>
                a.OrganizationId == organizationId &&
                a.EmployeeId == employeeId &&
                a.Status != AppointmentStatus.Cancelled &&
                a.StartsAt >= windowStart && a.StartsAt <= windowEnd &&
                (excludeId == null || a.Id != excludeId)));

        return candidates.Where(s => s.Overlaps(startsAt, newEnd)).ToList();
    }

    public async Task<List<OccupancySlot>> GetOverlappingForClients(
        Guid organizationId, List<Guid> clientIds, DateTimeOffset startsAt, int durationMinutes, Guid? excludeId)
    {
        DateTimeOffset newEnd = startsAt.AddMinutes(durationMinutes);
        DateTimeOffset windowStart = startsAt.AddDays(-1);
        DateTimeOffset windowEnd = startsAt.AddDays(1);

        await using DatabaseContext context = DatabaseContext.GenerateContext(_databaseSettings.ConnectionString);
        List<OccupancySlot> candidates = await Project(context.Appointments
            .Where(a =>
                a.OrganizationId == organizationId &&
                a.Status != AppointmentStatus.Cancelled &&
                a.StartsAt >= windowStart && a.StartsAt <= windowEnd &&
                a.Bookings.Any(b => clientIds.Contains(b.ClientId) && b.Status != BookingStatus.Cancelled && b.Status != BookingStatus.NoShow) &&
                (excludeId == null || a.Id != excludeId)));

        return candidates.Where(s => s.Overlaps(startsAt, newEnd)).ToList();
    }

    public async Task<List<OccupancySlot>> GetOverlappingForRoom(
        Guid organizationId, Guid roomId, DateTimeOffset startsAt, int durationMinutes, Guid? excludeId)
    {
        DateTimeOffset newEnd = startsAt.AddMinutes(durationMinutes);
        DateTimeOffset windowStart = startsAt.AddDays(-1);
        DateTimeOffset windowEnd = startsAt.AddDays(1);

        await using DatabaseContext context = DatabaseContext.GenerateContext(_databaseSettings.ConnectionString);
        List<OccupancySlot> candidates = await Project(context.Appointments
            .Where(a =>
                a.OrganizationId == organizationId &&
                a.RoomId == roomId &&
                a.Status != AppointmentStatus.Cancelled &&
                a.StartsAt >= windowStart && a.StartsAt <= windowEnd &&
                (excludeId == null || a.Id != excludeId)));

        return candidates.Where(s => s.Overlaps(startsAt, newEnd)).ToList();
    }

    public async Task<List<OccupancySlot>> GetForEmployeeInRange(
        Guid organizationId, Guid employeeId, DateTimeOffset rangeFrom, DateTimeOffset rangeTo)
    {
        await using DatabaseContext context = DatabaseContext.GenerateContext(_databaseSettings.ConnectionString);
        return await Project(context.Appointments
            .Where(a =>
                a.OrganizationId == organizationId &&
                a.EmployeeId == employeeId &&
                a.Status != AppointmentStatus.Cancelled &&
                a.StartsAt >= rangeFrom && a.StartsAt <= rangeTo));
    }

    public async Task<List<OccupancySlot>> GetForEmployeesInRange(
        Guid organizationId, List<Guid> employeeIds, DateTimeOffset rangeFrom, DateTimeOffset rangeTo)
    {
        await using DatabaseContext context = DatabaseContext.GenerateContext(_databaseSettings.ConnectionString);
        return await Project(context.Appointments
            .Where(a =>
                a.OrganizationId == organizationId &&
                a.EmployeeId != null && employeeIds.Contains(a.EmployeeId.Value) &&
                a.Status != AppointmentStatus.Cancelled &&
                a.StartsAt >= rangeFrom && a.StartsAt <= rangeTo));
    }

    public async Task<List<OccupancySlot>> GetForRoomInRange(
        Guid organizationId, Guid roomId, DateTimeOffset rangeFrom, DateTimeOffset rangeTo)
    {
        await using DatabaseContext context = DatabaseContext.GenerateContext(_databaseSettings.ConnectionString);
        return await Project(context.Appointments
            .Where(a =>
                a.OrganizationId == organizationId &&
                a.RoomId == roomId &&
                a.Status != AppointmentStatus.Cancelled &&
                a.StartsAt >= rangeFrom && a.StartsAt <= rangeTo));
    }

    public async Task<List<OccupancySlot>> GetForClientsInRange(
        Guid organizationId, List<Guid> clientIds, DateTimeOffset rangeFrom, DateTimeOffset rangeTo)
    {
        await using DatabaseContext context = DatabaseContext.GenerateContext(_databaseSettings.ConnectionString);
        return await Project(context.Appointments
            .Where(a =>
                a.OrganizationId == organizationId &&
                a.Status != AppointmentStatus.Cancelled &&
                a.StartsAt >= rangeFrom && a.StartsAt <= rangeTo &&
                a.Bookings.Any(b => clientIds.Contains(b.ClientId) && b.Status != BookingStatus.Cancelled && b.Status != BookingStatus.NoShow)));
    }

    /// <summary>StartsAt se učitava kao i prije (isti offset kao materijalizirani Appointment.StartsAt), a End se računa
    /// u memoriji iz te iste vrijednosti — SQL aritmetika nad vremenom bi mogla promijeniti offset (vidi F-19).</summary>
    private static async Task<List<OccupancySlot>> Project(IQueryable<Appointment> query)
    {
        var rows = await query
            .Select(a => new
            {
                a.Id,
                a.StartsAt,
                a.DurationMinutes,
                a.EmployeeId,
                a.RoomId,
                ActiveClientIds = a.Bookings
                    .Where(b => b.Status != BookingStatus.Cancelled && b.Status != BookingStatus.NoShow)
                    .Select(b => b.ClientId)
                    .ToList()
            })
            .ToListAsync();

        return rows
            .Select(r => new OccupancySlot(
                r.Id.GetValueOrDefault(),
                r.StartsAt,
                r.StartsAt.AddMinutes(r.DurationMinutes),
                r.EmployeeId,
                r.RoomId,
                r.ActiveClientIds))
            .ToList();
    }
}
