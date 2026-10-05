#nullable disable
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using BlueDragon.DuneLight.Core.DTOs.Appointments;
using BlueDragon.DuneLight.Core.DTOs.Groups;
using BlueDragon.DuneLight.Core.Enums;
using BlueDragon.DuneLight.Core.Shared;
using BlueDragon.DuneLight.Infrastructure.Domain.Models.Groups;
using BlueDragon.DuneLight.Infrastructure.Domain.Models.Appointments;
using BlueDragon.DuneLight.Infrastructure.Domain.Models.Clients;
using BlueDragon.DuneLight.Infrastructure.Domain.Models.Commissions;
using BlueDragon.DuneLight.Infrastructure.Utils;
using ServiceEntity = BlueDragon.DuneLight.Infrastructure.Domain.Models.Catalog.Service;

namespace BlueDragon.DuneLight.UnitTests.Scheduling;

/// <summary>
/// Phase M1A — Appointment lifecycle is Scheduled / Cancelled / Closed, DERIVED from all of its participations
/// (<see cref="AppointmentLifecycle"/>): any Confirmed => Scheduled; otherwise all Cancelled => Cancelled; otherwise Closed
/// (operationally resolved — not "paid"). There is no Appointment Completed and no manual reopen.
/// </summary>
public class AppointmentLifecycleTests
{
    private const ParticipationStatus Cf = ParticipationStatus.Confirmed;
    private const ParticipationStatus Co = ParticipationStatus.Completed;
    private const ParticipationStatus Ca = ParticipationStatus.Cancelled;
    private const ParticipationStatus Ns = ParticipationStatus.NoShow;

    #region Derivation matrix

    // Columns: participation statuses, the CURRENT explicit Appointment cancellation, expected lifecycle.
    [Theory]
    // empty (valid: e.g. a generated group occurrence without members)
    [InlineData(new ParticipationStatus[0], false, AppointmentStatus.Scheduled)]
    [InlineData(new ParticipationStatus[0], true, AppointmentStatus.Cancelled)]
    // single participation
    [InlineData(new[] { Cf }, false, AppointmentStatus.Scheduled)]
    [InlineData(new[] { Co }, false, AppointmentStatus.Closed)]
    [InlineData(new[] { Ca }, false, AppointmentStatus.Scheduled)]   // individually cancelled: the session still exists
    [InlineData(new[] { Ca }, true, AppointmentStatus.Cancelled)]    // the session itself was cancelled
    [InlineData(new[] { Ns }, false, AppointmentStatus.Closed)]
    // two participations
    [InlineData(new[] { Cf, Cf }, false, AppointmentStatus.Scheduled)]
    [InlineData(new[] { Co, Cf }, false, AppointmentStatus.Scheduled)]
    [InlineData(new[] { Ca, Cf }, false, AppointmentStatus.Scheduled)]
    [InlineData(new[] { Ns, Cf }, false, AppointmentStatus.Scheduled)]
    [InlineData(new[] { Co, Co }, false, AppointmentStatus.Closed)]
    [InlineData(new[] { Co, Ca }, false, AppointmentStatus.Closed)]
    [InlineData(new[] { Co, Ca }, true, AppointmentStatus.Closed)]   // cancelled after partial execution
    [InlineData(new[] { Co, Ns }, false, AppointmentStatus.Closed)]
    [InlineData(new[] { Ca, Ca }, false, AppointmentStatus.Scheduled)]
    [InlineData(new[] { Ca, Ca }, true, AppointmentStatus.Cancelled)]
    [InlineData(new[] { Ca, Ns }, false, AppointmentStatus.Closed)]
    [InlineData(new[] { Ns, Ns }, false, AppointmentStatus.Closed)]
    // three-way terminal mix, order independence, and Confirmed always wins (a correction re-opens)
    [InlineData(new[] { Co, Ca, Ns }, false, AppointmentStatus.Closed)]
    [InlineData(new[] { Ns, Ca, Co }, true, AppointmentStatus.Closed)]
    [InlineData(new[] { Ca, Ca, Cf }, true, AppointmentStatus.Scheduled)]
    public void Derive_FollowsTheLockedRules(ParticipationStatus[] statuses, bool explicitlyCancelled, AppointmentStatus expected)
    {
        Assert.Equal(expected, AppointmentLifecycle.Derive(statuses, explicitlyCancelled));
    }

