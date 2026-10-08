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
/// Phase M1C — the two hard scheduling invariants, segment-scoped and concurrency-safe:
/// (1) the same Employee cannot be assigned to overlapping occupying Segments;
/// (2) the same Client cannot have overlapping occupying Participations —
/// between appointments, between sibling segments of one appointment and across companies; adjacent ranges are allowed.
/// Multi-segment data here is seeded ARTIFICIALLY (written before M1E enabled production multi-segment creation; see
/// MultiSegmentAppointmentTests for the production path).
/// </summary>
public class SegmentConflictInvariantTests
{
    private static readonly DateTimeOffset T9 = new(2031, 3, 3, 9, 0, 0, TimeSpan.Zero);

    private static ISchedulingOccupancyHandler Occupancy(SchedulingWorld w) => w.Resolve<ISchedulingOccupancyHandler>();

    private static Task<IUnitOfWork> Begin(SchedulingWorld w) => w.Resolve<IUnitOfWorkFactory>().Begin();

    private static async Task Claim(SchedulingWorld w, params SegmentClaim[] claims)
    {
        await using IUnitOfWork uow = await Begin(w);
        await SchedulingConflictGuard.Claim(Occupancy(w), uow, w.OrganizationId, claims);
    }

    private static SegmentClaim New(DateTimeOffset start, int minutes, Guid[] employees = null, Guid[] clients = null, Guid? segmentId = null) =>
        new(segmentId, start, start.AddMinutes(minutes), employees ?? Array.Empty<Guid>(), clients ?? Array.Empty<Guid>());

    private static async Task SetParticipationStatusInDb(SchedulingWorld w, Guid participationId, ParticipationStatus status)
    {
        await using DatabaseContext db = w.NewDb();
        BookingSegmentParticipation p = await db.BookingSegmentParticipations.SingleAsync(x => x.Id == participationId);
        p.Status = status;
        await db.SaveChangesAsync();
    }

    private static async Task MarkExplicitlyCancelledInDb(SchedulingWorld w, Guid appointmentId)
    {
        await using DatabaseContext db = w.NewDb();
        Appointment a = await db.Appointments.SingleAsync(x => x.Id == appointmentId);
        a.CancelledAt = DateTimeOffset.UtcNow;
        await db.SaveChangesAsync();
    }

    /// <summary>A fresh client and a create request for the default employee — isolates the EMPLOYEE invariant.</summary>
    private static async Task<TestAppointmentSpec> EmployeeProbe(SchedulingWorld w, DateTimeOffset start)
    {
        Client fresh = await w.AddClient("Probe", Guid.NewGuid().ToString("N")[..6]);
        return w.CreateRequest(start, client: fresh);
    }

    /// <summary>A fresh employee and a create request for the default client — isolates the CLIENT invariant.</summary>
    private static async Task<TestAppointmentSpec> ClientProbe(SchedulingWorld w, DateTimeOffset start)
    {
        Employee fresh = await w.AddEmployee("Probe" + Guid.NewGuid().ToString("N")[..6]);
        return w.CreateRequest(start, employee: fresh);
    }

    #region Intervals (half-open, one predicate)

    [Theory]
    [InlineData(9, 0, 10, 0, 10, 0, 11, 0, false)]   // adjacent after
    [InlineData(10, 0, 11, 0, 9, 0, 10, 0, false)]   // adjacent before
    [InlineData(9, 0, 10, 0, 9, 59, 11, 0, true)]    // one minute of overlap
    [InlineData(9, 0, 10, 0, 9, 0, 10, 0, true)]     // same exact range
    [InlineData(9, 0, 12, 0, 10, 0, 11, 0, true)]    // A contains B
    [InlineData(10, 0, 11, 0, 9, 0, 12, 0, true)]    // B contains A
    public void Interval_IsHalfOpen(int ah, int am, int aeh, int aem, int bh, int bm, int beh, int bem, bool overlaps)
    {
        DateTimeOffset day = T9.Date;
        DateTimeOffset aStart = day.AddHours(ah).AddMinutes(am), aEnd = day.AddHours(aeh).AddMinutes(aem);
        DateTimeOffset bStart = day.AddHours(bh).AddMinutes(bm), bEnd = day.AddHours(beh).AddMinutes(bem);

        Assert.Equal(overlaps, SchedulingInterval.Overlaps(aStart, aEnd, bStart, bEnd));
        Assert.Equal(overlaps, SchedulingInterval.Overlaps(bStart, bEnd, aStart, aEnd));
        Assert.Equal(overlaps, New(aStart, (int)(aEnd - aStart).TotalMinutes).Overlaps(New(bStart, (int)(bEnd - bStart).TotalMinutes)));
        // The EF predicate is the same inequality.
        AppointmentSegment segment = new() { PlannedStart = aStart, PlannedEnd = aEnd };
        Assert.Equal(overlaps, SchedulingInterval.SegmentOverlaps(bStart, bEnd).Compile()(segment));
    }

