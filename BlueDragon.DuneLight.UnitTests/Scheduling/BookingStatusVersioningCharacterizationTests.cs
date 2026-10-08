#nullable disable
using System;
using System.Linq;
using System.Threading.Tasks;
using BlueDragon.DuneLight.Core.DTOs.Appointments;
using BlueDragon.DuneLight.Core.Enums;
using BlueDragon.DuneLight.Core.Events;
using BlueDragon.DuneLight.Core.Shared;
using BlueDragon.DuneLight.Infrastructure.Domain.Models.Appointments;
using BlueDragon.DuneLight.Infrastructure.Domain.Models.Commissions;
using BlueDragon.DuneLight.Infrastructure.Domain.Models.Outbox;
using BlueDragon.DuneLight.Infrastructure.Utils;
using BlueDragon.DuneLight.Infrastructure.Domain.Models.Clients;

namespace BlueDragon.DuneLight.UnitTests.Scheduling;

/// <summary>
/// CHARACTERIZATION (matrix J): <see cref="BookingStatusVersioning"/> and the StatusVersion counter it maintains.
///
/// StatusVersion identifies ONE occurrence of a status transition. It is the idempotency identity of the
/// booking.cancelled.v1 / booking.no-show.v1 outbox events (key <c>booking-cancelled:{participationId}:{version}</c> since M0), of
/// Notification.SourceVersion, and of CommissionEntry.SourceVersion — so it must advance exactly once per REAL transition
/// and never on an idempotent re-set. The first region is a pure unit test of the helper; the second region proves the
/// same numbers through the real flows.
/// </summary>
public class BookingStatusVersioningCharacterizationTests
{
    #region The helper in isolation

    [Fact]
    public void TrySetStatus_ARealTransition_ChangesTheStatusAndIncrementsTheVersionByExactlyOne()
    {
        Booking booking = SingleSegmentTestExtensions.InMemoryBooking(BookingStatus.Confirmed, 0);

        bool changed = ParticipationLifecycle.TrySetStatus(Assert.Single(booking.Participations), ParticipationStatus.Completed);

        Assert.True(changed);
        Assert.Equal(BookingStatus.Completed, booking.Status);
        Assert.Equal(1, booking.StatusVersion);
    }

    [Theory]
    [InlineData(BookingStatus.Confirmed)]
    [InlineData(BookingStatus.Completed)]
    [InlineData(BookingStatus.Cancelled)]
    [InlineData(BookingStatus.NoShow)]
    public void TrySetStatus_TheSameStatusAgain_IsANoOp_AndDoesNotIncrementTheVersion(BookingStatus status)
    {
        Booking booking = SingleSegmentTestExtensions.InMemoryBooking(status, 7);

        bool changed = ParticipationLifecycle.TrySetStatus(Assert.Single(booking.Participations), BookingParticipations.ToParticipationStatus(status));

        Assert.False(changed);
        Assert.Equal(status, booking.Status);
        Assert.Equal(7, booking.StatusVersion);
    }

    [Fact]
    public void TrySetStatus_ACycleOfTransitions_AdvancesOncePerRealChange_AndSkipsRepeats()
    {
        Booking booking = SingleSegmentTestExtensions.InMemoryBooking(BookingStatus.Confirmed);

        ParticipationLifecycle.TrySetStatus(Assert.Single(booking.Participations), ParticipationStatus.NoShow);      // 1
        ParticipationLifecycle.TrySetStatus(Assert.Single(booking.Participations), ParticipationStatus.NoShow);      // repeat: no change
        ParticipationLifecycle.TrySetStatus(Assert.Single(booking.Participations), ParticipationStatus.Confirmed);   // 2
        ParticipationLifecycle.TrySetStatus(Assert.Single(booking.Participations), ParticipationStatus.NoShow);      // 3 — a NEW occurrence of NoShow
        ParticipationLifecycle.TrySetStatus(Assert.Single(booking.Participations), ParticipationStatus.Cancelled);   // 4

        Assert.Equal(4, booking.StatusVersion);
        Assert.Equal(BookingStatus.Cancelled, booking.Status);
    }

