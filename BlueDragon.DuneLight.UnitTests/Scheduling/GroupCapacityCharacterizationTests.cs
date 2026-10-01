#nullable disable
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using BlueDragon.DuneLight.Core.DTOs.Appointments;
using BlueDragon.DuneLight.Core.DTOs.Groups;
using BlueDragon.DuneLight.Core.Enums;
using BlueDragon.DuneLight.Core.Events;
using BlueDragon.DuneLight.Core.Interfaces.Appointments;
using BlueDragon.DuneLight.Core.Shared;
using BlueDragon.DuneLight.Core.Shared.Exceptions;
using BlueDragon.DuneLight.Infrastructure.Domain.Models.Appointments;
using BlueDragon.DuneLight.Infrastructure.Domain.Models.Catalog;
using BlueDragon.DuneLight.Infrastructure.Domain.Models.Clients;
using BlueDragon.DuneLight.Infrastructure.Domain.Models.Employees;
using BlueDragon.DuneLight.Infrastructure.Domain.Models.Groups;
using Microsoft.Extensions.DependencyInjection;
using ServiceEntity = BlueDragon.DuneLight.Infrastructure.Domain.Models.Catalog.Service;

namespace BlueDragon.DuneLight.UnitTests.Scheduling;

/// <summary>
/// CHARACTERIZATION (matrix I): HARD group capacity. The current model enforces capacity at three places and never as a
/// warning: (1) the roster (GroupService.AddMember counts ACTIVE members against Group.Capacity), (2) each future
/// occurrence (GroupCapacityGuard counts CONFIRMED Bookings against Group.Capacity, under a FOR UPDATE lock on the
/// Appointment row) when a guest Booking is added, when a member is synced onto existing occurrences, and when a Booking
/// returns to Confirmed, and (3) generation (a group over capacity generates nothing).
///
/// Capacity belongs to the GROUP, not the occurrence, and is read live: editing Group.Capacity changes the limit of every
/// already-generated occurrence.
///
/// The future "soft capacity" rule is deliberately NOT implemented or asserted here; these tests pin the hard limit that
/// exists today.
/// </summary>
public class GroupCapacityCharacterizationTests
{
    /// <summary>Group of the given capacity, <paramref name="members"/> active members, one generated future occurrence.</summary>
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