    [Fact]
    public async Task Interval_AdjacentIsAllowed_OneMinuteOverlapIsBlocked_ThroughTheRealCreate()
    {
        await using SchedulingWorld w = await SchedulingWorld.Create(nameof(Interval_AdjacentIsAllowed_OneMinuteOverlapIsBlocked_ThroughTheRealCreate));
        await w.CreateAppointment(SchedulingWorld.Future(9)); // 09:00-09:30

        await w.CreateAppointment(await EmployeeProbe(w, SchedulingWorld.Future(9, 30)));
        await SchedulingAssert.BusinessRule(ErrorCodes.AppointmentOverlap,
            async () => await w.CreateAppointment(await EmployeeProbe(w, SchedulingWorld.Future(9, 29))));
        await SchedulingAssert.BusinessRule(ErrorCodes.AppointmentOverlap,
            async () => await w.CreateAppointment(await EmployeeProbe(w, SchedulingWorld.Future(9))));
    }

    #endregion

    #region Internal target-state validation (proposed segments, before persistence)

    [Fact]
    public void TargetState_SameEmployeeOnOverlappingProposedSegments_IsRejected()
    {
        Guid x = Guid.NewGuid();
        BusinessRuleException ex = Assert.Throws<BusinessRuleException>(() => SchedulingConflicts.EnsureTargetStateConsistent(new[]
        {
            New(T9, 60, employees: new[] { x }),
            New(T9.AddMinutes(30), 60, employees: new[] { x })
        }));
        Assert.Equal(ErrorCodes.AppointmentOverlap, ex.Code);
    }

    [Fact]
    public void TargetState_OneBookingWithParticipationsOnOverlappingSiblingSegments_IsRejected()
    {
        Guid client = Guid.NewGuid();
        Appointment proposed = AppointmentFactory.CreateIndividual(
            Guid.NewGuid(), Guid.NewGuid(), null, null, Guid.NewGuid(), DateTimeOffset.UtcNow,
            new[]
            {
                new SegmentPlan(Guid.NewGuid(), T9, T9.AddMinutes(60), new[] { Guid.NewGuid() }, null, new[] { new ParticipantPlan(client, BookingPricing.Zero) }),
                new SegmentPlan(Guid.NewGuid(), T9.AddMinutes(30), T9.AddMinutes(90), new[] { Guid.NewGuid() }, null, new[] { new ParticipantPlan(client, BookingPricing.Zero) })
            });
        Assert.Equal(2, Assert.Single(proposed.Bookings).Participations.Count); // the shape the core would persist

        BusinessRuleException ex = Assert.Throws<BusinessRuleException>(() => SchedulingConflicts.EnsureTargetStateConsistent(
            proposed.Segments.Select(s => new SegmentClaim(null, s.PlannedStart, s.PlannedEnd,
                s.Employees.Select(e => e.EmployeeId).ToList(), new[] { client })).ToList()));
        Assert.Equal(ErrorCodes.AppointmentOverlap, ex.Code);
    }

    [Fact]
    public void TargetState_SequentialSegmentsForTheSameSubjects_AndParallelSegmentsForDifferentSubjects_AreAllowed()
    {
        Guid x = Guid.NewGuid(), y = Guid.NewGuid(), c1 = Guid.NewGuid(), c2 = Guid.NewGuid();
        SegmentPlan first = new(Guid.NewGuid(), T9, T9.AddMinutes(60), new[] { x }, null, new[] { new ParticipantPlan(c1, BookingPricing.Zero) });
        SegmentPlan sequential = new(Guid.NewGuid(), T9.AddMinutes(60), T9.AddMinutes(90), new[] { x }, null, new[] { new ParticipantPlan(c1, BookingPricing.Zero) });
        SegmentPlan parallel = new(Guid.NewGuid(), T9, T9.AddMinutes(60), new[] { y }, null, new[] { new ParticipantPlan(c2, BookingPricing.Zero) });

        SchedulingConflicts.EnsureTargetStateConsistent(new[] { first, sequential, parallel }.Select(SegmentClaim.ForNew).ToList());
    }

    [Fact]
    public void TargetState_SameClientOnOverlappingProposedSegments_IsRejected_EvenWithDifferentEmployees()
    {
        Guid client = Guid.NewGuid();
        SegmentPlan a = new(Guid.NewGuid(), T9, T9.AddMinutes(60), new[] { Guid.NewGuid() }, null, new[] { new ParticipantPlan(client, BookingPricing.Zero) });
        SegmentPlan b = new(Guid.NewGuid(), T9.AddMinutes(59), T9.AddMinutes(120), new[] { Guid.NewGuid() }, null, new[] { new ParticipantPlan(client, BookingPricing.Zero) });

        Assert.Throws<BusinessRuleException>(() => SchedulingConflicts.EnsureTargetStateConsistent(new[] { a, b }.Select(SegmentClaim.ForNew).ToList()));
    }

