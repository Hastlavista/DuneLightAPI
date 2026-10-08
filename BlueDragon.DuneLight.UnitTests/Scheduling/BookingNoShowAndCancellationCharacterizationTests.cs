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
        AppointmentDto created = await w.CreateAppointment(SchedulingWorld.Past(10), extraClients: partner);

        BookingDto dto = await w.SetBookingStatus(created.Id, w.Client, BookingStatus.NoShow, "did not show");

        Assert.Equal(BookingStatusSummary.NoShow, dto.Status);
        Appointment a = await w.LoadAppointment(created.Id);
        Assert.Equal(AppointmentStatus.Scheduled, a.Status); // the frame does not follow a single-booking no-show
        Booking b = a.Bookings.Single(x => x.ClientId == w.Client.Id);
        Assert.Equal(BookingStatus.NoShow, b.Status);
        Assert.Equal(1, b.StatusVersion);
        // CHANGED in P1 (D3, intentional): a no-show has its own NoShowAt/By/Reason — it no longer reuses CancellationReason.
        BookingSegmentParticipation p = b.Participations.Single();
        Assert.Null(p.CancellationReason);
        Assert.Equal("did not show", p.NoShowReason);
        Assert.NotNull(p.NoShowAt);
        Assert.Equal(w.ActorUserId, p.NoShowBy);
        Assert.Null(b.IsLateCancellation); // lateness is a Client-cancellation-only classification
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
        AppointmentDto created = await w.CreateAppointment(SchedulingWorld.Past(10));

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
        AppointmentDto created = await w.CreateAppointment(SchedulingWorld.Past(10));

        await w.SetBookingStatus(created.Id, w.Client, BookingStatus.NoShow);

        Booking b = await w.LoadBooking(created.Id, w.Client);
        OutboxMessage message = Assert.Single(await w.LoadOutbox());
        Assert.Equal(OutboxEventTypes.BookingNoShowV1, message.Type);
        Assert.Equal($"booking-noshow:{b.Participations.Single().Id}:1", message.IdempotencyKey); // M0: participation occurrence
        Assert.Equal(OutboxMessageStatus.Pending, message.Status);
    }


    [Fact]
    public async Task IndividualNoShow_Repeated_IsATrueNoOp()
    {
        // CHANGED in P1 (D12, intentional): a repeated same-status command is a true no-op (it used to be ALREADY_COMPLETED).
        await using SchedulingWorld w = await SchedulingWorld.Create(nameof(IndividualNoShow_Repeated_IsATrueNoOp));
        AppointmentDto created = await w.CreateAppointment(SchedulingWorld.Past(10));
        await w.SetBookingStatus(created.Id, w.Client, BookingStatus.NoShow);

        await w.SetBookingStatus(created.Id, w.Client, BookingStatus.NoShow);

        Assert.Equal(1, (await w.LoadBooking(created.Id, w.Client)).StatusVersion);
        Assert.Single(await w.LoadOutbox());
        Assert.Single(await w.LoadAuditLog(created.Id), l => l.ChangeType == "BookingStatus");
        Assert.Single(await w.LoadPolicyConsequences(created.Id)); // one NoShow consequence, not two
    }

    [Fact]
    public async Task IndividualNoShow_BeforeTheSegmentStarts_IsRejected()
    {
        // CHANGED in P1 (D3, intentional): a no-show before start used to be allowed for existing participations.
        await using SchedulingWorld w = await SchedulingWorld.Create(nameof(IndividualNoShow_BeforeTheSegmentStarts_IsRejected));
        AppointmentDto created = await w.CreateAppointment(SchedulingWorld.Future(10));

        await SchedulingAssert.BusinessRule(ErrorCodes.AttendanceBeforeStart, () => w.SetBookingStatus(created.Id, w.Client, BookingStatus.NoShow));
        Assert.Equal(BookingStatus.Confirmed, (await w.LoadBooking(created.Id, w.Client)).Status);
    }

    [Fact]
    public async Task GroupNoShow_ThroughTheAttendanceAdapter_MapsAttendedFalseToNoShow()
    {
        await using SchedulingWorld w = await SchedulingWorld.Create(nameof(GroupNoShow_ThroughTheAttendanceAdapter_MapsAttendedFalseToNoShow));
        var svc = await w.AddGroupService();
        var group = await w.CreateGroup(svc, capacity: 3);
        await w.AddGroupMember(group, w.Client);
        Appointment occurrence = await w.GenerateSingleOccurrence(group);
        await w.MoveToPast(occurrence.Id.Value); // P1: attendance (no-show) only after the segment started

        GroupAttendanceListDto list = await w.GroupAttendance.SetAttendance(w.OrganizationId, w.ActorUserId, true, occurrence.Id.Value,
            new SetGroupAttendanceRequest { ClientId = w.Client.Id.Value, SegmentId = Assert.Single(occurrence.Segments).Id, Attended = false });

        Booking b = await w.LoadBooking(occurrence.Id.Value, w.Client);
        Assert.Equal(BookingStatus.NoShow, b.Status);
        Assert.Equal(1, b.StatusVersion);
        Assert.False(Assert.Single(Assert.Single(list.Segments).Recorded).Attended);
        Assert.Empty(await w.LoadPayments(b.Id.Value));
        Assert.Single(await w.LoadOutbox(), m => m.Type == OutboxEventTypes.BookingNoShowV1);
    }

    #endregion

    #region L. No-show — appointment-wide path

    [Fact]
    public async Task MarkNoShow_OnTheAppointment_EndsTheAppointmentClosed_AndEveryConfirmedBookingNoShow()
    {
        await using SchedulingWorld w = await SchedulingWorld.Create(nameof(MarkNoShow_OnTheAppointment_EndsTheAppointmentClosed_AndEveryConfirmedBookingNoShow));
        Client partner = await w.AddClient("Partner", "Client");
        AppointmentDto created = await w.CreateAppointment(SchedulingWorld.Past(10), extraClients: partner);

        AppointmentDto dto = await w.Appointments.MarkNoShow(w.OrganizationId, w.ActorUserId, true, created.Id,
            new NoShowRequest { NoShowReason = "nobody came" });

        // M1A: the appointment status is DERIVED — every participation NoShow is an operationally resolved (not cancelled)
        // outcome, so the bulk no-show ends Closed (it used to be forced to Cancelled).
        Assert.Equal(AppointmentStatus.Closed, dto.Status);
        Appointment a = await w.LoadAppointment(created.Id);
        Assert.Equal(AppointmentStatus.Closed, a.Status);
        // CHANGED in P1 (D3): the no-show reason lives on each participation (NoShowReason); a bulk no-show is not an
        // appointment cancellation and no longer writes Appointment.CancellationReason.
        Assert.Null(a.CancellationReason);
        Assert.All(a.Bookings, b =>
        {
            Assert.Equal(BookingStatus.NoShow, b.Status);
            Assert.Equal(1, b.StatusVersion);
            Assert.Equal("nobody came", b.Participations.Single().NoShowReason);
            Assert.Null(b.CancellationReason);
            Assert.Null(b.IsLateCancellation);
        });
        Assert.Equal(2, (await w.LoadOutbox()).Count(m => m.Type == OutboxEventTypes.BookingNoShowV1));
        Assert.Single(await w.LoadAuditLog(created.Id), l => l.ChangeType == "Status" && l.OldValue == "Scheduled" && l.NewValue == "Closed");
        Assert.Equal(2, (await w.LoadAuditLog(created.Id)).Count(l => l.ChangeType == "BookingStatus"));
    }

    [Fact]
    public async Task MarkNoShow_OnTheAppointment_LeavesAlreadyTerminalBookingsUntouched()
    {
        await using SchedulingWorld w = await SchedulingWorld.Create(nameof(MarkNoShow_OnTheAppointment_LeavesAlreadyTerminalBookingsUntouched));
        Client cancelled = await w.AddClient("Cancelled", "Client");
        Appointment seeded = await w.SeedAppointment(SchedulingWorld.Past(10),
            bookings: new[] { (w.Client, BookingStatus.Confirmed, 50m), (cancelled, BookingStatus.Cancelled, 50m) });

        await w.Appointments.MarkNoShow(w.OrganizationId, w.ActorUserId, true, seeded.Id.Value, new NoShowRequest { NoShowReason = "x" });

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
            () => w.Appointments.MarkNoShow(w.OrganizationId, w.ActorUserId, true, completed.Id, new NoShowRequest()));
    }

    [Fact]
    public void TheAppointmentStatusEnum_HasNoNoShowMember()
    {
        // M1A: the lifecycle is exactly Scheduled / Cancelled / Closed — no NoShow and no Completed (execution belongs to
        // the participation).
        Assert.Equal(new[] { "Scheduled", "Cancelled", "Closed" }, Enum.GetNames<AppointmentStatus>());
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

        Assert.Equal(BookingStatusSummary.Cancelled, dto.Status);
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
    public async Task IndividualCancel_OfTheLastBooking_DoesNotCancelTheAppointment()
    {
        await using SchedulingWorld w = await SchedulingWorld.Create(nameof(IndividualCancel_OfTheLastBooking_DoesNotCancelTheAppointment));
        AppointmentDto created = await w.CreateAppointment(SchedulingWorld.Future(10));

        await w.SetBookingStatus(created.Id, w.Client, BookingStatus.Cancelled);

        // M1A.1: only an explicit Appointment cancellation cancels the session — a participation cancellation (even of the
        // last one) leaves it Scheduled, so the frame keeps blocking the employee (the pre-M1A behaviour again).
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
        Assert.Equal($"booking-cancelled:{b.Participations.Single().Id}:1", message.IdempotencyKey); // M0: participation occurrence
        AppointmentAuditLog audit = Assert.Single(await w.LoadAuditLog(created.Id), l => l.ChangeType == "BookingStatus");
        Assert.Equal("Confirmed", audit.OldValue);
        Assert.Equal("Cancelled", audit.NewValue);
        Assert.Equal(b.Id, audit.BookingId);
        Assert.Equal(b.Participations.Single().Id, audit.BookingSegmentParticipationId);
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
        await w.SetCancellationWindowMinutes(60);
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

        Assert.False(CancellationPolicyRules.IsLateCancellation(now.AddMinutes(60), now, 60)); // exactly at the cutoff
        Assert.True(CancellationPolicyRules.IsLateCancellation(now.AddMinutes(60).AddTicks(-1), now, 60));
        Assert.False(CancellationPolicyRules.IsLateCancellation(now.AddMinutes(61), now, 60));
        Assert.True(CancellationPolicyRules.IsLateCancellation(now.AddMinutes(-5), now, 60)); // already started
    }

    [Fact]
    public async Task IndividualCancel_CanBeCorrectedBackToConfirmed_AndTheMetadataIsCleared()
    {
        // CHANGED in P1 (D12, intentional): Individual Cancelled -> Confirmed used to be rejected (400); one matrix now allows it.
        await using SchedulingWorld w = await SchedulingWorld.Create(nameof(IndividualCancel_CanBeCorrectedBackToConfirmed_AndTheMetadataIsCleared));
        AppointmentDto created = await w.CreateAppointment(SchedulingWorld.Future(10));
        await w.SetBookingStatus(created.Id, w.Client, BookingStatus.Cancelled, "changed my mind");

        await w.SetBookingStatus(created.Id, w.Client, BookingStatus.Confirmed);

        Booking b = await w.LoadBooking(created.Id, w.Client);
        Assert.Equal(BookingStatus.Confirmed, b.Status);
        Assert.Equal(2, b.StatusVersion);
        BookingSegmentParticipation p = b.Participations.Single();
        Assert.Null(p.CancellationInitiator);
        Assert.Null(p.CancelledAt);
        Assert.Null(p.CancellationReason);
        Assert.Null(p.IsLateCancellation);
        Assert.Null(p.CancellationPolicyId);
    }



    #endregion

    #region M. Cancellation — group booking and appointment-wide

    [Fact]
    public async Task GroupCancel_ComputesTheLateFlag_AndARepeatIsATrueNoOp()
    {
        await using SchedulingWorld w = await SchedulingWorld.Create(nameof(GroupCancel_ComputesTheLateFlag_AndARepeatIsATrueNoOp));
        var svc = await w.AddGroupService();
        var group = await w.CreateGroup(svc, capacity: 3);
        await w.AddGroupMember(group, w.Client);
        Appointment occurrence = await w.GenerateSingleOccurrence(group);

        await w.SetBookingStatus(occurrence.Id.Value, w.Client, BookingStatus.Cancelled, "first");
        await w.SetBookingStatus(occurrence.Id.Value, w.Client, BookingStatus.Cancelled, "second");

        Booking b = await w.LoadBooking(occurrence.Id.Value, w.Client);
        Assert.Equal(false, b.IsLateCancellation);
        Assert.Equal(1, b.StatusVersion); // no second increment
        // CHANGED in P1 (D12): same status is a true no-op — no re-stamping, so the first reason stays.
        Assert.Equal("first", b.CancellationReason);
        Assert.Single(await w.LoadOutbox(), m => m.Type == OutboxEventTypes.BookingCancelledV1); // no second event
    }

    [Fact]
    public async Task AppointmentCancel_CancelsEveryConfirmedBooking_AndClassifiesLatenessPerParticipation()
    {
        await using SchedulingWorld w = await SchedulingWorld.Create(nameof(AppointmentCancel_CancelsEveryConfirmedBooking_AndClassifiesLatenessPerParticipation));
        Client partner = await w.AddClient("Partner", "Client");
        AppointmentDto created = await w.CreateAppointment(SchedulingWorld.Future(10), extraClients: partner);

        AppointmentDto dto = await w.Appointments.Cancel(w.OrganizationId, w.ActorUserId, true, created.Id,
            SchedulingWorld.BusinessCancel("studio closed"));

        Assert.Equal(AppointmentStatus.Cancelled, dto.Status);
        Appointment a = await w.LoadAppointment(created.Id);
        Assert.Equal("studio closed", a.CancellationReason);
        Assert.All(a.Bookings, b =>
        {
            Assert.Equal(BookingStatus.Cancelled, b.Status);
            Assert.Equal(1, b.StatusVersion);
            // CHANGED in P1 (D2, intentional): an appointment-wide cancellation is always Business — no classification and no
            // consequence (M1E.1 had classified each participation as if the client cancelled).
            Assert.Null(b.IsLateCancellation);
            Assert.Equal(CancellationInitiator.Business, b.Participations.Single().CancellationInitiator);
            Assert.Equal("studio closed", b.CancellationReason);
        });
        Assert.Equal(2, (await w.LoadOutbox()).Count(m => m.Type == OutboxEventTypes.BookingCancelledV1));
    }


    [Fact]
    public async Task AppointmentCancel_OnACompletedAppointment_IsRejected()
    {
        await using SchedulingWorld w = await SchedulingWorld.Create(nameof(AppointmentCancel_OnACompletedAppointment_IsRejected));
        AppointmentDto completed = await w.CompleteNew(w.CompleteRequest(SchedulingWorld.Past(10)));

        await SchedulingAssert.BusinessRule(ErrorCodes.AlreadyCompleted,
            () => w.Appointments.Cancel(w.OrganizationId, w.ActorUserId, true, completed.Id, SchedulingWorld.BusinessCancel()));
    }

    [Fact]
    public async Task AppointmentCancel_Twice_IsAnIdempotentNoOpForBookings_ButOverwritesTheReason()
    {
        await using SchedulingWorld w = await SchedulingWorld.Create(nameof(AppointmentCancel_Twice_IsAnIdempotentNoOpForBookings_ButOverwritesTheReason));
        AppointmentDto created = await w.CreateAppointment(SchedulingWorld.Future(10));
        await w.Appointments.Cancel(w.OrganizationId, w.ActorUserId, true, created.Id, SchedulingWorld.BusinessCancel("first"));

        await w.Appointments.Cancel(w.OrganizationId, w.ActorUserId, true, created.Id, SchedulingWorld.BusinessCancel("second"));

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
        Assert.Equal(NotificationSourceType.Participation, n.SourceType); // M0: the occurrence source is the participation
        Assert.Equal(created.Bookings.Single().Participations.Single().Id, n.SourceId);
        Assert.Equal(1, n.SourceVersion);
        Assert.Equal(NotificationStatus.Pending, n.Status);
        Assert.Equal(w.Client.Id, n.ClientId);
    }

    [Fact]
    public async Task ANoShowCorrection_CancelsThePendingNotificationOfTheCorrectedOccurrence()
    {
        await using SchedulingWorld w = await SchedulingWorld.Create(nameof(ANoShowCorrection_CancelsThePendingNotificationOfTheCorrectedOccurrence));
        AppointmentDto created = await w.CreateAppointment(SchedulingWorld.Past(10));
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
        AppointmentDto created = await w.CreateAppointment(SchedulingWorld.Past(10));
        await w.SetBookingStatus(created.Id, w.Client, BookingStatus.NoShow);
        await w.SetBookingStatus(created.Id, w.Client, BookingStatus.Confirmed); // corrected BEFORE the outbox ran

        await w.ProcessOutbox(Assert.Single(await w.LoadOutbox()));

        Assert.Equal(NotificationStatus.Cancelled, Assert.Single(await w.LoadNotifications()).Status);
    }

    [Fact]
    public async Task ASecondNoShowOccurrence_GetsItsOwnNotification_IndependentOfTheCancelledFirstOne()
    {
        await using SchedulingWorld w = await SchedulingWorld.Create(nameof(ASecondNoShowOccurrence_GetsItsOwnNotification_IndependentOfTheCancelledFirstOne));
        AppointmentDto created = await w.CreateAppointment(SchedulingWorld.Past(10));
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
