#nullable disable
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using BlueDragon.DuneLight.Core.DTOs.Appointments;
using BlueDragon.DuneLight.Core.DTOs.Groups;
using BlueDragon.DuneLight.Core.Enums;
using BlueDragon.DuneLight.Core.Events;
using BlueDragon.DuneLight.Core.Shared;
using BlueDragon.DuneLight.Infrastructure.Domain.Models.Appointments;
using BlueDragon.DuneLight.Infrastructure.Domain.Models.Clients;
using BlueDragon.DuneLight.Infrastructure.Domain.Models.Commissions;
using BlueDragon.DuneLight.Infrastructure.Domain.Models.Employees;
using BlueDragon.DuneLight.Infrastructure.Domain.Models.Notifications;
using BlueDragon.DuneLight.Infrastructure.Domain.Models.Outbox;
using BlueDragon.DuneLight.Infrastructure.Utils;

namespace BlueDragon.DuneLight.UnitTests.Scheduling;

/// <summary>
/// CHARACTERIZATION (matrix L and M): Confirmed → NoShow and Confirmed → Cancelled, at Booking level
/// (IBookingService.SetStatus) and at appointment level (AppointmentService.Cancel / MarkNoShow), for Individual and Group.
///
/// Shared facts pinned here: neither transition creates, voids or refunds any monetary Payment, neither touches
/// commissions, both write a "BookingStatus" audit row carrying the new StatusVersion, and both emit one outbox event per
/// affected Booking keyed on (bookingId, StatusVersion). The Appointment frame does NOT follow single-Booking
/// transitions — it stays Scheduled. Only the appointment-wide paths change Appointment.Status, and both of them end on
/// Cancelled (there is no NoShow appointment status).
/// </summary>
public class BookingNoShowAndCancellationCharacterizationTests
{
    #region L. No-show — booking level

    [Fact]
    public async Task IndividualNoShow_SetsStatusReasonAndVersion_AndLeavesTheAppointmentScheduled()
    {
        await using SchedulingWorld w = await SchedulingWorld.Create(nameof(IndividualNoShow_SetsStatusReasonAndVersion_AndLeavesTheAppointmentScheduled));
        Client partner = await w.AddClient("Partner", "Client");
        AppointmentDto created = await w.CreateAppointment(SchedulingWorld.Future(10), extraClients: partner);

        BookingDto dto = await w.SetBookingStatus(created.Id, w.Client, BookingStatus.NoShow, "did not show");

        Assert.Equal(BookingStatus.NoShow, dto.Status);
        Appointment a = await w.LoadAppointment(created.Id);
        Assert.Equal(AppointmentStatus.Scheduled, a.Status); // the frame does not follow a single-booking no-show
        Booking b = a.Bookings.Single(x => x.ClientId == w.Client.Id);
        Assert.Equal(BookingStatus.NoShow, b.Status);
        Assert.Equal(1, b.StatusVersion);
        Assert.Equal("did not show", b.CancellationReason);
        Assert.Null(b.IsLateCancellation); // lateness is a Cancelled-only classification
        Assert.Equal(BookingStatus.Confirmed, a.Bookings.Single(x => x.ClientId == partner.Id).Status);
        AppointmentAuditLog audit = Assert.Single(await w.LoadAuditLog(created.Id), l => l.ChangeType == "BookingStatus");
        Assert.Equal("NoShow", audit.NewValue);
        Assert.Equal(1, audit.StatusVersion);
    }

    [Fact]
    public async Task IndividualNoShow_CreatesNoPayment_NoCommission_AndKeepsTheAmount()
    {
        await using SchedulingWorld w = await SchedulingWorld.Create(nameof(IndividualNoShow_CreatesNoPayment_NoCommission_AndKeepsTheAmount));
        await w.AddCommissionRule(w.Employee, w.Service, CommissionCalculationType.Percentage, 10m);
        AppointmentDto created = await w.CreateAppointment(SchedulingWorld.Future(10));

        await w.SetBookingStatus(created.Id, w.Client, BookingStatus.NoShow, paymentMethod: PaymentMethod.Cash);

        Booking b = await w.LoadBooking(created.Id, w.Client);
        Assert.Equal(50m, b.Amount);
        Assert.Empty(await w.LoadPayments(b.Id.Value));
        Assert.Empty(await w.LoadCommissionEntries());
    }

