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
/// Occupancy read seam: pins the contract of <see cref="ISchedulingOccupancyHandler"/> directly (the service-level
/// behaviour it feeds is pinned by the characterization suite). M1C: one slot per SEGMENT; employee/room queries see only
/// segments that reserve their slot (SegmentOccupancy), client queries only occupying participations; the "Overlapping"
/// queries compute the half-open overlap in SQL (no candidate window) and exclude SEGMENTS, never an appointment; the
/// "InRange" queries return every segment overlapping the inclusive range. Assertions compare instants only, so the
/// results do not depend on the host timezone (see F-19).
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
        Assert.Equal(seeded.Segments.Single().Id.Value, slot.SegmentId);
        Assert.Equal(w.OrganizationId, slot.OrganizationId);
        Assert.Equal(w.Company.Id.Value, slot.CompanyId);
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
            Assert.Empty(await w.EmployeeOverlapping(w.Employee.Id.Value, start, 30));
            Assert.Empty(await w.RoomOverlapping(room.Id.Value, start, 30));
            Assert.Empty(await w.ClientsOverlapping(clients, start, 30));
        }

        foreach (DateTimeOffset start in new[] { SchedulingWorld.Future(9, 31), SchedulingWorld.Future(10, 29) })
        {
            Assert.Single(await w.EmployeeOverlapping(w.Employee.Id.Value, start, 30));
            Assert.Single(await w.RoomOverlapping(room.Id.Value, start, 30));
            Assert.Single(await w.ClientsOverlapping(clients, start, 30));
        }
    }

    [Fact]
    public async Task Overlaps_IsHalfOpen()
    {
        OccupancySlot slot = new(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(),
            SchedulingWorld.Future(10), SchedulingWorld.Future(11), Array.Empty<Guid>(), null, Array.Empty<Guid>());

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
    public async Task ExplicitlyCancelledUntouchedAppointments_AreExcludedFromEveryQuery()
    {
        await using SchedulingWorld w = await SchedulingWorld.Create(nameof(ExplicitlyCancelledUntouchedAppointments_AreExcludedFromEveryQuery));
        Room room = await w.AddRoom();
        // M1C: explicitly cancelled, no executed outcome — the segment no longer reserves its slot and the cancelled
        // participation does not occupy the client.
        await w.SeedAppointment(SchedulingWorld.Future(10), status: AppointmentStatus.Cancelled, room: room,
            bookings: (w.Client, BookingStatus.Cancelled, 50m));
        ISchedulingOccupancyHandler occupancy = Occupancy(w);
        Guid org = w.OrganizationId;
        List<Guid> clients = new() { w.Client.Id.Value };
        DateTimeOffset from = SchedulingWorld.FutureDay, to = SchedulingWorld.FutureDay.AddDays(1), at = SchedulingWorld.Future(10);

        Assert.Empty(await w.EmployeeOverlapping(w.Employee.Id.Value, at, 30));
        Assert.Empty(await w.RoomOverlapping(room.Id.Value, at, 30));
        Assert.Empty(await w.ClientsOverlapping(clients, at, 30));
        Assert.Empty(await occupancy.GetForEmployeeInRange(org, w.Employee.Id.Value, from, to));
        Assert.Empty(await occupancy.GetForEmployeesInRange(org, new List<Guid> { w.Employee.Id.Value }, from, to));
        Assert.Empty(await occupancy.GetForRoomInRange(org, room.Id.Value, from, to));
        Assert.Empty(await occupancy.GetForClientsInRange(org, clients, from, to));
    }

    [Fact]
    public async Task CompletedAppointments_StillOccupyTheSchedule()
    {
        await using SchedulingWorld w = await SchedulingWorld.Create(nameof(CompletedAppointments_StillOccupyTheSchedule));
        await w.SeedAppointment(SchedulingWorld.Future(10), status: AppointmentStatus.Closed,
            bookings: (w.Client, BookingStatus.Completed, 50m));

        Assert.Single(await w.EmployeeOverlapping(w.Employee.Id.Value, SchedulingWorld.Future(10), 30));
        Assert.Single(await w.ClientsOverlapping(new List<Guid> { w.Client.Id.Value }, SchedulingWorld.Future(10), 30));
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

        List<OccupancySlot> overlapping = await w.ClientsOverlapping(clients, SchedulingWorld.Future(10), 30);
        List<OccupancySlot> inRange = await occupancy.GetForClientsInRange(
            w.OrganizationId, clients, SchedulingWorld.FutureDay, SchedulingWorld.FutureDay.AddDays(1));

        Assert.Equal(occupies ? 1 : 0, overlapping.Count);
        Assert.Equal(occupies ? 1 : 0, inRange.Count);
        // The employee is busy regardless of how the client's booking ended.
        Assert.Single(await w.EmployeeOverlapping(w.Employee.Id.Value, SchedulingWorld.Future(10), 30));
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

        Assert.Empty(await w.ClientsOverlapping(new List<Guid> { outsider.Id.Value }, SchedulingWorld.Future(10), 30));

        OccupancySlot slot = Assert.Single(await w.ClientsOverlapping(
            new List<Guid> { outsider.Id.Value, partner.Id.Value }, SchedulingWorld.Future(10), 30));
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

        Assert.Single(await w.RoomOverlapping(concurrent.Id.Value, SchedulingWorld.Future(10), 30));
        Assert.Single(await Occupancy(w).GetForRoomInRange(
            w.OrganizationId, concurrent.Id.Value, SchedulingWorld.FutureDay, SchedulingWorld.FutureDay.AddDays(1)));
    }

    #endregion

    #region Self-exclusion (update / move)

    [Fact]
    public async Task Overlapping_ExcludesOnlyTheGivenSegment()
    {
        await using SchedulingWorld w = await SchedulingWorld.Create(nameof(Overlapping_ExcludesOnlyTheGivenSegment));
        Room room = await w.AddRoom();
        Appointment own = await w.SeedAppointment(SchedulingWorld.Future(10), room: room, bookings: (w.Client, BookingStatus.Confirmed, 50m));
        Guid ownSegment = own.Segments.Single().Id.Value;
        List<Guid> clients = new() { w.Client.Id.Value };
        DateTimeOffset shifted = SchedulingWorld.Future(10, 15);

        Assert.Empty(await w.EmployeeOverlapping(w.Employee.Id.Value, shifted, 30, ownSegment));
        Assert.Empty(await w.RoomOverlapping(room.Id.Value, shifted, 30, ownSegment));
        Assert.Empty(await w.ClientsOverlapping(clients, shifted, 30, ownSegment));

        Appointment other = await w.SeedAppointment(SchedulingWorld.Future(10, 30), room: room, bookings: (w.Client, BookingStatus.Confirmed, 50m));
        Assert.Equal(other.Id.Value, Assert.Single(await w.EmployeeOverlapping(w.Employee.Id.Value, shifted, 30, ownSegment)).AppointmentId);
        Assert.Equal(other.Id.Value, Assert.Single(await w.RoomOverlapping(room.Id.Value, shifted, 30, ownSegment)).AppointmentId);
        Assert.Equal(other.Id.Value, Assert.Single(await w.ClientsOverlapping(clients, shifted, 30, ownSegment)).AppointmentId);
    }

    [Fact]
    public async Task Overlapping_ASiblingSegmentOfTheSameAppointment_StaysVisible_WhenTheOwnSegmentIsExcluded()
    {
        await using SchedulingWorld w = await SchedulingWorld.Create(nameof(Overlapping_ASiblingSegmentOfTheSameAppointment_StaysVisible_WhenTheOwnSegmentIsExcluded));
        Appointment own = await w.SeedAppointment(SchedulingWorld.Future(10), bookings: (w.Client, BookingStatus.Confirmed, 50m)); // 10:00-10:30
        Guid ownSegment = own.Segments.Single().Id.Value;
        Guid siblingParticipation = await w.AddArtificialSegmentParticipation(own.Id.Value, w.Client, SchedulingWorld.Future(10, 30), 10m); // 10:30-11:00
        List<Guid> clients = new() { w.Client.Id.Value };

        // Editing the own segment onto 10:15-10:45: the AppointmentId is the same, yet the sibling conflicts.
        OccupancySlot employeeHit = Assert.Single(await w.EmployeeOverlapping(w.Employee.Id.Value, SchedulingWorld.Future(10, 15), 30, ownSegment));
        OccupancySlot clientHit = Assert.Single(await w.ClientsOverlapping(clients, SchedulingWorld.Future(10, 15), 30, ownSegment));
        Assert.Equal(own.Id.Value, employeeHit.AppointmentId);
        Assert.NotEqual(ownSegment, employeeHit.SegmentId);
        Assert.Equal(employeeHit.SegmentId, clientHit.SegmentId);
        Assert.Equal(SchedulingWorld.Future(10, 30), employeeHit.Start);
        Assert.NotEqual(Guid.Empty, siblingParticipation);
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
    public async Task Overlapping_HasNoCandidateWindow_ALongSegmentThatStartedDaysEarlierIsSeen()
    {
        await using SchedulingWorld w = await SchedulingWorld.Create(nameof(Overlapping_HasNoCandidateWindow_ALongSegmentThatStartedDaysEarlierIsSeen));
        DateTimeOffset requested = SchedulingWorld.Future(12);
        // CHANGED in M1C: the former ±1 day candidate window hid segments that started earlier but still overlapped;
        // the overlap is now computed directly in SQL with the half-open rule.
        Appointment onEdge = await w.SeedAppointment(requested.AddDays(-1), durationMinutes: 24 * 60 + 15);
        Appointment longer = await w.SeedAppointment(requested.AddDays(-2), durationMinutes: 2 * 24 * 60 + 60);
        await w.SeedAppointment(requested.AddDays(-1), durationMinutes: 24 * 60); // ends exactly at 12:00: adjacent

        List<OccupancySlot> overlapping = await w.EmployeeOverlapping(w.Employee.Id.Value, requested, 30);

        Assert.Equal(new[] { onEdge.Id.Value, longer.Id.Value }.OrderBy(id => id), overlapping.Select(s => s.AppointmentId).OrderBy(id => id));
    }

    [Fact]
    public async Task InRange_ReturnsEverySegmentOverlappingTheInclusiveRange()
    {
        await using SchedulingWorld w = await SchedulingWorld.Create(nameof(InRange_ReturnsEverySegmentOverlappingTheInclusiveRange));
        Room room = await w.AddRoom();
        DateTimeOffset from = SchedulingWorld.Future(10), to = SchedulingWorld.Future(14);
        Appointment atFrom = await w.SeedAppointment(from, room: room, bookings: (w.Client, BookingStatus.Confirmed, 50m));
        Appointment atTo = await w.SeedAppointment(to, room: room, bookings: (w.Client, BookingStatus.Confirmed, 50m));
        // CHANGED in M1C: starts one minute before the range but runs into it — now a candidate (the former StartsAt-only
        // filter missed it). Ending exactly at `from` (adjacent) or starting after `to` stays out.
        Appointment runsIn = await w.SeedAppointment(from.AddMinutes(-1), room: room, bookings: (w.Client, BookingStatus.Confirmed, 50m));
        await w.SeedAppointment(from.AddMinutes(-30), room: room, bookings: (w.Client, BookingStatus.Confirmed, 50m));
        await w.SeedAppointment(to.AddMinutes(1), room: room, bookings: (w.Client, BookingStatus.Confirmed, 50m));
        ISchedulingOccupancyHandler occupancy = Occupancy(w);
        Guid[] expected = new[] { atFrom.Id.Value, atTo.Id.Value, runsIn.Id.Value }.OrderBy(id => id).ToArray();

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

        Assert.Empty(await w.EmployeeOverlapping(w.Employee.Id.Value, SchedulingWorld.Future(10), 30));
        // Another organization's ids never match, even when asked for explicitly.
        Assert.Empty(await w.EmployeeOverlapping(other.Employee.Id.Value, SchedulingWorld.Future(10), 30));
        Assert.Empty(await w.ClientsOverlapping(new List<Guid> { other.Client.Id.Value }, SchedulingWorld.Future(10), 30));
    }

    #endregion
}
