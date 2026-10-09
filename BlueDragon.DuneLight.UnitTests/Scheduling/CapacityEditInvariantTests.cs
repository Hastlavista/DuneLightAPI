#nullable disable
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;
using BlueDragon.DuneLight.Core.DTOs.Appointments;
using BlueDragon.DuneLight.Core.DTOs.Catalog;
using BlueDragon.DuneLight.Core.Enums;
using BlueDragon.DuneLight.Core.Interfaces.Appointments;
using BlueDragon.DuneLight.Core.Interfaces.Catalog;
using BlueDragon.DuneLight.Core.Shared;
using BlueDragon.DuneLight.Core.Shared.Exceptions;
using BlueDragon.DuneLight.Infrastructure.Domain.Contexts;
using BlueDragon.DuneLight.Infrastructure.Domain.Models.Appointments;
using BlueDragon.DuneLight.Infrastructure.Domain.Models.Catalog;
using BlueDragon.DuneLight.Infrastructure.Domain.Models.Clients;
using BlueDragon.DuneLight.Infrastructure.Domain.Models.Employees;
using BlueDragon.DuneLight.Infrastructure.Handlers.Interfaces;
using BlueDragon.DuneLight.Infrastructure.UnitOfWork;
using BlueDragon.DuneLight.Infrastructure.Utils;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace BlueDragon.DuneLight.UnitTests.Scheduling;

/// <summary>
/// Phase M1D.1 — editing Room.Capacity / Resource.Capacity preserves the hard capacity invariant: increases are always
/// allowed; a decrease must cover the peak usage of every slot-reserving segment that has not ended yet (ongoing included,
/// the past never blocks); the edit serializes with scheduling through the SAME room/resource scheduling lock.
/// </summary>
public class CapacityEditInvariantTests
{
    private static IRoomService Rooms(SchedulingWorld w) => w.Resolve<IRoomService>();
    private static IResourceService Resources(SchedulingWorld w) => w.Resolve<IResourceService>();

    private static RoomUpdateRequest RoomEdit(Room room, int capacity) => new() { Name = room.Name, Capacity = capacity };
    private static ResourceUpdateRequest ResourceEdit(Resource resource, int capacity) => new() { Name = resource.Name, Capacity = capacity };

    private static Task<RoomDto> SetRoomCapacity(SchedulingWorld w, Room room, int capacity) =>
        Rooms(w).Update(w.OrganizationId, w.ActorUserId, room.Id.Value, RoomEdit(room, capacity));

    private static Task<ResourceDto> SetResourceCapacity(SchedulingWorld w, Resource resource, int capacity) =>
        Resources(w).Update(w.OrganizationId, w.ActorUserId, resource.Id.Value, ResourceEdit(resource, capacity));

    private static async Task<int> StoredRoomCapacity(SchedulingWorld w, Room room)
    {
        await using DatabaseContext db = w.NewDb();
        return await db.Rooms.Where(r => r.Id == room.Id).Select(r => r.Capacity).SingleAsync();
    }

    private static async Task<int> StoredResourceCapacity(SchedulingWorld w, Resource resource)
    {
        await using DatabaseContext db = w.NewDb();
        return await db.Resources.Where(r => r.Id == resource.Id).Select(r => r.Capacity).SingleAsync();
    }

    private static async Task<Client[]> Clients(SchedulingWorld w, int count)
    {
        Client[] clients = new Client[count];
        for (int i = 0; i < count; i++)
            clients[i] = await w.AddClient("P" + i, Guid.NewGuid().ToString("N")[..6]);
        return clients;
    }

    /// <summary>Seeds a segment in the room with the default employee and <paramref name="clients"/> Confirmed participants.</summary>
    private static async Task<Appointment> SeedInRoom(
        SchedulingWorld w, Room room, DateTimeOffset start, int clients, int minutes = 60,
        AppointmentStatus status = AppointmentStatus.Scheduled, BookingStatus participation = BookingStatus.Confirmed)
    {
        Employee employee = await w.AddEmployee("E" + Guid.NewGuid().ToString("N")[..6]);
        Client[] people = await Clients(w, clients);
        return await w.SeedAppointment(start, status: status, form: AppointmentForm.Group, employee: employee, room: room,
            durationMinutes: minutes, bookings: people.Select(c => (c, participation, 0m)).ToArray());
    }