    [Fact]
    public void TheModel_HasNoAppointmentCompleted_AndNoAppointmentVersion()
    {
        Assert.Equal(new[] { "Scheduled", "Cancelled", "Closed" }, Enum.GetNames<AppointmentStatus>());
        Assert.Null(typeof(Appointment).GetProperty("StatusVersion"));
        Assert.Null(typeof(Booking).GetProperty("Status"));
    }

    #endregion

    #region Helpers

    private static async Task<(ServiceEntity Service, GroupDto Group, Appointment Occurrence, Client[] Members)> GroupOccurrence(
        SchedulingWorld w, int members, bool withCommission = false)
    {
        ServiceEntity svc = await w.AddGroupService();
        if (withCommission)
            await w.AddCommissionRule(w.Employee, svc, CommissionCalculationType.Fixed, 20m);
        GroupDto group = await w.CreateGroup(svc, capacity: 6);
        List<Client> clients = new();
        for (int i = 0; i < members; i++)
        {
            Client c = i == 0 ? w.Client : await w.AddClient($"M{i}", "Client");
            clients.Add(c);
            await w.AddGroupMember(group, c);
        }
        return (svc, group, await w.GenerateSingleOccurrence(group), clients.ToArray());
    }

    private static async Task<AppointmentStatus> StatusOf(SchedulingWorld w, Guid appointmentId) =>
        (await w.LoadAppointment(appointmentId)).Status;

    private static async Task<List<AppointmentAuditLog>> StatusAudit(SchedulingWorld w, Guid appointmentId) =>
        (await w.LoadAuditLog(appointmentId)).Where(l => l.ChangeType == "Status").OrderBy(l => l.ChangedAt).ToList();

    #endregion

    #region Participation transitions drive the aggregate

    [Fact]
    public async Task CompletingOneOfTwo_LeavesScheduled_AndResolvingTheLastOneCloses()
    {
        await using SchedulingWorld w = await SchedulingWorld.Create(nameof(CompletingOneOfTwo_LeavesScheduled_AndResolvingTheLastOneCloses));
        (_, _, Appointment occurrence, Client[] members) = await GroupOccurrence(w, 2);
        Guid id = occurrence.Id.Value;

        await w.SetBookingStatus(id, members[0], BookingStatus.Completed);
        Assert.Equal(AppointmentStatus.Scheduled, await StatusOf(w, id)); // Completed + Confirmed

        await w.SetBookingStatus(id, members[1], BookingStatus.Cancelled);
        Assert.Equal(AppointmentStatus.Closed, await StatusOf(w, id));    // Completed + Cancelled
        AppointmentAuditLog audit = Assert.Single(await StatusAudit(w, id));
        Assert.Equal(("Scheduled", "Closed"), (audit.OldValue, audit.NewValue));
    }

    [Fact]
    public async Task CancellingEveryParticipation_OneByOne_KeepsTheSessionScheduled_AndItStillAcceptsABooking()
    {
        await using SchedulingWorld w = await SchedulingWorld.Create(nameof(CancellingEveryParticipation_OneByOne_KeepsTheSessionScheduled_AndItStillAcceptsABooking));
        Client partner = await w.AddClient("Partner", "Client");
        AppointmentDto created = await w.CreateAppointment(SchedulingWorld.Future(10), extraClients: partner);

        await w.SetBookingStatus(created.Id, w.Client, BookingStatus.Cancelled);
        await w.SetBookingStatus(created.Id, partner, BookingStatus.Cancelled);

        // M1A.1: individual cancellations never cancel the session — only an explicit Appointment cancel does.
        Appointment a = await w.LoadAppointment(created.Id);
        Assert.Equal((AppointmentStatus.Scheduled, (DateTimeOffset?)null), (a.Status, a.CancelledAt));
        Assert.Empty(await StatusAudit(w, created.Id));

        Client late = await w.AddClient("Late", "Client");
        AppointmentDto withLate = await w.AddClientToOnlySegment(created.Id, late);
        Assert.Equal(BookingStatusSummary.Confirmed, withLate.Bookings.Single(b => b.ClientId == late.Id).Status);
        Assert.Equal(AppointmentStatus.Scheduled, await StatusOf(w, created.Id));
    }

