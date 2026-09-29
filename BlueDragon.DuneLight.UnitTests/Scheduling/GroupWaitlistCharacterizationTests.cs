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
using BlueDragon.DuneLight.Infrastructure.Domain.Models.Catalog;
using BlueDragon.DuneLight.Infrastructure.Domain.Models.Clients;
using BlueDragon.DuneLight.Infrastructure.Domain.Models.Employees;
using BlueDragon.DuneLight.Infrastructure.Domain.Models.Groups;
using ServiceEntity = BlueDragon.DuneLight.Infrastructure.Domain.Models.Catalog.Service;

namespace BlueDragon.DuneLight.UnitTests.Scheduling;

/// <summary>
/// CHARACTERIZATION (matrix S): the group waitlist. Today it is a strict FIFO queue per occurrence (ordered by JoinedAt),
/// joinable only when the occurrence is FULL, and it promotes automatically inside the same transaction that frees a seat
/// (a Confirmed → Cancelled Booking transition, or a member leaving the group). A promoted entry becomes an ordinary
/// Confirmed Booking with a resolved price snapshot. Each candidate is re-validated at promotion time; an ineligible one
/// is Expired (with a reason) and the next candidate is tried. There is no priority/tag ordering and no manual approval step
/// — those are future work and are not asserted here.
/// </summary>
public class GroupWaitlistCharacterizationTests
{
    private static async Task<(SchedulingWorld W, GroupDto Group, Appointment Occurrence, Client[] Members)> Occurrence(
        string name, int capacity, int members)
    {
        SchedulingWorld w = await SchedulingWorld.Create(name);
        ServiceEntity svc = await w.AddGroupService();
        GroupDto group = await w.CreateGroup(svc, capacity);
        List<Client> clients = new();
        for (int i = 0; i < members; i++)
        {
            Client c = await w.AddClient($"Member{i}", "Client");
            await w.AddGroupMember(group, c);
            clients.Add(c);
        }

        return (w, group, await w.GenerateSingleOccurrence(group), clients.ToArray());
    }

    private static Task<WaitlistEntryDto> Join(SchedulingWorld w, Appointment occurrence, Client client) =>
        w.Waitlist.Join(w.OrganizationId, w.ActorUserId, true, occurrence.Id.Value, new WaitlistJoinRequest { ClientId = client.Id.Value });

    private static Task<WaitlistEntryDto> CancelEntry(SchedulingWorld w, Appointment occurrence, Client client) =>
        w.Waitlist.Cancel(w.OrganizationId, w.ActorUserId, true, occurrence.Id.Value, client.Id.Value);

    #region Joining

    [Fact]
    public async Task Join_WhenTheOccurrenceIsFull_CreatesAWaitingEntryAtPositionOne_AndAuditsIt()
    {
        (SchedulingWorld w, _, Appointment occurrence, _) = await Occurrence(nameof(Join_WhenTheOccurrenceIsFull_CreatesAWaitingEntryAtPositionOne_AndAuditsIt), 1, 1);
        await using SchedulingWorld _w = w;
        Client waiter = await w.AddClient("Waiter", "Client");

        WaitlistEntryDto entry = await Join(w, occurrence, waiter);

        Assert.Equal(WaitlistEntryStatus.Waiting, entry.Status);
        Assert.Equal(1, entry.Position);
        WaitlistEntry stored = Assert.Single(await w.LoadWaitlist(occurrence.Id.Value));
        Assert.Equal(waiter.Id, stored.ClientId);
        Assert.Equal(WaitlistEntryStatus.Waiting, stored.Status);
        AppointmentAuditLog audit = Assert.Single(await w.LoadAuditLog(occurrence.Id.Value), l => l.ChangeType == "WaitlistJoined");
        Assert.Equal(stored.Id, audit.WaitlistEntryId);
        Assert.Equal(waiter.Id.ToString(), audit.NewValue);
        // Joining the waitlist creates no Booking.
        Assert.DoesNotContain((await w.LoadAppointment(occurrence.Id.Value)).Bookings, b => b.ClientId == waiter.Id);
    }

