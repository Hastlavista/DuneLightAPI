#nullable disable
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using BlueDragon.DuneLight.Core.DTOs.Appointments;
using BlueDragon.DuneLight.Core.DTOs.Groups;
using BlueDragon.DuneLight.Core.Enums;
using BlueDragon.DuneLight.Core.Shared;
using BlueDragon.DuneLight.Infrastructure.Domain.Contexts;
using BlueDragon.DuneLight.Infrastructure.Domain.Models.Appointments;
using BlueDragon.DuneLight.Infrastructure.Domain.Models.Catalog;
using BlueDragon.DuneLight.Infrastructure.Domain.Models.Clients;
using BlueDragon.DuneLight.Infrastructure.Domain.Models.Employees;
using BlueDragon.DuneLight.Infrastructure.Utils;
using Microsoft.EntityFrameworkCore;
using ServiceEntity = BlueDragon.DuneLight.Infrastructure.Domain.Models.Catalog.Service;

namespace BlueDragon.DuneLight.UnitTests.Scheduling;

/// <summary>
/// Phase D3A — the appointment's single AppointmentSegment is the AUTHORITATIVE execution frame (service, planned range,
/// room, employee assignment). Every production flow creates exactly one segment; mutations write it; occupancy,
/// ownership, execution context and read models read it. Several tests change the segment directly in the database and
/// show that behaviour follows the segment — proof that nothing reads a second copy of the frame.
/// </summary>
public class AppointmentSegmentCutoverTests
{
    private static DateTimeOffset Z(int h, int mi = 0) => SchedulingWorld.Future(h, mi);

    private static async Task<AppointmentSegment> SegmentOf(SchedulingWorld w, Guid appointmentId)
    {
        await using DatabaseContext db = w.NewDb();
        return await db.AppointmentSegments.AsNoTracking().Include(s => s.Employees)
            .SingleAsync(s => s.AppointmentId == appointmentId); // Single: exactly one segment
    }

    private static async Task UpdateSegmentInDb(SchedulingWorld w, Guid appointmentId, Action<AppointmentSegment> change)
    {
        await using DatabaseContext db = w.NewDb();
        AppointmentSegment segment = await db.AppointmentSegments.SingleAsync(s => s.AppointmentId == appointmentId);
        change(segment);
        await db.SaveChangesAsync();
    }

    private static async Task ReassignSegmentEmployeeInDb(SchedulingWorld w, Guid appointmentId, Guid employeeId)
    {
        await using DatabaseContext db = w.NewDb();
        AppointmentSegment segment = await db.AppointmentSegments.Include(s => s.Employees).SingleAsync(s => s.AppointmentId == appointmentId);
        db.AppointmentSegmentEmployees.RemoveRange(segment.Employees);
        db.AppointmentSegmentEmployees.Add(new AppointmentSegmentEmployee { AppointmentSegmentId = segment.Id.Value, EmployeeId = employeeId });
        await db.SaveChangesAsync();
    }

    #region Creation: exactly one segment per appointment

    [Fact]
    public async Task Create_WritesTheFrameIntoExactlyOneSegment()
    {
        await using SchedulingWorld w = await SchedulingWorld.Create(nameof(Create_WritesTheFrameIntoExactlyOneSegment));
        Room room = await w.AddRoom();

        AppointmentDto dto = await w.CreateAppointment(Z(10), room: room);

        AppointmentSegment segment = await SegmentOf(w, dto.Id);
        Assert.Equal(w.OrganizationId, segment.OrganizationId);
        Assert.Equal(w.Service.Id, segment.ServiceId);
        Assert.Equal(Z(10), segment.PlannedStart);
        Assert.Equal(Z(10).AddMinutes(SchedulingWorld.DefaultServiceDuration), segment.PlannedEnd);
        Assert.Equal(TimeSpan.Zero, segment.PlannedStart.Offset);
        Assert.Equal(room.Id, segment.RoomId);
        Assert.Equal(w.Employee.Id.Value, Assert.Single(segment.Employees).EmployeeId);
        Assert.Null(segment.ActualStart);

        await using DatabaseContext db = w.NewDb();
        Assert.False(await db.AppointmentSegmentResources.AnyAsync(r => r.AppointmentSegmentId == segment.Id));
        Assert.False(await db.BookingSegmentParticipations.AnyAsync(p => p.AppointmentSegmentId == segment.Id));
    }