    [Fact]
    public async Task IndividualNoShow_EmitsOneNoShowEventKeyedOnTheBookingVersion()
    {
        await using SchedulingWorld w = await SchedulingWorld.Create(nameof(IndividualNoShow_EmitsOneNoShowEventKeyedOnTheBookingVersion));
        AppointmentDto created = await w.CreateAppointment(SchedulingWorld.Future(10));

        await w.SetBookingStatus(created.Id, w.Client, BookingStatus.NoShow);

        Booking b = await w.LoadBooking(created.Id, w.Client);
        OutboxMessage message = Assert.Single(await w.LoadOutbox());
        Assert.Equal(OutboxEventTypes.BookingNoShowV1, message.Type);
        Assert.Equal($"booking-noshow:{b.Id}:1", message.IdempotencyKey);
        Assert.Equal(OutboxMessageStatus.Pending, message.Status);
    }

    [Fact]
    public async Task IndividualNoShow_AsksForAPackageReturnThatDoesNotApply_WhenNothingWasCovered()
    {
        await using SchedulingWorld w = await SchedulingWorld.Create(nameof(IndividualNoShow_AsksForAPackageReturnThatDoesNotApply_WhenNothingWasCovered));
        AppointmentDto created = await w.CreateAppointment(SchedulingWorld.Future(10));

        // A Confirmed booking has no applied coverage yet (coverage is applied only while completing), so the flag is a no-op.
        await w.SetBookingStatus(created.Id, w.Client, BookingStatus.NoShow, returnPackageEntry: true);

        Assert.DoesNotContain(await w.LoadAuditLog(created.Id), l => l.ChangeType == "BookingPackageCoverageReturned");
        Assert.False((await w.LoadBooking(created.Id, w.Client)).PackageCoverageReturned);
    }

    [Fact]
    public async Task IndividualNoShow_Repeated_IsRejected()
    {
        await using SchedulingWorld w = await SchedulingWorld.Create(nameof(IndividualNoShow_Repeated_IsRejected));
        AppointmentDto created = await w.CreateAppointment(SchedulingWorld.Future(10));
        await w.SetBookingStatus(created.Id, w.Client, BookingStatus.NoShow);

        await SchedulingAssert.BusinessRule(ErrorCodes.AlreadyCompleted, () => w.SetBookingStatus(created.Id, w.Client, BookingStatus.NoShow));
    }

    [Fact]
    public async Task GroupNoShow_ThroughTheAttendanceAdapter_MapsAttendedFalseToNoShow()
    {
        await using SchedulingWorld w = await SchedulingWorld.Create(nameof(GroupNoShow_ThroughTheAttendanceAdapter_MapsAttendedFalseToNoShow));
        var svc = await w.AddGroupService();
        var group = await w.CreateGroup(svc, capacity: 3);
        await w.AddGroupMember(group, w.Client);
        Appointment occurrence = await w.GenerateSingleOccurrence(group);

        GroupAttendanceListDto list = await w.GroupAttendance.SetAttendance(w.OrganizationId, w.ActorUserId, true, occurrence.Id.Value,
            new SetGroupAttendanceRequest { ClientId = w.Client.Id.Value, Attended = false });

        Booking b = await w.LoadBooking(occurrence.Id.Value, w.Client);
        Assert.Equal(BookingStatus.NoShow, b.Status);
        Assert.Equal(1, b.StatusVersion);
        Assert.False(Assert.Single(list.Recorded).Attended);
        Assert.Empty(await w.LoadPayments(b.Id.Value));
        Assert.Single(await w.LoadOutbox(), m => m.Type == OutboxEventTypes.BookingNoShowV1);
    }

    #endregion

    #region L. No-show — appointment-wide path