    [Fact]
    public async Task Join_PositionsFollowJoinOrder()
    {
        (SchedulingWorld w, _, Appointment occurrence, _) = await Occurrence(nameof(Join_PositionsFollowJoinOrder), 1, 1);
        await using SchedulingWorld _w = w;
        Client first = await w.AddClient("First", "Waiter");
        Client second = await w.AddClient("Second", "Waiter");

        await Join(w, occurrence, first);
        WaitlistEntryDto secondEntry = await Join(w, occurrence, second);

        Assert.Equal(2, secondEntry.Position);
    }

    [Fact]
    public async Task Join_WhileSeatsRemain_IsRejected_UseABookingInstead()
    {
        (SchedulingWorld w, _, Appointment occurrence, _) = await Occurrence(nameof(Join_WhileSeatsRemain_IsRejected_UseABookingInstead), 2, 1);
        await using SchedulingWorld _w = w;
        Client waiter = await w.AddClient("Waiter", "Client");

        await SchedulingAssert.BusinessRule(ErrorCodes.CapacityAvailable, () => Join(w, occurrence, waiter));

        Assert.Empty(await w.LoadWaitlist(occurrence.Id.Value));
    }

    [Fact]
    public async Task Join_ForAClientWhoAlreadyHasAConfirmedBooking_IsRejected()
    {
        (SchedulingWorld w, _, Appointment occurrence, Client[] members) = await Occurrence(nameof(Join_ForAClientWhoAlreadyHasAConfirmedBooking_IsRejected), 1, 1);
        await using SchedulingWorld _w = w;

        await SchedulingAssert.BusinessRule(ErrorCodes.AlreadyBooked, () => Join(w, occurrence, members[0]));
    }

    [Fact]
    public async Task Join_Twice_IsRejected()
    {
        (SchedulingWorld w, _, Appointment occurrence, _) = await Occurrence(nameof(Join_Twice_IsRejected), 1, 1);
        await using SchedulingWorld _w = w;
        Client waiter = await w.AddClient("Waiter", "Client");
        await Join(w, occurrence, waiter);

        await SchedulingAssert.BusinessRule(ErrorCodes.AlreadyWaitlisted, () => Join(w, occurrence, waiter));
    }

    [Fact]
    public async Task Join_OnAnIndividualAppointment_IsNotAvailable()
    {
        await using SchedulingWorld w = await SchedulingWorld.Create(nameof(Join_OnAnIndividualAppointment_IsNotAvailable));
        AppointmentDto individual = await w.CreateAppointment(SchedulingWorld.Future(10));
        Client waiter = await w.AddClient("Waiter", "Client");

        await SchedulingAssert.BusinessRule(ErrorCodes.WaitlistNotAvailable,
            () => w.Waitlist.Join(w.OrganizationId, w.ActorUserId, true, individual.Id, new WaitlistJoinRequest { ClientId = waiter.Id.Value }));
    }

    [Fact]
    public async Task Join_OnAnOccurrenceThatAlreadyStarted_IsNotAvailable()
    {
        await using SchedulingWorld w = await SchedulingWorld.Create(nameof(Join_OnAnOccurrenceThatAlreadyStarted_IsNotAvailable));
        Appointment past = await w.SeedAppointment(SchedulingWorld.Past(10), AppointmentStatus.Scheduled, AppointmentForm.Group, employee: w.Employee);
        Client waiter = await w.AddClient("Waiter", "Client");

        await SchedulingAssert.BusinessRule(ErrorCodes.WaitlistNotAvailable, () => Join(w, past, waiter));
    }

    [Fact]
    public async Task Join_OwnScopeCaller_CannotJoinAClientOntoAnotherTrainersOccurrence()
    {
        (SchedulingWorld w, _, Appointment occurrence, _) = await Occurrence(nameof(Join_OwnScopeCaller_CannotJoinAClientOntoAnotherTrainersOccurrence), 1, 1);
        await using SchedulingWorld _w = w;
        Employee other = await w.AddEmployee("Other");
        Client waiter = await w.AddClient("Waiter", "Client");

        await SchedulingAssert.BusinessRule(ErrorCodes.NotOwner,
            () => w.Waitlist.Join(w.OrganizationId, other.UserId, false, occurrence.Id.Value, new WaitlistJoinRequest { ClientId = waiter.Id.Value }));
    }