    [Fact]
    public async Task CompleteNew_CreatesOneSegment()
    {
        await using SchedulingWorld w = await SchedulingWorld.Create(nameof(CompleteNew_CreatesOneSegment));

        AppointmentDto dto = await w.CompleteNew(w.CompleteRequest(SchedulingWorld.Past(10)));

        AppointmentSegment segment = await SegmentOf(w, dto.Id);
        Assert.Equal(SchedulingWorld.Past(10), segment.PlannedStart);
        Assert.Equal(w.Employee.Id.Value, Assert.Single(segment.Employees).EmployeeId);
    }

    [Fact]
    public async Task Recurring_CreatesOneSegmentPerAppointment_AtTheSameLocalWallClockAcrossDst()
    {
        await using SchedulingWorld w = await SchedulingWorld.Create(nameof(Recurring_CreatesOneSegmentPerAppointment_AtTheSameLocalWallClockAcrossDst), "Europe/Zagreb");

        List<AppointmentDto> created = await w.Appointments.CreateRecurring(w.OrganizationId, w.ActorUserId, true, new RecurringAppointmentCreateRequest
        {
            RecurrenceType = RecurrenceType.Weekly, ServiceId = w.Service.Id.Value, EmployeeId = w.Employee.Id.Value,
            CompanyId = w.Company.Id.Value, ClientIds = new List<Guid> { w.Client.Id.Value },
            FirstOccurrenceStartsAt = new DateTimeOffset(2031, 3, 24, 9, 0, 0, TimeSpan.Zero), // Monday 10:00 CET
            EndDate = new DateTimeOffset(2031, 4, 7, 12, 0, 0, TimeSpan.Zero)
        });

        List<DateTimeOffset> starts = new();
        foreach (AppointmentDto dto in created)
            starts.Add((await SegmentOf(w, dto.Id)).PlannedStart);
        Assert.Equal(new[]
        {
            new DateTimeOffset(2031, 3, 24, 9, 0, 0, TimeSpan.Zero),
            new DateTimeOffset(2031, 3, 31, 8, 0, 0, TimeSpan.Zero), // 10:00 CEST
            new DateTimeOffset(2031, 4, 7, 8, 0, 0, TimeSpan.Zero)
        }, starts.OrderBy(s => s).ToArray());
    }

    [Fact]
    public async Task GroupGeneration_CreatesOneSegmentPerOccurrence_FromTheGroupTemplate()
    {
        await using SchedulingWorld w = await SchedulingWorld.Create(nameof(GroupGeneration_CreatesOneSegmentPerOccurrence_FromTheGroupTemplate));
        ServiceEntity groupService = await w.AddGroupService();
        Room room = await w.AddRoom(allowConcurrent: true);
        GroupDto trained = await w.CreateGroup(groupService, capacity: 5, room: room);
        GroupDto trainerless = await w.CreateGroup(groupService, capacity: 5, withTrainer: false, slots: (DayOfWeek.Monday, TimeSpan.FromHours(14)));

        Appointment withTrainer = await w.GenerateSingleOccurrence(trained);
        Appointment withoutTrainer = await w.GenerateSingleOccurrence(trainerless);

        AppointmentSegment a = await SegmentOf(w, withTrainer.Id.Value);
        Assert.Equal(groupService.Id, a.ServiceId);
        Assert.Equal(room.Id, a.RoomId);
        Assert.Equal(Z(10), a.PlannedStart);
        Assert.Equal(Z(11), a.PlannedEnd);
        Assert.Equal(w.Employee.Id.Value, Assert.Single(a.Employees).EmployeeId);
        Assert.Empty((await SegmentOf(w, withoutTrainer.Id.Value)).Employees);
    }