    [Fact]
    public async Task MarkNoShow_OnTheAppointment_EndsTheAppointmentCancelled_AndEveryConfirmedBookingNoShow()
    {
        await using SchedulingWorld w = await SchedulingWorld.Create(nameof(MarkNoShow_OnTheAppointment_EndsTheAppointmentCancelled_AndEveryConfirmedBookingNoShow));
        Client partner = await w.AddClient("Partner", "Client");
        AppointmentDto created = await w.CreateAppointment(SchedulingWorld.Future(10), extraClients: partner);

        AppointmentDto dto = await w.Appointments.MarkNoShow(w.OrganizationId, w.ActorUserId, true, created.Id,
            new AppointmentCancelRequest { CancellationReason = "nobody came" });

        Assert.Equal(AppointmentStatus.Cancelled, dto.Status);
        Appointment a = await w.LoadAppointment(created.Id);
        Assert.Equal(AppointmentStatus.Cancelled, a.Status);
        Assert.Equal("nobody came", a.CancellationReason);
        Assert.All(a.Bookings, b =>
        {
            Assert.Equal(BookingStatus.NoShow, b.Status);
            Assert.Equal(1, b.StatusVersion);
            Assert.Equal("nobody came", b.CancellationReason);
            Assert.Null(b.IsLateCancellation);
        });
        Assert.Equal(2, (await w.LoadOutbox()).Count(m => m.Type == OutboxEventTypes.BookingNoShowV1));
        Assert.Single(await w.LoadAuditLog(created.Id), l => l.ChangeType == "Status" && l.OldValue == "Scheduled" && l.NewValue == "Cancelled");
        Assert.Equal(2, (await w.LoadAuditLog(created.Id)).Count(l => l.ChangeType == "BookingStatus"));
    }

    [Fact]
    public async Task MarkNoShow_OnTheAppointment_LeavesAlreadyTerminalBookingsUntouched()
    {
        await using SchedulingWorld w = await SchedulingWorld.Create(nameof(MarkNoShow_OnTheAppointment_LeavesAlreadyTerminalBookingsUntouched));
        Client cancelled = await w.AddClient("Cancelled", "Client");
        Appointment seeded = await w.SeedAppointment(SchedulingWorld.Future(10),
            bookings: new[] { (w.Client, BookingStatus.Confirmed, 50m), (cancelled, BookingStatus.Cancelled, 50m) });

        await w.Appointments.MarkNoShow(w.OrganizationId, w.ActorUserId, true, seeded.Id.Value, new AppointmentCancelRequest { CancellationReason = "x" });

        Appointment a = await w.LoadAppointment(seeded.Id.Value);
        Assert.Equal(BookingStatus.NoShow, a.Bookings.Single(b => b.ClientId == w.Client.Id).Status);
        Booking untouched = a.Bookings.Single(b => b.ClientId == cancelled.Id);
        Assert.Equal(BookingStatus.Cancelled, untouched.Status);
        Assert.Equal(0, untouched.StatusVersion);
    }

    [Fact]
    public async Task MarkNoShow_OnACompletedAppointment_IsRejected()
    {
        await using SchedulingWorld w = await SchedulingWorld.Create(nameof(MarkNoShow_OnACompletedAppointment_IsRejected));
        AppointmentDto completed = await w.CompleteNew(w.CompleteRequest(SchedulingWorld.Past(10)));

        await SchedulingAssert.BusinessRule(ErrorCodes.AlreadyCompleted,
            () => w.Appointments.MarkNoShow(w.OrganizationId, w.ActorUserId, true, completed.Id, new AppointmentCancelRequest()));
    }

    [Fact]
    public void TheAppointmentStatusEnum_HasNoNoShowMember()
    {
        Assert.Equal(new[] { "Scheduled", "Completed", "Cancelled" }, Enum.GetNames<AppointmentStatus>());
    }

    #endregion

    #region M. Cancellation — booking level

    [Fact]
    public async Task IndividualCancel_SetsStatusReasonVersionAndFlag_AndLeavesTheAppointmentScheduled()
    {
        await using SchedulingWorld w = await SchedulingWorld.Create(nameof(IndividualCancel_SetsStatusReasonVersionAndFlag_AndLeavesTheAppointmentScheduled));
        Client partner = await w.AddClient("Partner", "Client");
        AppointmentDto created = await w.CreateAppointment(SchedulingWorld.Future(10), extraClients: partner);

        BookingDto dto = await w.SetBookingStatus(created.Id, w.Client, BookingStatus.Cancelled, "client cancelled");

        Assert.Equal(BookingStatus.Cancelled, dto.Status);
        Appointment a = await w.LoadAppointment(created.Id);
        Assert.Equal(AppointmentStatus.Scheduled, a.Status);
        Booking b = a.Bookings.Single(x => x.ClientId == w.Client.Id);
        Assert.Equal(BookingStatus.Cancelled, b.Status);
        Assert.Equal(1, b.StatusVersion);
        Assert.Equal("client cancelled", b.CancellationReason);
        Assert.Equal(false, b.IsLateCancellation); // far in the future, well before any cutoff
        Assert.Equal(BookingStatus.Confirmed, a.Bookings.Single(x => x.ClientId == partner.Id).Status);
    }