        Appointment occurrence = await w.GenerateSingleOccurrence(group);
        return (w, group, occurrence, clients.ToArray());
    }

    private static int Field(object details, string name) =>
        (int)details.GetType().GetProperty(name).GetValue(details);

    #region Roster capacity (AddMember)

    [Fact]
    public async Task AddMember_WhenTheRosterIsAtCapacity_IsRejectedWithTheCounts()
    {
        (SchedulingWorld w, GroupDto group, _, _) = await Occurrence(nameof(AddMember_WhenTheRosterIsAtCapacity_IsRejectedWithTheCounts), capacity: 1, members: 1);
        await using SchedulingWorld _w = w;
        Client extra = await w.AddClient("Extra", "Client");

        BusinessRuleException ex = await SchedulingAssert.BusinessRule(ErrorCodes.GroupCapacityReached, () => w.AddGroupMember(group, extra));

        Assert.Equal(1, Field(ex.Details, "capacity"));
        Assert.Equal(1, Field(ex.Details, "activeMemberCount"));
        Assert.Equal(1, (await w.LoadGroup(group.Id)).Members.Count(m => m.IsActive));
    }

    [Fact]
    public async Task AddMember_BelowCapacity_JoinsTheRosterAndGetsAConfirmedBookingOnEveryFutureScheduledOccurrence()
    {
        (SchedulingWorld w, GroupDto group, Appointment occurrence, _) = await Occurrence(nameof(AddMember_BelowCapacity_JoinsTheRosterAndGetsAConfirmedBookingOnEveryFutureScheduledOccurrence), capacity: 3, members: 1);
        await using SchedulingWorld _w = w;
        Client joiner = await w.AddClient("Joiner", "Client");

        await w.AddGroupMember(group, joiner);

        Booking synced = (await w.LoadAppointment(occurrence.Id.Value)).Bookings.Single(b => b.ClientId == joiner.Id);
        Assert.Equal(BookingStatus.Confirmed, synced.Status);
        Assert.Equal(0, synced.StatusVersion);
        Assert.Equal(15m, synced.Amount);
        Assert.Equal(15m, synced.SuggestedAmount);
        Assert.Equal(2, (await w.LoadGroup(group.Id)).Members.Count(m => m.IsActive));
    }

    [Fact]
    public async Task AddMember_DoesNotTouchPastOccurrences()
    {
        (SchedulingWorld w, GroupDto group, _, _) = await Occurrence(nameof(AddMember_DoesNotTouchPastOccurrences), capacity: 3, members: 1);
        await using SchedulingWorld _w = w;
        Appointment past = await w.SeedAppointment(SchedulingWorld.Past(10), AppointmentStatus.Scheduled, AppointmentForm.Group,
            employee: w.Employee, groupId: group.Id);
        Client joiner = await w.AddClient("Joiner", "Client");

        await w.AddGroupMember(group, joiner);

        Assert.Empty((await w.LoadAppointment(past.Id.Value)).Bookings);
    }

    [Fact]
    public async Task AddMember_WhenAFutureOccurrenceIsAlreadyFullOfGuests_IsRejectedAndTheMembershipRollsBack()
    {
        (SchedulingWorld w, GroupDto group, Appointment occurrence, _) = await Occurrence(nameof(AddMember_WhenAFutureOccurrenceIsAlreadyFullOfGuests_IsRejectedAndTheMembershipRollsBack), capacity: 2, members: 1);
        await using SchedulingWorld _w = w;
        Client guest = await w.AddClient("Guest", "Client");
        Client joiner = await w.AddClient("Joiner", "Client");
        await w.AddGuest(occurrence, guest); // 1 member + 1 guest = 2 Confirmed = capacity, although the ROSTER has room (1 < 2)

        await SchedulingAssert.BusinessRule(ErrorCodes.GroupCapacityReached, () => w.AddGroupMember(group, joiner));

        // All-or-nothing: neither the membership row nor any Booking was created.
        Assert.Equal(1, (await w.LoadGroup(group.Id)).Members.Count(m => m.IsActive));
        Assert.DoesNotContain((await w.LoadAppointment(occurrence.Id.Value)).Bookings, b => b.ClientId == joiner.Id);
    }

    [Fact]
    public async Task AddMember_WhenTheClientIsBusyAtAFutureOccurrence_FailsWithRecurringConflictAndRollsBack()
    {
        (SchedulingWorld w, GroupDto group, Appointment occurrence, _) = await Occurrence(nameof(AddMember_WhenTheClientIsBusyAtAFutureOccurrence_FailsWithRecurringConflictAndRollsBack), capacity: 3, members: 1);
        await using SchedulingWorld _w = w;
        Client busy = await w.AddClient("Busy", "Client");
        Employee otherEmployee = await w.AddEmployee("Other");
        await w.CreateAppointment(occurrence.StartsAt, client: busy, employee: otherEmployee);

        BusinessRuleException ex = await SchedulingAssert.BusinessRule(ErrorCodes.RecurringConflict, () => w.AddGroupMember(group, busy));

        Assert.Contains(ErrorCodes.RecurringConflictReasonAppointment, SchedulingAssert.ConflictReasons(ex));
        Assert.Equal(1, (await w.LoadGroup(group.Id)).Members.Count(m => m.IsActive));
    }

    [Fact]
    public async Task AddMember_TheSameClientTwice_IsRejected()
    {
        (SchedulingWorld w, GroupDto group, _, Client[] members) = await Occurrence(nameof(AddMember_TheSameClientTwice_IsRejected), capacity: 3, members: 1);
        await using SchedulingWorld _w = w;

        await SchedulingAssert.BusinessRule(ErrorCodes.AlreadyMember, () => w.AddGroupMember(group, members[0]));
    }

    [Fact]
    public async Task AddMember_InactiveClient_IsRejected()
    {
        (SchedulingWorld w, GroupDto group, _, _) = await Occurrence(nameof(AddMember_InactiveClient_IsRejected), capacity: 3, members: 0);
        await using SchedulingWorld _w = w;
        Client inactive = await w.AddClient("Inactive", "Client", isActive: false);

        await SchedulingAssert.BusinessRule(ErrorCodes.InactiveClient, () => w.AddGroupMember(group, inactive));
    }

    [Fact]
    public async Task RemoveMember_CancelsTheirFutureConfirmedBookings_AuditsThem_AndEmitsACancelledEvent()
    {
        (SchedulingWorld w, GroupDto group, Appointment occurrence, Client[] members) = await Occurrence(nameof(RemoveMember_CancelsTheirFutureConfirmedBookings_AuditsThem_AndEmitsACancelledEvent), capacity: 3, members: 2);
        await using SchedulingWorld _w = w;
        Guid memberId = (await w.Groups.GetById(w.OrganizationId, group.Id)).Members.Single(m => m.ClientId == members[0].Id).Id;

        await w.Groups.RemoveMember(w.OrganizationId, w.ActorUserId, group.Id, memberId);

        Booking b = (await w.LoadAppointment(occurrence.Id.Value)).Bookings.Single(x => x.ClientId == members[0].Id);
        Assert.Equal(BookingStatus.Cancelled, b.Status);
        Assert.Equal(1, b.StatusVersion);
        Assert.Equal("Klijent uklonjen iz grupe", b.CancellationReason);
        Assert.Null(b.IsLateCancellation); // classification is only set by BookingService.SetStatus
        AppointmentAuditLog audit = Assert.Single(await w.LoadAuditLog(occurrence.Id.Value), l => l.ChangeType == "BookingStatus");
        Assert.Equal("Confirmed", audit.OldValue);
        Assert.Equal("Cancelled", audit.NewValue);
        OutboxMessage_Assert.SingleCancelled(await w.LoadOutbox(), b.Id.Value, b.Participations.Single().Id.Value, expectedVersion: 1);
        // The other member is untouched.
        Assert.Equal(BookingStatus.Confirmed, (await w.LoadAppointment(occurrence.Id.Value)).Bookings.Single(x => x.ClientId == members[1].Id).Status);
    }

    #endregion

    #region Occurrence capacity — guest bookings

    [Fact]
    public async Task AddBooking_Guest_WhileSeatsRemain_CreatesAConfirmedBookingWithTheResolvedPrice()
    {
        (SchedulingWorld w, _, Appointment occurrence, _) = await Occurrence(nameof(AddBooking_Guest_WhileSeatsRemain_CreatesAConfirmedBookingWithTheResolvedPrice), capacity: 2, members: 1);
        await using SchedulingWorld _w = w;
        Client guest = await w.AddClient("Guest", "Client");

        BookingDto dto = await w.AddGuest(occurrence, guest);

        Assert.Equal(BookingStatusSummary.Confirmed, dto.Status);
        Booking b = (await w.LoadAppointment(occurrence.Id.Value)).Bookings.Single(x => x.ClientId == guest.Id);
        Assert.Equal(15m, b.Amount);
        Assert.Equal(0, b.StatusVersion);
    }

    [Fact]
    public async Task AddBooking_Guest_WhenTheOccurrenceIsFull_IsRejected()
    {
        (SchedulingWorld w, _, Appointment occurrence, _) = await Occurrence(nameof(AddBooking_Guest_WhenTheOccurrenceIsFull_IsRejected), capacity: 1, members: 1);
        await using SchedulingWorld _w = w;
        Client guest = await w.AddClient("Guest", "Client");

        BusinessRuleException ex = await SchedulingAssert.BusinessRule(ErrorCodes.GroupCapacityReached, () => w.AddGuest(occurrence, guest));

        Assert.Equal(1, Field(ex.Details, "capacity"));
        Assert.Equal(1, Field(ex.Details, "confirmedCount"));
        Assert.Single((await w.LoadAppointment(occurrence.Id.Value)).Bookings);
    }

    [Fact]
    public async Task AddBooking_ForAClientWhoAlreadyHasABooking_ReturnsTheExistingOneEvenWhenFull()
    {
        (SchedulingWorld w, _, Appointment occurrence, Client[] members) = await Occurrence(nameof(AddBooking_ForAClientWhoAlreadyHasABooking_ReturnsTheExistingOneEvenWhenFull), capacity: 1, members: 1);
        await using SchedulingWorld _w = w;

        BookingDto dto = await w.AddGuest(occurrence, members[0]);

        Assert.Equal(BookingStatusSummary.Confirmed, dto.Status);
        Assert.Single((await w.LoadAppointment(occurrence.Id.Value)).Bookings);
    }

    [Fact]
    public async Task Capacity_CountsOnlyConfirmedBookings_CompletedAndNoShowBookingsDoNotOccupyASeat()
    {
        await using SchedulingWorld w = await SchedulingWorld.Create(nameof(Capacity_CountsOnlyConfirmedBookings_CompletedAndNoShowBookingsDoNotOccupyASeat));
        ServiceEntity svc = await w.AddGroupService();
        GroupDto group = await w.CreateGroup(svc, capacity: 2);
        Client done = await w.AddClient("Done", "Client");
        Client missed = await w.AddClient("Missed", "Client");
        Client guest = await w.AddClient("Guest", "Client");
        // Future occurrence whose two Bookings are already terminal (not a state the flows produce for a FUTURE start;
        // seeded only to isolate what the capacity count looks at).
        Appointment occurrence = await w.SeedAppointment(SchedulingWorld.Future(10), AppointmentStatus.Scheduled, AppointmentForm.Group,
            employee: w.Employee, service: svc, groupId: group.Id, durationMinutes: 60,
            bookings: new[] { (done, BookingStatus.Completed, 15m), (missed, BookingStatus.NoShow, 15m) });

        BookingDto dto = await w.AddGuest(occurrence, guest);

        Assert.Equal(BookingStatusSummary.Confirmed, dto.Status);
        Assert.Equal(3, (await w.LoadAppointment(occurrence.Id.Value)).Bookings.Count); // 3 Bookings > capacity 2, 1 Confirmed
    }

    [Fact]
    public async Task Capacity_IsReadLiveFromTheGroup_LoweringItBlocksBookingsOnAlreadyGeneratedOccurrences()
    {
        (SchedulingWorld w, GroupDto group, Appointment occurrence, _) = await Occurrence(nameof(Capacity_IsReadLiveFromTheGroup_LoweringItBlocksBookingsOnAlreadyGeneratedOccurrences), capacity: 3, members: 1);
        await using SchedulingWorld _w = w;
        Client guest = await w.AddClient("Guest", "Client");
        Client guest2 = await w.AddClient("Guest2", "Client");

        // FINDING: the occurrence has no capacity of its own; Group.Capacity is consulted at booking time.
        await w.Groups.Update(w.OrganizationId, w.ActorUserId, group.Id, new GroupUpdateRequest
        {
            Name = group.Name, ServiceId = group.ServiceId, CompanyId = group.CompanyId, Capacity = 1,
            DefaultTrainerId = group.DefaultTrainerId, DefaultRoomId = group.DefaultRoomId
        });
        await SchedulingAssert.BusinessRule(ErrorCodes.GroupCapacityReached, () => w.AddGuest(occurrence, guest));

        await w.Groups.Update(w.OrganizationId, w.ActorUserId, group.Id, new GroupUpdateRequest
        {
            Name = group.Name, ServiceId = group.ServiceId, CompanyId = group.CompanyId, Capacity = 5,
            DefaultTrainerId = group.DefaultTrainerId, DefaultRoomId = group.DefaultRoomId
        });
        BookingDto dto = await w.AddGuest(occurrence, guest2);
        Assert.Equal(BookingStatusSummary.Confirmed, dto.Status);
    }

    [Fact]
    public async Task AddBooking_OnAnIndividualAppointment_HasNoCapacityLimit()
    {
        await using SchedulingWorld w = await SchedulingWorld.Create(nameof(AddBooking_OnAnIndividualAppointment_HasNoCapacityLimit));
        AppointmentDto created = await w.CreateAppointment(SchedulingWorld.Future(10));
        List<Client> extra = new();
        for (int i = 0; i < 4; i++)
            extra.Add(await w.AddClient($"Extra{i}", "Client"));

        foreach (Client c in extra)
            await w.Bookings.AddBooking(w.OrganizationId, w.ActorUserId, true, created.Id, new BookingCreateRequest { ClientId = c.Id.Value });

        Assert.Equal(5, (await w.LoadAppointment(created.Id)).Bookings.Count);
    }

    [Fact]
    public async Task AddBooking_OnACancelledAppointment_IsRejected()
    {
        (SchedulingWorld w, _, Appointment occurrence, _) = await Occurrence(nameof(AddBooking_OnACancelledAppointment_IsRejected), capacity: 3, members: 1);
        await using SchedulingWorld _w = w;
        await w.Appointments.Cancel(w.OrganizationId, w.ActorUserId, true, occurrence.Id.Value, new AppointmentCancelRequest());
        Client guest = await w.AddClient("Guest", "Client");

        await SchedulingAssert.BusinessRule(ErrorCodes.AppointmentNotMovable, () => w.AddGuest(occurrence, guest));
    }

    [Fact]
    public async Task AddBooking_ForAClientBusyElsewhere_IsRejectedAsAnOverlap()
    {
        (SchedulingWorld w, _, Appointment occurrence, _) = await Occurrence(nameof(AddBooking_ForAClientBusyElsewhere_IsRejectedAsAnOverlap), capacity: 3, members: 1);
        await using SchedulingWorld _w = w;
        Client busy = await w.AddClient("Busy", "Client");
        Employee otherEmployee = await w.AddEmployee("Other");
        await w.CreateAppointment(occurrence.StartsAt, client: busy, employee: otherEmployee);

        await SchedulingAssert.BusinessRule(ErrorCodes.AppointmentOverlap, () => w.AddGuest(occurrence, busy));
    }

    [Fact]
    public async Task AddBooking_InactiveClient_IsRejected()
    {
        (SchedulingWorld w, _, Appointment occurrence, _) = await Occurrence(nameof(AddBooking_InactiveClient_IsRejected), capacity: 3, members: 1);
        await using SchedulingWorld _w = w;
        Client inactive = await w.AddClient("Inactive", "Client", isActive: false);

        await SchedulingAssert.BusinessRule(ErrorCodes.InactiveClient, () => w.AddGuest(occurrence, inactive));
    }

    [Fact]
    public async Task ConcurrentGuestBookings_ForTheLastSeat_ExactlyOneWins()
    {
        (SchedulingWorld w, _, Appointment occurrence, _) = await Occurrence(nameof(ConcurrentGuestBookings_ForTheLastSeat_ExactlyOneWins), capacity: 2, members: 1);
        await using SchedulingWorld _w = w;
        Client guestA = await w.AddClient("GuestA", "Client");
        Client guestB = await w.AddClient("GuestB", "Client");

        // Two independent service scopes = two independent connections/transactions racing for the one remaining seat.
        // GroupCapacityGuard locks the Appointment row FOR UPDATE and counts under the lock, so the loser sees the winner.
        using var scopeA = SchedulingTestHost.CreateScope();
        using var scopeB = SchedulingTestHost.CreateScope();
        Task<(bool Ok, string Code)> Attempt(IServiceScope scope, Client c) => Task.Run(async () =>
        {
            try
            {
                await scope.ServiceProvider.GetRequiredService<IBookingService>()
                    .AddBooking(w.OrganizationId, w.ActorUserId, true, occurrence.Id.Value, new BookingCreateRequest { ClientId = c.Id.Value });
                return (true, (string)null);
            }
            catch (BusinessRuleException ex)
            {
                return (false, ex.Code);
            }
        });

        (bool Ok, string Code)[] results = await Task.WhenAll(Attempt(scopeA, guestA), Attempt(scopeB, guestB));

        Assert.Equal(1, results.Count(r => r.Ok));
        Assert.Equal(ErrorCodes.GroupCapacityReached, results.Single(r => !r.Ok).Code);
        Assert.Equal(2, (await w.LoadAppointment(occurrence.Id.Value)).Bookings.Count(b => b.Status == BookingStatus.Confirmed));
    }

    #endregion

    #region Returning a Booking to Confirmed

    [Fact]
    public async Task ReturnToConfirmed_OnAFutureOccurrenceThatIsNowFull_IsRejected_AndTheBookingStaysCancelled()
    {
        (SchedulingWorld w, _, Appointment occurrence, Client[] members) = await Occurrence(nameof(ReturnToConfirmed_OnAFutureOccurrenceThatIsNowFull_IsRejected_AndTheBookingStaysCancelled), capacity: 1, members: 1);
        await using SchedulingWorld _w = w;
        Client guest = await w.AddClient("Guest", "Client");
        await w.SetBookingStatus(occurrence.Id.Value, members[0], BookingStatus.Cancelled, "changed my mind");
        await w.AddGuest(occurrence, guest); // takes the freed seat

        await SchedulingAssert.BusinessRule(ErrorCodes.GroupCapacityReached,
            () => w.SetBookingStatus(occurrence.Id.Value, members[0], BookingStatus.Confirmed));

        Booking b = await w.LoadBooking(occurrence.Id.Value, members[0]);
        Assert.Equal(BookingStatus.Cancelled, b.Status);
        Assert.Equal(1, b.StatusVersion); // no increment for the rejected attempt
    }

    [Fact]
    public async Task ReturnToConfirmed_WhenASeatIsFree_Succeeds_AndAdvancesTheStatusVersion()
    {
        (SchedulingWorld w, _, Appointment occurrence, Client[] members) = await Occurrence(nameof(ReturnToConfirmed_WhenASeatIsFree_Succeeds_AndAdvancesTheStatusVersion), capacity: 2, members: 1);
        await using SchedulingWorld _w = w;
        await w.SetBookingStatus(occurrence.Id.Value, members[0], BookingStatus.Cancelled, "oops");

        BookingDto dto = await w.SetBookingStatus(occurrence.Id.Value, members[0], BookingStatus.Confirmed);

        Assert.Equal(BookingStatusSummary.Confirmed, dto.Status);
        Assert.Equal(2, (await w.LoadBooking(occurrence.Id.Value, members[0])).StatusVersion);
    }

    [Fact]
    public async Task ConfirmedToConfirmed_IsAnIdempotentNoOp_EvenWhenTheOccurrenceIsFull()
    {
        (SchedulingWorld w, _, Appointment occurrence, Client[] members) = await Occurrence(nameof(ConfirmedToConfirmed_IsAnIdempotentNoOp_EvenWhenTheOccurrenceIsFull), capacity: 1, members: 1);
        await using SchedulingWorld _w = w;

        BookingDto dto = await w.SetBookingStatus(occurrence.Id.Value, members[0], BookingStatus.Confirmed);

        Assert.Equal(BookingStatusSummary.Confirmed, dto.Status);
        Assert.Equal(0, (await w.LoadBooking(occurrence.Id.Value, members[0])).StatusVersion);
    }

    [Fact]
    public async Task ReturnToConfirmed_OnAnOccurrenceThatAlreadyStarted_IgnoresCapacity()
    {
        await using SchedulingWorld w = await SchedulingWorld.Create(nameof(ReturnToConfirmed_OnAnOccurrenceThatAlreadyStarted_IgnoresCapacity));
        ServiceEntity svc = await w.AddGroupService();
        GroupDto group = await w.CreateGroup(svc, capacity: 1);
        Client seated = await w.AddClient("Seated", "Client");
        Client returning = await w.AddClient("Returning", "Client");
        Appointment past = await w.SeedAppointment(SchedulingWorld.Past(10), AppointmentStatus.Scheduled, AppointmentForm.Group,
            employee: w.Employee, service: svc, groupId: group.Id, durationMinutes: 60,
            bookings: new[] { (seated, BookingStatus.Confirmed, 15m), (returning, BookingStatus.NoShow, 15m) });

        // Capacity is only enforced for FUTURE occurrences: a historical correction may exceed the nominal capacity.
        BookingDto dto = await w.SetBookingStatus(past.Id.Value, returning, BookingStatus.Confirmed);

        Assert.Equal(BookingStatusSummary.Confirmed, dto.Status);
        Assert.Equal(2, (await w.LoadAppointment(past.Id.Value)).Bookings.Count(b => b.Status == BookingStatus.Confirmed));
    }

    #endregion
}

/// <summary>Assertions on outbox rows (kept next to the tests that use them).</summary>
internal static class OutboxMessage_Assert
{
    public static void SingleCancelled(
        List<BlueDragon.DuneLight.Infrastructure.Domain.Models.Outbox.OutboxMessage> outbox, Guid bookingId, Guid participationId, int expectedVersion)
    {
        // M0: the occurrence belongs to the PARTICIPATION (key = participation + its StatusVersion); the Booking is context.
        var message = Assert.Single(outbox, m => m.Type == OutboxEventTypes.BookingCancelledV1);
        Assert.Equal($"booking-cancelled:{participationId}:{expectedVersion}", message.IdempotencyKey);
        Assert.Contains(bookingId.ToString(), message.Payload, StringComparison.OrdinalIgnoreCase);
        Assert.Contains(participationId.ToString(), message.Payload, StringComparison.OrdinalIgnoreCase);
    }
}