    private static async Task<Appointment> SeedWithResource(
        SchedulingWorld w, Resource resource, DateTimeOffset start, int quantity, int minutes = 60,
        AppointmentStatus status = AppointmentStatus.Scheduled)
    {
        Employee employee = await w.AddEmployee("E" + Guid.NewGuid().ToString("N")[..6]);
        Appointment seeded = await w.SeedAppointment(start, status: status, form: AppointmentForm.Group, employee: employee, durationMinutes: minutes);
        await using DatabaseContext db = w.NewDb();
        db.AppointmentSegmentResources.Add(new AppointmentSegmentResource
        {
            AppointmentSegmentId = seeded.Segments.Single().Id.Value, ResourceId = resource.Id.Value, QuantityRequired = quantity
        });
        await db.SaveChangesAsync();
        return seeded;
    }

    private static DateTimeOffset Now => TestClock.UtcNow;

    #region Room

    [Fact]
    public async Task Room_Increase_AlwaysSucceeds()
    {
        await using SchedulingWorld w = await SchedulingWorld.Create(nameof(Room_Increase_AlwaysSucceeds));
        Room room = await w.AddRoom(capacity: 3);
        await SeedInRoom(w, room, SchedulingWorld.Future(10), clients: 2);

        Assert.Equal(10, (await SetRoomCapacity(w, room, 10)).Capacity);
        Assert.Equal(10, await StoredRoomCapacity(w, room));
    }

    [Fact]
    public async Task Room_Decrease_AboveOrExactlyAtThePeak_Succeeds_BelowItFails()
    {
        await using SchedulingWorld w = await SchedulingWorld.Create(nameof(Room_Decrease_AboveOrExactlyAtThePeak_Succeeds_BelowItFails));
        Room room = await w.AddRoom(capacity: 10);
        await SeedInRoom(w, room, SchedulingWorld.Future(10), clients: 2);                // 10:00-11:00, 3 people
        await SeedInRoom(w, room, SchedulingWorld.Future(10, 30), clients: 1);            // 10:30-11:30, 2 people -> peak 5 at 10:30
        await SeedInRoom(w, room, SchedulingWorld.Future(11, 30), clients: 3);            // adjacent, 4 people

        await SetRoomCapacity(w, room, 6);
        await SetRoomCapacity(w, room, 5);
        BusinessRuleException ex = await SchedulingAssert.BusinessRule(ErrorCodes.RoomCapacityBelowScheduledUsage, () => SetRoomCapacity(w, room, 4));

        Assert.Equal(5, await StoredRoomCapacity(w, room));
        Assert.Contains("5", ex.Message);
        string details = System.Text.Json.JsonSerializer.Serialize(ex.Details);
        Assert.Contains("\"proposedCapacity\":4", details);
        Assert.Contains("\"peakUsage\":5", details);
        Assert.Contains(SchedulingWorld.Future(10, 30).ToString("yyyy-MM-ddTHH:mm"), details);
    }

    [Fact]
    public async Task Room_PastOverload_DoesNotBlockADecrease_AndHistoryIsUntouched()
    {
        await using SchedulingWorld w = await SchedulingWorld.Create(nameof(Room_PastOverload_DoesNotBlockADecrease_AndHistoryIsUntouched));
        Room room = await w.AddRoom(capacity: 10);
        Appointment history = await SeedInRoom(w, room, SchedulingWorld.Past(10), clients: 7, status: AppointmentStatus.Closed,
            participation: BookingStatus.Completed);                                        // 8 people, yesterday-ish
        await SeedInRoom(w, room, SchedulingWorld.Future(10), clients: 1);                 // 2 people ahead

        await SetRoomCapacity(w, room, 2);

        Assert.Equal(2, await StoredRoomCapacity(w, room));
        Assert.Equal(8, (await w.RoomUsage(room.Id.Value, SchedulingWorld.Past(10), 60)).Sum(c => c.Amount));
        Assert.Equal(7, (await w.LoadAppointment(history.Id.Value)).Bookings.Count);
    }

    [Fact]
    public async Task Room_AnOngoingSegmentCounts_UntilItsPlannedEnd()
    {
        await using SchedulingWorld w = await SchedulingWorld.Create(nameof(Room_AnOngoingSegmentCounts_UntilItsPlannedEnd));
        Room room = await w.AddRoom(capacity: 10);
        await SeedInRoom(w, room, Now.AddMinutes(-30), clients: 3, minutes: 120);        // started, 4 people
        await SeedInRoom(w, room, Now.AddMinutes(-120), clients: 8, minutes: 60);        // already ended, 9 people

        await SchedulingAssert.BusinessRule(ErrorCodes.RoomCapacityBelowScheduledUsage, () => SetRoomCapacity(w, room, 3));
        await SetRoomCapacity(w, room, 4);
    }

