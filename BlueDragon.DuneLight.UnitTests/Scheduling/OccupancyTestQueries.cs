#nullable disable
using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using BlueDragon.DuneLight.Infrastructure.Domain.Models.Appointments;
using BlueDragon.DuneLight.Infrastructure.Handlers.Interfaces;
using BlueDragon.DuneLight.Infrastructure.UnitOfWork;

namespace BlueDragon.DuneLight.UnitTests.Scheduling;

/// <summary>M1C: the transactional overlap queries of <see cref="ISchedulingOccupancyHandler"/>, each in its own short
/// read transaction — for tests that pin the query contract directly.</summary>
public static class OccupancyTestQueries
{
    public static Task<List<OccupancySlot>> EmployeeOverlapping(
        this SchedulingWorld w, Guid employeeId, DateTimeOffset start, int minutes, params Guid[] excludedSegmentIds) =>
        InTransaction(w, (o, uow) => o.GetOverlappingForEmployees(
            uow, w.OrganizationId, new[] { employeeId }, start, start.AddMinutes(minutes), excludedSegmentIds));

    public static Task<List<OccupancySlot>> ClientsOverlapping(
        this SchedulingWorld w, IReadOnlyCollection<Guid> clientIds, DateTimeOffset start, int minutes, params Guid[] excludedSegmentIds) =>
        InTransaction(w, (o, uow) => o.GetOverlappingForClients(
            uow, w.OrganizationId, clientIds, start, start.AddMinutes(minutes), excludedSegmentIds));

    public static Task<List<OccupancySlot>> RoomOverlapping(
        this SchedulingWorld w, Guid roomId, DateTimeOffset start, int minutes, params Guid[] excludedSegmentIds) =>
        InTransaction(w, (o, uow) => o.GetOverlappingForRoom(
            uow, w.OrganizationId, roomId, start, start.AddMinutes(minutes), excludedSegmentIds));

    private static async Task<List<OccupancySlot>> InTransaction(
        SchedulingWorld w, Func<ISchedulingOccupancyHandler, IUnitOfWork, Task<List<OccupancySlot>>> query)
    {
        await using IUnitOfWork uow = await w.Resolve<IUnitOfWorkFactory>().Begin();
        return await query(w.Resolve<ISchedulingOccupancyHandler>(), uow);
    }
}