    [Fact]
    public async Task IndividualCancel_OfTheLastBooking_DoesNotCancelTheAppointmentFrame()
    {
        await using SchedulingWorld w = await SchedulingWorld.Create(nameof(IndividualCancel_OfTheLastBooking_DoesNotCancelTheAppointmentFrame));
        AppointmentDto created = await w.CreateAppointment(SchedulingWorld.Future(10));

        await w.SetBookingStatus(created.Id, w.Client, BookingStatus.Cancelled);

        // The frame keeps blocking the employee: nothing collapses an appointment whose every booking is cancelled.
        Assert.Equal(AppointmentStatus.Scheduled, (await w.LoadAppointment(created.Id)).Status);
        Client other = await w.AddClient("Other", "Client");
        await SchedulingAssert.BusinessRule(ErrorCodes.AppointmentOverlap, () => w.CreateAppointment(SchedulingWorld.Future(10), client: other));
    }

    [Fact]
    public async Task IndividualCancel_EmitsACancelledEvent_AndWritesAnAuditRow()
    {
        await using SchedulingWorld w = await SchedulingWorld.Create(nameof(IndividualCancel_EmitsACancelledEvent_AndWritesAnAuditRow));
        AppointmentDto created = await w.CreateAppointment(SchedulingWorld.Future(10));

        await w.SetBookingStatus(created.Id, w.Client, BookingStatus.Cancelled);

        Booking b = await w.LoadBooking(created.Id, w.Client);
        OutboxMessage message = Assert.Single(await w.LoadOutbox());
        Assert.Equal(OutboxEventTypes.BookingCancelledV1, message.Type);
        Assert.Equal($"booking-cancelled:{b.Id}:1", message.IdempotencyKey);
        AppointmentAuditLog audit = Assert.Single(await w.LoadAuditLog(created.Id), l => l.ChangeType == "BookingStatus");
        Assert.Equal("Confirmed", audit.OldValue);
        Assert.Equal("Cancelled", audit.NewValue);
        Assert.Equal(b.Id, audit.BookingId);
    }

    [Fact]
    public async Task IndividualCancel_DoesNotVoidOrRefundAnExistingManualPayment()
    {
        await using SchedulingWorld w = await SchedulingWorld.Create(nameof(IndividualCancel_DoesNotVoidOrRefundAnExistingManualPayment));
        AppointmentDto created = await w.CreateAppointment(SchedulingWorld.Future(10));
        Guid bookingId = created.Bookings.Single().Id;
        await w.PayBookingViaCheckout(bookingId, w.Client, 20m);

        await w.SetBookingStatus(created.Id, w.Client, BookingStatus.Cancelled);

        // Money is left exactly where it was; refund/void is a separate, explicit checkout operation.
        Payment payment = Assert.Single(await w.LoadPayments(bookingId));
        Assert.Equal(PaymentStatus.Completed, payment.Status);
        Assert.Equal(20m, payment.Amount);
        BookingDto dto = (await w.Appointments.GetById(w.OrganizationId, created.Id)).Bookings.Single();
        Assert.Equal(20m, dto.PaidAmount);
    }

