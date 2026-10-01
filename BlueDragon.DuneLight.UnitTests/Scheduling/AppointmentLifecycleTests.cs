#nullable disable
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using BlueDragon.DuneLight.Core.DTOs.Appointments;
using BlueDragon.DuneLight.Core.DTOs.Groups;
using BlueDragon.DuneLight.Core.Enums;
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

    [Theory]
    // single participation
    [InlineData(new[] { Cf }, AppointmentStatus.Scheduled)]
    [InlineData(new[] { Co }, AppointmentStatus.Closed)]
    [InlineData(new[] { Ca }, AppointmentStatus.Cancelled)]
    [InlineData(new[] { Ns }, AppointmentStatus.Closed)]
    // two participations
    [InlineData(new[] { Cf, Cf }, AppointmentStatus.Scheduled)]
    [InlineData(new[] { Co, Cf }, AppointmentStatus.Scheduled)]
    [InlineData(new[] { Ca, Cf }, AppointmentStatus.Scheduled)]
    [InlineData(new[] { Ns, Cf }, AppointmentStatus.Scheduled)]
    [InlineData(new[] { Co, Co }, AppointmentStatus.Closed)]
    [InlineData(new[] { Co, Ca }, AppointmentStatus.Closed)]
    [InlineData(new[] { Co, Ns }, AppointmentStatus.Closed)]
    [InlineData(new[] { Ca, Ca }, AppointmentStatus.Cancelled)]
    [InlineData(new[] { Ca, Ns }, AppointmentStatus.Closed)]
    [InlineData(new[] { Ns, Ns }, AppointmentStatus.Closed)]
    // three-way terminal mix, and order independence
    [InlineData(new[] { Co, Ca, Ns }, AppointmentStatus.Closed)]
    [InlineData(new[] { Ns, Ca, Co }, AppointmentStatus.Closed)]
    [InlineData(new[] { Ca, Ca, Cf }, AppointmentStatus.Scheduled)]
    public void Derive_FollowsTheLockedRules(ParticipationStatus[] statuses, AppointmentStatus expected)
    {
        Assert.Equal(expected, AppointmentLifecycle.Derive(statuses));
    }

    [Fact]
    public void Derive_HasNoAnswerForAnAppointmentWithoutParticipations()
    {
        Assert.Null(AppointmentLifecycle.Derive(Array.Empty<ParticipationStatus>()));
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
    public async Task CancellingEveryParticipation_OneByOne_CancelsTheAppointment()
    {
        await using SchedulingWorld w = await SchedulingWorld.Create(nameof(CancellingEveryParticipation_OneByOne_CancelsTheAppointment));
        Client partner = await w.AddClient("Partner", "Client");
        AppointmentDto created = await w.CreateAppointment(SchedulingWorld.Future(10), extraClients: partner);

        await w.SetBookingStatus(created.Id, w.Client, BookingStatus.Cancelled);
        Assert.Equal(AppointmentStatus.Scheduled, await StatusOf(w, created.Id));  // Cancelled + Confirmed
        await w.SetBookingStatus(created.Id, partner, BookingStatus.Cancelled);
        Assert.Equal(AppointmentStatus.Cancelled, await StatusOf(w, created.Id));  // Cancelled + Cancelled
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
        await w.CompleteExisting(created.Id, w.CompleteRequest(SchedulingWorld.Future(10)));
        Assert.Equal(AppointmentStatus.Closed, await StatusOf(w, created.Id)); // Completed + Cancelled

        await w.SetBookingStatus(created.Id, w.Client, BookingStatus.Confirmed); // correction

        Assert.Equal(AppointmentStatus.Scheduled, await StatusOf(w, created.Id)); // Confirmed + Cancelled
        Assert.Equal(("Closed", "Scheduled"), ((await StatusAudit(w, created.Id)).Last().OldValue, (await StatusAudit(w, created.Id)).Last().NewValue));
    }

    [Fact]
    public async Task Correction_CancelledToScheduled_WhenACancelledGroupParticipationReturnsToConfirmed()
    {
        await using SchedulingWorld w = await SchedulingWorld.Create(nameof(Correction_CancelledToScheduled_WhenACancelledGroupParticipationReturnsToConfirmed));
        (_, _, Appointment occurrence, Client[] members) = await GroupOccurrence(w, 1);
        Guid id = occurrence.Id.Value;

        await w.SetBookingStatus(id, members[0], BookingStatus.Cancelled);
        Assert.Equal(AppointmentStatus.Cancelled, await StatusOf(w, id));

        await w.SetBookingStatus(id, members[0], BookingStatus.Confirmed); // group correction (capacity allows)

        Assert.Equal(AppointmentStatus.Scheduled, await StatusOf(w, id));
        Assert.Equal(2, (await w.LoadBooking(id, members[0])).StatusVersion); // only the participation version moves
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
    public async Task GroupCloseOut_DoesNotSetAStatus_AllResolvedOccurrenceIsAlreadyClosed_AndCommissionIsEarnedOnce()
    {
        await using SchedulingWorld w = await SchedulingWorld.Create(nameof(GroupCloseOut_DoesNotSetAStatus_AllResolvedOccurrenceIsAlreadyClosed_AndCommissionIsEarnedOnce));
        (_, _, Appointment occurrence, Client[] members) = await GroupOccurrence(w, 1, withCommission: true);
        Guid id = occurrence.Id.Value;
        await w.SetBookingStatus(id, members[0], BookingStatus.Completed);
        Assert.Equal(AppointmentStatus.Closed, await StatusOf(w, id)); // closed by the participation, before any close-out

        AppointmentDto dto = await w.Appointments.CompleteGroupAppointment(w.OrganizationId, w.ActorUserId, true, id);
        await w.Appointments.CompleteGroupAppointment(w.OrganizationId, w.ActorUserId, true, id);

        Assert.Equal(AppointmentStatus.Closed, dto.Status);
        SchedulingAssert.HasNoWarnings(dto);
        Assert.Equal(20m, Assert.Single(await w.LoadCommissionEntries()).CommissionAmount);
        Assert.Single(await StatusAudit(w, id)); // only the derivation Scheduled -> Closed
    }

    [Fact]
    public async Task EmptyOccurrence_HasNoDerivedLifecycle_CloseOutKeepsIt_AndExplicitCancelKeepsTheOldOutcome()
    {
        await using SchedulingWorld w = await SchedulingWorld.Create(nameof(EmptyOccurrence_HasNoDerivedLifecycle_CloseOutKeepsIt_AndExplicitCancelKeepsTheOldOutcome));
        (_, _, Appointment occurrence, _) = await GroupOccurrence(w, 0); // a group without members: no participations
        Guid id = occurrence.Id.Value;
        Assert.Empty(occurrence.Bookings);
        Assert.Equal(AppointmentStatus.Scheduled, occurrence.Status);

        await w.Appointments.CompleteGroupAppointment(w.OrganizationId, w.ActorUserId, true, id);
        Assert.Equal(AppointmentStatus.Scheduled, await StatusOf(w, id)); // nothing to derive from: unchanged

        await w.Appointments.Cancel(w.OrganizationId, w.ActorUserId, true, id, new AppointmentCancelRequest());
        Assert.Equal(AppointmentStatus.Cancelled, await StatusOf(w, id)); // the single explicit exception (open question)
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

        AppointmentDto completed = await w.CompleteExisting(created.Id, w.CompleteRequest(SchedulingWorld.Future(10), clientPackageId: package.Id));

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

        AppointmentDto completed = await w.CompleteExisting(created.Id, w.CompleteRequest(SchedulingWorld.Future(10))); // no payment

        Assert.Equal(AppointmentStatus.Closed, completed.Status);
        Assert.Equal((false, SchedulingWorld.DefaultServicePrice), (completed.Bookings.Single().IsPaid, completed.Bookings.Single().OutstandingAmount));
    }

    #endregion
}
