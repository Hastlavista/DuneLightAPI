#nullable disable
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;
using BlueDragon.DuneLight.Core.DTOs.Appointments;
using BlueDragon.DuneLight.Core.DTOs.Groups;
using BlueDragon.DuneLight.Core.Enums;
using BlueDragon.DuneLight.Core.Interfaces.Appointments;
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
using ServiceEntity = BlueDragon.DuneLight.Infrastructure.Domain.Models.Catalog.Service;

namespace BlueDragon.DuneLight.UnitTests.Scheduling;

/// <summary>
/// Phase M1D — the physical capacity invariants, both HARD (no override), both time-sliced over half-open intervals:
/// Room.Capacity = concurrent PEOPLE (all assigned employees + Confirmed/Completed participations of segments that reserve
/// their slot); Resource.Capacity = concurrent QuantityRequired of slot-reserving segments.
/// </summary>
public class RoomResourceCapacityTests
{
    private static readonly DateTimeOffset T9 = new(2031, 3, 3, 9, 0, 0, TimeSpan.Zero);

    private static CapacityClaim C(int startMinute, int endMinute, int amount) =>
        new(T9.AddMinutes(startMinute), T9.AddMinutes(endMinute), amount);

    private static CapacityEvaluation Eval(int capacity, CapacityClaim[] existing, params CapacityClaim[] proposed) =>
        IntervalCapacity.Evaluate(existing, proposed, capacity);

    private static ISchedulingOccupancyHandler Occupancy(SchedulingWorld w) => w.Resolve<ISchedulingOccupancyHandler>();

    private static Task<IUnitOfWork> Begin(SchedulingWorld w) => w.Resolve<IUnitOfWorkFactory>().Begin();

    private static async Task<int> PeopleIn(SchedulingWorld w, Room room, DateTimeOffset at) =>
        (await w.RoomUsage(room.Id.Value, at, 30)).Sum(c => c.Amount);

    private static async Task MarkExplicitlyCancelledInDb(SchedulingWorld w, Guid appointmentId)
    {
        await using DatabaseContext db = w.NewDb();
        Appointment a = await db.Appointments.SingleAsync(x => x.Id == appointmentId);
        a.CancelledAt = TestClock.UtcNow;
        await db.SaveChangesAsync();
    }

    /// <summary>A fresh employee + fresh client: isolates the room/resource rule from the employee/client invariants.</summary>
    private static async Task<(Employee Employee, Client Client)> FreshPair(SchedulingWorld w)
    {
        string tag = Guid.NewGuid().ToString("N")[..6];
        return (await w.AddEmployee("E" + tag), await w.AddClient("C" + tag));
    }

    private static async Task<AppointmentDto> CreateInRoom(SchedulingWorld w, Room room, DateTimeOffset start)
    {
        (Employee employee, Client client) = await FreshPair(w);
        return await w.CreateAppointment(start, client: client, employee: employee, room: room);
    }

    private static async Task<AppointmentDto> CreateWithResources(
        SchedulingWorld w, DateTimeOffset start, params (Resource Resource, int Quantity)[] resources)
    {
        (Employee employee, Client client) = await FreshPair(w);
        return await w.Appointments.Create(w.OrganizationId, w.ActorUserId, true, new AppointmentCreateRequest
        {
            CompanyId = w.Company.Id.Value,
            Segments = new List<AppointmentSegmentCreateRequest>
            {
                new()
                {
                    ServiceId = w.Service.Id.Value,
                    PlannedStart = start,
                    EmployeeIds = new List<Guid> { employee.Id.Value },
                    Participants = new List<AppointmentParticipantCreateRequest> { new() { ClientId = client.Id.Value } },
                    Resources = resources
                        .Select(r => new AppointmentSegmentResourceRequest { ResourceId = r.Resource.Id.Value, QuantityRequired = r.Quantity })
                        .ToList()
                }
            }
        });
    }

    #region Capacity engine (time-sliced, half-open)

    [Fact]
    public void Engine_AdjacentClaims_DoNotAdd_EndCeasesBeforeStartAtTheSameInstant()
    {
        Assert.False(Eval(4, new[] { C(0, 60, 4) }, C(60, 120, 4)).Exceeds);
        Assert.Equal(4, Eval(4, new[] { C(0, 60, 4) }, C(60, 120, 4)).PeakUsage);
    }

    [Theory]
    [InlineData(0, 60, 0, 60)]    // exact overlap
    [InlineData(0, 60, 30, 90)]   // partial overlap
    [InlineData(0, 120, 30, 60)]  // existing contains proposed
    [InlineData(30, 60, 0, 120)]  // proposed contains existing
    public void Engine_OverlapsAdd(int es, int ee, int ps, int pe)
    {
        CapacityEvaluation evaluation = Eval(5, new[] { C(es, ee, 3) }, C(ps, pe, 3));
        Assert.True(evaluation.Exceeds);
        Assert.Equal(6, evaluation.PeakUsage);
        Assert.False(Eval(6, new[] { C(es, ee, 3) }, C(ps, pe, 3)).Exceeds);
    }

