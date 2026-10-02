#nullable disable
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using BlueDragon.DuneLight.Core.DTOs.Appointments;
using BlueDragon.DuneLight.Core.DTOs.Groups;
using BlueDragon.DuneLight.Core.Enums;
using BlueDragon.DuneLight.Core.Shared;
using BlueDragon.DuneLight.Core.Shared.Exceptions;
using BlueDragon.DuneLight.Infrastructure.Domain.Contexts;
using BlueDragon.DuneLight.Infrastructure.Domain.Models.Appointments;
using BlueDragon.DuneLight.Infrastructure.Domain.Models.Employees;
using BlueDragon.DuneLight.Infrastructure.Domain.Models.Catalog;
using BlueDragon.DuneLight.Infrastructure.Domain.Models.Clients;
using BlueDragon.DuneLight.Infrastructure.Utils;
using Microsoft.EntityFrameworkCore;
using ServiceEntity = BlueDragon.DuneLight.Infrastructure.Domain.Models.Catalog.Service;

namespace BlueDragon.DuneLight.UnitTests.Scheduling;

/// <summary>
/// Phase M1B — the application/read/API model is natively segment-based: derived appointment range, segment read model,
/// participation → segment addressing, segment ownership, the target create contract with its production guard, the
/// Update/Move single-segment compatibility boundary, appointment cancel across all segments and the bulk-NoShow rule for an
/// appointment without active participations. Multi-segment data is ARTIFICIAL (production still creates one segment).
/// </summary>
public class SegmentNativeModelTests
{
    private static readonly DateTimeOffset T0 = new(2031, 3, 3, 10, 0, 0, TimeSpan.Zero);

    private static Appointment WithSegments(params (int StartOffsetMinutes, int DurationMinutes)[] segments)
    {
        Appointment appointment = new() { Id = Guid.NewGuid(), OrganizationId = Guid.NewGuid() };
        foreach ((int offset, int duration) in segments)
            SingleSegmentTestExtensions.AddTestSegment(appointment, Guid.NewGuid(), Guid.NewGuid(), null, T0.AddMinutes(offset), duration);
        return appointment;
    }

    #region Derived range (pure)

    [Fact]
    public void Range_SingleSegment_IsTheSegment()
    {
        AppointmentRange range = AppointmentRange.Of(WithSegments((0, 45)));

        Assert.Equal(T0, range.PlannedStart);
        Assert.Equal(T0.AddMinutes(45), range.PlannedEnd);
        Assert.Equal(45, range.SpanMinutes);
    }

    [Fact]
    public void Range_SequentialSegments_IsMinStartToMaxEnd()
    {
        AppointmentRange range = AppointmentRange.Of(WithSegments((30, 60), (0, 30)));

        Assert.Equal(T0, range.PlannedStart);
        Assert.Equal(T0.AddMinutes(90), range.PlannedEnd);
        Assert.Equal(90, range.SpanMinutes);
    }

    [Fact]
    public void Range_ParallelSegments_IsNotTheSumOfDurations()
    {
        // Two 60-minute segments at the same time: the span is 60, never 120.
        AppointmentRange range = AppointmentRange.Of(WithSegments((0, 60), (0, 60), (15, 30)));

        Assert.Equal(T0, range.PlannedStart);
        Assert.Equal(T0.AddMinutes(60), range.PlannedEnd);
        Assert.Equal(60, range.SpanMinutes);
    }

    [Fact]
    public void Range_SegmentsWithAGap_IncludeTheGap()
    {
        // 30 min, 60 min gap, 30 min: the span is 120 (durations sum to 60).
        AppointmentRange range = AppointmentRange.Of(WithSegments((0, 30), (90, 30)));

        Assert.Equal(T0.AddMinutes(120), range.PlannedEnd);
        Assert.Equal(120, range.SpanMinutes);
        Assert.Equal(TimeSpan.FromMinutes(120), range.Span);
    }

