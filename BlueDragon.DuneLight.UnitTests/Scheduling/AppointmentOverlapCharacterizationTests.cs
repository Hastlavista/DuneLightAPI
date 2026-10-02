#nullable disable
using System;
using System.Threading.Tasks;
using BlueDragon.DuneLight.Core.DTOs.Appointments;
using BlueDragon.DuneLight.Core.Enums;
using BlueDragon.DuneLight.Core.Shared;
using BlueDragon.DuneLight.Infrastructure.Domain.Contexts;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using BlueDragon.DuneLight.Infrastructure.Domain.Models.Appointments;
using BlueDragon.DuneLight.Infrastructure.Domain.Models.Catalog;
using BlueDragon.DuneLight.Infrastructure.Domain.Models.Clients;
using BlueDragon.DuneLight.Infrastructure.Domain.Models.Employees;

namespace BlueDragon.DuneLight.UnitTests.Scheduling;

/// <summary>
/// CHARACTERIZATION (matrix B, C, D): hard overlap rules of AppointmentService.EnsureNoHardOverlap for the three
/// scheduled resources — Employee, Client and Room. All three raise the SAME code (APPOINTMENT_OVERLAP) and can never be
/// bypassed by OverrideAvailability. Each test is arranged so that exactly ONE of the three rules can fire (the other
/// two resources differ), which is how the rule under test is told apart from its siblings.
///
/// Interval semantics observed: half-open — an appointment ending exactly when another starts does not conflict.
/// The default Service is 30 minutes, so an appointment at 10:00 occupies [10:00, 10:30).
/// </summary>
public class AppointmentOverlapCharacterizationTests
{
    #region B. Employee overlap

    [Fact]
    public async Task EmployeeOverlap_PartiallyOverlappingAppointmentForTheSameEmployee_IsRejected()
    {
        await using SchedulingWorld w = await SchedulingWorld.Create(nameof(EmployeeOverlap_PartiallyOverlappingAppointmentForTheSameEmployee_IsRejected));
        Client other = await w.AddClient("Other", "Client");
        await w.CreateAppointment(SchedulingWorld.Future(10));

        await SchedulingAssert.BusinessRule(ErrorCodes.AppointmentOverlap,
            () => w.CreateAppointment(SchedulingWorld.Future(10, 15), client: other));

        Assert.Equal(1, await w.CountAppointments());
    }

    [Fact]
    public async Task EmployeeOverlap_SameStartTimeForTheSameEmployee_IsRejected()
    {
        await using SchedulingWorld w = await SchedulingWorld.Create(nameof(EmployeeOverlap_SameStartTimeForTheSameEmployee_IsRejected));
        Client other = await w.AddClient("Other", "Client");
        await w.CreateAppointment(SchedulingWorld.Future(10));

        await SchedulingAssert.BusinessRule(ErrorCodes.AppointmentOverlap,
            () => w.CreateAppointment(SchedulingWorld.Future(10), client: other));
    }

    [Fact]
    public async Task EmployeeOverlap_AppointmentStartingExactlyWhenTheExistingOneEnds_IsAllowed()
    {
        await using SchedulingWorld w = await SchedulingWorld.Create(nameof(EmployeeOverlap_AppointmentStartingExactlyWhenTheExistingOneEnds_IsAllowed));
        Client other = await w.AddClient("Other", "Client");
        await w.CreateAppointment(SchedulingWorld.Future(10));

        AppointmentDto adjacent = await w.CreateAppointment(SchedulingWorld.Future(10, 30), client: other);

        Assert.Equal(AppointmentStatus.Scheduled, adjacent.Status);
        Assert.Equal(2, await w.CountAppointments());
    }

    [Fact]
    public async Task EmployeeOverlap_AppointmentEndingExactlyWhenTheExistingOneStarts_IsAllowed()
    {
        await using SchedulingWorld w = await SchedulingWorld.Create(nameof(EmployeeOverlap_AppointmentEndingExactlyWhenTheExistingOneStarts_IsAllowed));
        Client other = await w.AddClient("Other", "Client");
        await w.CreateAppointment(SchedulingWorld.Future(10));

        // 09:30 + 30 min ends at exactly 10:00.
        AppointmentDto adjacent = await w.CreateAppointment(SchedulingWorld.Future(9, 30), client: other);

        Assert.Equal(AppointmentStatus.Scheduled, adjacent.Status);
    }