    [Fact]
    public async Task IndividualCancel_ClassifiesLateCancellationAgainstTheOrganizationCutoff_UsingTheRealClock()
    {
        await using SchedulingWorld w = await SchedulingWorld.Create(nameof(IndividualCancel_ClassifiesLateCancellationAgainstTheOrganizationCutoff_UsingTheRealClock));
        await w.SetCancellationCutoffMinutes(60);
        DateTimeOffset now = DateTimeOffset.UtcNow;
        Client lateClient = await w.AddClient("Late", "Client");
        Employee otherEmployee = await w.AddEmployee("Other");
        // Relative-to-now start times (the only tests that depend on the wall clock): 30 min ahead is inside the 60-min
        // cutoff, 3 h ahead is outside. Seeded directly because a real Create at "now + 30 min" would depend on working hours.
        Appointment soon = await w.SeedAppointment(now.AddMinutes(30), bookings: (w.Client, BookingStatus.Confirmed, 50m));
        Appointment later = await w.SeedAppointment(now.AddHours(3), employee: otherEmployee, bookings: (lateClient, BookingStatus.Confirmed, 50m));

        await w.SetBookingStatus(soon.Id.Value, w.Client, BookingStatus.Cancelled);
        await w.SetBookingStatus(later.Id.Value, lateClient, BookingStatus.Cancelled);

        Assert.Equal(true, (await w.LoadBooking(soon.Id.Value, w.Client)).IsLateCancellation);
        Assert.Equal(false, (await w.LoadBooking(later.Id.Value, lateClient)).IsLateCancellation);
    }

    [Fact]
    public void LateCancellationPolicy_TheBoundaryItselfIsNotLate_OneTickInsideIs()
    {
        DateTimeOffset now = new(2031, 3, 3, 8, 0, 0, TimeSpan.Zero);

        Assert.False(BookingCancellationPolicy.IsLateCancellation(now.AddMinutes(60), now, 60)); // exactly at the cutoff
        Assert.True(BookingCancellationPolicy.IsLateCancellation(now.AddMinutes(60).AddTicks(-1), now, 60));
        Assert.False(BookingCancellationPolicy.IsLateCancellation(now.AddMinutes(61), now, 60));
        Assert.True(BookingCancellationPolicy.IsLateCancellation(now.AddMinutes(-5), now, 60)); // already started
    }

    [Fact]
    public async Task IndividualCancel_CannotBeUndone_CancelledHasNoReturnPathForIndividualBookings()
    {
        await using SchedulingWorld w = await SchedulingWorld.Create(nameof(IndividualCancel_CannotBeUndone_CancelledHasNoReturnPathForIndividualBookings));
        AppointmentDto created = await w.CreateAppointment(SchedulingWorld.Future(10));
        await w.SetBookingStatus(created.Id, w.Client, BookingStatus.Cancelled);

        await SchedulingAssert.Validation(() => w.SetBookingStatus(created.Id, w.Client, BookingStatus.Confirmed));

        Booking b = await w.LoadBooking(created.Id, w.Client);
        Assert.Equal(BookingStatus.Cancelled, b.Status);
        Assert.Equal(1, b.StatusVersion);
    }

    [Fact]
    public async Task IndividualCancel_WithReturnPackageEntry_ReturnsAnAppliedCoverage_WhenOneIsPresent()
    {
        await using SchedulingWorld w = await SchedulingWorld.Create(nameof(IndividualCancel_WithReturnPackageEntry_ReturnsAnAppliedCoverage_WhenOneIsPresent));
        ClientPackage package = await w.AddClientPackage(w.Client, w.Service, 5, new DateTimeOffset(2035, 1, 1, 0, 0, 0, TimeSpan.Zero));
        Appointment seeded = await w.SeedAppointment(SchedulingWorld.Future(10), bookings: (w.Client, BookingStatus.Confirmed, 50m));
        await w.SeedCoverageApplied(await w.LoadBooking(seeded.Id.Value, w.Client), package); // 5 -> 4 (seeded state, see helper)

        await w.SetBookingStatus(seeded.Id.Value, w.Client, BookingStatus.Cancelled, returnPackageEntry: true);

        Booking b = await w.LoadBooking(seeded.Id.Value, w.Client);
        Assert.True(b.PackageCoverageApplied);
        Assert.True(b.PackageCoverageReturned);
        Assert.NotNull(b.PackageCoverageReturnedAt);
        Assert.Equal(w.ActorUserId, b.PackageCoverageReturnedBy);
        Assert.Equal(5, (await w.LoadClientPackage(package.Id.Value)).ServiceEntries.Single().RemainingEntries);
        Assert.Single(await w.LoadAuditLog(seeded.Id.Value), l => l.ChangeType == "BookingPackageCoverageReturned");
    }