    [Fact]
    public void Engine_Gaps_AndThreeOverlappingClaims()
    {
        // Gap: 09:00-09:30 and 10:00-10:30 never meet.
        Assert.False(Eval(3, new[] { C(0, 30, 3) }, C(60, 90, 3)).Exceeds);
        // Three claims overlapping in 09:20-09:30 only: peak = 2 + 2 + 2.
        CapacityEvaluation three = Eval(5, new[] { C(0, 30, 2), C(20, 60, 2) }, C(10, 40, 2));
        Assert.Equal(6, three.PeakUsage);
        Assert.Equal(T9.AddMinutes(20), three.PeakAt);
        Assert.True(three.Exceeds);
    }

    [Fact]
    public void Engine_ClaimsOverlappingTheCandidateButNotEachOther_AreNotNaivelySummed()
    {
        // Room capacity 10 — A 09:00-10:00 (6), B 10:00-11:00 (6), candidate 09:30-10:30 (4): 10 in each slice, allowed.
        CapacityEvaluation evaluation = Eval(10, new[] { C(0, 60, 6), C(60, 120, 6) }, C(30, 90, 4));
        Assert.False(evaluation.Exceeds);
        Assert.Equal(10, evaluation.PeakUsage);
        Assert.True(Eval(9, new[] { C(0, 60, 6), C(60, 120, 6) }, C(30, 90, 4)).Exceeds);
    }

    [Fact]
    public void Engine_OnlyTheProposedWindowsAreJudged_APreExistingOverloadElsewhereDoesNotBlock()
    {
        // Existing overload (5 > 4) at 09:00-10:00 — a proposal at 11:00 is unaffected; one touching 09:30 is refused.
        CapacityClaim[] existing = { C(0, 60, 3), C(0, 60, 2) };
        Assert.False(Eval(4, existing, C(120, 180, 4)).Exceeds);
        Assert.True(Eval(4, existing, C(30, 90, 1)).Exceeds);
    }

    [Fact]
    public void Engine_SeveralProposedSiblings_AreJudgedTogether_AndReportedIndividually()
    {
        CapacityEvaluation evaluation = Eval(4, Array.Empty<CapacityClaim>(), C(0, 60, 3), C(30, 90, 2), C(120, 180, 4));
        Assert.Equal(new[] { 0, 1 }, evaluation.ViolatingProposedIndexes);
    }

    #endregion

    #region Target state (proposed sibling segments, no database)

    private static SegmentClaim RoomClaim(Guid room, int startMinute, int endMinute, int employees, int clients) =>
        new(null, T9.AddMinutes(startMinute), T9.AddMinutes(endMinute), Array.Empty<Guid>(), Array.Empty<Guid>())
        {
            RoomId = room, RoomPeople = RoomPeopleCount.Of(employees, clients)
        };

    private static SegmentClaim ResourceClaimed(Guid resource, int startMinute, int endMinute, int quantity) =>
        new(null, T9.AddMinutes(startMinute), T9.AddMinutes(endMinute), Array.Empty<Guid>(), Array.Empty<Guid>())
        {
            Resources = new[] { new ResourceClaim(resource, quantity) }
        };

    [Fact]
    public void TargetState_RoomPeakBetweenSiblings_IsRejected_SequentialIsAllowed()
    {
        Guid room = Guid.NewGuid();
        Dictionary<Guid, int> rooms = new() { [room] = 8 };

        BusinessRuleException ex = Assert.Throws<BusinessRuleException>(() => SchedulingConflicts.EnsureTargetStateCapacity(
            new[] { RoomClaim(room, 0, 60, 1, 4), RoomClaim(room, 30, 90, 1, 3) }, rooms, new Dictionary<Guid, int>()));
        Assert.Equal(ErrorCodes.RoomCapacityExceeded, ex.Code);

        SchedulingConflicts.EnsureTargetStateCapacity(
            new[] { RoomClaim(room, 0, 60, 1, 4), RoomClaim(room, 60, 120, 1, 3) }, rooms, new Dictionary<Guid, int>());
    }

    [Fact]
    public void TargetState_ResourceBetweenSiblings_IsRejected_AdjacentIsAllowed()
    {
        Guid resource = Guid.NewGuid();
        Dictionary<Guid, int> resources = new() { [resource] = 4 };

        BusinessRuleException ex = Assert.Throws<BusinessRuleException>(() => SchedulingConflicts.EnsureTargetStateCapacity(
            new[] { ResourceClaimed(resource, 0, 60, 3), ResourceClaimed(resource, 30, 90, 2) }, new Dictionary<Guid, int>(), resources));
        Assert.Equal(ErrorCodes.ResourceCapacityExceeded, ex.Code);

        SchedulingConflicts.EnsureTargetStateCapacity(
            new[] { ResourceClaimed(resource, 0, 60, 4), ResourceClaimed(resource, 60, 120, 4) }, new Dictionary<Guid, int>(), resources);
    }

    [Fact]
    public void TargetState_FromConstructionCorePlans_CountsEveryEmployeeAndParticipant()
    {
        Guid room = Guid.NewGuid();
        SegmentPlan plan = new(Guid.NewGuid(), T9, T9.AddMinutes(60), new[] { Guid.NewGuid(), Guid.NewGuid() }, room,
            new[] { new ParticipantPlan(Guid.NewGuid(), BookingPricing.Zero), new ParticipantPlan(Guid.NewGuid(), BookingPricing.Zero) },
            new[] { new SegmentResourcePlan(Guid.NewGuid(), 2) });

        SegmentClaim claim = SegmentClaim.ForNew(plan);

        Assert.Equal(4, claim.RoomPeople); // two employees (never a hard-coded +1) + two clients
        Assert.Equal(2, Assert.Single(claim.Resources).Quantity);
    }

