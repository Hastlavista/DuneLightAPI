#nullable disable
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using BlueDragon.DuneLight.Core.DTOs.Appointments;
using BlueDragon.DuneLight.Core.Enums;
using BlueDragon.DuneLight.Infrastructure.Domain.Models.Appointments;
using BlueDragon.DuneLight.Infrastructure.Domain.Models.Catalog;
using BlueDragon.DuneLight.Infrastructure.Domain.Models.Clients;
using BlueDragon.DuneLight.Infrastructure.Domain.Models.Employees;
using BlueDragon.DuneLight.Infrastructure.Handlers.Interfaces;

namespace BlueDragon.DuneLight.UnitTests.Scheduling;

/// <summary>
/// S1 occupancy read seam: pins the contract of <see cref="ISchedulingOccupancyHandler"/> directly (the service-level
/// behaviour it feeds is pinned by the characterization suite). Every query excludes Cancelled appointments; the
/// "Overlapping" queries look at candidates whose StartsAt is within ±1 day of the requested start and keep only real
/// overlaps (half-open intervals); the "InRange" queries return every appointment whose StartsAt is inside the inclusive
/// range. Assertions compare instants only, so the results do not depend on the host timezone (see F-19).
/// </summary>
public class SchedulingOccupancyHandlerTests
{
    private static ISchedulingOccupancyHandler Occupancy(SchedulingWorld w) => w.Resolve<ISchedulingOccupancyHandler>();

    #region Shape

    [Fact]
    public async Task Slot_CarriesIdTimesEmployeeRoomAndOnlyActiveClients()
    {
        await using SchedulingWorld w = await SchedulingWorld.Create(nameof(Slot_CarriesIdTimesEmployeeRoomAndOnlyActiveClients));
        Room room = await w.AddRoom();
        Client completed = await w.AddClient("Completed");
        Client cancelled = await w.AddClient("Cancelled");
        Client noShow = await w.AddClient("NoShow");
        Appointment seeded = await w.SeedAppointment(SchedulingWorld.Future(10), room: room, durationMinutes: 45, bookings: new[]
        {
            (w.Client, BookingStatus.Confirmed, 50m),
            (completed, BookingStatus.Completed, 50m),
            (cancelled, BookingStatus.Cancelled, 50m),
            (noShow, BookingStatus.NoShow, 50m)
        });

        OccupancySlot slot = Assert.Single(await Occupancy(w).GetForEmployeeInRange(
            w.OrganizationId, w.Employee.Id.Value, SchedulingWorld.FutureDay, SchedulingWorld.FutureDay.AddDays(1)));

        Assert.Equal(seeded.Id.Value, slot.AppointmentId);
        Assert.Equal(SchedulingWorld.Future(10), slot.Start);
        Assert.Equal(SchedulingWorld.Future(10, 45), slot.End);
        Assert.Equal(w.Employee.Id, slot.EmployeeId);
        Assert.Equal(room.Id, slot.RoomId);
        Assert.Equal(
            new[] { w.Client.Id.Value, completed.Id.Value }.OrderBy(id => id),
            slot.ActiveClientIds.OrderBy(id => id));
    }

    [Fact]
    public async Task Slot_OfATrainerlessGroupOccurrence_HasNoEmployee_AndIsSkippedByTheMultiEmployeeQuery()
    {
        await using SchedulingWorld w = await SchedulingWorld.Create(nameof(Slot_OfATrainerlessGroupOccurrence_HasNoEmployee_AndIsSkippedByTheMultiEmployeeQuery));
        Room room = await w.AddRoom();
        await w.SeedAppointment(SchedulingWorld.Future(10), form: AppointmentForm.Group, room: room);

        OccupancySlot slot = Assert.Single(await Occupancy(w).GetForRoomInRange(
            w.OrganizationId, room.Id.Value, SchedulingWorld.FutureDay, SchedulingWorld.FutureDay.AddDays(1)));
        Assert.Null(slot.EmployeeId);
        Assert.Empty(slot.ActiveClientIds);

        Assert.Empty(await Occupancy(w).GetForEmployeesInRange(
            w.OrganizationId, new List<Guid> { w.Employee.Id.Value }, SchedulingWorld.FutureDay, SchedulingWorld.FutureDay.AddDays(1)));
    }

    #endregion

    #region Adjacency (half-open intervals)