    [Fact]
    public async Task ConcurrentGenerationOfTheSameRange_PersistsEachOccurrenceOnce()
    {
        await using SchedulingWorld w = await SchedulingWorld.Create(nameof(ConcurrentGenerationOfTheSameRange_PersistsEachOccurrenceOnce));
        ServiceEntity groupService = await w.AddGroupService();
        GroupDto group = await w.CreateGroup(groupService, capacity: 5);

        // Both may compute the same candidates before either commits; the per-slot lock + re-check lets only one write.
        Task<GenerateGroupAppointmentsResult>[] runs = Enumerable.Range(0, 4)
            .Select(_ => Task.Run(() => w.Groups.GenerateAppointments(w.OrganizationId, w.ActorUserId, new GenerateGroupAppointmentsRequest
            {
                GroupId = group.Id, FromDate = SchedulingWorld.FutureDay, ToDate = SchedulingWorld.FutureDay.AddDays(21)
            })))
            .ToArray();
        foreach (Task<GenerateGroupAppointmentsResult> run in runs)
        {
            try
            {
                await run;
            }
            catch (Core.Shared.Exceptions.BusinessRuleException ex) when (ex.Code == ErrorCodes.RecurringConflict)
            {
                // the loser of a race reports the duplicate exactly as before D3A
            }
        }

        Assert.Equal(4, await w.CountAppointments(q => q.Where(a => a.GroupId == group.Id)));
    }

    #endregion

    #region Mutation writes the segment (no second copy)

    [Fact]
    public async Task Update_RewritesTheSegmentTimeRoomAndDuration_AndKeepsPinnedF01()
    {
        await using SchedulingWorld w = await SchedulingWorld.Create(nameof(Update_RewritesTheSegmentTimeRoomAndDuration_AndKeepsPinnedF01));
        ServiceEntity longer = await w.AddService(45, 80m);
        Employee other = await w.AddEmployee("Other");
        await w.AssignEmployeeToService(other, longer);
        await w.AssignEmployeeToService(w.Employee, longer);
        Room room = await w.AddRoom();
        AppointmentDto created = await w.CreateAppointment(Z(10));

        await w.Appointments.Update(w.OrganizationId, w.ActorUserId, true, created.Id, w.UpdateRequest(created, r =>
        {
            r.StartsAt = Z(12);
            r.RoomId = room.Id;
            r.ServiceId = longer.Id.Value;
            r.EmployeeId = other.Id.Value;
        }));

        AppointmentSegment segment = await SegmentOf(w, created.Id);
        Assert.Equal(Z(12), segment.PlannedStart);
        Assert.Equal(Z(12).AddMinutes(45), segment.PlannedEnd); // duration follows the requested service
        Assert.Equal(room.Id, segment.RoomId);
        Assert.NotNull(segment.UpdatedAt);
        // Pinned F-01 (kept, not fixed): service and employee are validated but not persisted.
        Assert.Equal(w.Service.Id, segment.ServiceId);
        Assert.Equal(w.Employee.Id.Value, Assert.Single(segment.Employees).EmployeeId);
    }

