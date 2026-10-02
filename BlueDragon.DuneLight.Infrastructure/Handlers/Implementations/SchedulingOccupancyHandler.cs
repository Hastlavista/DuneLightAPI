using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using BlueDragon.DuneLight.Infrastructure.Domain.Contexts;
using BlueDragon.DuneLight.Infrastructure.Domain.Models.Appointments;
using BlueDragon.DuneLight.Infrastructure.Domain.Settings;
using BlueDragon.DuneLight.Infrastructure.Handlers.Interfaces;
using BlueDragon.DuneLight.Infrastructure.UnitOfWork;
using BlueDragon.DuneLight.Infrastructure.Utils;
using Microsoft.EntityFrameworkCore;

namespace BlueDragon.DuneLight.Infrastructure.Handlers.Implementations;

/// <summary>
/// Phase M1C — segmentna zauzetost: svaki upit radi nad AppointmentSegment (izvršni identitet), preklapanje se računa u SQL-u
/// poluotvorenom formulom (SchedulingInterval.SegmentOverlaps), zauzetost zaposlenika/prostorije slijedi SegmentOccupancy,
/// zauzetost klijenta ParticipationOccupancy, a isključenje je po segmentu. Metode bez IUnitOfWork otvaraju vlastiti
/// DbContext (bez zaključavanja) — za batch kandidate, pauze i available-slots.
/// </summary>
public class SchedulingOccupancyHandler : ISchedulingOccupancyHandler
{
    private readonly DatabaseSettings _databaseSettings;

    public SchedulingOccupancyHandler(DatabaseSettings databaseSettings)
    {
        _databaseSettings = databaseSettings;
    }

    public Task<List<OccupancySlot>> GetOverlappingForEmployees(
        IUnitOfWork uow, Guid organizationId, IReadOnlyCollection<Guid> employeeIds, DateTimeOffset start, DateTimeOffset end,
        IReadOnlyCollection<Guid> excludedSegmentIds)
    {
        return Project(EmployeeOverlaps(uow.Context, organizationId, employeeIds.ToList(), start, end, excludedSegmentIds));
    }

    public async Task<List<OccupancySlot>> GetOverlappingForEmployee(
        Guid organizationId, Guid employeeId, DateTimeOffset start, DateTimeOffset end, IReadOnlyCollection<Guid> excludedSegmentIds = null)
    {
        await using DatabaseContext context = DatabaseContext.GenerateContext(_databaseSettings.ConnectionString);
        return await Project(EmployeeOverlaps(context, organizationId, new List<Guid> { employeeId }, start, end, excludedSegmentIds));
    }

    public Task<List<OccupancySlot>> GetOverlappingForClients(
        IUnitOfWork uow, Guid organizationId, IReadOnlyCollection<Guid> clientIds, DateTimeOffset start, DateTimeOffset end,
        IReadOnlyCollection<Guid> excludedSegmentIds)
    {
        List<Guid> ids = clientIds.ToList();
        List<Guid> excluded = Excluded(excludedSegmentIds);
        return Project(Segments(uow.Context, organizationId)
            .Where(SchedulingInterval.SegmentOverlaps(start, end))
            .Where(s => !excluded.Contains(s.Id.Value))
            .Where(OccupiedByAnyClient(ids)));
    }

    public Task<List<OccupancySlot>> GetOverlappingForRoom(
        IUnitOfWork uow, Guid organizationId, Guid roomId, DateTimeOffset start, DateTimeOffset end, IReadOnlyCollection<Guid> excludedSegmentIds)
    {
        List<Guid> excluded = Excluded(excludedSegmentIds);
        return Project(Segments(uow.Context, organizationId)
            .Where(SegmentOccupancy.ReservesSlot)
            .Where(s => s.RoomId == roomId)
            .Where(SchedulingInterval.SegmentOverlaps(start, end))
            .Where(s => !excluded.Contains(s.Id.Value)));
    }

    public async Task<List<OccupancySlot>> GetForEmployeeInRange(
        Guid organizationId, Guid employeeId, DateTimeOffset rangeFrom, DateTimeOffset rangeTo)
    {
        await using DatabaseContext context = DatabaseContext.GenerateContext(_databaseSettings.ConnectionString);
        return await Project(InRange(Segments(context, organizationId).Where(SegmentOccupancy.ReservesSlot), rangeFrom, rangeTo)
            .Where(s => s.Employees.Any(e => e.EmployeeId == employeeId)));
    }

    public async Task<List<OccupancySlot>> GetForEmployeesInRange(
        Guid organizationId, List<Guid> employeeIds, DateTimeOffset rangeFrom, DateTimeOffset rangeTo)
    {
        await using DatabaseContext context = DatabaseContext.GenerateContext(_databaseSettings.ConnectionString);
        return await Project(InRange(Segments(context, organizationId).Where(SegmentOccupancy.ReservesSlot), rangeFrom, rangeTo)
            .Where(s => s.Employees.Any(e => employeeIds.Contains(e.EmployeeId))));
    }