    [Fact]
    public async Task NoShowAndCancelled_IsClosed_NotCancelled()
    {
        await using SchedulingWorld w = await SchedulingWorld.Create(nameof(NoShowAndCancelled_IsClosed_NotCancelled));
        Client partner = await w.AddClient("Partner", "Client");
        AppointmentDto created = await w.CreateAppointment(SchedulingWorld.Future(10), extraClients: partner);

        await w.SetBookingStatus(created.Id, w.Client, BookingStatus.NoShow);
        await w.SetBookingStatus(created.Id, partner, BookingStatus.Cancelled);

        Assert.Equal(AppointmentStatus.Closed, await StatusOf(w, created.Id));
    }

    #endregion

    #region Corrections re-open automatically

    [Fact]
    public async Task Correction_ClosedToScheduled_WhenACompletedParticipationReturnsToConfirmed()
    {
        await using SchedulingWorld w = await SchedulingWorld.Create(nameof(Correction_ClosedToScheduled_WhenACompletedParticipationReturnsToConfirmed));
        Client partner = await w.AddClient("Partner", "Client");
        AppointmentDto created = await w.CreateAppointment(SchedulingWorld.Future(10), extraClients: partner);
        await w.SetBookingStatus(created.Id, partner, BookingStatus.Cancelled);
        // The cancelled partner is terminal history, so completing only the first client does not remove it.
        await w.CompleteParticipations(created.Id, w.CompleteRequest(SchedulingWorld.Future(10)));
        Assert.Equal(AppointmentStatus.Closed, await StatusOf(w, created.Id)); // Completed + Cancelled

        await w.SetBookingStatus(created.Id, w.Client, BookingStatus.Confirmed); // correction

        Assert.Equal(AppointmentStatus.Scheduled, await StatusOf(w, created.Id)); // Confirmed + Cancelled
        Assert.Equal(("Closed", "Scheduled"), ((await StatusAudit(w, created.Id)).Last().OldValue, (await StatusAudit(w, created.Id)).Last().NewValue));
    }

    [Fact]
    public async Task Correction_ExplicitlyCancelledToScheduled_ClearsTheCurrentCancellation_KeepingItsHistory()
    {
        await using SchedulingWorld w = await SchedulingWorld.Create(nameof(Correction_ExplicitlyCancelledToScheduled_ClearsTheCurrentCancellation_KeepingItsHistory));
        (_, _, Appointment occurrence, Client[] members) = await GroupOccurrence(w, 1);
        Guid id = occurrence.Id.Value;

        await w.Appointments.Cancel(w.OrganizationId, w.ActorUserId, true, id, new AppointmentCancelRequest { CancellationReason = "trainer ill" });
        Appointment cancelled = await w.LoadAppointment(id);
        Assert.Equal(AppointmentStatus.Cancelled, cancelled.Status);
        Assert.NotNull(cancelled.CancelledAt);
        Assert.Equal((w.ActorUserId, "trainer ill"), (cancelled.CancelledBy.Value, cancelled.CancellationReason));

        await w.SetBookingStatus(id, members[0], BookingStatus.Confirmed); // valid group correction (capacity allows)

        Appointment reopened = await w.LoadAppointment(id);
        Assert.Equal(AppointmentStatus.Scheduled, reopened.Status);
        Assert.Equal(((DateTimeOffset?)null, (Guid?)null, (string)null), (reopened.CancelledAt, reopened.CancelledBy, reopened.CancellationReason));
        Assert.Equal(2, (await w.LoadBooking(id, members[0])).StatusVersion); // only the participation version moves

        // History is preserved: the explicit cancellation, its clearing, and both status changes.
        List<AppointmentAuditLog> audit = await w.LoadAuditLog(id);
        Assert.Single(audit, l => l.ChangeType == "AppointmentCancelled" && l.NewValue == "trainer ill");
        Assert.Single(audit, l => l.ChangeType == "AppointmentCancellationCleared" && l.OldValue == "trainer ill");
        Assert.Equal(new[] { ("Scheduled", "Cancelled"), ("Cancelled", "Scheduled") },
            (await StatusAudit(w, id)).Select(l => (l.OldValue, l.NewValue)).ToArray());
    }