    [Fact]
    public async Task Room_AnExplicitlyCancelledUnexecutedFutureSegment_DoesNotCount()
    {
        await using SchedulingWorld w = await SchedulingWorld.Create(nameof(Room_AnExplicitlyCancelledUnexecutedFutureSegment_DoesNotCount));
        Room room = await w.AddRoom(capacity: 10);
        await SeedInRoom(w, room, SchedulingWorld.Future(10), clients: 5, status: AppointmentStatus.Cancelled, participation: BookingStatus.Cancelled);

        await SetRoomCapacity(w, room, 1);
        Assert.Equal(1, await StoredRoomCapacity(w, room));
    }

    [Fact]
    public async Task Room_CatalogWritesThatAreNotACapacityEdit_NeverWriteCapacity()
    {
        await using SchedulingWorld w = await SchedulingWorld.Create(nameof(Room_CatalogWritesThatAreNotACapacityEdit_NeverWriteCapacity));
        Room room = await w.AddRoom(capacity: 4);
        Room stale = await w.Resolve<IRoomHandler>().GetById(w.OrganizationId, room.Id.Value);
        await SetRoomCapacity(w, room, 6);

        // A stale entity written by the general catalog path (rename/activation) must not restore the old capacity.
        stale.Note = "renamed elsewhere";
        await w.Resolve<IRoomHandler>().Update(stale);
        await Rooms(w).SetActive(w.OrganizationId, w.ActorUserId, room.Id.Value, false);

        Assert.Equal(6, await StoredRoomCapacity(w, room));
    }

    #endregion

    #region Resource

    [Fact]
    public async Task Resource_Increase_AlwaysSucceeds()
    {
        await using SchedulingWorld w = await SchedulingWorld.Create(nameof(Resource_Increase_AlwaysSucceeds));
        Resource tables = await w.AddResource(capacity: 2);
        await SeedWithResource(w, tables, SchedulingWorld.Future(10), 2);

        Assert.Equal(9, (await SetResourceCapacity(w, tables, 9)).Capacity);
    }

    [Fact]
    public async Task Resource_Decrease_AboveOrExactlyAtThePeak_Succeeds_BelowItFails()
    {
        await using SchedulingWorld w = await SchedulingWorld.Create(nameof(Resource_Decrease_AboveOrExactlyAtThePeak_Succeeds_BelowItFails));
        Resource tables = await w.AddResource(capacity: 10);
        await SeedWithResource(w, tables, SchedulingWorld.Future(10), 2);
        await SeedWithResource(w, tables, SchedulingWorld.Future(10, 30), 3);              // peak 5 at 10:30
        await SeedWithResource(w, tables, SchedulingWorld.Future(11, 30), 4);              // adjacent

        await SetResourceCapacity(w, tables, 6);
        await SetResourceCapacity(w, tables, 5);
        BusinessRuleException ex = await SchedulingAssert.BusinessRule(ErrorCodes.ResourceCapacityBelowScheduledUsage, () => SetResourceCapacity(w, tables, 4));

        Assert.Equal(5, await StoredResourceCapacity(w, tables));
        Assert.Contains("\"peakUsage\":5", System.Text.Json.JsonSerializer.Serialize(ex.Details));
    }

    [Fact]
    public async Task Resource_PastUsage_DoesNotBlock_OngoingCounts_ExplicitlyCancelledFutureDoesNot()
    {
        await using SchedulingWorld w = await SchedulingWorld.Create(nameof(Resource_PastUsage_DoesNotBlock_OngoingCounts_ExplicitlyCancelledFutureDoesNot));
        Resource tables = await w.AddResource(capacity: 10);
        await SeedWithResource(w, tables, SchedulingWorld.Past(10), 9, status: AppointmentStatus.Closed);      // history
        await SeedWithResource(w, tables, Now.AddMinutes(-20), 3, minutes: 120);                               // ongoing
        await SeedWithResource(w, tables, SchedulingWorld.Future(10), 8, status: AppointmentStatus.Cancelled); // explicitly cancelled

        await SchedulingAssert.BusinessRule(ErrorCodes.ResourceCapacityBelowScheduledUsage, () => SetResourceCapacity(w, tables, 2));
        await SetResourceCapacity(w, tables, 3);
        Assert.Equal(3, await StoredResourceCapacity(w, tables));
    }