    [Fact]
    public async Task TargetState_IsCheckedBeforeTheDatabase_TwoNewOverlappingSegmentsConflictEvenWithAnEmptySchedule()
    {
        await using SchedulingWorld w = await SchedulingWorld.Create(nameof(TargetState_IsCheckedBeforeTheDatabase_TwoNewOverlappingSegmentsConflictEvenWithAnEmptySchedule));
        Guid x = w.Employee.Id.Value;

        await SchedulingAssert.BusinessRule(ErrorCodes.AppointmentOverlap,
            () => Claim(w, New(SchedulingWorld.Future(9), 60, employees: new[] { x }), New(SchedulingWorld.Future(9, 30), 60, employees: new[] { x })));
        await Claim(w, New(SchedulingWorld.Future(9), 60, employees: new[] { x }), New(SchedulingWorld.Future(10), 60, employees: new[] { x }));
    }

    #endregion

    #region Lock order

    [Fact]
    public void LockKeys_AreDeterministic_Deduplicated_Sorted_EmployeesBeforeClients()
    {
        Guid e1 = Guid.NewGuid(), e2 = Guid.NewGuid(), c1 = Guid.NewGuid(), c2 = Guid.NewGuid();

        IReadOnlyList<long> keys = SchedulingLockOrder.Keys(new[] { e2, e1, e2 }, new[] { c2, c1, c1 });

        Assert.Equal(4, keys.Count);
        Assert.Equal(keys.OrderBy(k => k), keys);
        Assert.All(keys.Take(2), k => Assert.Contains(k, new[] { SchedulingLockOrder.EmployeeKey(e1), SchedulingLockOrder.EmployeeKey(e2) }));
        Assert.All(keys.Skip(2), k => Assert.Contains(k, new[] { SchedulingLockOrder.ClientKey(c1), SchedulingLockOrder.ClientKey(c2) }));
        Assert.Equal(keys, SchedulingLockOrder.Keys(new[] { e1, e2 }, new[] { c1, c2 })); // input order never matters
        Assert.NotEqual(SchedulingLockOrder.EmployeeKey(e1), SchedulingLockOrder.ClientKey(e1)); // the subject kind is part of the key
    }

    #endregion

    #region Employee invariant

    [Fact]
    public async Task Employee_DifferentAppointments_Overlap_IsBlocked()
    {
        await using SchedulingWorld w = await SchedulingWorld.Create(nameof(Employee_DifferentAppointments_Overlap_IsBlocked));
        await w.CreateAppointment(SchedulingWorld.Future(10));

        await SchedulingAssert.BusinessRule(ErrorCodes.AppointmentOverlap,
            async () => await w.CreateAppointment(await EmployeeProbe(w, SchedulingWorld.Future(10, 15))));
        Assert.Equal(1, await w.CountAppointments());
    }

    [Fact]
    public async Task Employee_SiblingSegmentOfTheSameAppointment_Overlap_IsBlocked()
    {
        await using SchedulingWorld w = await SchedulingWorld.Create(nameof(Employee_SiblingSegmentOfTheSameAppointment_Overlap_IsBlocked));
        AppointmentDto created = await w.CreateAppointment(SchedulingWorld.Future(10));
        await w.AddArtificialSegmentParticipation(created.Id, w.Client, SchedulingWorld.Future(11), 10m); // sibling B 11:00-11:30
        Guid segmentA = created.Segments.Single().Id;

        // Rewriting A onto 11:15: A excludes itself, B (same appointment) is still a conflict.
        await SchedulingAssert.BusinessRule(ErrorCodes.AppointmentOverlap,
            () => Claim(w, New(SchedulingWorld.Future(11, 15), 30, employees: new[] { w.Employee.Id.Value }, segmentId: segmentA)));
        // Adjacent to B is fine.
        await Claim(w, New(SchedulingWorld.Future(11, 30), 30, employees: new[] { w.Employee.Id.Value }, segmentId: segmentA));
    }

    [Fact]
    public async Task Employee_AdjacentSegments_AreAllowed()
    {
        await using SchedulingWorld w = await SchedulingWorld.Create(nameof(Employee_AdjacentSegments_AreAllowed));
        await w.CreateAppointment(SchedulingWorld.Future(10));

        await w.CreateAppointment(await EmployeeProbe(w, SchedulingWorld.Future(10, 30)));
        await w.CreateAppointment(await EmployeeProbe(w, SchedulingWorld.Future(9, 30)));
        Assert.Equal(3, await w.CountAppointments());
    }