    [Fact]
    public async Task TargetState_TwoPersistedNeighboursThatDoNotOverlapEachOther_UseTheRealPeak()
    {
        await using SchedulingWorld w = await SchedulingWorld.Create(nameof(TargetState_TwoPersistedNeighboursThatDoNotOverlapEachOther_UseTheRealPeak));
        Room room = await w.AddRoom(capacity: 4);
        await CreateInRoom(w, room, SchedulingWorld.Future(10));        // 10:00-10:30, 2 people
        await CreateInRoom(w, room, SchedulingWorld.Future(10, 30));    // 10:30-11:00, 2 people

        // A 10:15 two-person segment meets each neighbour separately: peak 4, allowed (naive sum would be 6).
        await CreateInRoom(w, room, SchedulingWorld.Future(10, 15));
        // A further one at 10:15 would peak at 6.
        await SchedulingAssert.BusinessRule(ErrorCodes.RoomCapacityExceeded, () => CreateInRoom(w, room, SchedulingWorld.Future(10, 20)));
    }

    #endregion

    #region Room people count

    [Theory]
    [InlineData(BookingStatus.Confirmed, 2)]
    [InlineData(BookingStatus.Completed, 2)]
    [InlineData(BookingStatus.NoShow, 1)]
    [InlineData(BookingStatus.Cancelled, 1)]
    public async Task People_EmployeePlusOnlyConfirmedOrCompletedParticipations(BookingStatus status, int people)
    {
        await using SchedulingWorld w = await SchedulingWorld.Create($"{nameof(People_EmployeePlusOnlyConfirmedOrCompletedParticipations)}-{status}");
        Room room = await w.AddRoom();
        await w.SeedAppointment(SchedulingWorld.Future(10), room: room, bookings: (w.Client, status, 50m));

        Assert.Equal(people, await PeopleIn(w, room, SchedulingWorld.Future(10)));
    }

    [Fact]
    public async Task People_MixedStatuses_CountOnlyEmployeesAndOccupyingClients()
    {
        await using SchedulingWorld w = await SchedulingWorld.Create(nameof(People_MixedStatuses_CountOnlyEmployeesAndOccupyingClients));
        Room room = await w.AddRoom(capacity: 10);
        Client[] c = new Client[6];
        for (int i = 0; i < 6; i++)
            c[i] = await w.AddClient($"Mix{i}");
        await w.SeedAppointment(SchedulingWorld.Future(10), form: AppointmentForm.Group, employee: w.Employee, room: room, bookings: new[]
        {
            (c[0], BookingStatus.Confirmed, 0m), (c[1], BookingStatus.Confirmed, 0m), (c[2], BookingStatus.Confirmed, 0m),
            (c[3], BookingStatus.Completed, 0m), (c[4], BookingStatus.Cancelled, 0m), (c[5], BookingStatus.NoShow, 0m)
        });

        Assert.Equal(5, await PeopleIn(w, room, SchedulingWorld.Future(10))); // 1 employee + 3 Confirmed + 1 Completed
    }

    [Fact]
    public async Task People_EmptyScheduledSegment_AndAllClientsIndividuallyCancelled_StillHoldTheEmployee()
    {
        await using SchedulingWorld w = await SchedulingWorld.Create(nameof(People_EmptyScheduledSegment_AndAllClientsIndividuallyCancelled_StillHoldTheEmployee));
        Room room = await w.AddRoom();
        await w.SeedAppointment(SchedulingWorld.Future(10), form: AppointmentForm.Group, employee: w.Employee, room: room);
        AppointmentDto created = await w.CreateAppointment(SchedulingWorld.Future(12), room: room);
        await w.SetBookingStatus(created.Id, w.Client, BookingStatus.Cancelled, "client");

        Assert.Equal(1, await PeopleIn(w, room, SchedulingWorld.Future(10)));
        Assert.Equal(1, await PeopleIn(w, room, SchedulingWorld.Future(12)));
    }

    [Fact]
    public async Task People_ExplicitlyCancelledUnexecutedSegment_ContributesZero_AndFreesTheRoom()
    {
        await using SchedulingWorld w = await SchedulingWorld.Create(nameof(People_ExplicitlyCancelledUnexecutedSegment_ContributesZero_AndFreesTheRoom));
        Room room = await w.AddRoom();
        AppointmentDto created = await w.CreateAppointment(SchedulingWorld.Future(10), room: room);
        await SchedulingAssert.BusinessRule(ErrorCodes.RoomCapacityExceeded, () => CreateInRoom(w, room, SchedulingWorld.Future(10)));

        await w.Appointments.Cancel(w.OrganizationId, w.ActorUserId, true, created.Id, SchedulingWorld.BusinessCancel());

        Assert.Equal(0, await PeopleIn(w, room, SchedulingWorld.Future(10)));
        await CreateInRoom(w, room, SchedulingWorld.Future(10));
    }