    [Fact]
    public async Task EmployeeOverlap_CancelledAppointmentNoLongerBlocksTheEmployee()
    {
        await using SchedulingWorld w = await SchedulingWorld.Create(nameof(EmployeeOverlap_CancelledAppointmentNoLongerBlocksTheEmployee));
        Client other = await w.AddClient("Other", "Client");
        AppointmentDto existing = await w.CreateAppointment(SchedulingWorld.Future(10));
        await w.Appointments.Cancel(w.OrganizationId, w.ActorUserId, true, existing.Id, new AppointmentCancelRequest { CancellationReason = "test" });

        AppointmentDto replacement = await w.CreateAppointment(SchedulingWorld.Future(10), client: other);

        Assert.Equal(AppointmentStatus.Scheduled, replacement.Status);
    }

    [Fact]
    public async Task EmployeeOverlap_AnotherEmployeeMayUseTheSameTime()
    {
        await using SchedulingWorld w = await SchedulingWorld.Create(nameof(EmployeeOverlap_AnotherEmployeeMayUseTheSameTime));
        Client other = await w.AddClient("Other", "Client");
        Employee secondEmployee = await w.AddEmployee("Second");
        await w.CreateAppointment(SchedulingWorld.Future(10));

        AppointmentDto parallel = await w.CreateAppointment(SchedulingWorld.Future(10), client: other, employee: secondEmployee);

        Assert.Equal(secondEmployee.Id, parallel.EmployeeId);
        Assert.Equal(2, await w.CountAppointments());
    }

    [Fact]
    public async Task EmployeeOverlap_CompletedAppointmentStillBlocksTheEmployee()
    {
        await using SchedulingWorld w = await SchedulingWorld.Create(nameof(EmployeeOverlap_CompletedAppointmentStillBlocksTheEmployee));
        Client other = await w.AddClient("Other", "Client");
        // Only Cancelled is excluded from the employee overlap query; Completed remains a busy interval.
        await w.SeedAppointment(SchedulingWorld.Future(10), AppointmentStatus.Closed, bookings: (w.Client, BookingStatus.Completed, 50m));

        await SchedulingAssert.BusinessRule(ErrorCodes.AppointmentOverlap,
            () => w.CreateAppointment(SchedulingWorld.Future(10), client: other));
    }

    [Fact]
    public async Task EmployeeOverlap_GroupOccurrenceOfTheSameEmployeeBlocksAnIndividualAppointment()
    {
        await using SchedulingWorld w = await SchedulingWorld.Create(nameof(EmployeeOverlap_GroupOccurrenceOfTheSameEmployeeBlocksAnIndividualAppointment));
        Client other = await w.AddClient("Other", "Client");
        // A Group occurrence is an Appointment row like any other, so it occupies its trainer.
        await w.SeedAppointment(SchedulingWorld.Future(10), form: AppointmentForm.Group, employee: w.Employee);

        await SchedulingAssert.BusinessRule(ErrorCodes.AppointmentOverlap,
            () => w.CreateAppointment(SchedulingWorld.Future(10), client: other));
    }

    [Fact]
    public async Task EmployeeOverlap_GroupOccurrenceWithoutATrainerDoesNotBlockAnyEmployee()
    {
        await using SchedulingWorld w = await SchedulingWorld.Create(nameof(EmployeeOverlap_GroupOccurrenceWithoutATrainerDoesNotBlockAnyEmployee));
        Client other = await w.AddClient("Other", "Client");
        await w.SeedAppointment(SchedulingWorld.Future(10), form: AppointmentForm.Group, employee: null);

        AppointmentDto created = await w.CreateAppointment(SchedulingWorld.Future(10), client: other);

        Assert.Equal(AppointmentStatus.Scheduled, created.Status);
    }