    [Fact]
    public async Task Employee_DifferentCompanies_Overlap_IsBlocked()
    {
        await using SchedulingWorld w = await SchedulingWorld.Create(nameof(Employee_DifferentCompanies_Overlap_IsBlocked));
        Company second = await w.AddCompany("Second company");
        await w.AssignEmployeeToCompany(w.Employee, second);
        await w.MakeServiceAvailableAt(w.Service, second);
        await w.CreateAppointment(SchedulingWorld.Future(10));
        Client fresh = await w.AddClient("Fresh");

        await SchedulingAssert.BusinessRule(ErrorCodes.AppointmentOverlap,
            () => w.CreateAppointment(w.CreateRequest(SchedulingWorld.Future(10, 10), client: fresh, company: second)));
        // Control: in the second company at an adjacent time the same employee is fine.
        await w.CreateAppointment(w.CreateRequest(SchedulingWorld.Future(10, 30), client: fresh, company: second));
    }

    [Fact]
    public async Task Employee_DifferentEmployeesInParallel_AreAllowed()
    {
        await using SchedulingWorld w = await SchedulingWorld.Create(nameof(Employee_DifferentEmployeesInParallel_AreAllowed));
        Employee colleague = await w.AddEmployee("Colleague");
        Client other = await w.AddClient("Other");

        await w.CreateAppointment(SchedulingWorld.Future(10));
        await w.CreateAppointment(w.CreateRequest(SchedulingWorld.Future(10), client: other, employee: colleague));
        Assert.Equal(2, await w.CountAppointments());
    }

    [Fact]
    public async Task EmployeeLifecycle_AScheduledEmptyGroupSegment_OccupiesTheTrainer()
    {
        await using SchedulingWorld w = await SchedulingWorld.Create(nameof(EmployeeLifecycle_AScheduledEmptyGroupSegment_OccupiesTheTrainer));
        await w.SeedAppointment(SchedulingWorld.Future(10), form: AppointmentForm.Group, employee: w.Employee);

        await SchedulingAssert.BusinessRule(ErrorCodes.AppointmentOverlap,
            async () => await w.CreateAppointment(await EmployeeProbe(w, SchedulingWorld.Future(10))));
    }

    [Fact]
    public async Task EmployeeLifecycle_AllClientsIndividuallyCancelled_ButTheAppointmentScheduled_StillOccupies()
    {
        await using SchedulingWorld w = await SchedulingWorld.Create(nameof(EmployeeLifecycle_AllClientsIndividuallyCancelled_ButTheAppointmentScheduled_StillOccupies));
        AppointmentDto created = await w.CreateAppointment(SchedulingWorld.Future(10));
        await w.SetBookingStatus(created.Id, w.Client, BookingStatus.Cancelled, "client");
        Assert.Equal(AppointmentStatus.Scheduled, (await w.LoadAppointment(created.Id)).Status);

        await SchedulingAssert.BusinessRule(ErrorCodes.AppointmentOverlap,
            async () => await w.CreateAppointment(await EmployeeProbe(w, SchedulingWorld.Future(10))));
    }

    [Fact]
    public async Task EmployeeLifecycle_AnExplicitlyCancelledUntouchedSegment_NoLongerOccupies()
    {
        await using SchedulingWorld w = await SchedulingWorld.Create(nameof(EmployeeLifecycle_AnExplicitlyCancelledUntouchedSegment_NoLongerOccupies));
        AppointmentDto created = await w.CreateAppointment(SchedulingWorld.Future(10));
        await w.Appointments.Cancel(w.OrganizationId, w.ActorUserId, true, created.Id, SchedulingWorld.BusinessCancel());

        await w.CreateAppointment(await EmployeeProbe(w, SchedulingWorld.Future(10)));
        Assert.Equal(2, await w.CountAppointments());
    }

    [Theory]
    [InlineData(BookingStatus.Completed)]
    [InlineData(BookingStatus.NoShow)]
    public async Task EmployeeLifecycle_AnExecutedSegment_StaysHistoricallyOccupying_EvenWhenTheAppointmentIsExplicitlyCancelled(BookingStatus outcome)
    {
        await using SchedulingWorld w = await SchedulingWorld.Create($"{nameof(EmployeeLifecycle_AnExecutedSegment_StaysHistoricallyOccupying_EvenWhenTheAppointmentIsExplicitlyCancelled)}-{outcome}");
        Appointment seeded = await w.SeedAppointment(SchedulingWorld.Future(10), status: AppointmentStatus.Closed, bookings: (w.Client, outcome, 50m));

        await SchedulingAssert.BusinessRule(ErrorCodes.AppointmentOverlap,
            async () => await w.CreateAppointment(await EmployeeProbe(w, SchedulingWorld.Future(10))));

        await MarkExplicitlyCancelledInDb(w, seeded.Id.Value);
        await SchedulingAssert.BusinessRule(ErrorCodes.AppointmentOverlap,
            async () => await w.CreateAppointment(await EmployeeProbe(w, SchedulingWorld.Future(10))));
    }

