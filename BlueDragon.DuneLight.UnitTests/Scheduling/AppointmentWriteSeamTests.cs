#nullable disable
using System;
using BlueDragon.DuneLight.Core.DTOs.Catalog;
using BlueDragon.DuneLight.Core.Enums;
using BlueDragon.DuneLight.Infrastructure.Domain.Models.Appointments;
using BlueDragon.DuneLight.Infrastructure.Utils;

namespace BlueDragon.DuneLight.UnitTests.Scheduling;

/// <summary>
/// S3 write seams, pure unit tests (no database): <see cref="AppointmentFactory"/>, <see cref="AppointmentFrameMutator"/>,
/// <see cref="BookingFactory"/> and the <see cref="AppointmentOwnership.IsAssignedToEmployee"/> predicate. The service-level
/// behaviour that goes through them stays pinned by the characterization suite.
/// </summary>
public class AppointmentWriteSeamTests
{
    private static readonly Guid Org = Guid.NewGuid();
    private static readonly Guid Company = Guid.NewGuid();
    private static readonly Guid User = Guid.NewGuid();
    private static readonly DateTimeOffset StartsAt = new(2031, 3, 3, 10, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset CreatedAt = new(2026, 9, 30, 8, 0, 0, TimeSpan.Zero);

    private static AppointmentFrame Frame(Guid? employeeId = null, Guid? roomId = null) =>
        new(Guid.NewGuid(), employeeId, roomId, StartsAt, 45);

    /// <summary>D3B1: bookings are created against the appointment's authoritative segment.</summary>
    private static AppointmentSegment NewSegment() =>
        AppointmentSegments.GetSingleExecutionSegment(
            AppointmentFactory.CreateIndividual(Org, Company, Frame(), AppointmentStatus.Scheduled, null, null, User, CreatedAt));

    #region AppointmentFactory

    [Theory]
    [InlineData(AppointmentStatus.Scheduled)]
    [InlineData(AppointmentStatus.Completed)]
    public void CreateIndividual_MapsTheFrameAndTheAppointmentLevelFields(AppointmentStatus status)
    {
        AppointmentFrame frame = Frame(Guid.NewGuid(), Guid.NewGuid());
        Guid recurrence = Guid.NewGuid();

        Appointment a = AppointmentFactory.CreateIndividual(Org, Company, frame, status, "note", recurrence, User, CreatedAt);

        Assert.NotNull(a.Id);
        Assert.NotEqual(Guid.Empty, a.Id.Value);
        Assert.Equal(Org, a.OrganizationId);
        Assert.Equal(Company, a.CompanyId);
        Assert.Equal(AppointmentForm.Individual, a.Form);
        Assert.Equal(status, a.Status);
        Assert.Equal(frame, AppointmentFrame.Of(a));
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
        // Navigations are never set (the appointment is attached to a fresh DbContext by the handlers).
        AppointmentSegment segment = AppointmentSegments.GetSingleExecutionSegment(a);
        Assert.Null(segment.Service);
        Assert.Null(segment.Room);
        Assert.Null(Assert.Single(segment.Employees).Employee);
        Assert.Null(a.Company);
    }

    [Fact]
    public void CreateIndividual_WithoutARoomOrRecurrence_KeepsThemNull()
    {
        Appointment a = AppointmentFactory.CreateIndividual(
            Org, Company, Frame(Guid.NewGuid(), roomId: null), AppointmentStatus.Scheduled, null, null, User, CreatedAt);

        Assert.Null(a.RoomId);
        Assert.Null(a.RecurrenceGroupId);
        Assert.Null(a.Note);
    }

    [Fact]
    public void CreateGroupOccurrence_IsAScheduledGroupAppointmentWithoutANote_AndMayHaveNoTrainer()
    {
        Guid group = Guid.NewGuid(), slot = Guid.NewGuid();
        AppointmentFrame frame = Frame(employeeId: null, roomId: Guid.NewGuid());

        Appointment a = AppointmentFactory.CreateGroupOccurrence(Org, Company, frame, group, slot, User, CreatedAt);

        Assert.Equal(AppointmentForm.Group, a.Form);
        Assert.Equal(AppointmentStatus.Scheduled, a.Status);
        Assert.Equal(frame, AppointmentFrame.Of(a));
        Assert.Null(a.EmployeeId);
        Assert.Equal(group, a.GroupId);
        Assert.Equal(slot, a.GroupSlotId);
        Assert.Null(a.Note);
        Assert.Null(a.RecurrenceGroupId);
        Assert.Equal(Company, a.CompanyId);
    }

    [Fact]
    public void Factory_GivesEveryAppointmentItsOwnId()
    {
        Appointment first = AppointmentFactory.CreateIndividual(Org, Company, Frame(), AppointmentStatus.Scheduled, null, null, User, CreatedAt);
        Appointment second = AppointmentFactory.CreateIndividual(Org, Company, Frame(), AppointmentStatus.Scheduled, null, null, User, CreatedAt);

        Assert.NotEqual(first.Id, second.Id);
    }

    #endregion

    #region AppointmentFrameMutator

    [Fact]
    public void Apply_WritesExactlyTheFiveFrameFields_InMemory()
    {
        Appointment a = AppointmentFactory.CreateIndividual(
            Org, Company, Frame(Guid.NewGuid(), Guid.NewGuid()), AppointmentStatus.Scheduled, "keep", null, User, CreatedAt);
        Guid id = a.Id.Value;
        AppointmentFrame next = new(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), StartsAt.AddHours(3), 90);

        AppointmentFrameMutator.Apply(a, next, CreatedAt);

        Assert.Equal(next, AppointmentFrame.Of(a));
        // Nothing outside the frame is touched — company, status, note and audit fields stay with the caller.
        Assert.Equal(id, a.Id);
        Assert.Equal(Company, a.CompanyId);
        Assert.Equal(AppointmentStatus.Scheduled, a.Status);
        Assert.Equal("keep", a.Note);
        Assert.Null(a.UpdatedAt);
        Assert.Null(a.UpdatedBy);
    }