    [Fact]
    public async Task Cancel_AWaitingEntry_MarksItCancelled_AndIsIdempotent()
    {
        (SchedulingWorld w, _, Appointment occurrence, _) = await Occurrence(nameof(Cancel_AWaitingEntry_MarksItCancelled_AndIsIdempotent), 1, 1);
        await using SchedulingWorld _w = w;
        Client waiter = await w.AddClient("Waiter", "Client");
        await Join(w, occurrence, waiter);

        WaitlistEntryDto first = await CancelEntry(w, occurrence, waiter);
        WaitlistEntryDto again = await CancelEntry(w, occurrence, waiter);

        Assert.Equal(WaitlistEntryStatus.Cancelled, first.Status);
        Assert.Equal(WaitlistEntryStatus.Cancelled, again.Status);
        Assert.Single(await w.LoadAuditLog(occurrence.Id.Value), l => l.ChangeType == "WaitlistCancelled");
        // A cancelled entry can be replaced by a fresh Join.
        WaitlistEntryDto rejoined = await Join(w, occurrence, waiter);
        Assert.Equal(WaitlistEntryStatus.Waiting, rejoined.Status);
    }

    #endregion

    #region Promotion — FIFO, capacity-bound

    [Fact]
    public async Task ACancelledBooking_PromotesTheFirstWaiter_IntoAConfirmedBookingWithAPriceSnapshot()
    {
        (SchedulingWorld w, _, Appointment occurrence, Client[] members) = await Occurrence(nameof(ACancelledBooking_PromotesTheFirstWaiter_IntoAConfirmedBookingWithAPriceSnapshot), 1, 1);
        await using SchedulingWorld _w = w;
        Client first = await w.AddClient("First", "Waiter");
        Client second = await w.AddClient("Second", "Waiter");
        await Join(w, occurrence, first);
        await Join(w, occurrence, second);

        await w.SetBookingStatus(occurrence.Id.Value, members[0], BookingStatus.Cancelled, "cannot come");

        Appointment a = await w.LoadAppointment(occurrence.Id.Value);
        Booking promoted = a.Bookings.Single(b => b.ClientId == first.Id);
        Assert.Equal(BookingStatus.Confirmed, promoted.Status);
        Assert.Equal(0, promoted.StatusVersion);
        Assert.Equal(15m, promoted.Amount);
        Assert.Equal(15m, promoted.SuggestedAmount);
        Assert.Null(promoted.ClientPackageId); // an ordinary Booking: package/payment is resolved later, at check-in
        Assert.DoesNotContain(a.Bookings, b => b.ClientId == second.Id);
        Assert.Equal(1, a.Bookings.Count(b => b.Status == BookingStatus.Confirmed)); // never exceeds capacity

        List<WaitlistEntry> entries = await w.LoadWaitlist(occurrence.Id.Value);
        WaitlistEntry firstEntry = entries.Single(e => e.ClientId == first.Id);
        Assert.Equal(WaitlistEntryStatus.Promoted, firstEntry.Status);
        Assert.Equal(promoted.Id, firstEntry.PromotedBookingId);
        Assert.NotNull(firstEntry.PromotedAt);
        Assert.Equal(WaitlistEntryStatus.Waiting, entries.Single(e => e.ClientId == second.Id).Status);
    }

    [Fact]
    public async Task Promotion_IsAuditedAndEmitsAPromotedEventOnce()
    {
        (SchedulingWorld w, _, Appointment occurrence, Client[] members) = await Occurrence(nameof(Promotion_IsAuditedAndEmitsAPromotedEventOnce), 1, 1);
        await using SchedulingWorld _w = w;
        Client waiter = await w.AddClient("Waiter", "Client");
        await Join(w, occurrence, waiter);

        await w.SetBookingStatus(occurrence.Id.Value, members[0], BookingStatus.Cancelled, "cannot come");

        WaitlistEntry entry = Assert.Single(await w.LoadWaitlist(occurrence.Id.Value));
        AppointmentAuditLog audit = Assert.Single(await w.LoadAuditLog(occurrence.Id.Value), l => l.ChangeType == "WaitlistPromoted");
        Assert.Equal(entry.Id, audit.WaitlistEntryId);
        Assert.Equal(entry.PromotedBookingId, audit.BookingId);
        Assert.Equal("Waiting", audit.OldValue);
        Assert.Equal("Promoted", audit.NewValue);
        var promotedEvent = Assert.Single(await w.LoadOutbox(), m => m.Type == OutboxEventTypes.WaitlistPromotedV1);
        Assert.Equal($"waitlist-promoted:{entry.Id}", promotedEvent.IdempotencyKey);
        // The cancellation that freed the seat ALSO emitted its own event (a separate notification stream).
        Assert.Single(await w.LoadOutbox(), m => m.Type == OutboxEventTypes.BookingCancelledV1);
    }