    [Fact]
    public async Task EmployeeLifecycle_PartialExecutionThenAppointmentCancel_TheCompletedSegmentOccupies_TheCancelledOneDoesNot()
    {
        await using SchedulingWorld w = await SchedulingWorld.Create(nameof(EmployeeLifecycle_PartialExecutionThenAppointmentCancel_TheCompletedSegmentOccupies_TheCancelledOneDoesNot));
        AppointmentDto created = await w.CreateAppointment(SchedulingWorld.Future(10));          // segment A 10:00
        await w.AddArtificialSegmentParticipation(created.Id, w.Client, SchedulingWorld.Future(14), 10m); // segment B 14:00
        Guid onA = (await w.LoadParticipations(created.Id, w.Client))[0].Id.Value;
        await SetParticipationStatusInDb(w, onA, ParticipationStatus.Completed);

        AppointmentDto cancelled = await w.Appointments.Cancel(w.OrganizationId, w.ActorUserId, true, created.Id, SchedulingWorld.BusinessCancel());
        Assert.Equal(AppointmentStatus.Closed, cancelled.Status);

        // Appointment status alone (Closed) says nothing per segment: A keeps the employee, B released it.
        await SchedulingAssert.BusinessRule(ErrorCodes.AppointmentOverlap,
            async () => await w.CreateAppointment(await EmployeeProbe(w, SchedulingWorld.Future(10))));
        await w.CreateAppointment(await EmployeeProbe(w, SchedulingWorld.Future(14)));
    }

    #endregion

    #region Client invariant

    [Fact]
    public async Task Client_DifferentAppointments_Overlap_IsBlocked()
    {
        await using SchedulingWorld w = await SchedulingWorld.Create(nameof(Client_DifferentAppointments_Overlap_IsBlocked));
        await w.CreateAppointment(SchedulingWorld.Future(10));

        await SchedulingAssert.BusinessRule(ErrorCodes.AppointmentOverlap,
            async () => await w.CreateAppointment(await ClientProbe(w, SchedulingWorld.Future(10, 29))));
    }

    [Fact]
    public async Task Client_SiblingSegmentOfTheSameAppointment_Overlap_IsBlocked()
    {
        await using SchedulingWorld w = await SchedulingWorld.Create(nameof(Client_SiblingSegmentOfTheSameAppointment_Overlap_IsBlocked));
        AppointmentDto created = await w.CreateAppointment(SchedulingWorld.Future(10));
        await w.AddArtificialSegmentParticipation(created.Id, w.Client, SchedulingWorld.Future(11), 10m); // B 11:00-11:30
        Guid segmentA = created.Segments.Single().Id;
        Guid client = w.Client.Id.Value;

        // Self-exclusion: A is excluded, B (same appointment, same booking) is visible.
        await SchedulingAssert.BusinessRule(ErrorCodes.AppointmentOverlap,
            () => Claim(w, New(SchedulingWorld.Future(11, 15), 30, clients: new[] { client }, segmentId: segmentA)));
        await Claim(w, New(SchedulingWorld.Future(10, 15), 30, clients: new[] { client }, segmentId: segmentA));

        // A new participation on another sibling overlapping B is rejected too (no exclusion for a new participation).
        Appointment loaded = await w.LoadAppointment(created.Id);
        AppointmentSegment probeSegment = new()
        {
            Id = Guid.NewGuid(), AppointmentId = created.Id, PlannedStart = SchedulingWorld.Future(11, 20), PlannedEnd = SchedulingWorld.Future(11, 50)
        };
        await using IUnitOfWork uow = await Begin(w);
        await SchedulingAssert.BusinessRule(ErrorCodes.AppointmentOverlap,
            () => SchedulingConflictGuard.Claim(Occupancy(w), uow, w.OrganizationId, new[]
            {
                new SegmentClaim(null, probeSegment.PlannedStart, probeSegment.PlannedEnd, Array.Empty<Guid>(), new[] { client })
            }));
        Assert.Equal(2, loaded.Segments.Count);
    }

    [Fact]
    public async Task Client_AdjacentParticipations_AreAllowed()
    {
        await using SchedulingWorld w = await SchedulingWorld.Create(nameof(Client_AdjacentParticipations_AreAllowed));
        await w.CreateAppointment(SchedulingWorld.Future(10));

        await w.CreateAppointment(await ClientProbe(w, SchedulingWorld.Future(10, 30)));
        await w.CreateAppointment(await ClientProbe(w, SchedulingWorld.Future(9, 30)));
        Assert.Equal(3, await w.CountAppointments());
    }

    [Fact]
    public async Task Client_DifferentCompanies_Overlap_IsBlocked()
    {
        await using SchedulingWorld w = await SchedulingWorld.Create(nameof(Client_DifferentCompanies_Overlap_IsBlocked));
        Company second = await w.AddCompany("Second company");
        Employee there = await w.AddEmployee("There", companyId: second.Id);
        await w.MakeServiceAvailableAt(w.Service, second);
        await w.CreateAppointment(SchedulingWorld.Future(10));

        await SchedulingAssert.BusinessRule(ErrorCodes.AppointmentOverlap,
            () => w.CreateAppointment(w.CreateRequest(SchedulingWorld.Future(10, 10), employee: there, company: second)));
        await w.CreateAppointment(w.CreateRequest(SchedulingWorld.Future(10, 30), employee: there, company: second));
    }