    [Fact]
    public async Task Overlapping_AdjacentIntervalsDoNotOverlap_ForEmployeeRoomAndClient()
    {
        await using SchedulingWorld w = await SchedulingWorld.Create(nameof(Overlapping_AdjacentIntervalsDoNotOverlap_ForEmployeeRoomAndClient));
        Room room = await w.AddRoom();
        await w.SeedAppointment(SchedulingWorld.Future(10), room: room, bookings: (w.Client, BookingStatus.Confirmed, 50m)); // 10:00-10:30
        ISchedulingOccupancyHandler occupancy = Occupancy(w);
        List<Guid> clients = new() { w.Client.Id.Value };

        foreach (DateTimeOffset start in new[] { SchedulingWorld.Future(9, 30), SchedulingWorld.Future(10, 30) })
        {
            Assert.Empty(await occupancy.GetOverlappingForEmployee(w.OrganizationId, w.Employee.Id.Value, start, 30, null));
            Assert.Empty(await occupancy.GetOverlappingForRoom(w.OrganizationId, room.Id.Value, start, 30, null));
            Assert.Empty(await occupancy.GetOverlappingForClients(w.OrganizationId, clients, start, 30, null));
        }

        foreach (DateTimeOffset start in new[] { SchedulingWorld.Future(9, 31), SchedulingWorld.Future(10, 29) })
        {
            Assert.Single(await occupancy.GetOverlappingForEmployee(w.OrganizationId, w.Employee.Id.Value, start, 30, null));
            Assert.Single(await occupancy.GetOverlappingForRoom(w.OrganizationId, room.Id.Value, start, 30, null));
            Assert.Single(await occupancy.GetOverlappingForClients(w.OrganizationId, clients, start, 30, null));
        }
    }

    [Fact]
    public async Task Overlaps_IsHalfOpen()
    {
        OccupancySlot slot = new(Guid.NewGuid(), SchedulingWorld.Future(10), SchedulingWorld.Future(11), null, null, Array.Empty<Guid>());

        Assert.False(slot.Overlaps(SchedulingWorld.Future(9), SchedulingWorld.Future(10)));
        Assert.False(slot.Overlaps(SchedulingWorld.Future(11), SchedulingWorld.Future(12)));
        Assert.True(slot.Overlaps(SchedulingWorld.Future(9), SchedulingWorld.Future(10, 1)));
        Assert.True(slot.Overlaps(SchedulingWorld.Future(10, 59), SchedulingWorld.Future(12)));
        Assert.True(slot.Overlaps(SchedulingWorld.Future(10, 15), SchedulingWorld.Future(10, 45)));
        Assert.True(slot.Overlaps(SchedulingWorld.Future(9), SchedulingWorld.Future(12)));
    }

    #endregion

    #region Cancelled appointments and inactive bookings

    [Fact]
    public async Task CancelledAppointments_AreExcludedFromEveryQuery()
    {
        await using SchedulingWorld w = await SchedulingWorld.Create(nameof(CancelledAppointments_AreExcludedFromEveryQuery));
        Room room = await w.AddRoom();
        // The booking is still Confirmed: only the appointment status excludes it.
        await w.SeedAppointment(SchedulingWorld.Future(10), status: AppointmentStatus.Cancelled, room: room,
            bookings: (w.Client, BookingStatus.Confirmed, 50m));
        ISchedulingOccupancyHandler occupancy = Occupancy(w);
        Guid org = w.OrganizationId;
        List<Guid> clients = new() { w.Client.Id.Value };
        DateTimeOffset from = SchedulingWorld.FutureDay, to = SchedulingWorld.FutureDay.AddDays(1), at = SchedulingWorld.Future(10);

        Assert.Empty(await occupancy.GetOverlappingForEmployee(org, w.Employee.Id.Value, at, 30, null));
        Assert.Empty(await occupancy.GetOverlappingForRoom(org, room.Id.Value, at, 30, null));
        Assert.Empty(await occupancy.GetOverlappingForClients(org, clients, at, 30, null));
        Assert.Empty(await occupancy.GetForEmployeeInRange(org, w.Employee.Id.Value, from, to));
        Assert.Empty(await occupancy.GetForEmployeesInRange(org, new List<Guid> { w.Employee.Id.Value }, from, to));
        Assert.Empty(await occupancy.GetForRoomInRange(org, room.Id.Value, from, to));
        Assert.Empty(await occupancy.GetForClientsInRange(org, clients, from, to));
    }