    [Fact]
    public async Task Move_RewritesTheSegmentTimeEmployeeAndRoom_ReplacingTheSingleAssignment()
    {
        await using SchedulingWorld w = await SchedulingWorld.Create(nameof(Move_RewritesTheSegmentTimeEmployeeAndRoom_ReplacingTheSingleAssignment));
        Employee other = await w.AddEmployee("Other");
        Room room = await w.AddRoom();
        AppointmentDto created = await w.CreateAppointment(Z(10));

        await w.Appointments.Move(w.OrganizationId, w.ActorUserId, true, created.Id,
            new AppointmentMoveRequest { StartsAt = Z(13), EmployeeId = other.Id.Value, RoomId = room.Id });

        AppointmentSegment segment = await SegmentOf(w, created.Id);
        Assert.Equal(Z(13), segment.PlannedStart);
        Assert.Equal(Z(13).AddMinutes(SchedulingWorld.DefaultServiceDuration), segment.PlannedEnd);
        Assert.Equal(room.Id, segment.RoomId);
        Assert.Equal(other.Id.Value, Assert.Single(segment.Employees).EmployeeId);
        await using DatabaseContext db = w.NewDb();
        Assert.False(await db.AppointmentSegmentEmployees.AnyAsync(e => e.EmployeeId == w.Employee.Id && e.AppointmentSegmentId == segment.Id));
    }

    [Fact]
    public async Task CompleteExisting_ReplacesTheWholeFrameOnTheSegment()
    {
        await using SchedulingWorld w = await SchedulingWorld.Create(nameof(CompleteExisting_ReplacesTheWholeFrameOnTheSegment));
        ServiceEntity other = await w.AddService(20, 30m);
        Employee trainer = await w.AddEmployee("Trainer", serviceId: other.Id);
        AppointmentDto created = await w.CreateAppointment(Z(10));

        AppointmentCompleteRequest request = w.CompleteRequest(Z(11), employee: trainer, service: other);
        await w.CompleteExisting(created.Id, request);

        AppointmentSegment segment = await SegmentOf(w, created.Id);
        Assert.Equal(other.Id, segment.ServiceId);
        Assert.Equal(Z(11), segment.PlannedStart);
        Assert.Equal(Z(11).AddMinutes(20), segment.PlannedEnd);
        Assert.Equal(trainer.Id.Value, Assert.Single(segment.Employees).EmployeeId);
    }

    #endregion

    #region Every reader follows the segment

    [Fact]
    public async Task Occupancy_FollowsTheSegment()
    {
        await using SchedulingWorld w = await SchedulingWorld.Create(nameof(Occupancy_FollowsTheSegment));
        Client other = await w.AddClient("Other", "Client");
        AppointmentDto created = await w.CreateAppointment(Z(10));
        await UpdateSegmentInDb(w, created.Id, s => { s.PlannedStart = Z(14); s.PlannedEnd = Z(14, 30); });

        // The old slot is free, the segment's new slot is taken — for the employee.
        AppointmentDto free = await w.CreateAppointment(Z(10), client: other);
        await SchedulingAssert.BusinessRule(ErrorCodes.AppointmentOverlap, () => w.CreateAppointment(Z(14, 15), client: other));

        Assert.Equal(AppointmentStatus.Scheduled, free.Status);
    }

    [Fact]
    public async Task Occupancy_ExplicitlyRejectsASegmentWithSeveralEmployees()
    {
        await using SchedulingWorld w = await SchedulingWorld.Create(nameof(Occupancy_ExplicitlyRejectsASegmentWithSeveralEmployees));
        Employee second = await w.AddEmployee("Second");
        Client other = await w.AddClient("Other", "Client");
        AppointmentDto created = await w.CreateAppointment(Z(10));
        AppointmentSegment segment = await SegmentOf(w, created.Id);
        await using (DatabaseContext db = w.NewDb())
        {
            db.AppointmentSegmentEmployees.Add(new AppointmentSegmentEmployee { AppointmentSegmentId = segment.Id.Value, EmployeeId = second.Id.Value });
            await db.SaveChangesAsync();
        }

        // Multi-employee occupancy is not defined yet: the legacy single-employee slot refuses to collapse it.
        await Assert.ThrowsAsync<InvalidAppointmentSegmentStateException>(
            () => w.CreateAppointment(Z(10), client: other, employee: second));
    }