    [Fact]
    public async Task Client_DifferentClientsInParallel_AreAllowed()
    {
        await using SchedulingWorld w = await SchedulingWorld.Create(nameof(Client_DifferentClientsInParallel_AreAllowed));
        Client other = await w.AddClient("Other");
        Employee colleague = await w.AddEmployee("Colleague");

        await w.CreateAppointment(SchedulingWorld.Future(10));
        await w.CreateAppointment(w.CreateRequest(SchedulingWorld.Future(10), client: other, employee: colleague));
        Assert.Equal(2, await w.CountAppointments());
    }

    [Theory]
    [InlineData(BookingStatus.Confirmed, true)]
    [InlineData(BookingStatus.Completed, true)]
    [InlineData(BookingStatus.Cancelled, false)]
    [InlineData(BookingStatus.NoShow, false)]
    public async Task ClientLifecycle_OnlyConfirmedAndCompletedOccupy(BookingStatus status, bool occupies)
    {
        await using SchedulingWorld w = await SchedulingWorld.Create($"{nameof(ClientLifecycle_OnlyConfirmedAndCompletedOccupy)}-{status}");
        await w.SeedAppointment(SchedulingWorld.Future(10), bookings: (w.Client, status, 50m));

        Func<Task> probe = async () => await w.CreateAppointment(await ClientProbe(w, SchedulingWorld.Future(10)));
        if (occupies)
            await SchedulingAssert.BusinessRule(ErrorCodes.AppointmentOverlap, probe);
        else
            await probe();
    }

    [Fact]
    public async Task Client_AddClient_IsCheckedAgainstTheConcreteSegment()
    {
        await using SchedulingWorld w = await SchedulingWorld.Create(nameof(Client_AddClient_IsCheckedAgainstTheConcreteSegment));
        Employee colleague = await w.AddEmployee("Colleague");
        Client partner = await w.AddClient("Partner");
        AppointmentDto mine = await w.CreateAppointment(SchedulingWorld.Future(10));                                      // client busy 10:00
        AppointmentDto theirs = await w.CreateAppointment(w.CreateRequest(SchedulingWorld.Future(10, 15), client: partner, employee: colleague));

        await SchedulingAssert.BusinessRule(ErrorCodes.AppointmentOverlap,
            () => w.AddClientToOnlySegment(theirs.Id, w.Client));

        // A cancelled participation does not occupy: after cancelling the 10:00 booking the same AddClient succeeds.
        await w.SetBookingStatus(mine.Id, w.Client, BookingStatus.Cancelled, "client");
        await w.AddClientToOnlySegment(theirs.Id, w.Client);
    }

    [Fact]
    public async Task Client_ReactivatingACancelledGroupParticipation_IsCheckedLikeANewOne()
    {
        await using SchedulingWorld w = await SchedulingWorld.Create(nameof(Client_ReactivatingACancelledGroupParticipation_IsCheckedLikeANewOne));
        ServiceEntity svc = await w.AddGroupService();
        GroupDto group = await w.CreateGroup(svc, capacity: 5);
        await w.AddGroupMember(group, w.Client);
        Appointment occurrence = await w.GenerateSingleOccurrence(group); // 10:00-11:00
        await w.SetBookingStatus(occurrence.Id.Value, w.Client, BookingStatus.Cancelled, "client");

        // The cancelled participation frees the client: an overlapping individual appointment is allowed...
        Employee colleague = await w.AddEmployee("Colleague");
        await w.CreateAppointment(w.CreateRequest(SchedulingWorld.Future(10, 30), employee: colleague));

        // ...and therefore re-confirming the group participation would double-book the client.
        await SchedulingAssert.BusinessRule(ErrorCodes.AppointmentOverlap,
            () => w.SetBookingStatus(occurrence.Id.Value, w.Client, BookingStatus.Confirmed));
        Assert.Equal(ParticipationStatus.Cancelled, Assert.Single(await w.LoadParticipations(occurrence.Id.Value, w.Client)).Status);
    }

    [Fact]
    public async Task Client_AGuestRecordedAsNoShow_DoesNotOccupy_SoItIsNotAConflict()
    {
        await using SchedulingWorld w = await SchedulingWorld.Create(nameof(Client_AGuestRecordedAsNoShow_DoesNotOccupy_SoItIsNotAConflict));
        ServiceEntity svc = await w.AddGroupService();
        GroupDto group = await w.CreateGroup(svc, capacity: 5);
        Appointment occurrence = await w.SeedAppointment(SchedulingWorld.Past(10), form: AppointmentForm.Group, service: svc, groupId: group.Id);
        Employee colleague = await w.AddEmployee("Colleague");
        await w.SeedAppointment(SchedulingWorld.Past(10), employee: colleague, bookings: (w.Client, BookingStatus.Confirmed, 50m));

        await SchedulingAssert.BusinessRule(ErrorCodes.AppointmentOverlap,
            () => w.SetBookingStatus(occurrence.Id.Value, w.Client, BookingStatus.Completed));
        await w.SetBookingStatus(occurrence.Id.Value, w.Client, BookingStatus.NoShow);
    }