    [Fact]
    public async Task Resource_CatalogWritesThatAreNotACapacityEdit_NeverWriteCapacity()
    {
        await using SchedulingWorld w = await SchedulingWorld.Create(nameof(Resource_CatalogWritesThatAreNotACapacityEdit_NeverWriteCapacity));
        Resource tables = await w.AddResource(capacity: 4);
        Resource stale = await w.Resolve<IResourceHandler>().GetById(w.OrganizationId, tables.Id.Value);
        await SetResourceCapacity(w, tables, 6);

        stale.Note = "stale";
        await w.Resolve<IResourceHandler>().Update(stale);
        await Resources(w).SetActive(w.OrganizationId, w.ActorUserId, tables.Id.Value, false);

        Assert.Equal(6, await StoredResourceCapacity(w, tables));
    }

    #endregion

    #region Concurrency (real database)

    private static async Task<Exception> InOwnScope(Func<IServiceProvider, Task> action)
    {
        using IServiceScope scope = SchedulingTestHost.CreateScope();
        try
        {
            await action(scope.ServiceProvider);
            return null;
        }
        catch (Exception ex)
        {
            return ex;
        }
    }

    private static async Task<int> WaitersOn(SchedulingWorld w, long key)
    {
        long hi = (long)((ulong)key >> 32), lo = key & 0xFFFF_FFFFL;
        await using DatabaseContext db = w.NewDb();
        return await db.Database.SqlQuery<int>(
            $"SELECT count(*)::int AS \"Value\" FROM pg_locks WHERE locktype = 'advisory' AND NOT granted AND objsubid = 1 AND classid = {hi}::bigint::oid AND objid = {lo}::bigint::oid")
            .SingleAsync();
    }

    /// <summary>Holds the subject lock, starts both requests (both end up waiting on that SAME lock), releases them together.</summary>
    private static async Task<Exception[]> Gated(SchedulingWorld w, long key, Func<IUnitOfWork, Task> takeLock, params Func<Task<Exception>>[] requests)
    {
        Task<Exception[]> race;
        await using (IUnitOfWork gate = await w.Resolve<IUnitOfWorkFactory>().Begin())
        {
            await takeLock(gate);
            race = Task.WhenAll(requests.Select(r => Task.Run(r)));
            Stopwatch sw = Stopwatch.StartNew();
            while (await WaitersOn(w, key) < requests.Length)
            {
                Assert.True(sw.Elapsed < TimeSpan.FromSeconds(30), "Both requests should wait on the same subject lock.");
                await Task.Delay(20);
            }
            await gate.CommitAsync();
        }

        return await race;
    }

    [Fact]
    public async Task Race_RoomCapacityDecreaseVersusAddBooking_OnlyLegalOutcomes_RepeatedRounds()
    {
        await using SchedulingWorld w = await SchedulingWorld.Create(nameof(Race_RoomCapacityDecreaseVersusAddBooking_OnlyLegalOutcomes_RepeatedRounds));
        ISchedulingOccupancyHandler occupancy = w.Resolve<ISchedulingOccupancyHandler>();
        HashSet<string> seen = new();

        for (int round = 0; round < 6; round++)
        {
            Room room = await w.AddRoom(capacity: 3);
            DateTimeOffset start = SchedulingWorld.Future(8).AddDays(7 * round);
            Employee employee = await w.AddEmployee("R" + round);
            Client owner = await w.AddClient("Owner" + round), joiner = await w.AddClient("Joiner" + round);
            AppointmentDto created = await w.CreateAppointment(w.CreateRequest(start, client: owner, employee: employee, room: room)); // 2 people

            Exception[] outcomes = await Gated(w, SchedulingLockOrder.RoomKey(room.Id.Value),
                gate => occupancy.LockSchedulingSubjects(gate, null, null, new[] { room.Id.Value }),
                () => InOwnScope(sp => sp.GetRequiredService<IAppointmentService>().AddClient(
                    w.OrganizationId, w.ActorUserId, true, created.Id, new AppointmentClientAddRequest
                    {
                        ClientId = joiner.Id.Value,
                        Participations = new List<AppointmentClientParticipationRequest> { new() { SegmentId = created.Segments[0].Id } }
                    })),
                () => InOwnScope(sp => sp.GetRequiredService<IRoomService>().Update(
                    w.OrganizationId, w.ActorUserId, room.Id.Value, RoomEdit(room, 2))));

            int capacity = await StoredRoomCapacity(w, room);
            int people = (await w.RoomUsage(room.Id.Value, start, 30)).Sum(c => c.Amount);
            Assert.True(people <= capacity, $"Invalid final state: {people} people in a capacity-{capacity} room.");
            Assert.Single(outcomes, o => o == null);
            string loser = Assert.IsType<BusinessRuleException>(Assert.Single(outcomes, o => o != null)).Code;
            if (outcomes[0] == null)
                Assert.Equal((ErrorCodes.RoomCapacityBelowScheduledUsage, 3, 3), (loser, capacity, people)); // booking first
            else
                Assert.Equal((ErrorCodes.RoomCapacityExceeded, 2, 2), (loser, capacity, people));             // decrease first
            seen.Add(loser);
        }

        Assert.NotEmpty(seen);
    }