    [Fact]
    public async Task Ownership_FollowsTheSegmentEmployee()
    {
        await using SchedulingWorld w = await SchedulingWorld.Create(nameof(Ownership_FollowsTheSegmentEmployee));
        Employee other = await w.AddEmployee("Other");
        AppointmentDto created = await w.CreateAppointment(Z(10));
        await ReassignSegmentEmployeeInDb(w, created.Id, other.Id.Value);
        AppointmentMoveRequest move = new() { StartsAt = Z(11) };

        await SchedulingAssert.BusinessRule(ErrorCodes.NotOwner,
            () => w.Appointments.Move(w.OrganizationId, w.Employee.UserId, false, created.Id, move));
        AppointmentDto moved = await w.Appointments.Move(w.OrganizationId, other.UserId, false, created.Id, move);

        Assert.Equal(Z(11), moved.StartsAt);
        Assert.Equal(other.Id, moved.EmployeeId);
    }

    [Fact]
    public async Task Ownership_ATrainerlessSegmentHasNoOwner_ButFullScopeIsUnchanged()
    {
        await using SchedulingWorld w = await SchedulingWorld.Create(nameof(Ownership_ATrainerlessSegmentHasNoOwner_ButFullScopeIsUnchanged));
        ServiceEntity groupService = await w.AddGroupService();
        GroupDto group = await w.CreateGroup(groupService, capacity: 5, withTrainer: false);
        Appointment occurrence = await w.GenerateSingleOccurrence(group);
        AppointmentMoveRequest move = new() { StartsAt = Z(15) };

        await SchedulingAssert.BusinessRule(ErrorCodes.NotOwner,
            () => w.Appointments.Move(w.OrganizationId, w.Employee.UserId, false, occurrence.Id.Value, move));
        // Pre-existing (unchanged by D3A): a trainerless Move without a new employee resolves Guid.Empty and is NotFound.
        await SchedulingAssert.NotFound(() => w.Appointments.Move(w.OrganizationId, w.ActorUserId, true, occurrence.Id.Value, move));
        AppointmentDto moved = await w.Appointments.Move(w.OrganizationId, w.ActorUserId, true, occurrence.Id.Value,
            new AppointmentMoveRequest { StartsAt = Z(15), EmployeeId = w.Employee.Id.Value });

        Assert.Equal(Z(15), moved.StartsAt);
        Assert.Equal(w.Employee.Id, moved.EmployeeId);
        Assert.Equal(w.Employee.Id.Value, Assert.Single((await SegmentOf(w, occurrence.Id.Value)).Employees).EmployeeId);
    }

    [Fact]
    public async Task ExecutionContext_AndLateCancellation_FollowTheSegmentStart()
    {
        await using SchedulingWorld w = await SchedulingWorld.Create(nameof(ExecutionContext_AndLateCancellation_FollowTheSegmentStart));
        await w.SetCancellationCutoffMinutes(120);
        AppointmentDto created = await w.CreateAppointment(Z(10));
        DateTimeOffset now = DateTimeOffset.UtcNow;
        DateTimeOffset soon = new DateTimeOffset(now.Year, now.Month, now.Day, now.Hour, now.Minute, 0, TimeSpan.Zero).AddMinutes(30);
        await UpdateSegmentInDb(w, created.Id, s => { s.PlannedStart = soon; s.PlannedEnd = soon.AddMinutes(30); });

        Appointment loaded = await w.LoadAppointment(created.Id);
        AppointmentExecutionContext execution = ExecutionContextResolver.ForAppointment(loaded);
        await w.SetBookingStatus(created.Id, w.Client, BookingStatus.Cancelled, "late");

        Assert.Equal(soon, execution.StartsAt);
        Assert.Equal(w.Service.Id, execution.ServiceId);
        Assert.Equal(w.Employee.Id, execution.EmployeeId);
        Assert.True((await w.LoadBooking(created.Id, w.Client)).IsLateCancellation); // within the cutoff of the SEGMENT start
    }