    #endregion

    #region Self-exclusion through the segment time command

    [Fact]
    public async Task SegmentTimeChange_ExcludesOnlyItsOwnSegment()
    {
        await using SchedulingWorld w = await SchedulingWorld.Create(nameof(SegmentTimeChange_ExcludesOnlyItsOwnSegment));
        AppointmentDto created = await w.CreateAppointment(SchedulingWorld.Future(10));
        AppointmentDto blocker = await w.CreateAppointment(await EmployeeProbe(w, SchedulingWorld.Future(11)));

        // Overlapping its OWN old range is fine (self-excluded)...
        await w.MoveOnlySegment(created.Id, SchedulingWorld.Future(10, 10));
        // ...another segment of the same employee is not.
        await SchedulingAssert.BusinessRule(ErrorCodes.AppointmentOverlap,
            () => w.MoveOnlySegment(created.Id, SchedulingWorld.Future(10, 45)));
        await SchedulingAssert.BusinessRule(ErrorCodes.AppointmentOverlap,
            () => w.MoveOnlySegment(created.Id, SchedulingWorld.Future(11, 15)));
        Assert.Equal(SchedulingWorld.Future(10, 10), (await w.Appointments.GetById(w.OrganizationId, created.Id)).PlannedStart);
        Assert.NotEqual(blocker.Id, created.Id);
    }

    #endregion

    #region Waitlist: promotion never waits on a client lock

    [Fact]
    public async Task TryLockClientSchedule_IsNonBlocking_AndFailsWhileAnotherTransactionHoldsTheClient()
    {
        await using SchedulingWorld w = await SchedulingWorld.Create(nameof(TryLockClientSchedule_IsNonBlocking_AndFailsWhileAnotherTransactionHoldsTheClient));
        Guid client = w.Client.Id.Value;

        await using (IUnitOfWork holder = await Begin(w))
        {
            await Occupancy(w).LockSchedulingSubjects(holder, Array.Empty<Guid>(), new[] { client });
            await using IUnitOfWork other = await Begin(w);
            Stopwatch sw = Stopwatch.StartNew();
            Assert.False(await Occupancy(w).TryLockClientSchedule(other, client));
            Assert.True(sw.Elapsed < TimeSpan.FromSeconds(5));
            Assert.True(await Occupancy(w).TryLockClientSchedule(other, Guid.NewGuid()));
        }

        await using IUnitOfWork after = await Begin(w);
        Assert.True(await Occupancy(w).TryLockClientSchedule(after, client));
    }

    #endregion

    #region Concurrency (real database, real transactions)

    private static async Task<Exception> CreateInOwnScope(SchedulingWorld w, TestAppointmentSpec request)
    {
        using IServiceScope scope = SchedulingTestHost.CreateScope();
        try
        {
            await scope.ServiceProvider.GetRequiredService<IAppointmentService>().Create(w.OrganizationId, w.ActorUserId, true, request.ToTarget());
            return null;
        }
        catch (Exception ex)
        {
            return ex;
        }
    }

    /// <summary>Counts transactions currently WAITING on the given advisory lock key.</summary>
    private static async Task<int> WaitersOn(SchedulingWorld w, long key)
    {
        long hi = (long)((ulong)key >> 32), lo = key & 0xFFFF_FFFFL;
        await using DatabaseContext db = w.NewDb();
        return await db.Database.SqlQuery<int>(
            $"SELECT count(*)::int AS \"Value\" FROM pg_locks WHERE locktype = 'advisory' AND NOT granted AND objsubid = 1 AND classid = {hi}::bigint::oid AND objid = {lo}::bigint::oid")
            .SingleAsync();
    }

    private static async Task WaitUntil(Func<Task<bool>> condition)
    {
        Stopwatch sw = Stopwatch.StartNew();
        while (!await condition())
        {
            if (sw.Elapsed > TimeSpan.FromSeconds(30))
                throw new TimeoutException("Condition not reached.");
            await Task.Delay(20);
        }
    }

    private static void AssertExactlyOneWinner(Exception[] outcomes)
    {
        Assert.Single(outcomes, o => o == null);
        Exception loser = Assert.Single(outcomes, o => o != null);
        BusinessRuleException business = Assert.IsType<BusinessRuleException>(loser);
        Assert.Equal(ErrorCodes.AppointmentOverlap, business.Code);
    }