    public async Task<List<OccupancySlot>> GetForRoomInRange(
        Guid organizationId, Guid roomId, DateTimeOffset rangeFrom, DateTimeOffset rangeTo)
    {
        await using DatabaseContext context = DatabaseContext.GenerateContext(_databaseSettings.ConnectionString);
        return await Project(InRange(Segments(context, organizationId).Where(SegmentOccupancy.ReservesSlot), rangeFrom, rangeTo)
            .Where(s => s.RoomId == roomId));
    }

    public async Task<List<OccupancySlot>> GetForClientsInRange(
        Guid organizationId, List<Guid> clientIds, DateTimeOffset rangeFrom, DateTimeOffset rangeTo)
    {
        await using DatabaseContext context = DatabaseContext.GenerateContext(_databaseSettings.ConnectionString);
        return await Project(InRange(Segments(context, organizationId), rangeFrom, rangeTo)
            .Where(OccupiedByAnyClient(clientIds)));
    }

    public async Task LockSchedulingSubjects(IUnitOfWork uow, IEnumerable<Guid> employeeIds, IEnumerable<Guid> clientIds)
    {
        foreach (long key in SchedulingLockOrder.Keys(employeeIds, clientIds))
            await uow.Context.Database.ExecuteSqlInterpolatedAsync($"SELECT pg_advisory_xact_lock({key})");
    }

    public async Task<bool> TryLockClientSchedule(IUnitOfWork uow, Guid clientId)
    {
        long key = SchedulingLockOrder.ClientKey(clientId);
        return await uow.Context.Database
            .SqlQuery<bool>($"SELECT pg_try_advisory_xact_lock({key}) AS \"Value\"")
            .SingleAsync();
    }

    private static IQueryable<AppointmentSegment> Segments(DatabaseContext context, Guid organizationId) =>
        context.AppointmentSegments.AsNoTracking().Where(s => s.OrganizationId == organizationId);

    private static IQueryable<AppointmentSegment> EmployeeOverlaps(
        DatabaseContext context, Guid organizationId, List<Guid> employeeIds, DateTimeOffset start, DateTimeOffset end,
        IReadOnlyCollection<Guid> excludedSegmentIds)
    {
        List<Guid> excluded = Excluded(excludedSegmentIds);
        return Segments(context, organizationId)
            .Where(SegmentOccupancy.ReservesSlot)
            .Where(s => s.Employees.Any(e => employeeIds.Contains(e.EmployeeId)))
            .Where(SchedulingInterval.SegmentOverlaps(start, end))
            .Where(s => !excluded.Contains(s.Id.Value));
    }

    /// <summary>Kandidati za batch provjere: segmenti koji se preklapaju sa [rangeFrom, rangeTo] (nadskup — precizna
    /// provjera po occurrenceu kroz OccupancySlot.Overlaps kod pozivatelja).</summary>
    private static IQueryable<AppointmentSegment> InRange(IQueryable<AppointmentSegment> query, DateTimeOffset rangeFrom, DateTimeOffset rangeTo) =>
        query.Where(s => s.PlannedStart <= rangeTo && rangeFrom < s.PlannedEnd);

    private static System.Linq.Expressions.Expression<Func<AppointmentSegment, bool>> OccupiedByAnyClient(List<Guid> clientIds) =>
        s => s.Participations.AsQueryable().Where(ParticipationOccupancy.OccupiesSchedule).Any(p => clientIds.Contains(p.Booking.ClientId));

    private static List<Guid> Excluded(IReadOnlyCollection<Guid> excludedSegmentIds) =>
        excludedSegmentIds?.ToList() ?? new List<Guid>();

    /// <summary>Jedan OccupancySlot po segmentu: identitet (organizacija, poslovnica, termin, SEGMENT), raspon, zaposlenici,
    /// prostorija i klijenti koje OVAJ segment zauzima (ParticipationOccupancy).</summary>
    private static async Task<List<OccupancySlot>> Project(IQueryable<AppointmentSegment> query)
    {
        var rows = await query
            .Select(s => new
            {
                s.Id,
                s.OrganizationId,
                s.Appointment.CompanyId,
                s.AppointmentId,
                s.PlannedStart,
                s.PlannedEnd,
                s.RoomId,
                EmployeeIds = s.Employees.Select(e => e.EmployeeId).ToList(),
                ActiveClientIds = s.Participations.AsQueryable()
                    .Where(ParticipationOccupancy.OccupiesSchedule)
                    .Select(p => p.Booking.ClientId)
                    .Distinct()
                    .ToList()
            })
            .ToListAsync();

        return rows
            .Select(r => new OccupancySlot(
                r.OrganizationId, r.CompanyId, r.AppointmentId, r.Id.Value, r.PlannedStart, r.PlannedEnd, r.EmployeeIds, r.RoomId, r.ActiveClientIds))
            .ToList();
    }
}