    #endregion

    #region Appointment-level and Booking-level cancellation

    [Fact]
    public async Task AppointmentCancel_AllActive_EndsCancelled()
    {
        await using SchedulingWorld w = await SchedulingWorld.Create(nameof(AppointmentCancel_AllActive_EndsCancelled));
        Client partner = await w.AddClient("Partner", "Client");
        AppointmentDto created = await w.CreateAppointment(SchedulingWorld.Future(10), extraClients: partner);

        AppointmentDto dto = await w.Appointments.Cancel(w.OrganizationId, w.ActorUserId, true, created.Id,
            new AppointmentCancelRequest { CancellationReason = "closed" });

        Assert.Equal(AppointmentStatus.Cancelled, dto.Status);
        Assert.All((await w.LoadAppointment(created.Id)).Bookings, b => Assert.Equal((BookingStatus.Cancelled, 1), (b.Status, b.StatusVersion)));
    }

    [Fact]
    public async Task AppointmentCancel_PartiallyCompleted_CancelsOnlyTheActive_AndEndsClosed()
    {
        await using SchedulingWorld w = await SchedulingWorld.Create(nameof(AppointmentCancel_PartiallyCompleted_CancelsOnlyTheActive_AndEndsClosed));
        (_, _, Appointment occurrence, Client[] members) = await GroupOccurrence(w, 2);
        Guid id = occurrence.Id.Value;
        await w.SetBookingStatus(id, members[0], BookingStatus.Completed);

        AppointmentDto dto = await w.Appointments.Cancel(w.OrganizationId, w.ActorUserId, true, id, new AppointmentCancelRequest { CancellationReason = "storm" });

        Booking completed = await w.LoadBooking(id, members[0]);
        Booking cancelled = await w.LoadBooking(id, members[1]);
        Assert.Equal((BookingStatus.Completed, 1), (completed.Status, completed.StatusVersion)); // terminal: untouched
        Assert.Equal((BookingStatus.Cancelled, 1, "storm"), (cancelled.Status, cancelled.StatusVersion, cancelled.CancellationReason));
        Assert.Equal(AppointmentStatus.Closed, dto.Status); // Completed + Cancelled
        // The appointment cancellation is still an auditable fact although the aggregate result is Closed.
        Assert.NotNull((await w.LoadAppointment(id)).CancelledAt);
        Assert.Single(await w.LoadAuditLog(id), l => l.ChangeType == "AppointmentCancelled" && l.NewValue == "storm");

        // Nothing active is left: a second appointment-level cancel is refused (operationally resolved).
        await SchedulingAssert.BusinessRule(Core.Shared.ErrorCodes.AlreadyCompleted,
            () => w.Appointments.Cancel(w.OrganizationId, w.ActorUserId, true, id, new AppointmentCancelRequest()));
    }

    [Fact]
    public async Task BookingCancel_ChangesOnlyThatBookingsActiveParticipations_AndTheAppointmentDerivesFromAll()
    {
        await using SchedulingWorld w = await SchedulingWorld.Create(nameof(BookingCancel_ChangesOnlyThatBookingsActiveParticipations_AndTheAppointmentDerivesFromAll));
        Client partner = await w.AddClient("Partner", "Client");
        AppointmentDto created = await w.CreateAppointment(SchedulingWorld.Future(10), extraClients: partner);
        Guid second = await w.AddArtificialSegmentParticipation(created.Id, w.Client, SchedulingWorld.Future(14), 30m); // artificial

        await w.Bookings.CancelBooking(w.OrganizationId, w.ActorUserId, true, created.Id, w.Client.Id.Value, new BookingCancelRequest());

        Assert.All(await w.LoadParticipations(created.Id, w.Client), p => Assert.Equal((Ca, 1), (p.Status, p.StatusVersion)));
        BookingSegmentParticipation partnerParticipation = Assert.Single(await w.LoadParticipations(created.Id, partner));
        Assert.Equal((Cf, 0), (partnerParticipation.Status, partnerParticipation.StatusVersion));
        Assert.Equal(AppointmentStatus.Scheduled, await StatusOf(w, created.Id)); // the partner is still Confirmed

        await w.Bookings.SetParticipationStatus(w.OrganizationId, w.ActorUserId, true, partnerParticipation.Id.Value,
            new BookingSetStatusRequest { Status = BookingStatus.NoShow });

        Assert.Equal(AppointmentStatus.Closed, await StatusOf(w, created.Id)); // Cancelled x2 + NoShow
        Assert.Single(await StatusAudit(w, created.Id)); // exactly one appointment-level change: Scheduled -> Closed
    }