    [Fact]
    public async Task EmployeeOverlap_IsAppliedOnCreate_NotJustThroughTheClientOrRoomRule()
    {
        await using SchedulingWorld w = await SchedulingWorld.Create(nameof(EmployeeOverlap_IsAppliedOnCreate_NotJustThroughTheClientOrRoomRule));
        Client other = await w.AddClient("Other", "Client");
        Room roomA = await w.AddRoom();
        Room roomB = await w.AddRoom();
        await w.CreateAppointment(SchedulingWorld.Future(10), room: roomA);

        // Different client AND different room: only the Employee rule can reject this.
        BusinessRuleExceptionHolder holder = await BusinessRuleExceptionHolder.Capture(
            () => w.CreateAppointment(SchedulingWorld.Future(10), client: other, room: roomB));

        Assert.Equal(ErrorCodes.AppointmentOverlap, holder.Code);
        Assert.Contains("Trener", holder.Message);
    }

    [Fact]
    public async Task EmployeeOverlap_OverrideAvailabilityDoesNotBypassIt()
    {
        await using SchedulingWorld w = await SchedulingWorld.Create(nameof(EmployeeOverlap_OverrideAvailabilityDoesNotBypassIt));
        Client other = await w.AddClient("Other", "Client");
        await w.CreateAppointment(SchedulingWorld.Future(10));

        await SchedulingAssert.BusinessRule(ErrorCodes.AppointmentOverlap,
            () => w.CreateAppointment(w.CreateRequest(SchedulingWorld.Future(10), client: other, overrideAvailability: true)));
    }

    #endregion

    #region C. Client overlap

    [Fact]
    public async Task ClientOverlap_SameClientWithAnOverlappingActiveAppointment_IsRejected()
    {
        await using SchedulingWorld w = await SchedulingWorld.Create(nameof(ClientOverlap_SameClientWithAnOverlappingActiveAppointment_IsRejected));
        Employee secondEmployee = await w.AddEmployee("Second");
        await w.CreateAppointment(SchedulingWorld.Future(10));

        // Different employee, no room: only the Client rule can fire.
        BusinessRuleExceptionHolder holder = await BusinessRuleExceptionHolder.Capture(
            () => w.CreateAppointment(SchedulingWorld.Future(10, 15), employee: secondEmployee));

        Assert.Equal(ErrorCodes.AppointmentOverlap, holder.Code);
        Assert.Contains(w.Client.FirstName, holder.Message);
        Assert.Equal(1, await w.CountAppointments());
    }

    [Fact]
    public async Task ClientOverlap_DifferentClientsMayOverlapInTime()
    {
        await using SchedulingWorld w = await SchedulingWorld.Create(nameof(ClientOverlap_DifferentClientsMayOverlapInTime));
        Client other = await w.AddClient("Other", "Client");
        Employee secondEmployee = await w.AddEmployee("Second");
        await w.CreateAppointment(SchedulingWorld.Future(10));

        AppointmentDto parallel = await w.CreateAppointment(SchedulingWorld.Future(10), client: other, employee: secondEmployee);

        Assert.Equal(AppointmentStatus.Scheduled, parallel.Status);
    }

    [Fact]
    public async Task ClientOverlap_AdjacentAppointmentsOfTheSameClientAreAllowed()
    {
        await using SchedulingWorld w = await SchedulingWorld.Create(nameof(ClientOverlap_AdjacentAppointmentsOfTheSameClientAreAllowed));
        Employee secondEmployee = await w.AddEmployee("Second");
        await w.CreateAppointment(SchedulingWorld.Future(10));

        AppointmentDto adjacent = await w.CreateAppointment(SchedulingWorld.Future(10, 30), employee: secondEmployee);

        Assert.Equal(AppointmentStatus.Scheduled, adjacent.Status);
    }

    [Fact]
    public async Task ClientOverlap_CancelledAppointmentDoesNotBlockTheClient()
    {
        await using SchedulingWorld w = await SchedulingWorld.Create(nameof(ClientOverlap_CancelledAppointmentDoesNotBlockTheClient));
        Employee secondEmployee = await w.AddEmployee("Second");
        AppointmentDto existing = await w.CreateAppointment(SchedulingWorld.Future(10));
        await w.Appointments.Cancel(w.OrganizationId, w.ActorUserId, true, existing.Id, new AppointmentCancelRequest { CancellationReason = "test" });

        AppointmentDto replacement = await w.CreateAppointment(SchedulingWorld.Future(10), employee: secondEmployee);

        Assert.Equal(AppointmentStatus.Scheduled, replacement.Status);
    }

