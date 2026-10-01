#nullable disable
using System;
using System.Linq;
using BlueDragon.DuneLight.Core.DTOs.Catalog;
using BlueDragon.DuneLight.Core.Enums;
using BlueDragon.DuneLight.Infrastructure.Domain.Models.Appointments;
using BlueDragon.DuneLight.Infrastructure.Utils;

namespace BlueDragon.DuneLight.UnitTests.Scheduling;

/// <summary>
/// Write seams, pure unit tests (no database): the construction core <see cref="AppointmentFactory"/>,
/// <see cref="SegmentMutator"/>, <see cref="BookingFactory"/> and the <see cref="AppointmentOwnership.IsAssignedToSegment"/>
/// predicate. The service-level behaviour that goes through them stays pinned by the characterization suite.
/// </summary>
public class AppointmentWriteSeamTests
{
    private static readonly Guid Org = Guid.NewGuid();
    private static readonly Guid Company = Guid.NewGuid();
    private static readonly Guid User = Guid.NewGuid();
    private static readonly DateTimeOffset StartsAt = new(2031, 3, 3, 10, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset CreatedAt = new(2026, 9, 30, 8, 0, 0, TimeSpan.Zero);
    private static readonly BookingPricing Price = new(50m, 50m, false);

    private static SegmentPlan Plan(
        Guid? employeeId = null, Guid? roomId = null, DateTimeOffset? start = null, int minutes = 45, Guid? serviceId = null,
        params Guid[] clients) =>
        new(serviceId ?? Guid.NewGuid(), start ?? StartsAt, (start ?? StartsAt).AddMinutes(minutes),
            employeeId.HasValue ? new[] { employeeId.Value } : Array.Empty<Guid>(), roomId,
            clients.Select(c => new ParticipantPlan(c, Price)).ToList());

    private static Appointment Individual(params SegmentPlan[] plans) =>
        AppointmentFactory.CreateIndividual(Org, Company, null, null, User, CreatedAt, plans, ParticipationStatus.Confirmed);

    /// <summary>Bookings are created against a segment of an appointment.</summary>
    private static AppointmentSegment NewSegment() => Individual(Plan()).Segments.Single();

    #region AppointmentFactory (construction core)

    // M1A: creation ALWAYS initializes Scheduled (CompleteNew derives Closed from its Completed participations).
    [Fact]
    public void CreateIndividual_MapsTheSegmentAndTheAppointmentLevelFields()
    {
        Guid employee = Guid.NewGuid(), room = Guid.NewGuid(), recurrence = Guid.NewGuid();
        SegmentPlan plan = Plan(employee, room);

        Appointment a = AppointmentFactory.CreateIndividual(Org, Company, "note", recurrence, User, CreatedAt, new[] { plan }, ParticipationStatus.Confirmed);

        Assert.NotNull(a.Id);
        Assert.NotEqual(Guid.Empty, a.Id.Value);
        Assert.Equal(Org, a.OrganizationId);
        Assert.Equal(Company, a.CompanyId);
        Assert.Equal(AppointmentForm.Individual, a.Form);
        Assert.Equal(AppointmentStatus.Scheduled, a.Status);
        Assert.Equal("note", a.Note);
        Assert.Equal(recurrence, a.RecurrenceGroupId);
        Assert.Null(a.GroupId);
        Assert.Null(a.GroupSlotId);
        Assert.Null(a.CancellationReason);
        Assert.Equal(CreatedAt, a.CreatedAt);
        Assert.Equal(User, a.CreatedBy);
        Assert.Null(a.UpdatedAt);
        Assert.Null(a.UpdatedBy);
        Assert.Empty(a.Bookings);

        AppointmentSegment segment = Assert.Single(a.Segments);
        Assert.Equal(a.Id.Value, segment.AppointmentId);
        Assert.Equal(Org, segment.OrganizationId);
        Assert.Equal(plan.ServiceId, segment.ServiceId);
        Assert.Equal(plan.PlannedStart, segment.PlannedStart);
        Assert.Equal(plan.PlannedEnd, segment.PlannedEnd);
        Assert.Equal(room, segment.RoomId);
        Assert.Equal(employee, Assert.Single(segment.Employees).EmployeeId);
        // Navigations are never set (the appointment is attached to a fresh DbContext by the handlers).
        Assert.Null(segment.Service);
        Assert.Null(segment.Room);
        Assert.Null(Assert.Single(segment.Employees).Employee);
        Assert.Null(a.Company);
    }

    [Fact]
    public void CreateIndividual_WithoutARoomOrRecurrence_KeepsThemNull()
    {
        Appointment a = Individual(Plan(Guid.NewGuid(), roomId: null));

        Assert.Null(a.RoomId);
        Assert.Null(a.RecurrenceGroupId);
        Assert.Null(a.Note);
    }

    [Fact]
    public void CreateIndividual_RejectsNoSegments_AndASegmentThatDoesNotEndAfterItStarts()
    {
        Assert.Throws<InvalidAppointmentSegmentStateException>(() => Individual());
        Assert.Throws<InvalidAppointmentSegmentStateException>(() => Individual(Plan(minutes: 0)));
    }

    [Fact]
    public void CreateGroupOccurrence_IsAScheduledGroupAppointmentWithoutANote_AndMayHaveNoTrainer()
    {
        Guid group = Guid.NewGuid(), slot = Guid.NewGuid(), room = Guid.NewGuid();

        Appointment a = AppointmentFactory.CreateGroupOccurrence(Org, Company, group, slot, User, CreatedAt, Plan(employeeId: null, roomId: room));

        Assert.Equal(AppointmentForm.Group, a.Form);
        Assert.Equal(AppointmentStatus.Scheduled, a.Status);
        AppointmentSegment segment = Assert.Single(a.Segments);
        Assert.Empty(segment.Employees);
        Assert.Equal(room, segment.RoomId);
        Assert.Null(a.EmployeeId);
        Assert.Equal(group, a.GroupId);
        Assert.Equal(slot, a.GroupSlotId);
        Assert.Null(a.Note);
        Assert.Null(a.RecurrenceGroupId);
        Assert.Equal(Company, a.CompanyId);
        // A group occurrence may have no members: a valid empty occurrence still has its segment.
        Assert.Empty(a.Bookings);
    }

    [Fact]
    public void CreateGroupOccurrence_GivesEveryMemberOneBookingWithOneConfirmedParticipationOnTheSegment()
    {
        Guid c1 = Guid.NewGuid(), c2 = Guid.NewGuid();

        Appointment a = AppointmentFactory.CreateGroupOccurrence(
            Org, Company, Guid.NewGuid(), Guid.NewGuid(), User, CreatedAt, Plan(clients: new[] { c1, c2 }));

        AppointmentSegment segment = Assert.Single(a.Segments);
        Assert.Equal(new[] { c1, c2 }, a.Bookings.Select(b => b.ClientId).ToArray());
        Assert.All(a.Bookings, b =>
        {
            BookingSegmentParticipation p = Assert.Single(b.Participations);
            Assert.Equal(segment.Id, p.AppointmentSegmentId);
            Assert.Equal(ParticipationStatus.Confirmed, p.Status);
            Assert.Equal(0, p.StatusVersion);
        });
    }

    [Fact]
    public void Factory_GivesEveryAppointmentItsOwnId()
    {
        Assert.NotEqual(Individual(Plan()).Id, Individual(Plan()).Id);
    }

    [Fact]
    public void CreationCore_TwoSegments_OneClientOnBoth_GetsOneBookingAndTwoParticipations()
    {
        Guid client = Guid.NewGuid();
        SegmentPlan first = Plan(Guid.NewGuid(), start: StartsAt, minutes: 30, clients: client);
        SegmentPlan second = Plan(Guid.NewGuid(), start: StartsAt.AddMinutes(30), minutes: 60, clients: client);

        Appointment a = Individual(first, second);

        Assert.Equal(2, a.Segments.Count);
        Booking booking = Assert.Single(a.Bookings);
        Assert.Equal(client, booking.ClientId);
        Assert.Equal(a.Id.Value, booking.AppointmentId);
        Assert.Equal(
            a.Segments.Select(s => s.Id).OrderBy(id => id),
            booking.Participations.Select(p => (Guid?)p.AppointmentSegmentId).OrderBy(id => id));
        Assert.All(booking.Participations, p => Assert.Equal(booking.Id.Value, p.BookingId));
    }

    [Fact]
    public void CreationCore_DifferentClientsOnDifferentSegments_EachGetsTheirOwnBookingOnTheirOwnSegment()
    {
        Guid onlyFirst = Guid.NewGuid(), onlySecond = Guid.NewGuid(), both = Guid.NewGuid();
        SegmentPlan first = Plan(Guid.NewGuid(), clients: new[] { onlyFirst, both });
        SegmentPlan second = Plan(Guid.NewGuid(), start: StartsAt.AddHours(2), clients: new[] { onlySecond, both });

        Appointment a = Individual(first, second);

        AppointmentSegment segA = a.Segments.Single(s => s.PlannedStart == first.PlannedStart);
        AppointmentSegment segB = a.Segments.Single(s => s.PlannedStart == second.PlannedStart);
        Assert.Equal(3, a.Bookings.Count);
        Assert.Equal(segA.Id.Value, Assert.Single(a.Bookings.Single(b => b.ClientId == onlyFirst).Participations).AppointmentSegmentId);
        Assert.Equal(segB.Id.Value, Assert.Single(a.Bookings.Single(b => b.ClientId == onlySecond).Participations).AppointmentSegmentId);
        Assert.Equal(2, a.Bookings.Single(b => b.ClientId == both).Participations.Count);
    }

    [Fact]
    public void CreationCore_InitialParticipationStatus_IsAppliedToEveryParticipation()
    {
        Guid client = Guid.NewGuid();
        Appointment a = AppointmentFactory.CreateIndividual(
            Org, Company, null, null, User, CreatedAt,
            new[] { Plan(clients: client), Plan(start: StartsAt.AddHours(1), clients: client) }, ParticipationStatus.Completed);

        Assert.All(Assert.Single(a.Bookings).Participations, p => Assert.Equal(ParticipationStatus.Completed, p.Status));
        // The factory never decides the appointment status — the caller derives it.
        Assert.Equal(AppointmentStatus.Scheduled, a.Status);
    }

    #endregion

    #region SegmentMutator

    [Fact]
    public void SegmentMutator_WritesOnlyTheAddressedSegment_InMemory()
    {
        Guid client = Guid.NewGuid();
        Appointment a = Individual(Plan(Guid.NewGuid(), Guid.NewGuid(), clients: client), Plan(Guid.NewGuid(), start: StartsAt.AddHours(2), clients: client));
        a.Note = "keep";
        AppointmentSegment target = a.Segments.First();
        AppointmentSegment other = a.Segments.Last();
        (Guid service, Guid? room, DateTimeOffset start, DateTimeOffset end, Guid employee) otherBefore =
            (other.ServiceId, other.RoomId, other.PlannedStart, other.PlannedEnd, other.Employees.Single().EmployeeId);
        Guid newService = Guid.NewGuid(), newRoom = Guid.NewGuid(), newEmployee = Guid.NewGuid();

        SegmentMutator.ChangeService(target, newService, CreatedAt);
        SegmentMutator.ChangeRoom(target, newRoom, CreatedAt);
        SegmentMutator.ChangeTime(target, StartsAt.AddHours(5), StartsAt.AddHours(6), CreatedAt);
        SegmentMutator.AssignEmployees(target, new[] { newEmployee }, CreatedAt);

        Assert.Equal(newService, target.ServiceId);
        Assert.Equal(newRoom, target.RoomId);
        Assert.Equal(StartsAt.AddHours(5), target.PlannedStart);
        Assert.Equal(StartsAt.AddHours(6), target.PlannedEnd);
        Assert.Equal(newEmployee, Assert.Single(target.Employees).EmployeeId);
        Assert.Equal(otherBefore, (other.ServiceId, other.RoomId, other.PlannedStart, other.PlannedEnd, other.Employees.Single().EmployeeId));
        // Nothing outside the segment is touched — company, status, note and audit fields stay with the caller.
        Assert.Equal(Company, a.CompanyId);
        Assert.Equal(AppointmentStatus.Scheduled, a.Status);
        Assert.Equal("keep", a.Note);
        Assert.Null(a.UpdatedAt);
    }

    [Fact]
    public void SegmentMutator_CanClearTheEmployeesAndTheRoom_AndRejectsAnEmptyTimeRange()
    {
        AppointmentSegment segment = Individual(Plan(Guid.NewGuid(), Guid.NewGuid())).Segments.Single();

        SegmentMutator.AssignEmployees(segment, Array.Empty<Guid>(), CreatedAt);
        SegmentMutator.ChangeRoom(segment, null, CreatedAt);

        Assert.Empty(segment.Employees);
        Assert.Null(segment.RoomId);
        Assert.ThrowsAny<Exception>(() => SegmentMutator.ChangeTime(segment, StartsAt, StartsAt, CreatedAt));
    }

    [Fact]
    public void SegmentMutator_ChangeTime_KeepsTheOtherFieldsExactly()
    {
        // The Move shape: only the time changes; service, employee and room are kept.
        Guid room = Guid.NewGuid(), employee = Guid.NewGuid();
        AppointmentSegment segment = Individual(Plan(employee, room)).Segments.Single();
        Guid service = segment.ServiceId;

        SegmentMutator.ChangeTime(segment, StartsAt.AddDays(1), StartsAt.AddDays(1).AddMinutes(45), CreatedAt);

        Assert.Equal(service, segment.ServiceId);
        Assert.Equal(employee, segment.Employees.Single().EmployeeId);
        Assert.Equal(room, segment.RoomId);
        Assert.Equal(45, AppointmentSegments.DurationMinutes(segment));
        Assert.Equal(StartsAt.AddDays(1), segment.PlannedStart);
    }

    #endregion

    #region BookingFactory

    [Fact]
    public void CreateConfirmed_IsAFreshConfirmedBooking_WithPricingAndNoPackageOrCancellationState()
    {
        AppointmentSegment segment = NewSegment();
        Guid appointment = segment.AppointmentId, client = Guid.NewGuid();

        Booking b = BookingFactory.CreateConfirmed(Org, segment, client, new BookingPricing(40m, 50m, true), CreatedAt);

        Assert.NotNull(b.Id);
        Assert.Equal(Org, b.OrganizationId);
        Assert.Equal(appointment, b.AppointmentId);
        Assert.Equal(client, b.ClientId);
        Assert.Equal(BookingStatus.Confirmed, b.Status);
        Assert.Equal(0, b.StatusVersion);
        Assert.Equal(40m, b.Amount);
        Assert.Equal(50m, b.SuggestedAmount);
        Assert.True(b.IsAmountManuallyOverridden);
        Assert.Null(b.ClientPackageId);
        Assert.Null(b.CoverageType);
        Assert.False(b.PackageCoverageApplied);
        Assert.False(b.PackageCoverageReturned);
        Assert.Null(b.PackageCoverageReturnedAt);
        Assert.Null(b.PackageCoverageReturnedBy);
        Assert.Null(b.CancellationReason);
        Assert.Null(b.IsLateCancellation);
        Assert.Null(b.Note);
        Assert.Equal(CreatedAt, b.CreatedAt);
        Assert.Null(b.UpdatedAt);
        Assert.Null(b.UpdatedBy);
    }

    [Fact]
    public void CreateConfirmed_AtTheSuggestedPrice_IsNotAManualOverride()
    {
        Booking b = BookingFactory.CreateConfirmed(Org, NewSegment(), Guid.NewGuid(), BookingPricing.AtSuggested(new ResolvePriceResponse { Price = 35m, Source = PriceSource.Default }), CreatedAt);

        Assert.Equal(35m, b.Amount);
        Assert.Equal(35m, b.SuggestedAmount);
        Assert.False(b.IsAmountManuallyOverridden);
    }

    [Fact]
    public void CreateCompletedAtCreation_IsCompletedAtStatusVersionZero_WithoutAPackage()
    {
        // F-07 (pinned): CompleteNew creates the Booking directly as Completed without going through TrySetStatus.
        Booking b = BookingFactory.CreateCompletedAtCreation(Org, NewSegment(), Guid.NewGuid(), new BookingPricing(50m, 50m, false), CreatedAt);

        Assert.Equal(BookingStatus.Completed, b.Status);
        Assert.Equal(0, b.StatusVersion);
        Assert.Null(b.ClientPackageId);
        Assert.False(b.PackageCoverageApplied);
        Assert.False(b.PackageCoverageReturned);
        Assert.Null(b.CoverageType);
    }

    [Fact]
    public void CreateCompletedAtCreation_NeverRecordsPackageUsage_TheLedgerDoes()
    {
        // D3B3A: the factory only shapes Booking + Participation; package usage is a PackageConsumption written by the
        // ledger (IPackageConsumptionLedgerService) in CompleteNew's transaction — see PackageConsumptionLedgerTests.
        Booking b = BookingFactory.CreateCompletedAtCreation(Org, NewSegment(), Guid.NewGuid(), new BookingPricing(50m, 50m, false), CreatedAt);

        Assert.Empty(BookingParticipations.GetSingleParticipation(b).PackageConsumptions);
        Assert.False(b.PackageCoverageApplied);
    }

    #endregion

    #region Ownership predicate

    [Fact]
    public void IsAssignedToSegment_OnlyForTheSegmentsEmployees_NeverForATrainerlessSegment()
    {
        Guid employee = Guid.NewGuid();
        AppointmentSegment assigned = Individual(Plan(employee)).Segments.Single();
        AppointmentSegment trainerless = Individual(Plan(employeeId: null)).Segments.Single();

        Assert.True(AppointmentOwnership.IsAssignedToSegment(assigned, employee));
        Assert.False(AppointmentOwnership.IsAssignedToSegment(assigned, Guid.NewGuid()));
        Assert.False(AppointmentOwnership.IsAssignedToSegment(trainerless, employee));
        Assert.False(AppointmentOwnership.IsAssignedToSegment(trainerless, Guid.Empty));
    }

    [Fact]
    public void IsAssignedToSegment_IsPerSegment_AssignedToAIsNotAssignedToB()
    {
        Guid employeeA = Guid.NewGuid(), employeeB = Guid.NewGuid();
        Appointment a = Individual(Plan(employeeA), Plan(employeeB, start: StartsAt.AddHours(1)));
        AppointmentSegment segA = a.Segments.First(), segB = a.Segments.Last();

        Assert.True(AppointmentOwnership.IsAssignedToSegment(segA, employeeA));
        Assert.False(AppointmentOwnership.IsAssignedToSegment(segB, employeeA));
        Assert.True(AppointmentOwnership.IsAssignedToSegment(segB, employeeB));
        Assert.False(AppointmentOwnership.IsAssignedToSegment(segA, employeeB));
    }

    #endregion
}