    [Fact]
    public async Task Promotion_FillsFreedSeatsInFifoOrder_NeverBeyondCapacity()
    {
        (SchedulingWorld w, _, Appointment occurrence, Client[] members) = await Occurrence(nameof(Promotion_FillsFreedSeatsInFifoOrder_NeverBeyondCapacity), 2, 2);
        await using SchedulingWorld _w = w;
        Client w1 = await w.AddClient("W1", "Waiter");
        Client w2 = await w.AddClient("W2", "Waiter");
        Client w3 = await w.AddClient("W3", "Waiter");
        await Join(w, occurrence, w1);
        await Join(w, occurrence, w2);
        await Join(w, occurrence, w3);

        await w.SetBookingStatus(occurrence.Id.Value, members[0], BookingStatus.Cancelled);
        await w.SetBookingStatus(occurrence.Id.Value, members[1], BookingStatus.Cancelled);

        List<WaitlistEntry> entries = await w.LoadWaitlist(occurrence.Id.Value);
        Assert.Equal(WaitlistEntryStatus.Promoted, entries.Single(e => e.ClientId == w1.Id).Status);
        Assert.Equal(WaitlistEntryStatus.Promoted, entries.Single(e => e.ClientId == w2.Id).Status);
        Assert.Equal(WaitlistEntryStatus.Waiting, entries.Single(e => e.ClientId == w3.Id).Status);
        Assert.Equal(2, (await w.LoadAppointment(occurrence.Id.Value)).Bookings.Count(b => b.Status == BookingStatus.Confirmed));
    }

    [Fact]
    public async Task Promotion_SkipsAndExpiresAWaiterWhoBecameInactive_ThenPromotesTheNext()
    {
        (SchedulingWorld w, _, Appointment occurrence, Client[] members) = await Occurrence(nameof(Promotion_SkipsAndExpiresAWaiterWhoBecameInactive_ThenPromotesTheNext), 1, 1);
        await using SchedulingWorld _w = w;
        Client gone = await w.AddClient("Gone", "Waiter");
        Client next = await w.AddClient("Next", "Waiter");
        await Join(w, occurrence, gone);
        await Join(w, occurrence, next);
        await w.SetClientActive(gone, false);

        await w.SetBookingStatus(occurrence.Id.Value, members[0], BookingStatus.Cancelled);

        List<WaitlistEntry> entries = await w.LoadWaitlist(occurrence.Id.Value);
        WaitlistEntry expired = entries.Single(e => e.ClientId == gone.Id);
        Assert.Equal(WaitlistEntryStatus.Expired, expired.Status);
        Assert.Equal(WaitlistExpiredReasons.ClientInactive, expired.ExpiredReason);
        Assert.Equal(WaitlistEntryStatus.Promoted, entries.Single(e => e.ClientId == next.Id).Status);
    }

    [Fact]
    public async Task Promotion_ExpiresAWaiterWhoNowHasAConflictingAppointment_ThenPromotesTheNext()
    {
        (SchedulingWorld w, _, Appointment occurrence, Client[] members) = await Occurrence(nameof(Promotion_ExpiresAWaiterWhoNowHasAConflictingAppointment_ThenPromotesTheNext), 1, 1);
        await using SchedulingWorld _w = w;
        Client busy = await w.AddClient("Busy", "Waiter");
        Client next = await w.AddClient("Next", "Waiter");
        Employee other = await w.AddEmployee("Other");
        await Join(w, occurrence, busy);
        await Join(w, occurrence, next);
        await w.CreateAppointment(occurrence.StartsAt, client: busy, employee: other);

        await w.SetBookingStatus(occurrence.Id.Value, members[0], BookingStatus.Cancelled);

        List<WaitlistEntry> entries = await w.LoadWaitlist(occurrence.Id.Value);
        Assert.Equal(WaitlistExpiredReasons.ClientScheduleConflict, entries.Single(e => e.ClientId == busy.Id).ExpiredReason);
        Assert.Equal(WaitlistEntryStatus.Promoted, entries.Single(e => e.ClientId == next.Id).Status);
    }