    [Fact]
    public void Range_AnAppointmentWithoutSegments_IsAnIntegrityError()
    {
        Assert.ThrowsAny<InvalidOperationException>(() => AppointmentRange.Of(WithSegments()));
    }

    [Fact]
    public void Range_IsIndependentOfParticipations_ZeroParticipationsIsNotZeroSegments()
    {
        Appointment appointment = WithSegments((0, 45));

        Assert.Empty(appointment.Bookings);
        Assert.Equal(45, AppointmentRange.Of(appointment).SpanMinutes);
    }

    #endregion

    #region Read model (persisted, artificial multi-segment)

    /// <summary>Seeds a second segment (different service, room and employee) with a participation of the client's Booking.</summary>
    private static async Task<(Guid SegmentBId, Guid ParticipationBId, ServiceEntity ServiceB, Room RoomB, Employee EmployeeB)> AddSecondSegment(
        SchedulingWorld w, Guid appointmentId, DateTimeOffset start, int minutes = 30,
        ParticipationStatus status = ParticipationStatus.Confirmed)
    {
        ServiceEntity serviceB = await w.AddService(30, 20m, name: "Second service");
        Room roomB = await w.AddRoom();
        Employee employeeB = await w.AddEmployee("Second");
        Guid participationB = await w.AddArtificialSegmentParticipation(appointmentId, w.Client, start, 20m, status, minutes);

        await using DatabaseContext db = w.NewDb();
        BookingSegmentParticipation p = await db.BookingSegmentParticipations.AsNoTracking().SingleAsync(x => x.Id == participationB);
        AppointmentSegment segment = await db.AppointmentSegments.Include(s => s.Employees).SingleAsync(s => s.Id == p.AppointmentSegmentId);
        segment.ServiceId = serviceB.Id.Value;
        segment.RoomId = roomB.Id;
        db.RemoveRange(segment.Employees);
        segment.Employees.Add(new AppointmentSegmentEmployee { AppointmentSegmentId = segment.Id.Value, EmployeeId = employeeB.Id.Value });
        await db.SaveChangesAsync();

        return (segment.Id.Value, participationB, serviceB, roomB, employeeB);
    }

    [Fact]
    public async Task ReadModel_SingleSegment_ExposesTheSegment_AndTheCompatibilityFieldsMatchIt()
    {
        await using SchedulingWorld w = await SchedulingWorld.Create(nameof(ReadModel_SingleSegment_ExposesTheSegment_AndTheCompatibilityFieldsMatchIt));
        Room room = await w.AddRoom();
        AppointmentDto created = await w.CreateAppointment(SchedulingWorld.Future(10), room: room);

        AppointmentDto dto = await w.Appointments.GetById(w.OrganizationId, created.Id);

        AppointmentSegmentDto segment = Assert.Single(dto.Segments);
        Assert.Equal(w.Service.Id.Value, segment.ServiceId);
        Assert.Equal(w.Service.Name, segment.ServiceName);
        Assert.Equal(SchedulingWorld.Future(10), segment.PlannedStart);
        Assert.Equal(SchedulingWorld.Future(10).AddMinutes(w.Service.DefaultDurationMinutes), segment.PlannedEnd);
        Assert.Equal(room.Id, segment.RoomId);
        Assert.Equal(room.Name, segment.RoomName);
        Assert.Equal(w.Employee.Id.Value, Assert.Single(segment.Employees).EmployeeId);
        Assert.NotNull(segment.Employees[0].EmployeeName);
        Assert.Empty(segment.Resources);

        Assert.Equal(segment.PlannedStart, dto.PlannedStart);
        Assert.Equal(segment.PlannedEnd, dto.PlannedEnd);
        // Compatibility projection = the only segment.
        Assert.Equal(dto.PlannedStart, dto.StartsAt);
        Assert.Equal(w.Service.DefaultDurationMinutes, dto.DurationMinutes);
        Assert.Equal(segment.ServiceId, dto.ServiceId);
        Assert.Equal(segment.Employees[0].EmployeeId, dto.EmployeeId);
        Assert.Equal(segment.RoomId, dto.RoomId);

        BookingParticipationDto participation = Assert.Single(Assert.Single(dto.Bookings).Participations);
        Assert.Equal(segment.Id, participation.AppointmentSegmentId);
    }