    [Fact]
    public async Task CompletedAppointments_StillOccupyTheSchedule()
    {
        await using SchedulingWorld w = await SchedulingWorld.Create(nameof(CompletedAppointments_StillOccupyTheSchedule));
        await w.SeedAppointment(SchedulingWorld.Future(10), status: AppointmentStatus.Completed,
            bookings: (w.Client, BookingStatus.Completed, 50m));

        Assert.Single(await Occupancy(w).GetOverlappingForEmployee(w.OrganizationId, w.Employee.Id.Value, SchedulingWorld.Future(10), 30, null));
        Assert.Single(await Occupancy(w).GetOverlappingForClients(
            w.OrganizationId, new List<Guid> { w.Client.Id.Value }, SchedulingWorld.Future(10), 30, null));
    }

    [Theory]
    [InlineData(BookingStatus.Cancelled, false)]
    [InlineData(BookingStatus.NoShow, false)]
    [InlineData(BookingStatus.Confirmed, true)]
    [InlineData(BookingStatus.Completed, true)]
    public async Task ClientQueries_OnlyCountActiveBookings(BookingStatus bookingStatus, bool occupies)
    {
        await using SchedulingWorld w = await SchedulingWorld.Create($"{nameof(ClientQueries_OnlyCountActiveBookings)}-{bookingStatus}");
        await w.SeedAppointment(SchedulingWorld.Future(10), bookings: (w.Client, bookingStatus, 50m));
        ISchedulingOccupancyHandler occupancy = Occupancy(w);
        List<Guid> clients = new() { w.Client.Id.Value };

        List<OccupancySlot> overlapping = await occupancy.GetOverlappingForClients(w.OrganizationId, clients, SchedulingWorld.Future(10), 30, null);
        List<OccupancySlot> inRange = await occupancy.GetForClientsInRange(
            w.OrganizationId, clients, SchedulingWorld.FutureDay, SchedulingWorld.FutureDay.AddDays(1));

        Assert.Equal(occupies ? 1 : 0, overlapping.Count);
        Assert.Equal(occupies ? 1 : 0, inRange.Count);
        // The employee is busy regardless of how the client's booking ended.
        Assert.Single(await occupancy.GetOverlappingForEmployee(w.OrganizationId, w.Employee.Id.Value, SchedulingWorld.Future(10), 30, null));
    }

    [Fact]
    public async Task ClientQueries_MatchOnAnyRequestedClient_ButReportEveryActiveClientOfTheAppointment()
    {
        await using SchedulingWorld w = await SchedulingWorld.Create(nameof(ClientQueries_MatchOnAnyRequestedClient_ButReportEveryActiveClientOfTheAppointment));
        Client partner = await w.AddClient("Partner");
        Client outsider = await w.AddClient("Outsider");
        await w.SeedAppointment(SchedulingWorld.Future(10), bookings: new[]
        {
            (w.Client, BookingStatus.Confirmed, 50m),
            (partner, BookingStatus.Confirmed, 50m)
        });
        ISchedulingOccupancyHandler occupancy = Occupancy(w);

        Assert.Empty(await occupancy.GetOverlappingForClients(
            w.OrganizationId, new List<Guid> { outsider.Id.Value }, SchedulingWorld.Future(10), 30, null));

        OccupancySlot slot = Assert.Single(await occupancy.GetOverlappingForClients(
            w.OrganizationId, new List<Guid> { outsider.Id.Value, partner.Id.Value }, SchedulingWorld.Future(10), 30, null));
        Assert.Equal(
            new[] { w.Client.Id.Value, partner.Id.Value }.OrderBy(id => id),
            slot.ActiveClientIds.OrderBy(id => id));
    }

    #endregion

    #region Room: the seam does not interpret AllowConcurrentBookings