    [Fact]
    public async Task People_HistoricalCompletedSegmentAfterAppointmentCancellation_StillCountsEmployeeAndCompleted_NotNoShow()
    {
        await using SchedulingWorld w = await SchedulingWorld.Create(nameof(People_HistoricalCompletedSegmentAfterAppointmentCancellation_StillCountsEmployeeAndCompleted_NotNoShow));
        Room room = await w.AddRoom(capacity: 10);
        Client a = await w.AddClient("A"), b = await w.AddClient("B"), c = await w.AddClient("C");
        Appointment seeded = await w.SeedAppointment(SchedulingWorld.Future(10), status: AppointmentStatus.Closed, form: AppointmentForm.Group,
            employee: w.Employee, room: room,
            bookings: new[] { (a, BookingStatus.Completed, 0m), (b, BookingStatus.Completed, 0m), (c, BookingStatus.NoShow, 0m) });
        await MarkExplicitlyCancelledInDb(w, seeded.Id.Value);

        Assert.Equal(3, await PeopleIn(w, room, SchedulingWorld.Future(10)));
    }

    [Fact]
    public async Task People_AnArtificialMultiEmployeeSegment_CountsEveryEmployee()
    {
        await using SchedulingWorld w = await SchedulingWorld.Create(nameof(People_AnArtificialMultiEmployeeSegment_CountsEveryEmployee));
        Room room = await w.AddRoom(capacity: 3);
        Employee second = await w.AddEmployee("Second");
        AppointmentDto created = await w.CreateAppointment(SchedulingWorld.Future(10), room: room);
        await using (DatabaseContext db = w.NewDb())
        {
            db.AppointmentSegmentEmployees.Add(new AppointmentSegmentEmployee { AppointmentSegmentId = created.Segments.Single().Id, EmployeeId = second.Id.Value });
            await db.SaveChangesAsync();
        }

        Assert.Equal(3, await PeopleIn(w, room, SchedulingWorld.Future(10)));
        // One more person on that segment would exceed capacity 3 (multi-employee AddBooking itself is still refused by
        // pricing attribution — MULTI_EMPLOYEE_NOT_SUPPORTED territory — so the capacity side is checked through the guard).
        Client extra = await w.AddClient("Extra");
        Appointment loaded = await w.LoadAppointment(created.Id);
        await using IUnitOfWork uow = await Begin(w);
        await SchedulingAssert.BusinessRule(ErrorCodes.RoomCapacityExceeded,
            () => SchedulingConflictGuard.ClaimParticipationActivation(Occupancy(w), uow, w.OrganizationId,
                SegmentClaim.ForParticipationActivation(loaded, loaded.Segments.Single(), extra.Id.Value, Array.Empty<ResourceClaim>())));
    }

    #endregion

    #region Room scheduling rule

    [Fact]
    public async Task Room_SameRoomParallelSegmentsAreSummed_AdjacentAreNot_DifferentRoomsAreIndependent()
    {
        await using SchedulingWorld w = await SchedulingWorld.Create(nameof(Room_SameRoomParallelSegmentsAreSummed_AdjacentAreNot_DifferentRoomsAreIndependent));
        Room room = await w.AddRoom(capacity: 4);
        Room other = await w.AddRoom(capacity: 2);

        await CreateInRoom(w, room, SchedulingWorld.Future(10));
        await CreateInRoom(w, room, SchedulingWorld.Future(10));                 // 4 people
        await SchedulingAssert.BusinessRule(ErrorCodes.RoomCapacityExceeded, () => CreateInRoom(w, room, SchedulingWorld.Future(10, 15)));
        await CreateInRoom(w, room, SchedulingWorld.Future(10, 30));             // adjacent
        await CreateInRoom(w, other, SchedulingWorld.Future(10));                // another room

        BusinessRuleException ex = await SchedulingAssert.BusinessRule(ErrorCodes.RoomCapacityExceeded, () => CreateInRoom(w, other, SchedulingWorld.Future(10)));
        Assert.NotNull(ex.Details);
    }

    [Fact]
    public async Task Room_StructuralRules_WrongCompanyAndInactive_AreHardErrors()
    {
        await using SchedulingWorld w = await SchedulingWorld.Create(nameof(Room_StructuralRules_WrongCompanyAndInactive_AreHardErrors));
        Company second = await w.AddCompany("Second");
        Room foreign = await w.AddRoom(second, capacity: 50);
        Room inactive = await w.AddRoom(isActive: false, capacity: 50);

        await SchedulingAssert.BusinessRule(ErrorCodes.RoomCompanyMismatch, () => w.CreateAppointment(SchedulingWorld.Future(10), room: foreign));
        await SchedulingAssert.BusinessRule(ErrorCodes.InactiveRoom, () => w.CreateAppointment(SchedulingWorld.Future(10), room: inactive));
    }

    [Fact]
    public async Task Room_SegmentTimeChange_ExcludesOnlyTheRewrittenSegment()
    {
        await using SchedulingWorld w = await SchedulingWorld.Create(nameof(Room_SegmentTimeChange_ExcludesOnlyTheRewrittenSegment));
        Room room = await w.AddRoom();
        AppointmentDto created = await w.CreateAppointment(SchedulingWorld.Future(10), room: room); // the room is full at 10:00
        await CreateInRoom(w, room, SchedulingWorld.Future(11));

        // Moving within its own old range does not count itself twice...
        await w.MoveOnlySegment(created.Id, SchedulingWorld.Future(10, 10));
        // ...but onto the other segment it would put four people in a two-person room.
        await SchedulingAssert.BusinessRule(ErrorCodes.RoomCapacityExceeded,
            () => w.MoveOnlySegment(created.Id, SchedulingWorld.Future(10, 50)));
    }