    [Fact]
    public async Task ReadModel_TwoSegments_WithDifferentServicesRoomsAndEmployees_AndOneBookingOnBoth()
    {
        await using SchedulingWorld w = await SchedulingWorld.Create(nameof(ReadModel_TwoSegments_WithDifferentServicesRoomsAndEmployees_AndOneBookingOnBoth));
        AppointmentDto created = await w.CreateAppointment(SchedulingWorld.Future(10));
        DateTimeOffset startB = SchedulingWorld.Future(10).AddMinutes(w.Service.DefaultDurationMinutes + 30); // a 30-minute gap
        (Guid segmentB, Guid participationB, ServiceEntity serviceB, Room roomB, Employee employeeB) =
            await AddSecondSegment(w, created.Id, startB, minutes: 40);

        AppointmentDto dto = await w.Appointments.GetById(w.OrganizationId, created.Id);

        Assert.Equal(2, dto.Segments.Count);
        AppointmentSegmentDto a = dto.Segments[0], b = dto.Segments[1]; // ordered by PlannedStart
        Assert.Equal(w.Service.Id.Value, a.ServiceId);
        Assert.Equal(w.Employee.Id.Value, Assert.Single(a.Employees).EmployeeId);
        Assert.Null(a.RoomId);
        Assert.Equal(segmentB, b.Id);
        Assert.Equal(serviceB.Id.Value, b.ServiceId);
        Assert.Equal(serviceB.Name, b.ServiceName);
        Assert.Equal(roomB.Id, b.RoomId);
        Assert.Equal(roomB.Name, b.RoomName);
        Assert.Equal(employeeB.Id.Value, Assert.Single(b.Employees).EmployeeId);

        // Derived range spans both segments including the gap.
        Assert.Equal(a.PlannedStart, dto.PlannedStart);
        Assert.Equal(startB.AddMinutes(40), dto.PlannedEnd);
        Assert.Equal((int)(dto.PlannedEnd - dto.PlannedStart).TotalMinutes, dto.DurationMinutes);

        // No authoritative appointment-level service/employee/room: the compatibility projection is empty.
        Assert.Null(dto.ServiceId);
        Assert.Null(dto.EmployeeId);
        Assert.Null(dto.RoomId);

        // One Booking, two participations, each carrying its own SegmentId.
        BookingDto booking = Assert.Single(dto.Bookings);
        Assert.Equal(
            new[] { a.Id, b.Id }.OrderBy(x => x),
            booking.Participations.Select(p => p.AppointmentSegmentId).OrderBy(x => x));
        Assert.Equal(segmentB, booking.Participations.Single(p => p.Id == participationB).AppointmentSegmentId);
    }

    [Fact]
    public async Task ReadModel_Schedule_DerivesTheCellFromSegments()
    {
        await using SchedulingWorld w = await SchedulingWorld.Create(nameof(ReadModel_Schedule_DerivesTheCellFromSegments));
        AppointmentDto created = await w.CreateAppointment(SchedulingWorld.Future(10));
        await AddSecondSegment(w, created.Id, SchedulingWorld.Future(12));

        List<AppointmentScheduleCellDto> cells = await w.Appointments.GetSchedule(w.OrganizationId, new AppointmentScheduleQuery
        {
            From = SchedulingWorld.FutureDay, To = SchedulingWorld.FutureDay.AddDays(1), CompanyId = w.Company.Id.Value
        });

        AppointmentScheduleCellDto cell = Assert.Single(cells, c => c.Id == created.Id);
        Assert.Equal(2, cell.Segments.Count);
        Assert.Equal(SchedulingWorld.Future(10), cell.PlannedStart);
        Assert.Equal(SchedulingWorld.Future(12).AddMinutes(30), cell.PlannedEnd);
        Assert.Equal(cell.PlannedStart, cell.StartsAt);
        Assert.Null(cell.ServiceId);
    }