    [Fact]
    public async Task ARemovedGroupMember_FreesTheirSeat_AndThePromotionRunsForEveryFutureOccurrence()
    {
        (SchedulingWorld w, GroupDto group, Appointment occurrence, Client[] members) = await Occurrence(nameof(ARemovedGroupMember_FreesTheirSeat_AndThePromotionRunsForEveryFutureOccurrence), 1, 1);
        await using SchedulingWorld _w = w;
        Client waiter = await w.AddClient("Waiter", "Client");
        await Join(w, occurrence, waiter);
        Guid memberId = (await w.Groups.GetById(w.OrganizationId, group.Id)).Members.Single().Id;

        await w.Groups.RemoveMember(w.OrganizationId, w.ActorUserId, group.Id, memberId);

        Assert.Equal(WaitlistEntryStatus.Promoted, Assert.Single(await w.LoadWaitlist(occurrence.Id.Value)).Status);
        Assert.Contains((await w.LoadAppointment(occurrence.Id.Value)).Bookings, b => b.ClientId == waiter.Id && b.Status == BookingStatus.Confirmed);
    }

    [Fact]
    public async Task ANoShow_DoesNotPromoteAnyone_ButItDoesFreeTheSeatForTheCapacityCount()
    {
        (SchedulingWorld w, _, Appointment occurrence, Client[] members) = await Occurrence(nameof(ANoShow_DoesNotPromoteAnyone_ButItDoesFreeTheSeatForTheCapacityCount), 1, 1);
        await using SchedulingWorld _w = w;
        Client waiter = await w.AddClient("Waiter", "Client");
        Client latecomer = await w.AddClient("Latecomer", "Client");
        await Join(w, occurrence, waiter);

        await w.SetBookingStatus(occurrence.Id.Value, members[0], BookingStatus.NoShow);

        Assert.Equal(WaitlistEntryStatus.Waiting, Assert.Single(await w.LoadWaitlist(occurrence.Id.Value)).Status);
        // The waiter stays queued while the Confirmed count is now 0 < capacity, so the "full" precondition of Join no
        // longer holds for a new client — a small inconsistency of the current model.
        await SchedulingAssert.BusinessRule(ErrorCodes.CapacityAvailable, () => Join(w, occurrence, latecomer));
    }

    #endregion

    #region Occurrence closing expires the queue

    [Fact]
    public async Task CancellingTheWholeOccurrence_ExpiresWaitingEntries_WithoutPromoting()
    {
        (SchedulingWorld w, _, Appointment occurrence, _) = await Occurrence(nameof(CancellingTheWholeOccurrence_ExpiresWaitingEntries_WithoutPromoting), 1, 1);
        await using SchedulingWorld _w = w;
        Client waiter = await w.AddClient("Waiter", "Client");
        await Join(w, occurrence, waiter);

        await w.Appointments.Cancel(w.OrganizationId, w.ActorUserId, true, occurrence.Id.Value, new AppointmentCancelRequest { CancellationReason = "trainer ill" });

        WaitlistEntry entry = Assert.Single(await w.LoadWaitlist(occurrence.Id.Value));
        Assert.Equal(WaitlistEntryStatus.Expired, entry.Status);
        Assert.Equal(WaitlistExpiredReasons.AppointmentCancelled, entry.ExpiredReason);
        Assert.DoesNotContain((await w.LoadAppointment(occurrence.Id.Value)).Bookings, b => b.ClientId == waiter.Id);
    }

    [Fact]
    public async Task CompletingTheGroupOccurrence_ExpiresWaitingEntries()
    {
        (SchedulingWorld w, _, Appointment occurrence, _) = await Occurrence(nameof(CompletingTheGroupOccurrence_ExpiresWaitingEntries), 1, 1);
        await using SchedulingWorld _w = w;
        Client waiter = await w.AddClient("Waiter", "Client");
        await Join(w, occurrence, waiter);

        await w.Appointments.CompleteGroupAppointment(w.OrganizationId, w.ActorUserId, true, occurrence.Id.Value);

        WaitlistEntry entry = Assert.Single(await w.LoadWaitlist(occurrence.Id.Value));
        Assert.Equal(WaitlistEntryStatus.Expired, entry.Status);
        Assert.Equal(WaitlistExpiredReasons.AppointmentCompleted, entry.ExpiredReason);
    }

    #endregion
}