    [Fact]
    public void Apply_CanClearTheEmployeeAndTheRoom_NoNormalization()
    {
        Appointment a = AppointmentFactory.CreateIndividual(
            Org, Company, Frame(Guid.NewGuid(), Guid.NewGuid()), AppointmentStatus.Scheduled, null, null, User, CreatedAt);

        AppointmentFrameMutator.Apply(a, AppointmentFrame.Of(a) with { EmployeeId = null, RoomId = null }, CreatedAt);

        Assert.Null(a.EmployeeId);
        Assert.Null(a.RoomId);
    }

    [Fact]
    public void Apply_WithAPartialFrame_KeepsTheUnchangedFieldsExactly()
    {
        // The Move shape: only the time and (optionally) employee/room change; service and duration are kept.
        Guid room = Guid.NewGuid(), employee = Guid.NewGuid();
        Appointment a = AppointmentFactory.CreateIndividual(
            Org, Company, Frame(employee, room), AppointmentStatus.Scheduled, null, null, User, CreatedAt);
        AppointmentFrame before = AppointmentFrame.Of(a);

        AppointmentFrameMutator.Apply(a, before with { StartsAt = StartsAt.AddDays(1) }, CreatedAt);

        Assert.Equal(before.ServiceId, a.ServiceId);
        Assert.Equal(employee, a.EmployeeId);
        Assert.Equal(room, a.RoomId);
        Assert.Equal(45, a.DurationMinutes);
        Assert.Equal(StartsAt.AddDays(1), a.StartsAt);
    }

    [Fact]
    public void Of_ReadsTheInMemoryValues_WithoutANavigationOrStoreRoundTrip()
    {
        Appointment a = new() { Id = Guid.NewGuid() };
        AppointmentFrameMutator.NewSegment(a, new AppointmentFrame(Guid.NewGuid(), null, null, StartsAt, 30));

        AppointmentFrame frame = AppointmentFrame.Of(a);

        Assert.Equal(new AppointmentFrame(a.ServiceId, null, null, StartsAt, 30), frame);
        Assert.Throws<ArgumentNullException>(() => AppointmentFrame.Of(null));
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
    public void IsAssignedToEmployee_OnlyForTheAppointmentsEmployee_NeverForATrainerlessAppointment()
    {
        Guid employee = Guid.NewGuid();
        Appointment assigned = new() { Id = Guid.NewGuid() };
        AppointmentFrameMutator.NewSegment(assigned, Frame(employee));
        Appointment trainerless = new() { Id = Guid.NewGuid() };
        AppointmentFrameMutator.NewSegment(trainerless, Frame(employeeId: null));

        Assert.True(AppointmentOwnership.IsAssignedToEmployee(assigned, employee));
        Assert.False(AppointmentOwnership.IsAssignedToEmployee(assigned, Guid.NewGuid()));
        Assert.False(AppointmentOwnership.IsAssignedToEmployee(trainerless, employee));
        Assert.False(AppointmentOwnership.IsAssignedToEmployee(trainerless, Guid.Empty));
    }

    #endregion
}