    [Fact]
    public async Task GroupGeneration_CreatesOneSegmentDirectly_WithMembersParticipating_AndTheCellExposesIt()
    {
        await using SchedulingWorld w = await SchedulingWorld.Create(nameof(GroupGeneration_CreatesOneSegmentDirectly_WithMembersParticipating_AndTheCellExposesIt));
        ServiceEntity svc = await w.AddGroupService(durationMinutes: 50);
        Room room = await w.AddRoom();
        GroupDto group = await w.CreateGroup(svc, capacity: 5, room: room);
        await w.AddGroupMember(group, w.Client);

        GenerateGroupAppointmentsResult result = await w.GenerateOccurrences(group, SchedulingWorld.FutureDay);

        AppointmentScheduleCellDto cell = Assert.Single(result.Created);
        AppointmentSegmentDto segment = Assert.Single(cell.Segments);
        Assert.Equal(svc.Id.Value, segment.ServiceId);
        Assert.Equal(room.Id, segment.RoomId);
        Assert.Equal(room.Name, segment.RoomName);
        Assert.Equal(50, (int)(segment.PlannedEnd - segment.PlannedStart).TotalMinutes);
        Assert.Equal(segment.PlannedStart, cell.PlannedStart);
        Assert.Equal(segment.PlannedEnd, cell.PlannedEnd);
        Assert.Equal(cell.PlannedStart, cell.StartsAt);
        Assert.Equal(50, cell.DurationMinutes);
        Assert.Equal(group.DefaultTrainerId, Assert.Single(segment.Employees).EmployeeId);
        Assert.NotNull(segment.Employees[0].EmployeeName);

        Appointment persisted = await w.LoadAppointment(cell.Id);
        AppointmentSegment stored = Assert.Single(persisted.Segments);
        Assert.Equal(segment.Id, stored.Id.Value);
        Assert.Equal(stored.Id.Value, Assert.Single((await w.LoadParticipations(cell.Id, w.Client))).AppointmentSegmentId);
    }

    #endregion

    #region Ownership and execution follow the participation's segment (service level)

    [Fact]
    public async Task ParticipationCommand_OwnScope_FollowsTheParticipationsSegment()
    {
        await using SchedulingWorld w = await SchedulingWorld.Create(nameof(ParticipationCommand_OwnScope_FollowsTheParticipationsSegment));
        AppointmentDto created = await w.CreateAppointment(SchedulingWorld.Future(10));
        (_, Guid participationB, _, _, Employee employeeB) = await AddSecondSegment(w, created.Id, SchedulingWorld.Future(12));
        Guid participationA = (await w.LoadParticipations(created.Id, w.Client))[0].Id.Value;
        BookingSetStatusRequest cancel = new() { Status = BookingStatus.Cancelled, CancellationReason = "r" };

        // The employee of segment B may not touch the participation on segment A...
        await SchedulingAssert.BusinessRule(ErrorCodes.NotOwner,
            () => w.Bookings.SetParticipationStatus(w.OrganizationId, employeeB.UserId, false, participationA, cancel));
        // ...but may act on the participation on its own segment.
        await w.Bookings.SetParticipationStatus(w.OrganizationId, employeeB.UserId, false, participationB, cancel);

        List<BookingSegmentParticipation> after = await w.LoadParticipations(created.Id, w.Client);
        Assert.Equal(ParticipationStatus.Confirmed, after.Single(p => p.Id == participationA).Status);
        Assert.Equal(ParticipationStatus.Cancelled, after.Single(p => p.Id == participationB).Status);
    }