    [Fact]
    public async Task ClientOverlap_ACancelledBookingOnAScheduledAppointmentDoesNotBlockTheClient()
    {
        await using SchedulingWorld w = await SchedulingWorld.Create(nameof(ClientOverlap_ACancelledBookingOnAScheduledAppointmentDoesNotBlockTheClient));
        Employee secondEmployee = await w.AddEmployee("Second");
        Client duoPartner = await w.AddClient("Partner", "Client");
        // Appointment stays Scheduled (partner still booked); only this client's Booking is Cancelled.
        await w.SeedAppointment(SchedulingWorld.Future(10),
            bookings: new[] { (w.Client, BookingStatus.Cancelled, 50m), (duoPartner, BookingStatus.Confirmed, 50m) });

        AppointmentDto other = await w.CreateAppointment(SchedulingWorld.Future(10), employee: secondEmployee);

        Assert.Equal(AppointmentStatus.Scheduled, other.Status);
    }

    [Fact]
    public async Task ClientOverlap_ANoShowBookingDoesNotBlockTheClient()
    {
        await using SchedulingWorld w = await SchedulingWorld.Create(nameof(ClientOverlap_ANoShowBookingDoesNotBlockTheClient));
        Employee secondEmployee = await w.AddEmployee("Second");
        Client duoPartner = await w.AddClient("Partner", "Client");
        await w.SeedAppointment(SchedulingWorld.Future(10),
            bookings: new[] { (w.Client, BookingStatus.NoShow, 50m), (duoPartner, BookingStatus.Confirmed, 50m) });

        AppointmentDto other = await w.CreateAppointment(SchedulingWorld.Future(10), employee: secondEmployee);

        Assert.Equal(AppointmentStatus.Scheduled, other.Status);
    }

    [Fact]
    public async Task ClientOverlap_ACompletedBookingStillBlocksTheClient()
    {
        await using SchedulingWorld w = await SchedulingWorld.Create(nameof(ClientOverlap_ACompletedBookingStillBlocksTheClient));
        Employee secondEmployee = await w.AddEmployee("Second");
        await w.SeedAppointment(SchedulingWorld.Future(10), AppointmentStatus.Closed, bookings: (w.Client, BookingStatus.Completed, 50m));

        await SchedulingAssert.BusinessRule(ErrorCodes.AppointmentOverlap,
            () => w.CreateAppointment(SchedulingWorld.Future(10), employee: secondEmployee));
    }

    [Fact]
    public async Task ClientOverlap_AnyClientOfAMultiClientRequestBlocksTheWholeRequest()
    {
        await using SchedulingWorld w = await SchedulingWorld.Create(nameof(ClientOverlap_AnyClientOfAMultiClientRequestBlocksTheWholeRequest));
        Client busy = await w.AddClient("Busy", "Client");
        Client free = await w.AddClient("Free", "Client");
        Employee secondEmployee = await w.AddEmployee("Second");
        await w.CreateAppointment(SchedulingWorld.Future(10), client: busy);

        await SchedulingAssert.BusinessRule(ErrorCodes.AppointmentOverlap,
            () => w.CreateAppointment(SchedulingWorld.Future(10), client: free, employee: secondEmployee, extraClients: busy));

        Assert.Equal(1, await w.CountAppointments());
    }

    #endregion

    #region D. Room overlap

    [Fact]
    public async Task RoomOverlap_ExclusiveRoom_OverlappingUseIsRejected()
    {
        await using SchedulingWorld w = await SchedulingWorld.Create(nameof(RoomOverlap_ExclusiveRoom_OverlappingUseIsRejected));
        Room room = await w.AddRoom();
        Client other = await w.AddClient("Other", "Client");
        Employee secondEmployee = await w.AddEmployee("Second");
        await w.CreateAppointment(SchedulingWorld.Future(10), room: room);

        // Different employee and client: only the Room rule can fire. CHANGED in M1D: the room rule is people capacity
        // (capacity 2 = one employee + one client), reported as ROOM_CAPACITY_EXCEEDED.
        BusinessRuleExceptionHolder holder = await BusinessRuleExceptionHolder.Capture(
            () => w.CreateAppointment(SchedulingWorld.Future(10, 15), client: other, employee: secondEmployee, room: room));

        Assert.Equal(ErrorCodes.RoomCapacityExceeded, holder.Code);
        Assert.Contains("prostorije", holder.Message);
    }

