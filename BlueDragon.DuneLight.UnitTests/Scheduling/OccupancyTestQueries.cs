#nullable disable
using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using BlueDragon.DuneLight.Infrastructure.Domain.Models.Appointments;
using BlueDragon.DuneLight.Infrastructure.Handlers.Interfaces;
using BlueDragon.DuneLight.Infrastructure.UnitOfWork;
using BlueDragon.DuneLight.Infrastructure.Utils;

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

    /// <summary>M1D: room usage in PEOPLE per occupying segment overlapping the range.</summary>
    public static async Task<List<CapacityClaim>> RoomUsage(
        this SchedulingWorld w, Guid roomId, DateTimeOffset start, int minutes, params Guid[] excludedSegmentIds)
    {
        await using IUnitOfWork uow = await w.Resolve<IUnitOfWorkFactory>().Begin();
        return await w.Resolve<ISchedulingOccupancyHandler>().GetRoomUsage(
            uow, w.OrganizationId, roomId, start, start.AddMinutes(minutes), excludedSegmentIds);
    }

    /// <summary>M1D: resource usage (QuantityRequired) per occupying segment overlapping the range.</summary>
    public static async Task<List<CapacityClaim>> ResourceUsage(
        this SchedulingWorld w, Guid resourceId, DateTimeOffset start, int minutes, params Guid[] excludedSegmentIds)
    {
        await using IUnitOfWork uow = await w.Resolve<IUnitOfWorkFactory>().Begin();
        return await w.Resolve<ISchedulingOccupancyHandler>().GetResourceUsage(
            uow, w.OrganizationId, resourceId, start, start.AddMinutes(minutes), excludedSegmentIds);
    }

    private static async Task<List<OccupancySlot>> InTransaction(
        SchedulingWorld w, Func<ISchedulingOccupancyHandler, IUnitOfWork, Task<List<OccupancySlot>>> query)
    {
        await using IUnitOfWork uow = await w.Resolve<IUnitOfWorkFactory>().Begin();
        return await query(w.Resolve<ISchedulingOccupancyHandler>(), uow);
    }
}