    [Fact]
    public async Task Race_ResourceCapacityDecreaseVersusScheduling_OnlyLegalOutcomes_RepeatedRounds()
    {
        await using SchedulingWorld w = await SchedulingWorld.Create(nameof(Race_ResourceCapacityDecreaseVersusScheduling_OnlyLegalOutcomes_RepeatedRounds));
        ISchedulingOccupancyHandler occupancy = w.Resolve<ISchedulingOccupancyHandler>();

        for (int round = 0; round < 6; round++)
        {
            Resource tables = await w.AddResource(capacity: 3);
            DateTimeOffset start = SchedulingWorld.Future(8).AddDays(7 * round);
            await SeedWithResource(w, tables, start, 2, minutes: 30);
            Employee employee = await w.AddEmployee("X" + round);
            Client client = await w.AddClient("X" + round);
            AppointmentCreateRequest request = new()
            {
                CompanyId = w.Company.Id.Value,
                Segments = new List<AppointmentSegmentCreateRequest>
                {
                    new()
                    {
                        ServiceId = w.Service.Id.Value, PlannedStart = start, EmployeeIds = new List<Guid> { employee.Id.Value },
                        Participants = new List<AppointmentParticipantCreateRequest> { new() { ClientId = client.Id.Value } },
                        Resources = new List<AppointmentSegmentResourceRequest> { new() { ResourceId = tables.Id.Value, QuantityRequired = 1 } }
                    }
                }
            };

            Exception[] outcomes = await Gated(w, SchedulingLockOrder.ResourceKey(tables.Id.Value),
                gate => occupancy.LockSchedulingSubjects(gate, null, null, null, new[] { tables.Id.Value }),
                () => InOwnScope(sp => sp.GetRequiredService<IAppointmentService>().Create(w.OrganizationId, w.ActorUserId, true, request)),
                () => InOwnScope(sp => sp.GetRequiredService<IResourceService>().Update(
                    w.OrganizationId, w.ActorUserId, tables.Id.Value, ResourceEdit(tables, 2))));

            int capacity = await StoredResourceCapacity(w, tables);
            int used = (await w.ResourceUsage(tables.Id.Value, start, 30)).Sum(c => c.Amount);
            Assert.True(used <= capacity, $"Invalid final state: {used} units of a capacity-{capacity} resource.");
            Assert.Single(outcomes, o => o == null);
            string loser = Assert.IsType<BusinessRuleException>(Assert.Single(outcomes, o => o != null)).Code;
            Assert.Equal(outcomes[0] == null ? ErrorCodes.ResourceCapacityBelowScheduledUsage : ErrorCodes.ResourceCapacityExceeded, loser);
        }
    }

    [Fact]
    public async Task CapacityEdits_OfOtherRoomsAndResources_AreNotBlocked()
    {
        await using SchedulingWorld w = await SchedulingWorld.Create(nameof(CapacityEdits_OfOtherRoomsAndResources_AreNotBlocked));
        Room busyRoom = await w.AddRoom(capacity: 5), freeRoom = await w.AddRoom(capacity: 5);
        Resource busyResource = await w.AddResource(capacity: 5), freeResource = await w.AddResource(capacity: 5);

        await using IUnitOfWork holder = await w.Resolve<IUnitOfWorkFactory>().Begin();
        await w.Resolve<ISchedulingOccupancyHandler>().LockSchedulingSubjects(holder, null, null, new[] { busyRoom.Id.Value }, new[] { busyResource.Id.Value });

        Task<Exception> roomEdit = Task.Run(() => InOwnScope(sp => sp.GetRequiredService<IRoomService>().Update(
            w.OrganizationId, w.ActorUserId, freeRoom.Id.Value, RoomEdit(freeRoom, 2))));
        Task<Exception> resourceEdit = Task.Run(() => InOwnScope(sp => sp.GetRequiredService<IResourceService>().Update(
            w.OrganizationId, w.ActorUserId, freeResource.Id.Value, ResourceEdit(freeResource, 2))));
        Task both = Task.WhenAll(roomEdit, resourceEdit);

        Assert.Same(both, await Task.WhenAny(both, Task.Delay(TimeSpan.FromSeconds(20))));
        Assert.Null(await roomEdit);
        Assert.Null(await resourceEdit);
    }

    #endregion
}