    #endregion

    #region Dynamic participation

    [Fact]
    public async Task AddClient_FillsTheLastPersonSlot_TheNextIsRejected()
    {
        await using SchedulingWorld w = await SchedulingWorld.Create(nameof(AddClient_FillsTheLastPersonSlot_TheNextIsRejected));
        Room room = await w.AddRoom(capacity: 3);
        AppointmentDto created = await w.CreateAppointment(SchedulingWorld.Future(10), room: room);
        Client second = await w.AddClient("Second"), third = await w.AddClient("Third");

        await w.AddClientToOnlySegment(created.Id, second);
        BusinessRuleException ex = await SchedulingAssert.BusinessRule(ErrorCodes.RoomCapacityExceeded,
            () => w.AddClientToOnlySegment(created.Id, third));

        Assert.Contains("3", ex.Message);
        Assert.Equal(3, await PeopleIn(w, room, SchedulingWorld.Future(10)));
    }

    [Fact]
    public async Task Guest_ConfirmedOrCompletedConsumesAPerson_NoShowDoesNot()
    {
        await using SchedulingWorld w = await SchedulingWorld.Create(nameof(Guest_ConfirmedOrCompletedConsumesAPerson_NoShowDoesNot));
        ServiceEntity svc = await w.AddGroupService();
        GroupDto group = await w.CreateGroup(svc, capacity: 5);
        Room room = await w.AddRoom(); // trainer + one member = full
        Client member = await w.AddClient("Member");
        Appointment occurrence = await w.SeedAppointment(SchedulingWorld.Past(10), form: AppointmentForm.Group, employee: w.Employee,
            service: svc, room: room, groupId: group.Id, bookings: (member, BookingStatus.Confirmed, 0m));
        Client guest = await w.AddClient("Guest");

        await SchedulingAssert.BusinessRule(ErrorCodes.RoomCapacityExceeded,
            () => w.SetBookingStatus(occurrence.Id.Value, guest, BookingStatus.Completed));
        await w.SetBookingStatus(occurrence.Id.Value, guest, BookingStatus.NoShow);
        Assert.Equal(2, await PeopleIn(w, room, SchedulingWorld.Past(10)));
    }

    [Fact]
    public async Task Correction_CancelledBackToConfirmed_RechecksRoomCapacity()
    {
        await using SchedulingWorld w = await SchedulingWorld.Create(nameof(Correction_CancelledBackToConfirmed_RechecksRoomCapacity));
        ServiceEntity svc = await w.AddGroupService();
        Room room = await w.AddRoom(); // 2 people
        GroupDto group = await w.CreateGroup(svc, capacity: 5, room: room);
        await w.AddGroupMember(group, w.Client);
        Appointment occurrence = await w.GenerateSingleOccurrence(group); // trainer + member = 2
        await w.SetBookingStatus(occurrence.Id.Value, w.Client, BookingStatus.Cancelled, "client"); // 1
        Client guest = await w.AddClient("Guest");
        await w.AddGuest(occurrence, guest); // 2 again

        await SchedulingAssert.BusinessRule(ErrorCodes.RoomCapacityExceeded,
            () => w.SetBookingStatus(occurrence.Id.Value, w.Client, BookingStatus.Confirmed));
        Assert.Equal(ParticipationStatus.Cancelled, Assert.Single(await w.LoadParticipations(occurrence.Id.Value, w.Client)).Status);
    }

    [Fact]
    public async Task Reopen_OfAnExplicitlyCancelledOccurrence_RechecksEveryInvariantAtomically()
    {
        await using SchedulingWorld w = await SchedulingWorld.Create(nameof(Reopen_OfAnExplicitlyCancelledOccurrence_RechecksEveryInvariantAtomically));
        ServiceEntity svc = await w.AddGroupService();
        Room room = await w.AddRoom(); // 2 people
        GroupDto group = await w.CreateGroup(svc, capacity: 5, room: room);
        await w.AddGroupMember(group, w.Client);
        Appointment occurrence = await w.GenerateSingleOccurrence(group);
        await w.Appointments.Cancel(w.OrganizationId, w.ActorUserId, true, occurrence.Id.Value, SchedulingWorld.BusinessCancel());

        // The cancelled occurrence released trainer, client and room — another session takes the room.
        await CreateInRoom(w, room, SchedulingWorld.Future(10));

        // Re-confirming the member would reactivate the segment (trainer + member = 2 more people): refused, nothing applied.
        await SchedulingAssert.BusinessRule(ErrorCodes.RoomCapacityExceeded,
            () => w.SetBookingStatus(occurrence.Id.Value, w.Client, BookingStatus.Confirmed));
        Appointment after = await w.LoadAppointment(occurrence.Id.Value);
        Assert.Equal(AppointmentStatus.Cancelled, after.Status);
        Assert.NotNull(after.CancelledAt);
        Assert.Equal(ParticipationStatus.Cancelled, Assert.Single(await w.LoadParticipations(occurrence.Id.Value, w.Client)).Status);
    }