    [Fact]
    public void TrySetStatus_DoesNotTouchAnyOtherBookingField()
    {
        Booking booking = SingleSegmentTestExtensions.InMemoryBooking(BookingStatus.Confirmed, cancellationReason: "r");
        ParticipationPrice.Apply(Assert.Single(booking.Participations), new BookingPricing(50m, 50m, false)); // D3B2: price lives on the participation
        booking.Note = "n";
        Assert.Single(booking.Participations).PackageConsumptions.Add(new PackageConsumption
        {
            Id = Guid.NewGuid(), Status = PackageConsumptionStatus.Consumed, Units = 1 // D3B3A: package usage is ledger state
        });

        ParticipationLifecycle.TrySetStatus(Assert.Single(booking.Participations), ParticipationStatus.Cancelled);

        Assert.Equal(50m, booking.Amount);
        Assert.Equal("n", booking.Note);
        Assert.Equal("r", booking.CancellationReason);
        Assert.True(booking.PackageCoverageApplied);
    }

    #endregion

    #region Through the real flows

    [Fact]
    public async Task Individual_CancelThroughSetStatus_MovesTheVersionFromZeroToOne_AndKeysTheOutboxEventOnIt()
    {
        await using SchedulingWorld w = await SchedulingWorld.Create(nameof(Individual_CancelThroughSetStatus_MovesTheVersionFromZeroToOne_AndKeysTheOutboxEventOnIt));
        AppointmentDto created = await w.CreateAppointment(SchedulingWorld.Future(10));

        await w.SetBookingStatus(created.Id, w.Client, BookingStatus.Cancelled, "cannot come");

        Booking b = await w.LoadBooking(created.Id, w.Client);
        Assert.Equal(1, b.StatusVersion);
        OutboxMessage message = Assert.Single(await w.LoadOutbox());
        Assert.Equal(OutboxEventTypes.BookingCancelledV1, message.Type);
        Assert.Equal($"booking-cancelled:{b.Participations.Single().Id}:1", message.IdempotencyKey); // M0: participation occurrence
        AppointmentAuditLog audit = Assert.Single(await w.LoadAuditLog(created.Id), l => l.ChangeType == "BookingStatus");
        Assert.Equal(1, audit.StatusVersion);
    }

    [Fact]
    public async Task Individual_CompleteExisting_MovesTheBookingToVersionOne()
    {
        await using SchedulingWorld w = await SchedulingWorld.Create(nameof(Individual_CompleteExisting_MovesTheBookingToVersionOne));
        AppointmentDto created = await w.CreateAppointment(SchedulingWorld.Future(10));

        await w.CompleteParticipations(created.Id, w.CompleteRequest(SchedulingWorld.Future(10)));

        Assert.Equal(1, (await w.LoadBooking(created.Id, w.Client)).StatusVersion);
    }

    [Fact]
    public async Task Individual_CompleteNow_CompletesThroughOneTransition_AtVersionOne()
    {
        await using SchedulingWorld w = await SchedulingWorld.Create(nameof(Individual_CompleteNow_CompletesThroughOneTransition_AtVersionOne));

        AppointmentDto completed = await w.CompleteNew(w.CompleteRequest(SchedulingWorld.Past(10)));

        // CHANGED in M1H (was FINDING F-07: version 0, the booking was built directly as Completed): CompleteNow creates the
        // participation Confirmed and runs the same lifecycle transition as completing an existing one — version 1, like
        // every other completion. Commission entries and audit rows no longer differ by path.
        Booking b = await w.LoadBooking(completed.Id, w.Client);
        Assert.Equal(BookingStatus.Completed, b.Status);
        Assert.Equal(1, b.StatusVersion);
    }

    [Fact]
    public async Task Individual_CorrectionThenReCompletion_ProducesFurtherVersions_AndDistinctCommissionOccurrences()
    {
        await using SchedulingWorld w = await SchedulingWorld.Create(nameof(Individual_CorrectionThenReCompletion_ProducesFurtherVersions_AndDistinctCommissionOccurrences));
        await w.AddCommissionRule(w.Employee, w.Service, CommissionCalculationType.Percentage, 10m);
        AppointmentDto created = await w.CreateAppointment(SchedulingWorld.Future(10));
        Guid bookingId = created.Bookings.Single().Id;

        await w.CompleteParticipations(created.Id, w.CompleteRequest(SchedulingWorld.Future(10)));                       // Confirmed(0) -> Completed(1)
        Assert.Equal(1, (await w.LoadBooking(created.Id, w.Client)).StatusVersion);

        await w.SetBookingStatus(created.Id, w.Client, BookingStatus.Confirmed);                                    // Completed(1) -> Confirmed(2)
        Assert.Equal(2, (await w.LoadBooking(created.Id, w.Client)).StatusVersion);

        await w.CompleteParticipations(created.Id, w.CompleteRequest(SchedulingWorld.Future(10)));                       // Confirmed(2) -> Completed(3)
        Assert.Equal(3, (await w.LoadBooking(created.Id, w.Client)).StatusVersion);

        // Two distinct occurrences of "earned": the first was reversed by the correction, the second is the live one.
        var entries = await w.LoadCommissionEntries();
        Assert.Equal(2, entries.Count);
        CommissionEntry first = entries.Single(e => e.SourceVersion == 1);
        CommissionEntry second = entries.Single(e => e.SourceVersion == 3);
        Assert.Equal(CommissionEntryStatus.Reversed, first.Status);
        Assert.Equal(CommissionEntryStatus.Earned, second.Status);
        Assert.All(entries, e => Assert.Equal(bookingId, e.BookingId));
    }