    [Fact]
    public async Task WholeAppointmentCommand_OwnScope_RequiresAssignmentToEverySegment()
    {
        await using SchedulingWorld w = await SchedulingWorld.Create(nameof(WholeAppointmentCommand_OwnScope_RequiresAssignmentToEverySegment));
        AppointmentDto created = await w.CreateAppointment(SchedulingWorld.Future(10));
        await AddSecondSegment(w, created.Id, SchedulingWorld.Future(12));

        // Assigned to segment A only: cancelling the whole appointment touches segment B too → needs `all`.
        await SchedulingAssert.BusinessRule(ErrorCodes.NotOwner,
            () => w.Appointments.Cancel(w.OrganizationId, w.Employee.UserId, false, created.Id, new AppointmentCancelRequest()));

        Assert.Equal(AppointmentStatus.Scheduled, (await w.LoadAppointment(created.Id)).Status);
    }

    #endregion

    #region Appointment cancel across all segments

    [Fact]
    public async Task AppointmentCancel_CancelsEveryActiveParticipationOnAllSegments_LeavesTerminalOnesUnchanged()
    {
        await using SchedulingWorld w = await SchedulingWorld.Create(nameof(AppointmentCancel_CancelsEveryActiveParticipationOnAllSegments_LeavesTerminalOnesUnchanged));
        AppointmentDto created = await w.CreateAppointment(SchedulingWorld.Future(10));
        (_, Guid onB, _, _, _) = await AddSecondSegment(w, created.Id, SchedulingWorld.Future(12));
        Guid terminal = await w.AddArtificialSegmentParticipation(created.Id, w.Client, SchedulingWorld.Future(14), 10m, ParticipationStatus.NoShow);

        AppointmentDto cancelled = await w.Appointments.Cancel(
            w.OrganizationId, w.ActorUserId, true, created.Id, new AppointmentCancelRequest { CancellationReason = "closed" });

        // Derived afterwards: the untouched NoShow is resolved work, so the explicitly cancelled appointment is Closed
        // (M1A.1 rule 2) — the cancellation fact is still recorded.
        Assert.Equal(AppointmentStatus.Closed, cancelled.Status);
        Assert.NotNull((await w.LoadAppointment(created.Id)).CancelledAt);
        List<BookingSegmentParticipation> after = await w.LoadParticipations(created.Id, w.Client);
        Assert.Equal(3, after.Count);
        Assert.Equal(ParticipationStatus.Cancelled, after[0].Status);
        Assert.Equal(ParticipationStatus.Cancelled, after.Single(p => p.Id == onB).Status);
        BookingSegmentParticipation untouched = after.Single(p => p.Id == terminal);
        Assert.Equal(ParticipationStatus.NoShow, untouched.Status);
        Assert.Equal(0, untouched.StatusVersion);
    }

    [Fact]
    public async Task AppointmentCancel_WithOnlyConfirmedParticipationsOnTwoSegments_CancelsBoth_AndIsCancelled()
    {
        await using SchedulingWorld w = await SchedulingWorld.Create(nameof(AppointmentCancel_WithOnlyConfirmedParticipationsOnTwoSegments_CancelsBoth_AndIsCancelled));
        AppointmentDto created = await w.CreateAppointment(SchedulingWorld.Future(10));
        await AddSecondSegment(w, created.Id, SchedulingWorld.Future(12));

        AppointmentDto cancelled = await w.Appointments.Cancel(w.OrganizationId, w.ActorUserId, true, created.Id, new AppointmentCancelRequest());

        Assert.Equal(AppointmentStatus.Cancelled, cancelled.Status);
        Assert.All(await w.LoadParticipations(created.Id, w.Client), p => Assert.Equal(ParticipationStatus.Cancelled, p.Status));
        Assert.Equal(2, (await w.LoadAppointment(created.Id)).Segments.Count); // there is no Segment.Cancelled
    }