    [Fact]
    public async Task IndividualCancel_WithoutReturnPackageEntry_KeepsTheEntryConsumed_ReturnIsOptIn()
    {
        await using SchedulingWorld w = await SchedulingWorld.Create(nameof(IndividualCancel_WithoutReturnPackageEntry_KeepsTheEntryConsumed_ReturnIsOptIn));
        ClientPackage package = await w.AddClientPackage(w.Client, w.Service, 5, new DateTimeOffset(2035, 1, 1, 0, 0, 0, TimeSpan.Zero));
        Appointment seeded = await w.SeedAppointment(SchedulingWorld.Future(10), bookings: (w.Client, BookingStatus.Confirmed, 50m));
        await w.SeedCoverageApplied(await w.LoadBooking(seeded.Id.Value, w.Client), package);

        await w.SetBookingStatus(seeded.Id.Value, w.Client, BookingStatus.Cancelled);

        Assert.False((await w.LoadBooking(seeded.Id.Value, w.Client)).PackageCoverageReturned);
        Assert.Equal(4, (await w.LoadClientPackage(package.Id.Value)).ServiceEntries.Single().RemainingEntries);
    }

    #endregion

    #region M. Cancellation — group booking and appointment-wide

    [Fact]
    public async Task GroupCancel_ComputesTheLateFlag_AndIsIdempotentOnRepeat()
    {
        await using SchedulingWorld w = await SchedulingWorld.Create(nameof(GroupCancel_ComputesTheLateFlag_AndIsIdempotentOnRepeat));
        var svc = await w.AddGroupService();
        var group = await w.CreateGroup(svc, capacity: 3);
        await w.AddGroupMember(group, w.Client);
        Appointment occurrence = await w.GenerateSingleOccurrence(group);

        await w.SetBookingStatus(occurrence.Id.Value, w.Client, BookingStatus.Cancelled, "first");
        await w.SetBookingStatus(occurrence.Id.Value, w.Client, BookingStatus.Cancelled, "second");

        Booking b = await w.LoadBooking(occurrence.Id.Value, w.Client);
        Assert.Equal(false, b.IsLateCancellation);
        Assert.Equal(1, b.StatusVersion); // no second increment
        Assert.Equal("second", b.CancellationReason); // but the reason is overwritten on the repeat
        Assert.Single(await w.LoadOutbox(), m => m.Type == OutboxEventTypes.BookingCancelledV1); // no second event
    }

    [Fact]
    public async Task AppointmentCancel_CancelsTheFrameAndEveryConfirmedBooking_WithoutTheLateFlag()
    {
        await using SchedulingWorld w = await SchedulingWorld.Create(nameof(AppointmentCancel_CancelsTheFrameAndEveryConfirmedBooking_WithoutTheLateFlag));
        Client partner = await w.AddClient("Partner", "Client");
        AppointmentDto created = await w.CreateAppointment(SchedulingWorld.Future(10), extraClients: partner);

        AppointmentDto dto = await w.Appointments.Cancel(w.OrganizationId, w.ActorUserId, true, created.Id,
            new AppointmentCancelRequest { CancellationReason = "studio closed" });

        Assert.Equal(AppointmentStatus.Cancelled, dto.Status);
        Appointment a = await w.LoadAppointment(created.Id);
        Assert.Equal("studio closed", a.CancellationReason);
        Assert.All(a.Bookings, b =>
        {
            Assert.Equal(BookingStatus.Cancelled, b.Status);
            Assert.Equal(1, b.StatusVersion);
            Assert.Null(b.IsLateCancellation); // a business cancellation is never a "late client cancellation"
            Assert.Equal("studio closed", b.CancellationReason);
        });
        Assert.Equal(2, (await w.LoadOutbox()).Count(m => m.Type == OutboxEventTypes.BookingCancelledV1));
    }