    [Fact]
    public async Task ReadModel_ReturnsTheSameContractValues_FromTheSegment()
    {
        await using SchedulingWorld w = await SchedulingWorld.Create(nameof(ReadModel_ReturnsTheSameContractValues_FromTheSegment));
        Room room = await w.AddRoom();
        AppointmentDto created = await w.CreateAppointment(Z(10), room: room);

        AppointmentDto read = await w.Appointments.GetById(w.OrganizationId, created.Id);

        Assert.Equal(Z(10), read.StartsAt);
        Assert.Equal(SchedulingWorld.DefaultServiceDuration, read.DurationMinutes);
        Assert.Equal(w.Service.Id, read.ServiceId);
        Assert.Equal(w.Service.Name, read.ServiceName);
        Assert.Equal(w.Employee.Id, read.EmployeeId);
        Assert.Equal($"{w.Employee.FirstName} {w.Employee.LastName}", read.EmployeeName);
        Assert.Equal(room.Id, read.RoomId);
        Assert.Equal(room.Name, read.RoomName);

        // Duration is derived from the planned range, not stored twice.
        await UpdateSegmentInDb(w, created.Id, s => s.PlannedEnd = s.PlannedStart.AddMinutes(75));
        Assert.Equal(75, (await w.Appointments.GetById(w.OrganizationId, created.Id)).DurationMinutes);
    }

    #endregion

    #region Unsupported segment shapes fail explicitly

    [Fact]
    public async Task AnAppointmentWithoutASegment_FailsExplicitly()
    {
        await using SchedulingWorld w = await SchedulingWorld.Create(nameof(AnAppointmentWithoutASegment_FailsExplicitly));
        AppointmentDto created = await w.CreateAppointment(Z(10));
        await using (DatabaseContext db = w.NewDb())
        {
            await db.AppointmentSegments.Where(s => s.AppointmentId == created.Id).ExecuteDeleteAsync();
        }

        await Assert.ThrowsAsync<InvalidAppointmentSegmentStateException>(() => w.Appointments.GetById(w.OrganizationId, created.Id));
        await Assert.ThrowsAsync<InvalidAppointmentSegmentStateException>(() => w.Appointments.Move(
            w.OrganizationId, w.ActorUserId, true, created.Id, new AppointmentMoveRequest { StartsAt = Z(11) }));
    }

    [Fact]
    public async Task AnAppointmentWithTwoSegments_IsRefusedBySingleFrameOperations_AndNothingChanges()
    {
        await using SchedulingWorld w = await SchedulingWorld.Create(nameof(AnAppointmentWithTwoSegments_IsRefusedBySingleFrameOperations_AndNothingChanges));
        AppointmentDto created = await w.CreateAppointment(Z(10));
        await using (DatabaseContext db = w.NewDb())
        {
            db.AppointmentSegments.Add(new AppointmentSegment
            {
                Id = Guid.NewGuid(), OrganizationId = w.OrganizationId, AppointmentId = created.Id, ServiceId = w.Service.Id.Value,
                PlannedStart = Z(10, 30), PlannedEnd = Z(11), CreatedAt = DateTimeOffset.UtcNow
            });
            await db.SaveChangesAsync();
        }

        await Assert.ThrowsAsync<InvalidAppointmentSegmentStateException>(() => w.Appointments.Move(
            w.OrganizationId, w.ActorUserId, true, created.Id, new AppointmentMoveRequest { StartsAt = Z(12) }));
        await Assert.ThrowsAsync<InvalidAppointmentSegmentStateException>(() => w.Appointments.Update(
            w.OrganizationId, w.ActorUserId, true, created.Id, w.UpdateRequest(created, r => r.StartsAt = Z(12))));

        await using DatabaseContext verify = w.NewDb();
        Assert.Equal(new[] { Z(10), Z(10, 30) },
            await verify.AppointmentSegments.Where(s => s.AppointmentId == created.Id).OrderBy(s => s.PlannedStart).Select(s => s.PlannedStart).ToArrayAsync());
    }

    #endregion
}