    [Fact]
    public async Task Individual_NoShowCorrectionThenNoShowAgain_ProducesANewOccurrenceKeyForTheOutbox()
    {
        await using SchedulingWorld w = await SchedulingWorld.Create(nameof(Individual_NoShowCorrectionThenNoShowAgain_ProducesANewOccurrenceKeyForTheOutbox));
        AppointmentDto created = await w.CreateAppointment(SchedulingWorld.Past(10));
        Guid participationId = created.Bookings.Single().Participations.Single().Id; // M0: occurrences are per participation

        await w.SetBookingStatus(created.Id, w.Client, BookingStatus.NoShow);      // 1
        await w.SetBookingStatus(created.Id, w.Client, BookingStatus.Confirmed);   // 2 (correction)
        await w.SetBookingStatus(created.Id, w.Client, BookingStatus.NoShow);      // 3

        Assert.Equal(3, (await w.LoadBooking(created.Id, w.Client)).StatusVersion);
        Assert.Equal(
            new[] { $"booking-noshow:{participationId}:1", $"booking-noshow:{participationId}:3" },
            (await w.LoadOutbox()).Where(m => m.Type == OutboxEventTypes.BookingNoShowV1).Select(m => m.IdempotencyKey).OrderBy(k => k).ToArray());
    }

    [Fact]
    public async Task Group_CancelledCyclingBackAndForth_AdvancesTheVersionOnEveryRealTransition_ButNotOnRepeats()
    {
        await using SchedulingWorld w = await SchedulingWorld.Create(nameof(Group_CancelledCyclingBackAndForth_AdvancesTheVersionOnEveryRealTransition_ButNotOnRepeats));
        Infrastructure.Domain.Models.Catalog.Service svc = await w.AddGroupService();
        var group = await w.CreateGroup(svc, capacity: 3);
        await w.AddGroupMember(group, w.Client);
        Appointment occurrence = await w.GenerateSingleOccurrence(group);

        await w.SetBookingStatus(occurrence.Id.Value, w.Client, BookingStatus.Cancelled);   // 1
        await w.SetBookingStatus(occurrence.Id.Value, w.Client, BookingStatus.Cancelled);   // repeat (group is idempotent here)
        await w.SetBookingStatus(occurrence.Id.Value, w.Client, BookingStatus.Confirmed);   // 2
        await w.SetBookingStatus(occurrence.Id.Value, w.Client, BookingStatus.Cancelled);   // 3

        Assert.Equal(3, (await w.LoadBooking(occurrence.Id.Value, w.Client)).StatusVersion);
        Assert.Equal(2, (await w.LoadOutbox()).Count(m => m.Type == OutboxEventTypes.BookingCancelledV1)); // versions 1 and 3 only
    }

    [Fact]
    public async Task Individual_RepeatingATerminalStatus_IsATrueNoOp_LikeGroup()
    {
        // CHANGED in P1 (D12, intentional): Individual repeat used to be ALREADY_COMPLETED; same status is now a true no-op.
        await using SchedulingWorld w = await SchedulingWorld.Create(nameof(Individual_RepeatingATerminalStatus_IsATrueNoOp_LikeGroup));
        AppointmentDto created = await w.CreateAppointment(SchedulingWorld.Future(10));
        await w.SetBookingStatus(created.Id, w.Client, BookingStatus.Cancelled);

        await w.SetBookingStatus(created.Id, w.Client, BookingStatus.Cancelled);

        Assert.Equal(1, (await w.LoadBooking(created.Id, w.Client)).StatusVersion);
        Assert.Single(await w.LoadOutbox());
    }

    #endregion
}