    [Fact]
    public async Task WaitlistPromotion_RespectsRoomCapacity_SeparatelyFromGroupCapacity()
    {
        await using SchedulingWorld w = await SchedulingWorld.Create(nameof(WaitlistPromotion_RespectsRoomCapacity_SeparatelyFromGroupCapacity));
        ServiceEntity svc = await w.AddGroupService();
        Room room = await w.AddRoom(capacity: 3); // trainer + 2 members
        GroupDto group = await w.CreateGroup(svc, capacity: 2, room: room);
        Client second = await w.AddClient("Second");
        await w.AddGroupMember(group, w.Client);
        await w.AddGroupMember(group, second);
        Appointment occurrence = await w.GenerateSingleOccurrence(group);
        Client waiter = await w.AddClient("Waiter");
        await w.Waitlist.Join(w.OrganizationId, w.ActorUserId, true, occurrence.Id.Value,
            new WaitlistJoinRequest { ClientId = waiter.Id.Value, SegmentId = Assert.Single(occurrence.Segments).Id });

        // The room shrinks to 2 people (catalog edit): after one member cancels, the business seat is free but the room is not.
        await using (DatabaseContext db = w.NewDb())
        {
            Room tracked = await db.Rooms.SingleAsync(r => r.Id == room.Id);
            tracked.Capacity = 2;
            await db.SaveChangesAsync();
        }
        await w.SetBookingStatus(occurrence.Id.Value, second, BookingStatus.Cancelled, "client");

        Assert.Equal(WaitlistEntryStatus.Waiting, Assert.Single(await w.LoadWaitlist(occurrence.Id.Value)).Status);
        Assert.Equal(2, await PeopleIn(w, room, SchedulingWorld.Future(10)));
    }

    #endregion

    #region Resources

    [Fact]
    public async Task Resource_Capacity3_Quantity2PlusOverlapping1_IsAllowed_2Plus2_IsRejected_Adjacent3Plus3_IsAllowed()
    {
        await using SchedulingWorld w = await SchedulingWorld.Create(nameof(Resource_Capacity3_Quantity2PlusOverlapping1_IsAllowed_2Plus2_IsRejected_Adjacent3Plus3_IsAllowed));
        Resource tables = await w.AddResource(capacity: 3, name: "Tables");

        AppointmentDto first = await CreateWithResources(w, SchedulingWorld.Future(10), (tables, 2));
        await CreateWithResources(w, SchedulingWorld.Future(10, 15), (tables, 1));
        BusinessRuleException ex = await SchedulingAssert.BusinessRule(ErrorCodes.ResourceCapacityExceeded,
            () => CreateWithResources(w, SchedulingWorld.Future(10, 20), (tables, 1)));
        Assert.Contains("Tables", ex.Message);

        await CreateWithResources(w, SchedulingWorld.Future(12), (tables, 3));
        await CreateWithResources(w, SchedulingWorld.Future(12, 30), (tables, 3));

        AppointmentSegmentResourceDto read = Assert.Single(Assert.Single(first.Segments).Resources);
        Assert.Equal((tables.Id.Value, "Tables", 2), (read.ResourceId, read.ResourceName, read.QuantityRequired));
    }

    [Fact]
    public async Task Resource_2Plus2OnCapacity3_IsRejected()
    {
        await using SchedulingWorld w = await SchedulingWorld.Create(nameof(Resource_2Plus2OnCapacity3_IsRejected));
        Resource tables = await w.AddResource(capacity: 3);
        await CreateWithResources(w, SchedulingWorld.Future(10), (tables, 2));

        await SchedulingAssert.BusinessRule(ErrorCodes.ResourceCapacityExceeded, () => CreateWithResources(w, SchedulingWorld.Future(10), (tables, 2)));
        Assert.Equal(1, await w.CountAppointments());
    }

    [Fact]
    public async Task Resource_DifferentResourcesAreIndependent_SeveralOnOneSegmentAreEachChecked()
    {
        await using SchedulingWorld w = await SchedulingWorld.Create(nameof(Resource_DifferentResourcesAreIndependent_SeveralOnOneSegmentAreEachChecked));
        Resource bikes = await w.AddResource(capacity: 2), mats = await w.AddResource(capacity: 5);

        await CreateWithResources(w, SchedulingWorld.Future(10), (bikes, 2), (mats, 1));
        await CreateWithResources(w, SchedulingWorld.Future(10), (mats, 4));
        await SchedulingAssert.BusinessRule(ErrorCodes.ResourceCapacityExceeded,
            () => CreateWithResources(w, SchedulingWorld.Future(10), (mats, 0 + 1), (bikes, 1)));
    }