    [Fact]
    public async Task AppointmentCancel_ReturnsThePackageEntryOnlyForTheClientsListed()
    {
        await using SchedulingWorld w = await SchedulingWorld.Create(nameof(AppointmentCancel_ReturnsThePackageEntryOnlyForTheClientsListed));
        Client partner = await w.AddClient("Partner", "Client");
        ClientPackage p1 = await w.AddClientPackage(w.Client, w.Service, 5, new DateTimeOffset(2035, 1, 1, 0, 0, 0, TimeSpan.Zero));
        ClientPackage p2 = await w.AddClientPackage(partner, w.Service, 5, new DateTimeOffset(2035, 1, 1, 0, 0, 0, TimeSpan.Zero));
        Appointment seeded = await w.SeedAppointment(SchedulingWorld.Future(10),
            bookings: new[] { (w.Client, BookingStatus.Confirmed, 50m), (partner, BookingStatus.Confirmed, 50m) });
        await w.SeedCoverageApplied(await w.LoadBooking(seeded.Id.Value, w.Client), p1);
        await w.SeedCoverageApplied(await w.LoadBooking(seeded.Id.Value, partner), p2);

        await w.Appointments.Cancel(w.OrganizationId, w.ActorUserId, true, seeded.Id.Value,
            new AppointmentCancelRequest { CancellationReason = "x", ReturnEntryForClientIds = new List<Guid> { w.Client.Id.Value } });

        Assert.Equal(5, (await w.LoadClientPackage(p1.Id.Value)).ServiceEntries.Single().RemainingEntries); // returned
        Assert.Equal(4, (await w.LoadClientPackage(p2.Id.Value)).ServiceEntries.Single().RemainingEntries); // not listed: stays consumed
    }

    [Fact]
    public async Task AppointmentCancel_OnACompletedAppointment_IsRejected()
    {
        await using SchedulingWorld w = await SchedulingWorld.Create(nameof(AppointmentCancel_OnACompletedAppointment_IsRejected));
        AppointmentDto completed = await w.CompleteNew(w.CompleteRequest(SchedulingWorld.Past(10)));

        await SchedulingAssert.BusinessRule(ErrorCodes.AlreadyCompleted,
            () => w.Appointments.Cancel(w.OrganizationId, w.ActorUserId, true, completed.Id, new AppointmentCancelRequest()));
    }

    [Fact]
    public async Task AppointmentCancel_Twice_IsAnIdempotentNoOpForBookings_ButOverwritesTheReason()
    {
        await using SchedulingWorld w = await SchedulingWorld.Create(nameof(AppointmentCancel_Twice_IsAnIdempotentNoOpForBookings_ButOverwritesTheReason));
        AppointmentDto created = await w.CreateAppointment(SchedulingWorld.Future(10));
        await w.Appointments.Cancel(w.OrganizationId, w.ActorUserId, true, created.Id, new AppointmentCancelRequest { CancellationReason = "first" });

        await w.Appointments.Cancel(w.OrganizationId, w.ActorUserId, true, created.Id, new AppointmentCancelRequest { CancellationReason = "second" });

        Appointment a = await w.LoadAppointment(created.Id);
        Assert.Equal("second", a.CancellationReason); // FINDING: the recorded reason is silently replaced
        Assert.Equal("first", a.Bookings.Single().CancellationReason); // the Booking kept its original reason
        Assert.Single(await w.LoadOutbox());
        Assert.Single(await w.LoadAuditLog(created.Id), l => l.ChangeType == "Status");
    }

    #endregion

    #region Outbox → Notification (occurrence-aware, StatusVersion-keyed)

    [Fact]
    public async Task CancelledEvent_ProducesAPendingNotificationForThatOccurrence_AndReprocessingIsIdempotent()
    {
        await using SchedulingWorld w = await SchedulingWorld.Create(nameof(CancelledEvent_ProducesAPendingNotificationForThatOccurrence_AndReprocessingIsIdempotent));
        AppointmentDto created = await w.CreateAppointment(SchedulingWorld.Future(10));
        await w.SetBookingStatus(created.Id, w.Client, BookingStatus.Cancelled);
        OutboxMessage message = Assert.Single(await w.LoadOutbox());

        await w.ProcessOutbox(message);
        await w.ProcessOutbox(message);

        Notification n = Assert.Single(await w.LoadNotifications());
        Assert.Equal(NotificationType.BookingCancelled, n.Type);
        Assert.Equal(NotificationSourceType.Booking, n.SourceType);
        Assert.Equal(created.Bookings.Single().Id, n.SourceId);
        Assert.Equal(1, n.SourceVersion);
        Assert.Equal(NotificationStatus.Pending, n.Status);
        Assert.Equal(w.Client.Id, n.ClientId);
    }

