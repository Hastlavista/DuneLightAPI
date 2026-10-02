#nullable disable
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using BlueDragon.DuneLight.Core.DTOs.Appointments;
using BlueDragon.DuneLight.Core.DTOs.Catalog;
using BlueDragon.DuneLight.Core.Enums;
using BlueDragon.DuneLight.Core.Interfaces.Catalog;
using BlueDragon.DuneLight.Core.Shared;
using BlueDragon.DuneLight.Infrastructure.Domain.Contexts;
using BlueDragon.DuneLight.Infrastructure.Domain.Models.Catalog;
using BlueDragon.DuneLight.Infrastructure.Domain.Models.Clients;
using BlueDragon.DuneLight.Infrastructure.Domain.Models.Employees;
using Microsoft.EntityFrameworkCore;

namespace BlueDragon.DuneLight.UnitTests.Scheduling;

/// <summary>
/// Room.Capacity: the maximum number of PEOPLE concurrently in a Room, required and ≥ 1, exposed through the Room API.
/// M1D: it is THE room scheduling rule (the legacy AllowConcurrentBookings flag is gone) — see RoomResourceCapacityTests
/// for the people-count and time-sliced rules.
/// </summary>
public class RoomCapacityTests
{
    private static RoomCreateRequest CreateRequest(SchedulingWorld w, string name, int capacity, Company company = null) => new()
    {
        CompanyId = (company ?? w.Company).Id.Value, Name = name, Capacity = capacity
    };

    private static RoomUpdateRequest UpdateRequest(RoomDto room, int capacity, string name = null) => new()
    {
        Name = name ?? room.Name, Capacity = capacity,
        Note = room.Note, SortOrder = room.SortOrder
    };

    #region Room API

    [Fact]
    public async Task Create_StoresAndReturnsCapacity()
    {
        await using SchedulingWorld w = await SchedulingWorld.Create(nameof(Create_StoresAndReturnsCapacity));
        IRoomService rooms = w.Resolve<IRoomService>();

        RoomDto exclusive = await rooms.Create(w.OrganizationId, w.ActorUserId, CreateRequest(w, "Pilates", 12));
        RoomDto shared = await rooms.Create(w.OrganizationId, w.ActorUserId, CreateRequest(w, "Hall", 1));

        Assert.Equal(12, exclusive.Capacity);
        Assert.Equal(1, shared.Capacity);

        Assert.Equal(12, (await rooms.GetById(w.OrganizationId, exclusive.Id)).Capacity);
        await using DatabaseContext db = w.NewDb();
        Assert.Equal(12, await db.Rooms.Where(r => r.Id == exclusive.Id).Select(r => r.Capacity).SingleAsync());
    }

    [Fact]
    public async Task Update_ChangesCapacity_AndListReturnsIt()
    {
        await using SchedulingWorld w = await SchedulingWorld.Create(nameof(Update_ChangesCapacity_AndListReturnsIt));
        IRoomService rooms = w.Resolve<IRoomService>();
        RoomDto room = await rooms.Create(w.OrganizationId, w.ActorUserId, CreateRequest(w, "Massage", 2));

        RoomDto updated = await rooms.Update(w.OrganizationId, w.ActorUserId, room.Id, UpdateRequest(room, 4));

        Assert.Equal(4, updated.Capacity);
        Assert.Equal(room.CompanyId, updated.CompanyId);
        PagedResult<RoomDto> page = await rooms.GetPaged(w.OrganizationId, w.Company.Id.Value, new PagedRequest { Page = 1, PageSize = 50 });
        Assert.Equal(4, Assert.Single(page.Items, r => r.Id == room.Id).Capacity);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(int.MinValue)]
    public async Task NonPositiveCapacity_IsRejected_OnCreateAndUpdate(int capacity)
    {
        await using SchedulingWorld w = await SchedulingWorld.Create($"{nameof(NonPositiveCapacity_IsRejected_OnCreateAndUpdate)}-{capacity}");
        IRoomService rooms = w.Resolve<IRoomService>();
        RoomDto room = await rooms.Create(w.OrganizationId, w.ActorUserId, CreateRequest(w, "Valid", 3));

        await SchedulingAssert.Validation(() => rooms.Create(w.OrganizationId, w.ActorUserId, CreateRequest(w, "Invalid", capacity)));
        await SchedulingAssert.Validation(() => rooms.Update(w.OrganizationId, w.ActorUserId, room.Id, UpdateRequest(room, capacity)));

        Assert.Equal(3, (await rooms.GetById(w.OrganizationId, room.Id)).Capacity);
        Assert.Single((await rooms.GetPaged(w.OrganizationId, null, new PagedRequest { Page = 1, PageSize = 50 })).Items);
    }