    #endregion

    #region Bulk NoShow without active participations

    [Fact]
    public async Task BulkNoShow_OnAnEmptyGroupOccurrence_IsRejected_AndTheAppointmentStaysScheduled()
    {
        await using SchedulingWorld w = await SchedulingWorld.Create(nameof(BulkNoShow_OnAnEmptyGroupOccurrence_IsRejected_AndTheAppointmentStaysScheduled));
        ServiceEntity svc = await w.AddGroupService();
        GroupDto group = await w.CreateGroup(svc, capacity: 5);
        Appointment occurrence = await w.GenerateSingleOccurrence(group);
        Assert.Empty(occurrence.Bookings);

        await SchedulingAssert.BusinessRule(ErrorCodes.NoActiveParticipations,
            () => w.Appointments.MarkNoShow(w.OrganizationId, w.ActorUserId, true, occurrence.Id.Value, new AppointmentCancelRequest()));

        Appointment after = await w.LoadAppointment(occurrence.Id.Value);
        Assert.Equal(AppointmentStatus.Scheduled, after.Status);
        Assert.Single(after.Segments); // the occurrence keeps its segment — zero participations is not zero segments
        Assert.Null(after.CancelledAt);
        Assert.Empty((await w.LoadAuditLog(occurrence.Id.Value)).Where(l => l.ChangeType.Contains("NoShow")));
    }

    [Fact]
    public async Task BulkNoShow_WhenEveryParticipationIsIndividuallyCancelled_IsRejected_AndStaysScheduled()
    {
        await using SchedulingWorld w = await SchedulingWorld.Create(nameof(BulkNoShow_WhenEveryParticipationIsIndividuallyCancelled_IsRejected_AndStaysScheduled));
        AppointmentDto created = await w.CreateAppointment(SchedulingWorld.Future(10));
        await w.SetBookingStatus(created.Id, w.Client, BookingStatus.Cancelled, "client");

        await SchedulingAssert.BusinessRule(ErrorCodes.NoActiveParticipations,
            () => w.Appointments.MarkNoShow(w.OrganizationId, w.ActorUserId, true, created.Id, new AppointmentCancelRequest()));

        Assert.Equal(AppointmentStatus.Scheduled, (await w.LoadAppointment(created.Id)).Status);
        Assert.Equal(ParticipationStatus.Cancelled, Assert.Single(await w.LoadParticipations(created.Id, w.Client)).Status);
    }

    #endregion

    #region Target create contract and the production guard

    private static AppointmentCreateRequest Target(SchedulingWorld w, params AppointmentSegmentCreateRequest[] segments) => new()
    {
        CompanyId = w.Company.Id.Value,
        Note = "target",
        Segments = segments.ToList()
    };

    private static AppointmentSegmentCreateRequest Segment(
        SchedulingWorld w, DateTimeOffset start, IEnumerable<Guid> employeeIds = null, decimal? amount = null, params Guid[] clients) => new()
    {
        ServiceId = w.Service.Id.Value,
        PlannedStart = start,
        EmployeeIds = (employeeIds ?? new[] { w.Employee.Id.Value }).ToList(),
        Participants = (clients.Length == 0 ? new[] { w.Client.Id.Value } : clients)
            .Select(c => new AppointmentParticipantCreateRequest { ClientId = c, Amount = amount }).ToList()
    };

    private static Task<AppointmentDto> CreateTarget(SchedulingWorld w, AppointmentCreateRequest request) =>
        w.Appointments.Create(w.OrganizationId, w.ActorUserId, true, request);