    [Fact]
    public async Task RoomOverlap_ExclusiveRoom_AdjacentUseIsAllowed()
    {
        await using SchedulingWorld w = await SchedulingWorld.Create(nameof(RoomOverlap_ExclusiveRoom_AdjacentUseIsAllowed));
        Room room = await w.AddRoom();
        Client other = await w.AddClient("Other", "Client");
        Employee secondEmployee = await w.AddEmployee("Second");
        await w.CreateAppointment(SchedulingWorld.Future(10), room: room);

        AppointmentDto adjacent = await w.CreateAppointment(SchedulingWorld.Future(10, 30), client: other, employee: secondEmployee, room: room);

        Assert.Equal(room.Id, adjacent.RoomId);
    }

    [Fact]
    public async Task RoomOverlap_ExclusiveRoom_CancelledAppointmentDoesNotBlock()
    {
        await using SchedulingWorld w = await SchedulingWorld.Create(nameof(RoomOverlap_ExclusiveRoom_CancelledAppointmentDoesNotBlock));
        Room room = await w.AddRoom();
        Client other = await w.AddClient("Other", "Client");
        Employee secondEmployee = await w.AddEmployee("Second");
        AppointmentDto existing = await w.CreateAppointment(SchedulingWorld.Future(10), room: room);
        await w.Appointments.Cancel(w.OrganizationId, w.ActorUserId, true, existing.Id, new AppointmentCancelRequest { CancellationReason = "test" });

        AppointmentDto reuse = await w.CreateAppointment(SchedulingWorld.Future(10), client: other, employee: secondEmployee, room: room);

        Assert.Equal(room.Id, reuse.RoomId);
    }

    [Fact]
    public async Task RoomOverlap_ExclusiveRoom_DifferentRoomsDoNotConflict()
    {
        await using SchedulingWorld w = await SchedulingWorld.Create(nameof(RoomOverlap_ExclusiveRoom_DifferentRoomsDoNotConflict));
        Room roomA = await w.AddRoom();
        Room roomB = await w.AddRoom();
        Client other = await w.AddClient("Other", "Client");
        Employee secondEmployee = await w.AddEmployee("Second");
        await w.CreateAppointment(SchedulingWorld.Future(10), room: roomA);

        AppointmentDto parallel = await w.CreateAppointment(SchedulingWorld.Future(10), client: other, employee: secondEmployee, room: roomB);

        Assert.Equal(roomB.Id, parallel.RoomId);
    }

    [Fact]
    public async Task RoomOverlap_ConcurrentRoom_OverlappingAppointmentsAreAllowedWithoutAnyNumericCapacity()
    {
        await using SchedulingWorld w = await SchedulingWorld.Create(nameof(RoomOverlap_ConcurrentRoom_OverlappingAppointmentsAreAllowedWithoutAnyNumericCapacity));
        Room room = await w.AddRoom(capacity: 50);

        // Three simultaneous appointments (distinct employees and clients) in one AllowConcurrentBookings room:
        // Room has no capacity number, so the flag means "unlimited", not "up to N".
        for (int i = 0; i < 3; i++)
        {
            Client client = await w.AddClient($"Concurrent{i}", "Client");
            Employee employee = await w.AddEmployee($"Concurrent{i}");
            AppointmentDto dto = await w.CreateAppointment(SchedulingWorld.Future(10), client: client, employee: employee, room: room);
            Assert.Equal(room.Id, dto.RoomId);
        }

        Assert.Equal(3, await w.CountAppointments(q => q.Where(a => a.Segments.Any(s => s.RoomId == room.Id))));
    }

    [Fact]
    public async Task RoomOverlap_ConcurrentRoom_StillAppliesTheEmployeeAndClientRules()
    {
        await using SchedulingWorld w = await SchedulingWorld.Create(nameof(RoomOverlap_ConcurrentRoom_StillAppliesTheEmployeeAndClientRules));
        Room room = await w.AddRoom(capacity: 50);
        Client other = await w.AddClient("Other", "Client");
        await w.CreateAppointment(SchedulingWorld.Future(10), room: room);

        // Same employee in a concurrent room is still a conflict — the flag only relaxes the ROOM rule.
        await SchedulingAssert.BusinessRule(ErrorCodes.AppointmentOverlap,
            () => w.CreateAppointment(SchedulingWorld.Future(10), client: other, room: room));
    }