    #endregion

    #region Group close-out and empty occurrences

    [Fact]
    public async Task GroupCloseOut_RecordsTheFactOnce_EarnsTheCommissionOnce_AndDoesNotSetAStatus()
    {
        await using SchedulingWorld w = await SchedulingWorld.Create(nameof(GroupCloseOut_RecordsTheFactOnce_EarnsTheCommissionOnce_AndDoesNotSetAStatus));
        (_, _, Appointment occurrence, Client[] members) = await GroupOccurrence(w, 1, withCommission: true);
        Guid id = occurrence.Id.Value;
        await w.SetBookingStatus(id, members[0], BookingStatus.Completed);
        Assert.Equal(AppointmentStatus.Closed, await StatusOf(w, id)); // closed by the participation, before any close-out

        AppointmentDto first = await w.Appointments.CompleteGroupAppointment(w.OrganizationId, w.ActorUserId, true, id);
        DateTimeOffset closedOutAt = first.ClosedOutAt.Value;
        AppointmentDto repeat = await w.Appointments.CompleteGroupAppointment(w.OrganizationId, w.ActorUserId, true, id);

        Appointment a = await w.LoadAppointment(id);
        Assert.Equal((closedOutAt, w.ActorUserId), (a.ClosedOutAt.Value, a.ClosedOutBy.Value)); // the original fact is kept
        Assert.Equal(closedOutAt, repeat.ClosedOutAt);
        Assert.Single(await w.LoadAuditLog(id), l => l.ChangeType == "GroupClosedOut");
        Assert.Equal(AppointmentStatus.Closed, first.Status);
        SchedulingAssert.HasNoWarnings(first);
        Assert.Equal(20m, Assert.Single(await w.LoadCommissionEntries()).CommissionAmount);
        Assert.Single(await StatusAudit(w, id)); // only the derivation Scheduled -> Closed
    }

    [Fact]
    public async Task GroupCloseOut_Repeated_AfterARuleWasAdded_DoesNotEarnRetroactively_AndExpiresTheWaitlistOnce()
    {
        await using SchedulingWorld w = await SchedulingWorld.Create(nameof(GroupCloseOut_Repeated_AfterARuleWasAdded_DoesNotEarnRetroactively_AndExpiresTheWaitlistOnce));
        ServiceEntity svc = await w.AddGroupService();
        GroupDto group = await w.CreateGroup(svc, capacity: 1);
        await w.AddGroupMember(group, w.Client);
        Appointment occurrence = await w.GenerateSingleOccurrence(group);
        Guid id = occurrence.Id.Value;
        Client waiter = await w.AddClient("Waiter", "Client");
        await w.Waitlist.Join(w.OrganizationId, w.ActorUserId, true, id,
            new WaitlistJoinRequest { ClientId = waiter.Id.Value, SegmentId = Assert.Single(occurrence.Segments).Id });

        AppointmentDto first = await w.Appointments.CompleteGroupAppointment(w.OrganizationId, w.ActorUserId, true, id); // no rule yet
        Assert.Empty(await w.LoadCommissionEntries());
        Assert.Equal(WaitlistEntryStatus.Expired, Assert.Single(await w.LoadWaitlist(id)).Status);
        DateTimeOffset? expiredAt = Assert.Single(await w.LoadWaitlist(id)).UpdatedAt;

        await w.AddCommissionRule(w.Employee, svc, CommissionCalculationType.Fixed, 20m);
        AppointmentDto repeat = await w.Appointments.CompleteGroupAppointment(w.OrganizationId, w.ActorUserId, true, id);

        Assert.Empty(await w.LoadCommissionEntries()); // the close-out already happened: no retroactive commission
        Assert.Equal(expiredAt, Assert.Single(await w.LoadWaitlist(id)).UpdatedAt); // no repeated waitlist effect
        Assert.Single(await w.LoadAuditLog(id), l => l.ChangeType == "GroupClosedOut");

        // The member is still Confirmed: the session stays Scheduled and both calls warn about the unresolved member.
        Assert.Equal(AppointmentStatus.Scheduled, repeat.Status);
        SchedulingAssert.HasWarning(first, WarningCodes.GroupAppointmentUnresolvedBookings);
        SchedulingAssert.HasWarning(repeat, WarningCodes.GroupAppointmentUnresolvedBookings);

        // Later the final member is resolved => the occurrence derives Closed (the close-out never set it).
        await w.SetBookingStatus(id, w.Client, BookingStatus.Completed);
        Assert.Equal(AppointmentStatus.Closed, await StatusOf(w, id));
    }