    [Fact]
    public async Task TargetCreate_SingleSegment_CreatesTheSameShapeAsTheCompatibilityRequest()
    {
        await using SchedulingWorld w = await SchedulingWorld.Create(nameof(TargetCreate_SingleSegment_CreatesTheSameShapeAsTheCompatibilityRequest));
        Client second = await w.AddClient("Second");

        AppointmentDto target = await CreateTarget(w, Target(w, Segment(w, SchedulingWorld.Future(10), amount: 33m, clients: new[] { w.Client.Id.Value, second.Id.Value })));
        AppointmentDto flat = await w.CreateAppointment(w.CreateRequest(SchedulingWorld.Future(14), amount: 33m, extraClients: second));

        foreach (AppointmentDto dto in new[] { target, flat })
        {
            Assert.Equal(AppointmentStatus.Scheduled, dto.Status);
            AppointmentSegmentDto segment = Assert.Single(dto.Segments);
            Assert.Equal(w.Service.Id.Value, segment.ServiceId);
            Assert.Equal(w.Employee.Id.Value, Assert.Single(segment.Employees).EmployeeId);
            Assert.Equal(w.Service.DefaultDurationMinutes, (int)(segment.PlannedEnd - segment.PlannedStart).TotalMinutes);
            Assert.Equal(2, dto.Bookings.Count);
            Assert.All(dto.Bookings, b =>
            {
                BookingParticipationDto p = Assert.Single(b.Participations);
                Assert.Equal(segment.Id, p.AppointmentSegmentId);
                Assert.Equal(BookingStatus.Confirmed, p.Status);
                Assert.Equal(33m, p.Amount);
            });
        }
        Assert.Equal("target", target.Note);
    }

    [Fact]
    public async Task TargetCreate_HonoursAnExplicitPlannedEnd()
    {
        await using SchedulingWorld w = await SchedulingWorld.Create(nameof(TargetCreate_HonoursAnExplicitPlannedEnd));
        AppointmentSegmentCreateRequest segment = Segment(w, SchedulingWorld.Future(10));
        segment.PlannedEnd = SchedulingWorld.Future(10).AddMinutes(25);

        AppointmentDto dto = await CreateTarget(w, Target(w, segment));

        Assert.Equal(SchedulingWorld.Future(10).AddMinutes(25), dto.PlannedEnd);
        Assert.Equal(25, dto.DurationMinutes);
    }

    [Fact]
    public async Task TargetCreate_MoreThanOneSegment_IsEnabled_AndPersistsEverySegment()
    {
        // CHANGED in M1E: MULTI_SEGMENT_NOT_ENABLED is removed — a multi-segment create succeeds (one Booking per client,
        // one Participation per segment). Detailed multi-segment behaviour: MultiSegmentAppointmentTests.
        await using SchedulingWorld w = await SchedulingWorld.Create(nameof(TargetCreate_MoreThanOneSegment_IsEnabled_AndPersistsEverySegment));

        AppointmentDto dto = await CreateTarget(w, Target(w, Segment(w, SchedulingWorld.Future(10)), Segment(w, SchedulingWorld.Future(12))));

        Assert.Equal(2, dto.Segments.Count);
        Assert.Equal(2, Assert.Single(dto.Bookings).Participations.Count);
        Assert.Equal(1, await w.CountAppointments());
    }

    [Fact]
    public async Task ProductionGuard_RejectsMoreThanOneOrNoEmployee_ButNoLongerResources()
    {
        await using SchedulingWorld w = await SchedulingWorld.Create(nameof(ProductionGuard_RejectsMoreThanOneOrNoEmployee_ButNoLongerResources));
        Employee other = await w.AddEmployee("Other");

        ValidationAppException two = await SchedulingAssert.Validation(() => CreateTarget(w,
            Target(w, Segment(w, SchedulingWorld.Future(10), new[] { w.Employee.Id.Value, other.Id.Value }))));
        ValidationAppException none = await SchedulingAssert.Validation(() => CreateTarget(w,
            Target(w, Segment(w, SchedulingWorld.Future(10), Array.Empty<Guid>()))));
        // CHANGED in M1D: resources are enabled — an unknown resource is now a structural NOT_FOUND, not a product guard.
        AppointmentSegmentCreateRequest withResource = Segment(w, SchedulingWorld.Future(10));
        withResource.Resources = new List<AppointmentSegmentResourceRequest> { new() { ResourceId = Guid.NewGuid(), QuantityRequired = 1 } };
        await SchedulingAssert.NotFound(() => CreateTarget(w, Target(w, withResource)));

        // CHANGED in M1G: two employees are allowed but need an explicit pricing source; an individual segment still needs one.
        Assert.Equal(ErrorCodes.PricingSourceRequired, two.Code);
        Assert.Null(none.Code);
        Assert.Equal(0, await w.CountAppointments());
    }

