using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using BlueDragon.DuneLight.Core.Enums;
using BlueDragon.DuneLight.Infrastructure.Domain.Contexts;
using BlueDragon.DuneLight.Infrastructure.Domain.Models.Appointments;
using BlueDragon.DuneLight.Infrastructure.Domain.Settings;
using BlueDragon.DuneLight.Infrastructure.Handlers.Interfaces;
using BlueDragon.DuneLight.Infrastructure.Utils;
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
        List<OccupancySlot> candidates = await Project(ActiveSegments(context, organizationId)
            .Where(s =>
                s.Employees.Any(e => e.EmployeeId == employeeId) &&
                s.PlannedStart >= windowStart && s.PlannedStart <= windowEnd &&
                (excludeId == null || s.AppointmentId != excludeId)));

        return candidates.Where(s => s.Overlaps(startsAt, newEnd)).ToList();
    }

    public async Task<List<OccupancySlot>> GetOverlappingForClients(
        Guid organizationId, List<Guid> clientIds, DateTimeOffset startsAt, int durationMinutes, Guid? excludeId)
    {
        DateTimeOffset newEnd = startsAt.AddMinutes(durationMinutes);
        DateTimeOffset windowStart = startsAt.AddDays(-1);
        DateTimeOffset windowEnd = startsAt.AddDays(1);

        await using DatabaseContext context = DatabaseContext.GenerateContext(_databaseSettings.ConnectionString);
        List<OccupancySlot> candidates = await Project(ActiveSegments(context, organizationId)
            .Where(s =>
                s.PlannedStart >= windowStart && s.PlannedStart <= windowEnd &&
                s.Participations.AsQueryable().Where(ParticipationOccupancy.OccupiesSchedule).Any(p => clientIds.Contains(p.Booking.ClientId)) &&
                (excludeId == null || s.AppointmentId != excludeId)));

        return candidates.Where(s => s.Overlaps(startsAt, newEnd)).ToList();
    }

    public async Task<List<OccupancySlot>> GetOverlappingForRoom(
        Guid organizationId, Guid roomId, DateTimeOffset startsAt, int durationMinutes, Guid? excludeId)
    {
        DateTimeOffset newEnd = startsAt.AddMinutes(durationMinutes);
        DateTimeOffset windowStart = startsAt.AddDays(-1);
        DateTimeOffset windowEnd = startsAt.AddDays(1);

        await using DatabaseContext context = DatabaseContext.GenerateContext(_databaseSettings.ConnectionString);
        List<OccupancySlot> candidates = await Project(ActiveSegments(context, organizationId)
            .Where(s =>
                s.RoomId == roomId &&
                s.PlannedStart >= windowStart && s.PlannedStart <= windowEnd &&
                (excludeId == null || s.AppointmentId != excludeId)));

        return candidates.Where(s => s.Overlaps(startsAt, newEnd)).ToList();
    }

    public async Task<List<OccupancySlot>> GetForEmployeeInRange(
        Guid organizationId, Guid employeeId, DateTimeOffset rangeFrom, DateTimeOffset rangeTo)
    {
        await using DatabaseContext context = DatabaseContext.GenerateContext(_databaseSettings.ConnectionString);
        return await Project(ActiveSegments(context, organizationId)
            .Where(s =>
                s.Employees.Any(e => e.EmployeeId == employeeId) &&
                s.PlannedStart >= rangeFrom && s.PlannedStart <= rangeTo));
    }

    public async Task<List<OccupancySlot>> GetForEmployeesInRange(
        Guid organizationId, List<Guid> employeeIds, DateTimeOffset rangeFrom, DateTimeOffset rangeTo)
    {
        await using DatabaseContext context = DatabaseContext.GenerateContext(_databaseSettings.ConnectionString);
        return await Project(ActiveSegments(context, organizationId)
            .Where(s =>
                s.Employees.Any(e => employeeIds.Contains(e.EmployeeId)) &&
                s.PlannedStart >= rangeFrom && s.PlannedStart <= rangeTo));
    }

    public async Task<List<OccupancySlot>> GetForRoomInRange(
        Guid organizationId, Guid roomId, DateTimeOffset rangeFrom, DateTimeOffset rangeTo)
    {
        await using DatabaseContext context = DatabaseContext.GenerateContext(_databaseSettings.ConnectionString);
        return await Project(ActiveSegments(context, organizationId)
            .Where(s =>
                s.RoomId == roomId &&
                s.PlannedStart >= rangeFrom && s.PlannedStart <= rangeTo));
    }

    public async Task<List<OccupancySlot>> GetForClientsInRange(
        Guid organizationId, List<Guid> clientIds, DateTimeOffset rangeFrom, DateTimeOffset rangeTo)
    {
        await using DatabaseContext context = DatabaseContext.GenerateContext(_databaseSettings.ConnectionString);
        return await Project(ActiveSegments(context, organizationId)
            .Where(s =>
                s.PlannedStart >= rangeFrom && s.PlannedStart <= rangeTo &&
                s.Participations.AsQueryable().Where(ParticipationOccupancy.OccupiesSchedule).Any(p => clientIds.Contains(p.Booking.ClientId))));
    }

    /// <summary>Segmenti ne-otkazanih termina organizacije — status je i dalje na razini termina.</summary>
    private static IQueryable<AppointmentSegment> ActiveSegments(DatabaseContext context, Guid organizationId) =>
        context.AppointmentSegments.Where(s =>
            s.OrganizationId == organizationId &&
            s.Appointment.Status != AppointmentStatus.Cancelled);

    /// <summary>Phase M0: zauzetost klijenta je SEGMENTNA — segment zauzima klijenta samo kroz VLASTITA sudjelovanja
    /// (ParticipationOccupancy.OccupiesSchedule: Confirmed/Completed), nikad kroz "bilo koje" sudjelovanje Bookinga na
    /// terminu. Phase D3A: jedan OccupancySlot po segmentu (danas točno jedan po terminu). Segment s više zaposlenika se
    /// NE sažima u jednog — OccupancySlot nosi jednog zaposlenika, pa se takav segment eksplicitno odbija
    /// (<see cref="AppointmentSegments.GetSingleEmployeeId"/>) dok višezaposlenička zauzetost ne bude definirana.</summary>
    private static async Task<List<OccupancySlot>> Project(IQueryable<AppointmentSegment> query)
    {
        var rows = await query
            .Select(s => new
            {
                s.Id,
                s.AppointmentId,
                s.PlannedStart,
                s.PlannedEnd,
                s.RoomId,
                EmployeeIds = s.Employees.Select(e => e.EmployeeId).ToList(),
                // Phase M0: klijenti koje zauzima OVAJ segment — sudjelovanja tog segmenta po centralnom pravilu.
                ActiveClientIds = s.Participations.AsQueryable()
                    .Where(ParticipationOccupancy.OccupiesSchedule)
                    .Select(p => p.Booking.ClientId)
                    .Distinct()
                    .ToList()
            })
            .ToListAsync();

        return rows
            .Select(r => new OccupancySlot(
                r.AppointmentId,
                r.PlannedStart,
                r.PlannedEnd,
                AppointmentSegments.GetSingleEmployeeId(r.Id, r.EmployeeIds),
                r.RoomId,
                r.ActiveClientIds))
            .ToList();
    }
}