    [Fact]
    public async Task Resource_StructuralAndValidationRules()
    {
        await using SchedulingWorld w = await SchedulingWorld.Create(nameof(Resource_StructuralAndValidationRules));
        Resource ok = await w.AddResource(capacity: 5);
        Resource inactive = await w.AddResource(capacity: 5, isActive: false);
        Resource foreign = await w.AddResource(await w.AddCompany("Second"), capacity: 5);

        await SchedulingAssert.Validation(() => CreateWithResources(w, SchedulingWorld.Future(10), (ok, 0)));
        await SchedulingAssert.Validation(() => CreateWithResources(w, SchedulingWorld.Future(10), (ok, -1)));
        await SchedulingAssert.Validation(() => CreateWithResources(w, SchedulingWorld.Future(10), (ok, 1), (ok, 1))); // duplicates are not merged
        await SchedulingAssert.BusinessRule(ErrorCodes.InactiveResource, () => CreateWithResources(w, SchedulingWorld.Future(10), (inactive, 1)));
        await SchedulingAssert.BusinessRule(ErrorCodes.ResourceCompanyMismatch, () => CreateWithResources(w, SchedulingWorld.Future(10), (foreign, 1)));
        Assert.Equal(0, await w.CountAppointments());
    }

    [Fact]
    public async Task Resource_ExplicitCancellationReleasesUnexecutedOccupancy_HistoricalOccupancyIsKept()
    {
        await using SchedulingWorld w = await SchedulingWorld.Create(nameof(Resource_ExplicitCancellationReleasesUnexecutedOccupancy_HistoricalOccupancyIsKept));
        Resource tables = await w.AddResource(capacity: 2);
        AppointmentDto unexecuted = await CreateWithResources(w, SchedulingWorld.Future(10), (tables, 2));
        AppointmentDto executed = await CreateWithResources(w, SchedulingWorld.Future(12), (tables, 2));

        await w.Appointments.Cancel(w.OrganizationId, w.ActorUserId, true, unexecuted.Id, SchedulingWorld.BusinessCancel());
        await CreateWithResources(w, SchedulingWorld.Future(10), (tables, 2));

        await using (DatabaseContext db = w.NewDb())
        {
            BookingSegmentParticipation p = await db.BookingSegmentParticipations.SingleAsync(x => x.AppointmentSegmentId == executed.Segments.Single().Id);
            p.Status = ParticipationStatus.Completed;
            await db.SaveChangesAsync();
        }
        await MarkExplicitlyCancelledInDb(w, executed.Id);
        await SchedulingAssert.BusinessRule(ErrorCodes.ResourceCapacityExceeded, () => CreateWithResources(w, SchedulingWorld.Future(12), (tables, 1)));
        Assert.Equal(2, (await w.ResourceUsage(tables.Id.Value, SchedulingWorld.Future(12), 30)).Sum(c => c.Amount));
    }

    [Fact]
    public async Task Resource_MovingASegment_CarriesItsResourcesIntoTheCheck()
    {
        await using SchedulingWorld w = await SchedulingWorld.Create(nameof(Resource_MovingASegment_CarriesItsResourcesIntoTheCheck));
        Resource tables = await w.AddResource(capacity: 2);
        AppointmentDto mover = await CreateWithResources(w, SchedulingWorld.Future(10), (tables, 2));
        await CreateWithResources(w, SchedulingWorld.Future(12), (tables, 1));

        await w.MoveOnlySegment(mover.Id, SchedulingWorld.Future(10, 15));
        await SchedulingAssert.BusinessRule(ErrorCodes.ResourceCapacityExceeded,
            () => w.MoveOnlySegment(mover.Id, SchedulingWorld.Future(12)));
    }

    #endregion

    #region Lock keys