    [Fact]
    public async Task ProductionGuard_RejectsADuplicateParticipantOnASegment()
    {
        await using SchedulingWorld w = await SchedulingWorld.Create(nameof(ProductionGuard_RejectsADuplicateParticipantOnASegment));

        await SchedulingAssert.Validation(() => CreateTarget(w,
            Target(w, Segment(w, SchedulingWorld.Future(10), clients: new[] { w.Client.Id.Value, w.Client.Id.Value }))));

        Assert.Equal(0, await w.CountAppointments());
    }

    #endregion

    #region Update / Move: single-segment compatibility boundary

    [Fact]
    public async Task Move_OnASingleSegmentAppointment_MovesTheSegment_KeepingItsDuration()
    {
        await using SchedulingWorld w = await SchedulingWorld.Create(nameof(Move_OnASingleSegmentAppointment_MovesTheSegment_KeepingItsDuration));
        AppointmentDto created = await w.CreateAppointment(SchedulingWorld.Future(10));

        AppointmentDto moved = await w.Appointments.Move(
            w.OrganizationId, w.ActorUserId, true, created.Id, new AppointmentMoveRequest { StartsAt = SchedulingWorld.Future(15) });

        AppointmentSegmentDto segment = Assert.Single(moved.Segments);
        Assert.Equal(SchedulingWorld.Future(15), segment.PlannedStart);
        Assert.Equal(SchedulingWorld.Future(15).AddMinutes(w.Service.DefaultDurationMinutes), segment.PlannedEnd);
        Assert.Equal(created.Segments[0].Id, segment.Id);
        Assert.Equal(segment.PlannedStart, moved.PlannedStart);
    }

    [Fact]
    public async Task UpdateAndMove_OnAMultiSegmentAppointment_AreRejected_AndChangeNothing()
    {
        await using SchedulingWorld w = await SchedulingWorld.Create(nameof(UpdateAndMove_OnAMultiSegmentAppointment_AreRejected_AndChangeNothing));
        AppointmentDto created = await w.CreateAppointment(SchedulingWorld.Future(10));
        await w.AddArtificialSegmentParticipation(created.Id, w.Client, SchedulingWorld.Future(12), 10m);

        // CHANGED in M1E: a business error (SEGMENT_SELECTION_REQUIRED), not an integrity exception / 500.
        BusinessRuleException move = await Assert.ThrowsAsync<BusinessRuleException>(() => w.Appointments.Move(
            w.OrganizationId, w.ActorUserId, true, created.Id, new AppointmentMoveRequest { StartsAt = SchedulingWorld.Future(15) }));
        BusinessRuleException update = await Assert.ThrowsAsync<BusinessRuleException>(() => w.Appointments.Update(
            w.OrganizationId, w.ActorUserId, true, created.Id, w.UpdateRequest(created, r => r.StartsAt = SchedulingWorld.Future(15))));
        Assert.Equal(ErrorCodes.SegmentSelectionRequired, move.Code);
        Assert.Equal(ErrorCodes.SegmentSelectionRequired, update.Code);

        Appointment after = await w.LoadAppointment(created.Id);
        Assert.Equal(new[] { SchedulingWorld.Future(10), SchedulingWorld.Future(12) }, after.Segments.Select(s => s.PlannedStart).OrderBy(x => x));
    }

    #endregion
}