    [Fact]
    public async Task RoomQueries_ReturnAppointmentsInAConcurrentRoomToo_TheFlagIsTheCallersDecision()
    {
        await using SchedulingWorld w = await SchedulingWorld.Create(nameof(RoomQueries_ReturnAppointmentsInAConcurrentRoomToo_TheFlagIsTheCallersDecision));
        Room concurrent = await w.AddRoom(allowConcurrent: true);
        await w.SeedAppointment(SchedulingWorld.Future(10), room: concurrent);

        Assert.Single(await Occupancy(w).GetOverlappingForRoom(w.OrganizationId, concurrent.Id.Value, SchedulingWorld.Future(10), 30, null));
        Assert.Single(await Occupancy(w).GetForRoomInRange(
            w.OrganizationId, concurrent.Id.Value, SchedulingWorld.FutureDay, SchedulingWorld.FutureDay.AddDays(1)));
    }

    #endregion

    #region Self-exclusion (update / move)

    [Fact]
    public async Task Overlapping_ExcludeIdSkipsOnlyThatAppointment()
    {
        await using SchedulingWorld w = await SchedulingWorld.Create(nameof(Overlapping_ExcludeIdSkipsOnlyThatAppointment));
        Room room = await w.AddRoom();
        Appointment own = await w.SeedAppointment(SchedulingWorld.Future(10), room: room, bookings: (w.Client, BookingStatus.Confirmed, 50m));
        ISchedulingOccupancyHandler occupancy = Occupancy(w);
        List<Guid> clients = new() { w.Client.Id.Value };
        DateTimeOffset shifted = SchedulingWorld.Future(10, 15);

        Assert.Empty(await occupancy.GetOverlappingForEmployee(w.OrganizationId, w.Employee.Id.Value, shifted, 30, own.Id));
        Assert.Empty(await occupancy.GetOverlappingForRoom(w.OrganizationId, room.Id.Value, shifted, 30, own.Id));
        Assert.Empty(await occupancy.GetOverlappingForClients(w.OrganizationId, clients, shifted, 30, own.Id));

        Appointment other = await w.SeedAppointment(SchedulingWorld.Future(10, 30), room: room, bookings: (w.Client, BookingStatus.Confirmed, 50m));
        Assert.Equal(other.Id.Value, Assert.Single(await occupancy.GetOverlappingForEmployee(w.OrganizationId, w.Employee.Id.Value, shifted, 30, own.Id)).AppointmentId);
        Assert.Equal(other.Id.Value, Assert.Single(await occupancy.GetOverlappingForRoom(w.OrganizationId, room.Id.Value, shifted, 30, own.Id)).AppointmentId);
        Assert.Equal(other.Id.Value, Assert.Single(await occupancy.GetOverlappingForClients(w.OrganizationId, clients, shifted, 30, own.Id)).AppointmentId);
    }

    [Fact]
    public async Task Move_OntoAnOverlappingPartOfItsOwnSlot_DoesNotConflictWithItself()
    {
        await using SchedulingWorld w = await SchedulingWorld.Create(nameof(Move_OntoAnOverlappingPartOfItsOwnSlot_DoesNotConflictWithItself));
        Room room = await w.AddRoom();
        AppointmentDto created = await w.CreateAppointment(SchedulingWorld.Future(10), room: room);

        AppointmentDto moved = await w.Appointments.Move(w.OrganizationId, w.ActorUserId, true, created.Id,
            new AppointmentMoveRequest { StartsAt = SchedulingWorld.Future(10, 15) });

        Assert.Equal(SchedulingWorld.Future(10, 15), moved.StartsAt);
    }

    [Fact]
    public async Task Update_OntoAnOverlappingPartOfItsOwnSlot_DoesNotConflictWithItself()
    {
        await using SchedulingWorld w = await SchedulingWorld.Create(nameof(Update_OntoAnOverlappingPartOfItsOwnSlot_DoesNotConflictWithItself));
        Room room = await w.AddRoom();
        AppointmentDto created = await w.CreateAppointment(SchedulingWorld.Future(10), room: room);

        AppointmentDto updated = await w.Appointments.Update(w.OrganizationId, w.ActorUserId, true, created.Id,
            w.UpdateRequest(created, r => r.StartsAt = SchedulingWorld.Future(10, 15)));

        Assert.Equal(SchedulingWorld.Future(10, 15), updated.StartsAt);
    }

    #endregion

    #region Window boundaries