    [Fact]
    public void LockKeys_RoomsAndResourcesHaveTheirOwnNamespaces_AfterEmployeesAndClients()
    {
        Guid id = Guid.NewGuid();
        IReadOnlyList<long> keys = SchedulingLockOrder.Keys(new[] { id }, new[] { id }, new[] { id }, new[] { id });

        Assert.Equal(4, keys.Count);
        Assert.Equal(
            new[] { SchedulingLockOrder.EmployeeKey(id), SchedulingLockOrder.ClientKey(id), SchedulingLockOrder.RoomKey(id), SchedulingLockOrder.ResourceKey(id) },
            keys);
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

    private static async Task Gate(SchedulingWorld w, long key, Guid? room, Guid? resource, Func<Task<Exception[]>> start, Action<Exception[]> verify)
    {
        Task<Exception[]> race;
        await using (IUnitOfWork gate = await Begin(w))
        {
            await Occupancy(w).LockSchedulingSubjects(gate, null, null, room.HasValue ? new[] { room.Value } : null, resource.HasValue ? new[] { resource.Value } : null);
            race = start();
            Stopwatch sw = Stopwatch.StartNew();
            while (await WaitersOn(w, key) < 2)
            {
                Assert.True(sw.Elapsed < TimeSpan.FromSeconds(30), "Both requests should be waiting on the lock.");
                await Task.Delay(20);
            }
            await gate.CommitAsync();
        }

        verify(await race);
    }

    private static void ExactlyOneWinner(Exception[] outcomes, string code)
    {
        Assert.Single(outcomes, o => o == null);
        Assert.Equal(code, Assert.IsType<BusinessRuleException>(Assert.Single(outcomes, o => o != null)).Code);
    }

    [Fact]
    public async Task Race_TwoRequestsForTheLastRoomPerson_ExactlyOneCommits()
    {
        await using SchedulingWorld w = await SchedulingWorld.Create(nameof(Race_TwoRequestsForTheLastRoomPerson_ExactlyOneCommits));
        Room room = await w.AddRoom(capacity: 3);
        AppointmentDto created = await w.CreateAppointment(SchedulingWorld.Future(10), room: room);
        Client a = await w.AddClient("A"), b = await w.AddClient("B");
        AppointmentClientAddRequest Join(Client c) => new()
        {
            ClientId = c.Id.Value,
            Participations = new List<AppointmentClientParticipationRequest> { new() { SegmentId = created.Segments[0].Id } }
        };

        await Gate(w, SchedulingLockOrder.RoomKey(room.Id.Value), room.Id, null,
            () => Task.WhenAll(
                Task.Run(() => InOwnScope(sp => sp.GetRequiredService<IAppointmentService>().AddClient(
                    w.OrganizationId, w.ActorUserId, true, created.Id, Join(a)))),
                Task.Run(() => InOwnScope(sp => sp.GetRequiredService<IAppointmentService>().AddClient(
                    w.OrganizationId, w.ActorUserId, true, created.Id, Join(b))))),
            outcomes => ExactlyOneWinner(outcomes, ErrorCodes.RoomCapacityExceeded));

        Assert.Equal(3, await PeopleIn(w, room, SchedulingWorld.Future(10)));
    }

    [Fact]
    public async Task Race_TwoSegmentsForTheLastResourceQuantity_ExactlyOneCommits()
    {
        await using SchedulingWorld w = await SchedulingWorld.Create(nameof(Race_TwoSegmentsForTheLastResourceQuantity_ExactlyOneCommits));
        Resource tables = await w.AddResource(capacity: 3);
        await CreateWithResources(w, SchedulingWorld.Future(10), (tables, 2));

        await Gate(w, SchedulingLockOrder.ResourceKey(tables.Id.Value), null, tables.Id,
            () => Task.WhenAll(
                Task.Run(() => InOwnScope(_ => CreateWithResources(w, SchedulingWorld.Future(10), (tables, 1)))),
                Task.Run(() => InOwnScope(_ => CreateWithResources(w, SchedulingWorld.Future(10, 10), (tables, 1))))),
            outcomes => ExactlyOneWinner(outcomes, ErrorCodes.ResourceCapacityExceeded));

        Assert.Equal(2, await w.CountAppointments());
    }

    [Fact]
    public async Task Race_UngatedRoomAndResourceRequests_AreStableOverManyRounds()
    {
        await using SchedulingWorld w = await SchedulingWorld.Create(nameof(Race_UngatedRoomAndResourceRequests_AreStableOverManyRounds));
        Room room = await w.AddRoom(capacity: 2);
        Resource tables = await w.AddResource(capacity: 1);
        const int rounds = 8;
        int created = 0;

        for (int round = 0; round < rounds; round++)
        {
            DateTimeOffset start = SchedulingWorld.Future(8).AddDays(7 * round);
            List<Task<Exception>> attempts = new();
            for (int i = 0; i < 3; i++)
            {
                (Employee employee, Client client) = await FreshPair(w);
                TestAppointmentSpec roomRequest = w.CreateRequest(start, client: client, employee: employee, room: room);
                attempts.Add(Task.Run(() => InOwnScope(sp => sp.GetRequiredService<IAppointmentService>().Create(w.OrganizationId, w.ActorUserId, true, roomRequest.ToTarget()))));
            }
            for (int i = 0; i < 3; i++)
                attempts.Add(Task.Run(() => InOwnScope(_ => CreateWithResources(w, start.AddHours(4), (tables, 1)))));

            Exception[] outcomes = await Task.WhenAll(attempts);
            Assert.Equal(1, outcomes.Take(3).Count(o => o == null));
            Assert.Equal(1, outcomes.Skip(3).Count(o => o == null));
            Assert.All(outcomes.Where(o => o != null), o => Assert.Contains(
                Assert.IsType<BusinessRuleException>(o).Code, new[] { ErrorCodes.RoomCapacityExceeded, ErrorCodes.ResourceCapacityExceeded }));
            created += 2;
        }

        Assert.Equal(created, await w.CountAppointments());
    }

    [Fact]
    public async Task Concurrency_DifferentRoomsAndResources_DoNotBlockEachOther()
    {
        await using SchedulingWorld w = await SchedulingWorld.Create(nameof(Concurrency_DifferentRoomsAndResources_DoNotBlockEachOther));
        Room busyRoom = await w.AddRoom(capacity: 10), freeRoom = await w.AddRoom(capacity: 10);
        Resource busyResource = await w.AddResource(capacity: 5), freeResource = await w.AddResource(capacity: 5);

        await using IUnitOfWork holder = await Begin(w);
        await Occupancy(w).LockSchedulingSubjects(holder, null, null, new[] { busyRoom.Id.Value }, new[] { busyResource.Id.Value });

        Task<Exception> inOtherRoom = Task.Run(() => InOwnScope(_ => CreateInRoom(w, freeRoom, SchedulingWorld.Future(10))));
        Task<Exception> withOtherResource = Task.Run(() => InOwnScope(_ => CreateWithResources(w, SchedulingWorld.Future(10), (freeResource, 1))));
        Task both = Task.WhenAll(inOtherRoom, withOtherResource);
        Assert.Same(both, await Task.WhenAny(both, Task.Delay(TimeSpan.FromSeconds(20))));
        Assert.Null(await inOtherRoom);
        Assert.Null(await withOtherResource);
    }

    #endregion
}