    [Fact]
    public async Task EmptyOccurrence_IsScheduled_CloseOutDoesNotChangeIt_AndAnExplicitCancelCancelsIt()
    {
        await using SchedulingWorld w = await SchedulingWorld.Create(nameof(EmptyOccurrence_IsScheduled_CloseOutDoesNotChangeIt_AndAnExplicitCancelCancelsIt));
        (_, _, Appointment occurrence, _) = await GroupOccurrence(w, 0); // a group without members: no participations
        Guid id = occurrence.Id.Value;
        Assert.Empty(occurrence.Bookings);
        Assert.Equal(AppointmentStatus.Scheduled, occurrence.Status);

        await w.Appointments.CompleteGroupAppointment(w.OrganizationId, w.ActorUserId, true, id);
        Assert.Equal(AppointmentStatus.Scheduled, await StatusOf(w, id)); // zero participations, not cancelled => Scheduled

        await w.Appointments.Cancel(w.OrganizationId, w.ActorUserId, true, id, new AppointmentCancelRequest());
        Assert.Equal(AppointmentStatus.Cancelled, await StatusOf(w, id)); // derived from the explicit cancellation alone
    }

    [Fact]
    public async Task GroupWhoseMembersAllCancelled_StaysScheduled_AcceptsAGuestAndTheWaitlist_UntilTheSessionIsCancelled()
    {
        await using SchedulingWorld w = await SchedulingWorld.Create(nameof(GroupWhoseMembersAllCancelled_StaysScheduled_AcceptsAGuestAndTheWaitlist_UntilTheSessionIsCancelled));
        (_, _, Appointment occurrence, Client[] members) = await GroupOccurrence(w, 2);
        Guid id = occurrence.Id.Value;

        foreach (Client member in members)
            await w.SetBookingStatus(id, member, BookingStatus.Cancelled);
        Assert.Equal(AppointmentStatus.Scheduled, await StatusOf(w, id));

        Client guest = await w.AddClient("Guest", "Client");
        await w.AddGuest(await w.LoadAppointment(id), guest);                               // AddGroupGuest still works
        Client waiter = await w.AddClient("Waiter", "Client");
        // The waitlist is judged on capacity, not on the aggregate status.
        await SchedulingAssert.BusinessRule(Core.Shared.ErrorCodes.CapacityAvailable,
            () => w.Waitlist.Join(w.OrganizationId, w.ActorUserId, true, id,
                new WaitlistJoinRequest { ClientId = waiter.Id.Value, SegmentId = Assert.Single(occurrence.Segments).Id }));
        Assert.Equal(AppointmentStatus.Scheduled, await StatusOf(w, id));

        await w.Appointments.Cancel(w.OrganizationId, w.ActorUserId, true, id, new AppointmentCancelRequest());
        Assert.Equal(AppointmentStatus.Cancelled, await StatusOf(w, id));
        Client another = await w.AddClient("Another", "Client");
        await SchedulingAssert.BusinessRule(Core.Shared.ErrorCodes.AppointmentNotMovable,
            () => w.AddGuest(occurrence, another));
        await SchedulingAssert.BusinessRule(Core.Shared.ErrorCodes.WaitlistNotAvailable,
            () => w.Waitlist.Join(w.OrganizationId, w.ActorUserId, true, id, new WaitlistJoinRequest { ClientId = another.Id.Value, SegmentId = Assert.Single(occurrence.Segments).Id }));
    }