    [Fact]
    public async Task Race_TwoTransactionsReleasedTogether_ForTheSameEmployee_ExactlyOneCommits()
    {
        await using SchedulingWorld w = await SchedulingWorld.Create(nameof(Race_TwoTransactionsReleasedTogether_ForTheSameEmployee_ExactlyOneCommits));
        TestAppointmentSpec first = await EmployeeProbe(w, SchedulingWorld.Future(10));
        TestAppointmentSpec second = await EmployeeProbe(w, SchedulingWorld.Future(10, 15));
        long key = SchedulingLockOrder.EmployeeKey(w.Employee.Id.Value);

        // Gate: hold the employee's scheduling lock, start both creates (both block at their FIRST lock, i.e. after every
        // pre-transaction validation saw an empty schedule), then release them together.
        Task<Exception[]> race;
        await using (IUnitOfWork gate = await Begin(w))
        {
            await Occupancy(w).LockSchedulingSubjects(gate, new[] { w.Employee.Id.Value }, Array.Empty<Guid>());
            race = Task.WhenAll(Task.Run(() => CreateInOwnScope(w, first)), Task.Run(() => CreateInOwnScope(w, second)));
            await WaitUntil(async () => await WaitersOn(w, key) == 2);
            await gate.CommitAsync();
        }

        AssertExactlyOneWinner(await race);
        Assert.Equal(1, await w.CountAppointments());
    }

    [Fact]
    public async Task Race_TwoTransactionsReleasedTogether_ForTheSameClient_ExactlyOneCommits()
    {
        await using SchedulingWorld w = await SchedulingWorld.Create(nameof(Race_TwoTransactionsReleasedTogether_ForTheSameClient_ExactlyOneCommits));
        TestAppointmentSpec first = await ClientProbe(w, SchedulingWorld.Future(10));
        TestAppointmentSpec second = await ClientProbe(w, SchedulingWorld.Future(10, 15));
        long key = SchedulingLockOrder.ClientKey(w.Client.Id.Value);

        Task<Exception[]> race;
        await using (IUnitOfWork gate = await Begin(w))
        {
            await Occupancy(w).LockSchedulingSubjects(gate, Array.Empty<Guid>(), new[] { w.Client.Id.Value });
            race = Task.WhenAll(Task.Run(() => CreateInOwnScope(w, first)), Task.Run(() => CreateInOwnScope(w, second)));
            await WaitUntil(async () => await WaitersOn(w, key) == 2);
            await gate.CommitAsync();
        }

        AssertExactlyOneWinner(await race);
        Assert.Equal(1, await w.CountAppointments());
    }

    [Fact]
    public async Task Race_UngatedConcurrentCreates_AreStable_ExactlyOneWinnerEveryRound()
    {
        await using SchedulingWorld w = await SchedulingWorld.Create(nameof(Race_UngatedConcurrentCreates_AreStable_ExactlyOneWinnerEveryRound));
        const int rounds = 12;

        for (int round = 0; round < rounds; round++)
        {
            DateTimeOffset start = SchedulingWorld.Future(8).AddDays(7 * round); // always the seeded Monday working day
            // Alternate the contested subject: even rounds the employee, odd rounds the client.
            TestAppointmentSpec a = round % 2 == 0 ? await EmployeeProbe(w, start) : await ClientProbe(w, start);
            TestAppointmentSpec b = round % 2 == 0 ? await EmployeeProbe(w, start.AddMinutes(10)) : await ClientProbe(w, start.AddMinutes(10));

            Exception[] outcomes = await Task.WhenAll(
                Task.Run(() => CreateInOwnScope(w, a)), Task.Run(() => CreateInOwnScope(w, b)), Task.Run(() => CreateInOwnScope(w, a)));

            Assert.Equal(1, outcomes.Count(o => o == null));
            Assert.All(outcomes.Where(o => o != null), o => Assert.Equal(ErrorCodes.AppointmentOverlap, Assert.IsType<BusinessRuleException>(o).Code));
        }

        Assert.Equal(rounds, await w.CountAppointments());
    }

    [Fact]
    public async Task Concurrency_DifferentSubjects_AreNotSerializedBehindOneLock()
    {
        await using SchedulingWorld w = await SchedulingWorld.Create(nameof(Concurrency_DifferentSubjects_AreNotSerializedBehindOneLock));
        Employee colleague = await w.AddEmployee("Colleague");
        Client other = await w.AddClient("Other");

        await using IUnitOfWork holder = await Begin(w);
        // Another transaction holds the default employee AND client for as long as this test runs...
        await Occupancy(w).LockSchedulingSubjects(holder, new[] { w.Employee.Id.Value }, new[] { w.Client.Id.Value });

        // ...yet scheduling a different employee for a different client in the same organization and company proceeds.
        Task<Exception> unrelated = Task.Run(() => CreateInOwnScope(w, w.CreateRequest(SchedulingWorld.Future(10), client: other, employee: colleague)));
        Task finished = await Task.WhenAny(unrelated, Task.Delay(TimeSpan.FromSeconds(20)));
        Assert.Same(unrelated, finished);
        Assert.Null(await unrelated);
        Assert.Equal(1, await w.CountAppointments());
    }

    #endregion
}