    [Fact]
    public async Task RoomCapacity_IsAlsoEnforcedAgainstGroupOccurrences_CountingTheirPeople()
    {
        await using SchedulingWorld w = await SchedulingWorld.Create(nameof(RoomCapacity_IsAlsoEnforcedAgainstGroupOccurrences_CountingTheirPeople));
        Room room = await w.AddRoom();
        Client other = await w.AddClient("Other", "Client");
        Employee secondEmployee = await w.AddEmployee("Second");
        // CHANGED in M1D: a trainerless EMPTY occurrence holds zero people (it no longer blocks the whole room)...
        Appointment occurrence = await w.SeedAppointment(SchedulingWorld.Future(10), form: AppointmentForm.Group, employee: null, room: room);
        await w.CreateAppointment(SchedulingWorld.Future(10), client: other, employee: secondEmployee, room: room);

        // ...but its participants count: one confirmed member + the two people above exceed capacity 2.
        Room second = await w.AddRoom();
        Client member = await w.AddClient("Member", "Client");
        await w.SeedAppointment(SchedulingWorld.Future(12), form: AppointmentForm.Group, employee: null, room: second,
            bookings: (member, BookingStatus.Confirmed, 0m));
        await SchedulingAssert.BusinessRule(ErrorCodes.RoomCapacityExceeded,
            () => w.CreateAppointment(SchedulingWorld.Future(12), room: second));
        Assert.NotNull(occurrence.Id);
    }

    #endregion

    #region Persistence: what the database does NOT enforce

    [Fact]
    public async Task Database_HasNoExclusionConstraint_OverlappingAppointmentsForTheSameEmployeeAndRoomCanBeWrittenDirectly()
    {
        await using SchedulingWorld w = await SchedulingWorld.Create(nameof(Database_HasNoExclusionConstraint_OverlappingAppointmentsForTheSameEmployeeAndRoomCanBeWrittenDirectly));
        Room room = await w.AddRoom();
        Client other = await w.AddClient("Other", "Client");

        // Overlap protection is an APPLICATION rule (check, then write). Bypassing the service shows the schema itself would
        // accept a double-booked employee and an exclusively-used room — which is why concurrent Create/Update/Move requests
        // can, in principle, both pass the check.
        await w.SeedAppointment(SchedulingWorld.Future(10), room: room, bookings: (w.Client, BookingStatus.Confirmed, 50m));
        await w.SeedAppointment(SchedulingWorld.Future(10), room: room, bookings: (other, BookingStatus.Confirmed, 50m));

        Assert.Equal(2, await w.CountAppointments(q => q.Where(a => a.Segments.Any(s => s.RoomId == room.Id && s.Employees.Any(e => e.EmployeeId == w.Employee.Id)))));
    }

    [Fact]
    public async Task Database_DoesEnforce_OneBookingPerClientPerAppointment()
    {
        await using SchedulingWorld w = await SchedulingWorld.Create(nameof(Database_DoesEnforce_OneBookingPerClientPerAppointment));
        Appointment seeded = await w.SeedAppointment(SchedulingWorld.Future(10), bookings: (w.Client, BookingStatus.Confirmed, 50m));

        await using DatabaseContext db = w.NewDb();
        db.Bookings.Add(new Booking
        {
            Id = Guid.NewGuid(), OrganizationId = w.OrganizationId, AppointmentId = seeded.Id.Value, ClientId = w.Client.Id.Value,
            CreatedAt = DateTimeOffset.UtcNow
        });

        DbUpdateException ex = await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync());
        Assert.Equal("ux_bookings_appointment_client", Assert.IsType<PostgresException>(ex.InnerException).ConstraintName);
    }

    #endregion
}

/// <summary>Small helper to assert on the message as well as the code (used to tell WHICH overlap rule fired, since the
/// three rules share one error code).</summary>
internal sealed class BusinessRuleExceptionHolder
{
    public string Code { get; private init; }
    public string Message { get; private init; }

    public static async Task<BusinessRuleExceptionHolder> Capture(Func<Task> action)
    {
        Core.Shared.Exceptions.BusinessRuleException ex =
            await Assert.ThrowsAsync<Core.Shared.Exceptions.BusinessRuleException>(action);
        return new BusinessRuleExceptionHolder { Code = ex.Code, Message = ex.Message };
    }
}