    #endregion

    #region "Worked" (employee history)

    [Fact]
    public async Task EmployeeWorked_IsACompletedParticipationOnTheirSegment_NotAClosedAppointment()
    {
        await using SchedulingWorld w = await SchedulingWorld.Create(nameof(EmployeeWorked_IsACompletedParticipationOnTheirSegment_NotAClosedAppointment));
        AppointmentDto worked = await w.CreateAppointment(SchedulingWorld.Future(10));
        await w.CompleteParticipations(worked.Id, w.CompleteRequest(SchedulingWorld.Future(10)));
        Client other = await w.AddClient("Other", "Client");
        AppointmentDto noShow = await w.CreateAppointment(SchedulingWorld.Future(12), client: other);
        await w.SetBookingStatus(noShow.Id, other, BookingStatus.NoShow);
        Assert.Equal(AppointmentStatus.Closed, await StatusOf(w, noShow.Id)); // Closed, but nobody was served

        Core.Shared.PagedResult<AppointmentDto> history = await w.Appointments.GetByEmployee(
            w.OrganizationId, w.Employee.Id.Value, new Core.Shared.PagedRequest());

        Assert.Equal(worked.Id, Assert.Single(history.Items).Id);
    }

    #endregion

    #region Commercial side effects are unchanged

    [Fact]
    public async Task CompletionAndCorrection_KeepPackagePaymentAndCommissionSemantics_WhileTheStatusIsDerived()
    {
        await using SchedulingWorld w = await SchedulingWorld.Create(nameof(CompletionAndCorrection_KeepPackagePaymentAndCommissionSemantics_WhileTheStatusIsDerived));
        await w.AddCommissionRule(w.Employee, w.Service, CommissionCalculationType.Percentage, 10m);
        ClientPackage package = await w.AddClientPackage(w.Client, w.Service, 5, new DateTimeOffset(2035, 1, 1, 0, 0, 0, TimeSpan.Zero));
        AppointmentDto created = await w.CreateAppointment(SchedulingWorld.Future(10));

        AppointmentDto completed = await w.CompleteParticipations(created.Id, w.CompleteRequest(SchedulingWorld.Future(10), clientPackageId: package.Id));

        Assert.Equal(AppointmentStatus.Closed, completed.Status);
        Assert.Equal(4, (await w.LoadClientPackage(package.Id.Value)).ServiceEntries.Single().RemainingEntries);
        Assert.Equal(CommissionEntryStatus.Earned, Assert.Single(await w.LoadCommissionEntries()).Status);
        BookingDto booking = completed.Bookings.Single();
        Assert.True(booking.IsPaid);              // settled by the package (not cash) ...
        Assert.Equal(0m, booking.PaidAmount);     // ... and Closed does not mean "paid in money"

        await w.SetBookingStatus(created.Id, w.Client, BookingStatus.Confirmed); // correction

        Assert.Equal(AppointmentStatus.Scheduled, await StatusOf(w, created.Id));
        Assert.Equal(5, (await w.LoadClientPackage(package.Id.Value)).ServiceEntries.Single().RemainingEntries);
        Assert.Equal(CommissionEntryStatus.Reversed, Assert.Single(await w.LoadCommissionEntries()).Status);
    }

    [Fact]
    public async Task ClosedDoesNotMeanPaid()
    {
        await using SchedulingWorld w = await SchedulingWorld.Create(nameof(ClosedDoesNotMeanPaid));
        AppointmentDto created = await w.CreateAppointment(SchedulingWorld.Future(10));

        AppointmentDto completed = await w.CompleteParticipations(created.Id, w.CompleteRequest(SchedulingWorld.Future(10))); // no payment

        Assert.Equal(AppointmentStatus.Closed, completed.Status);
        Assert.Equal((false, SchedulingWorld.DefaultServicePrice), (completed.Bookings.Single().IsPaid, completed.Bookings.Single().OutstandingAmount));
    }

    #endregion
}