    [Fact]
    public async Task ANoShowCorrection_CancelsThePendingNotificationOfTheCorrectedOccurrence()
    {
        await using SchedulingWorld w = await SchedulingWorld.Create(nameof(ANoShowCorrection_CancelsThePendingNotificationOfTheCorrectedOccurrence));
        AppointmentDto created = await w.CreateAppointment(SchedulingWorld.Future(10));
        await w.SetBookingStatus(created.Id, w.Client, BookingStatus.NoShow);
        await w.ProcessOutbox(Assert.Single(await w.LoadOutbox()));
        Assert.Equal(NotificationStatus.Pending, Assert.Single(await w.LoadNotifications()).Status);

        await w.SetBookingStatus(created.Id, w.Client, BookingStatus.Confirmed); // correction: NoShow(1) -> Confirmed(2)

        Notification n = Assert.Single(await w.LoadNotifications());
        Assert.Equal(1, n.SourceVersion);
        Assert.Equal(NotificationStatus.Cancelled, n.Status);
    }

    [Fact]
    public async Task ALateProcessedEventForAnAlreadyCorrectedOccurrence_IsStoredAsCancelled_NeverAsPending()
    {
        await using SchedulingWorld w = await SchedulingWorld.Create(nameof(ALateProcessedEventForAnAlreadyCorrectedOccurrence_IsStoredAsCancelled_NeverAsPending));
        AppointmentDto created = await w.CreateAppointment(SchedulingWorld.Future(10));
        await w.SetBookingStatus(created.Id, w.Client, BookingStatus.NoShow);
        await w.SetBookingStatus(created.Id, w.Client, BookingStatus.Confirmed); // corrected BEFORE the outbox ran

        await w.ProcessOutbox(Assert.Single(await w.LoadOutbox()));

        Assert.Equal(NotificationStatus.Cancelled, Assert.Single(await w.LoadNotifications()).Status);
    }

    [Fact]
    public async Task ASecondNoShowOccurrence_GetsItsOwnNotification_IndependentOfTheCancelledFirstOne()
    {
        await using SchedulingWorld w = await SchedulingWorld.Create(nameof(ASecondNoShowOccurrence_GetsItsOwnNotification_IndependentOfTheCancelledFirstOne));
        AppointmentDto created = await w.CreateAppointment(SchedulingWorld.Future(10));
        await w.SetBookingStatus(created.Id, w.Client, BookingStatus.NoShow);       // v1
        await w.SetBookingStatus(created.Id, w.Client, BookingStatus.Confirmed);    // v2
        await w.SetBookingStatus(created.Id, w.Client, BookingStatus.NoShow);       // v3
        foreach (OutboxMessage message in await w.LoadOutbox())
            await w.ProcessOutbox(message);

        List<Notification> notifications = (await w.LoadNotifications()).OrderBy(n => n.SourceVersion).ToList();

        Assert.Equal(new[] { 1, 3 }, notifications.Select(n => n.SourceVersion).ToArray());
        Assert.Equal(NotificationStatus.Cancelled, notifications[0].Status); // v1 was corrected
        Assert.Equal(NotificationStatus.Pending, notifications[1].Status);   // v3 is the live no-show
    }

    [Fact]
    public async Task ACancelledToConfirmedGroupCorrection_CancelsThePendingCancelledNotification()
    {
        await using SchedulingWorld w = await SchedulingWorld.Create(nameof(ACancelledToConfirmedGroupCorrection_CancelsThePendingCancelledNotification));
        var svc = await w.AddGroupService();
        var group = await w.CreateGroup(svc, capacity: 3);
        await w.AddGroupMember(group, w.Client);
        Appointment occurrence = await w.GenerateSingleOccurrence(group);
        await w.SetBookingStatus(occurrence.Id.Value, w.Client, BookingStatus.Cancelled);
        await w.ProcessOutbox(Assert.Single(await w.LoadOutbox()));

        await w.SetBookingStatus(occurrence.Id.Value, w.Client, BookingStatus.Confirmed);

        Assert.Equal(NotificationStatus.Cancelled, Assert.Single(await w.LoadNotifications()).Status);
    }

    #endregion
}