    [Fact]
    public async Task Database_RejectsNonPositiveCapacity_EvenWithoutTheService()
    {
        await using SchedulingWorld w = await SchedulingWorld.Create(nameof(Database_RejectsNonPositiveCapacity_EvenWithoutTheService));
        Room room = await w.AddRoom(capacity: 5);

        await using DatabaseContext db = w.NewDb();
        Room tracked = await db.Rooms.SingleAsync(r => r.Id == room.Id);
        tracked.Capacity = 0;

        DbUpdateException ex = await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync());
        Assert.Contains("ck_rooms_capacity_positive", ex.InnerException?.Message);
    }

    [Fact]
    public async Task TenantAndCompanyScoping_AreUnchanged()
    {
        await using SchedulingWorld w = await SchedulingWorld.Create(nameof(TenantAndCompanyScoping_AreUnchanged));
        await using SchedulingWorld other = await SchedulingWorld.Create($"{nameof(TenantAndCompanyScoping_AreUnchanged)}-other");
        IRoomService rooms = w.Resolve<IRoomService>();
        Company second = await w.AddCompany("Second");
        RoomDto room = await rooms.Create(w.OrganizationId, w.ActorUserId, CreateRequest(w, "Scoped", 6));
        await rooms.Create(w.OrganizationId, w.ActorUserId, CreateRequest(w, "Elsewhere", 6, company: second));

        // Another Organization can neither read nor change the Room, nor create one in this Organization's Company.
        await SchedulingAssert.NotFound(() => rooms.GetById(other.OrganizationId, room.Id));
        await SchedulingAssert.NotFound(() => rooms.Update(other.OrganizationId, other.ActorUserId, room.Id, UpdateRequest(room, 9)));
        await SchedulingAssert.NotFound(() => rooms.Create(other.OrganizationId, other.ActorUserId, CreateRequest(w, "Foreign", 2)));
        Assert.Empty((await rooms.GetPaged(other.OrganizationId, null, new PagedRequest { Page = 1, PageSize = 50 })).Items);

        // The company filter still scopes the list; the Room keeps its Company.
        PagedResult<RoomDto> firstCompany = await rooms.GetPaged(w.OrganizationId, w.Company.Id.Value, new PagedRequest { Page = 1, PageSize = 50 });
        Assert.Equal(new[] { room.Id }, firstCompany.Items.Select(r => r.Id).ToArray());
        Assert.Equal(6, (await rooms.GetById(w.OrganizationId, room.Id)).Capacity);
    }

    [Fact]
    public async Task ActiveNameUniqueness_IsUnchanged()
    {
        await using SchedulingWorld w = await SchedulingWorld.Create(nameof(ActiveNameUniqueness_IsUnchanged));
        IRoomService rooms = w.Resolve<IRoomService>();
        Company second = await w.AddCompany("Second");
        RoomDto first = await rooms.Create(w.OrganizationId, w.ActorUserId, CreateRequest(w, "Studio 1", 4));

        await SchedulingAssert.BusinessRule(ErrorCodes.DuplicateName,
            () => rooms.Create(w.OrganizationId, w.ActorUserId, CreateRequest(w, "  studio 1 ", 8)));

        RoomDto inOtherCompany = await rooms.Create(w.OrganizationId, w.ActorUserId, CreateRequest(w, "Studio 1", 8, company: second));
        await rooms.SetActive(w.OrganizationId, w.ActorUserId, first.Id, false);
        RoomDto reused = await rooms.Create(w.OrganizationId, w.ActorUserId, CreateRequest(w, "Studio 1", 2));

        Assert.Equal(second.Id, inOtherCompany.CompanyId);
        Assert.Equal(2, reused.Capacity);
        await SchedulingAssert.BusinessRule(ErrorCodes.DuplicateName, () => rooms.SetActive(w.OrganizationId, w.ActorUserId, first.Id, true));
    }

    #endregion

    #region Capacity is the room scheduling rule (M1D)

    [Fact]
    public async Task ALargeRoom_AcceptsOverlappingAppointments_WhileThePeopleFit()
    {
        await using SchedulingWorld w = await SchedulingWorld.Create(nameof(ALargeRoom_AcceptsOverlappingAppointments_WhileThePeopleFit));
        Room room = await w.AddRoom(capacity: 6);

        // Three overlapping individual appointments = six people: they fit; a fourth does not.
        for (int i = 0; i < 3; i++)
        {
            Client client = await w.AddClient($"Concurrent{i}", "Client");
            Employee employee = await w.AddEmployee($"Concurrent{i}");
            AppointmentDto dto = await w.CreateAppointment(SchedulingWorld.Future(10), client: client, employee: employee, room: room);
            Assert.Equal(room.Id, dto.RoomId);
        }

        Client last = await w.AddClient("Last", "Client");
        Employee lastEmployee = await w.AddEmployee("Last");
        await SchedulingAssert.BusinessRule(ErrorCodes.RoomCapacityExceeded,
            () => w.CreateAppointment(SchedulingWorld.Future(10), client: last, employee: lastEmployee, room: room));
        Assert.Equal(3, await w.CountAppointments(q => q.Where(a => a.Segments.Any(s => s.RoomId == room.Id))));
    }

    [Fact]
    public async Task ChangingCapacity_ChangesWhatFits()
    {
        await using SchedulingWorld w = await SchedulingWorld.Create(nameof(ChangingCapacity_ChangesWhatFits));
        IRoomService rooms = w.Resolve<IRoomService>();
        Room room = await w.AddRoom(capacity: 2);
        Client other = await w.AddClient("Other", "Client");
        Employee secondEmployee = await w.AddEmployee("Second");
        await w.CreateAppointment(SchedulingWorld.Future(10), room: room);

        await SchedulingAssert.BusinessRule(ErrorCodes.RoomCapacityExceeded,
            () => w.CreateAppointment(SchedulingWorld.Future(10), client: other, employee: secondEmployee, room: room));

        RoomDto dto = await rooms.GetById(w.OrganizationId, room.Id.Value);
        await rooms.Update(w.OrganizationId, w.ActorUserId, room.Id.Value, UpdateRequest(dto, 4));
        await w.CreateAppointment(SchedulingWorld.Future(10), client: other, employee: secondEmployee, room: room);
    }

    #endregion
}