    [Fact]
    public async Task Overlapping_CandidateWindowIsPlusMinusOneDay_Inclusive()
    {
        await using SchedulingWorld w = await SchedulingWorld.Create(nameof(Overlapping_CandidateWindowIsPlusMinusOneDay_Inclusive));
        DateTimeOffset requested = SchedulingWorld.Future(12);
        // Both start earlier than the ±1 day window allows or exactly on its edge, and both last long enough to overlap
        // the requested 12:00-12:30 slot.
        Appointment onEdge = await w.SeedAppointment(requested.AddDays(-1), durationMinutes: 24 * 60 + 15);
        await w.SeedAppointment(requested.AddDays(-1).AddMinutes(-1), durationMinutes: 24 * 60 + 60);

        List<OccupancySlot> overlapping = await Occupancy(w).GetOverlappingForEmployee(w.OrganizationId, w.Employee.Id.Value, requested, 30, null);

        // Pinned current behaviour: an appointment that started more than one day earlier is invisible even though it
        // still overlaps (known limitation of the candidate window, not changed by S1).
        Assert.Equal(onEdge.Id.Value, Assert.Single(overlapping).AppointmentId);
    }

    [Fact]
    public async Task InRange_BoundsAreInclusiveOnStartsAt()
    {
        await using SchedulingWorld w = await SchedulingWorld.Create(nameof(InRange_BoundsAreInclusiveOnStartsAt));
        Room room = await w.AddRoom();
        DateTimeOffset from = SchedulingWorld.Future(10), to = SchedulingWorld.Future(14);
        Appointment atFrom = await w.SeedAppointment(from, room: room, bookings: (w.Client, BookingStatus.Confirmed, 50m));
        Appointment atTo = await w.SeedAppointment(to, room: room, bookings: (w.Client, BookingStatus.Confirmed, 50m));
        // Starts one minute before the range but still runs into it: InRange filters on StartsAt only.
        await w.SeedAppointment(from.AddMinutes(-1), room: room, bookings: (w.Client, BookingStatus.Confirmed, 50m));
        await w.SeedAppointment(to.AddMinutes(1), room: room, bookings: (w.Client, BookingStatus.Confirmed, 50m));
        ISchedulingOccupancyHandler occupancy = Occupancy(w);
        Guid[] expected = new[] { atFrom.Id.Value, atTo.Id.Value }.OrderBy(id => id).ToArray();

        Assert.Equal(expected, (await occupancy.GetForEmployeeInRange(w.OrganizationId, w.Employee.Id.Value, from, to)).Select(s => s.AppointmentId).OrderBy(id => id));
        Assert.Equal(expected, (await occupancy.GetForEmployeesInRange(w.OrganizationId, new List<Guid> { w.Employee.Id.Value }, from, to)).Select(s => s.AppointmentId).OrderBy(id => id));
        Assert.Equal(expected, (await occupancy.GetForRoomInRange(w.OrganizationId, room.Id.Value, from, to)).Select(s => s.AppointmentId).OrderBy(id => id));
        Assert.Equal(expected, (await occupancy.GetForClientsInRange(w.OrganizationId, new List<Guid> { w.Client.Id.Value }, from, to)).Select(s => s.AppointmentId).OrderBy(id => id));
    }

    #endregion

    #region Scoping

    [Fact]
    public async Task Queries_AreScopedToTheOrganizationAndTheRequestedSubject()
    {
        await using SchedulingWorld w = await SchedulingWorld.Create(nameof(Queries_AreScopedToTheOrganizationAndTheRequestedSubject));
        await using SchedulingWorld other = await SchedulingWorld.Create(nameof(Queries_AreScopedToTheOrganizationAndTheRequestedSubject) + "-other");
        Employee colleague = await w.AddEmployee("Colleague");
        await w.SeedAppointment(SchedulingWorld.Future(10), employee: colleague);
        await other.SeedAppointment(SchedulingWorld.Future(10), bookings: (other.Client, BookingStatus.Confirmed, 50m));
        ISchedulingOccupancyHandler occupancy = Occupancy(w);

        Assert.Empty(await occupancy.GetOverlappingForEmployee(w.OrganizationId, w.Employee.Id.Value, SchedulingWorld.Future(10), 30, null));
        // Another organization's ids never match, even when asked for explicitly.
        Assert.Empty(await occupancy.GetOverlappingForEmployee(w.OrganizationId, other.Employee.Id.Value, SchedulingWorld.Future(10), 30, null));
        Assert.Empty(await occupancy.GetOverlappingForClients(
            w.OrganizationId, new List<Guid> { other.Client.Id.Value }, SchedulingWorld.Future(10), 30, null));
    }

    #endregion
}
