#nullable disable
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using BlueDragon.DuneLight.Core.DTOs.Appointments;
using BlueDragon.DuneLight.Core.DTOs.Checkouts;
using BlueDragon.DuneLight.Core.Enums;
using BlueDragon.DuneLight.Core.Events;
using BlueDragon.DuneLight.Core.Shared;
using BlueDragon.DuneLight.Infrastructure.Domain.Contexts;
using BlueDragon.DuneLight.Infrastructure.Domain.Models.Appointments;
using BlueDragon.DuneLight.Infrastructure.Domain.Models.Checkouts;
using BlueDragon.DuneLight.Infrastructure.Domain.Models.Clients;
using BlueDragon.DuneLight.Infrastructure.Domain.Models.Commissions;
using BlueDragon.DuneLight.Infrastructure.Domain.Models.Outbox;
using BlueDragon.DuneLight.Infrastructure.Handlers.Implementations;
using BlueDragon.DuneLight.Infrastructure.Handlers.Interfaces;
using BlueDragon.DuneLight.Infrastructure.Services;
using BlueDragon.DuneLight.Infrastructure.UnitOfWork;
using BlueDragon.DuneLight.Infrastructure.Utils;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace BlueDragon.DuneLight.UnitTests.Scheduling;

/// <summary>
/// Phase M0 — participation-native addressing, aggregation and locking. Production still creates exactly one segment and
/// one participation per Booking, so the multi-participation cases use ARTIFICIAL data
/// (<see cref="SchedulingWorld.AddArtificialSegmentParticipation"/>): a second segment on the same appointment and a second
/// participation of the same Booking on it. Booking has no lifecycle, price, settlement or version of its own — every
/// Booking-level value asserted here is a DERIVED summary of its participations.
/// </summary>
public class ParticipationNativeAddressingTests
{
    private const decimal SecondAmount = 30m; // first participation: the default service price (50)

    private static async Task<(SchedulingWorld World, AppointmentDto Appointment, Guid First, Guid Second)> TwoParticipations(
        string name, ParticipationStatus secondStatus = ParticipationStatus.Confirmed)
    {
        SchedulingWorld w = await SchedulingWorld.Create(name);
        AppointmentDto created = await w.CreateAppointment(SchedulingWorld.Future(10));
        Guid first = created.Bookings.Single().Participations.Single().Id;
        Guid second = await w.AddArtificialSegmentParticipation(created.Id, w.Client, SchedulingWorld.Future(14), SecondAmount, secondStatus);
        return (w, created, first, second);
    }

    private static Task<BookingDto> SetParticipation(SchedulingWorld w, Guid participationId, BookingStatus status) =>
        w.Bookings.SetParticipationStatus(w.OrganizationId, w.ActorUserId, true, participationId, new BookingSetStatusRequest { Status = status, CancellationInitiator = status == BookingStatus.Cancelled ? CancellationInitiator.Client : null });

    private static BookingSegmentParticipation P(ParticipationStatus status, decimal amount = 0m) => new()
    {
        Id = Guid.NewGuid(), Status = status, Amount = amount, SuggestedAmount = amount
    };

    private static Booking InMemory(params BookingSegmentParticipation[] participations)
    {
        Booking booking = new() { Id = Guid.NewGuid(), OrganizationId = Guid.NewGuid(), AppointmentId = Guid.NewGuid() };
        foreach (BookingSegmentParticipation p in participations)
        {
            p.BookingId = booking.Id.Value;
            p.OrganizationId = booking.OrganizationId;
            p.AppointmentSegmentId = Guid.NewGuid();
            booking.Participations.Add(p);
        }
        return booking;
    }

    private static void Settle(BookingSegmentParticipation participation, decimal amount, PaymentStatus status = PaymentStatus.Completed)
    {
        Payment payment = new() { Id = Guid.NewGuid(), Amount = amount, Status = status, CreatedAt = TestClock.UtcNow };
        CheckoutItem item = new() { Id = Guid.NewGuid(), Type = CheckoutItemType.Booking, Amount = amount, BookingSegmentParticipationId = participation.Id };
        item.Allocations.Add(new PaymentAllocation { Id = Guid.NewGuid(), Amount = amount, Payment = payment, PaymentId = payment.Id.Value });
        participation.CheckoutItems.Add(item);
    }

    #region Derived Booking status summary

    [Theory]
    [InlineData(new[] { ParticipationStatus.Confirmed, ParticipationStatus.Confirmed }, BookingStatusSummary.Confirmed)]
    [InlineData(new[] { ParticipationStatus.Completed, ParticipationStatus.Completed }, BookingStatusSummary.Completed)]
    [InlineData(new[] { ParticipationStatus.Cancelled, ParticipationStatus.Cancelled }, BookingStatusSummary.Cancelled)]
    [InlineData(new[] { ParticipationStatus.NoShow, ParticipationStatus.NoShow }, BookingStatusSummary.NoShow)]
    [InlineData(new[] { ParticipationStatus.Completed, ParticipationStatus.Cancelled }, BookingStatusSummary.Mixed)]
    [InlineData(new[] { ParticipationStatus.Confirmed, ParticipationStatus.Completed }, BookingStatusSummary.Mixed)]
    [InlineData(new[] { ParticipationStatus.NoShow, ParticipationStatus.Cancelled }, BookingStatusSummary.Mixed)]
    [InlineData(new[] { ParticipationStatus.Confirmed }, BookingStatusSummary.Confirmed)]
    [InlineData(new[] { ParticipationStatus.NoShow }, BookingStatusSummary.NoShow)]
    public void StatusSummary_IsTheCommonStatus_OrMixed(ParticipationStatus[] statuses, BookingStatusSummary expected)
    {
        Booking booking = InMemory(statuses.Select(s => P(s)).ToArray());

        Assert.Equal(expected, BookingSummary.StatusOf(booking));
    }

    [Fact]
    public void StatusSummary_IsNeverATransitionTarget_AndHasTheSameNamesAsBookingStatusForOneParticipation()
    {
        // Commands take BookingStatus, which has no Mixed member — the summary cannot be requested as a target.
        Assert.Equal(typeof(BookingStatus), typeof(BookingSetStatusRequest).GetProperty(nameof(BookingSetStatusRequest.Status))!.PropertyType);
        Assert.DoesNotContain("Mixed", Enum.GetNames<BookingStatus>());
        // Single-participation JSON output is unchanged: the first four names (and values) are identical.
        Assert.Equal(Enum.GetNames<BookingStatus>(), Enum.GetNames<BookingStatusSummary>().Take(4));
        foreach (BookingStatus status in Enum.GetValues<BookingStatus>())
            Assert.Equal((int)status, (int)Enum.Parse<BookingStatusSummary>(status.ToString()));
        // The read model exposes the summary, never a stored Booking status.
        Assert.Null(typeof(Booking).GetProperty("Status"));
        Assert.Null(typeof(Booking).GetProperty("StatusVersion"));
        Assert.Null(typeof(Booking).GetProperty("Amount"));
    }

    #endregion

    #region Derived Booking commercial summary

    [Fact]
    public void CommercialSummary_SumsPrices_SettledAndOutstanding_AcrossParticipations()
    {
        BookingSegmentParticipation a = P(ParticipationStatus.Completed, 50m);
        BookingSegmentParticipation b = P(ParticipationStatus.Confirmed, 30m);
        Settle(a, 50m);
        Settle(b, 10m);
        Settle(b, 99m, PaymentStatus.Voided); // a voided payment never counts

        BookingCommercialSummary summary = BookingCommercialSummary.Of(InMemory(a, b));

        Assert.Equal(80m, summary.FinalPrice);
        Assert.Equal(60m, summary.MonetarySettled);
        Assert.Equal(20m, summary.Outstanding);
        Assert.False(summary.FullySettled);
        Assert.Equal(2, summary.ParticipationCount);
        Assert.Equal(0, summary.PackageCoveredCount);
    }

    [Fact]
    public void CommercialSummary_PackageCoverageIsNotCash_AndFullySettledNeedsEveryParticipationAtZero()
    {
        BookingSegmentParticipation paid = P(ParticipationStatus.Completed, 50m);
        BookingSegmentParticipation covered = P(ParticipationStatus.Completed, 30m);
        Settle(paid, 50m);
        covered.PackageConsumptions.Add(new PackageConsumption { Id = Guid.NewGuid(), Status = PackageConsumptionStatus.Consumed, Units = 1 });

        BookingCommercialSummary summary = BookingCommercialSummary.Of(InMemory(paid, covered));

        Assert.Equal(80m, summary.FinalPrice);       // the price never changes because of a package
        Assert.Equal(50m, summary.MonetarySettled);  // the package is not money
        Assert.Equal(0m, summary.Outstanding);
        Assert.True(summary.FullySettled);
        Assert.Equal(1, summary.PackageCoveredCount);

        // One participation still owing => not fully settled, even if the other one is covered.
        BookingSegmentParticipation owing = P(ParticipationStatus.Confirmed, 20m);
        BookingCommercialSummary partly = BookingCommercialSummary.Of(InMemory(covered, owing));
        Assert.Equal((0m, 20m, false), (partly.MonetarySettled, partly.Outstanding, partly.FullySettled));
    }

    #endregion

    #region Participation-native addressing and Booking-wide commands

    [Fact]
    public async Task ParticipationCommand_AffectsOnlyItsTarget_AndTheBookingSummaryBecomesMixed()
    {
        (SchedulingWorld w, AppointmentDto created, Guid first, Guid second) = await TwoParticipations(nameof(ParticipationCommand_AffectsOnlyItsTarget_AndTheBookingSummaryBecomesMixed));
        await using SchedulingWorld _ = w;

        BookingDto dto = await SetParticipation(w, second, BookingStatus.Cancelled);

        List<BookingSegmentParticipation> rows = await w.LoadParticipations(created.Id, w.Client);
        Assert.Equal((ParticipationStatus.Confirmed, 0), (rows.Single(p => p.Id == first).Status, rows.Single(p => p.Id == first).StatusVersion));
        Assert.Equal((ParticipationStatus.Cancelled, 1), (rows.Single(p => p.Id == second).Status, rows.Single(p => p.Id == second).StatusVersion));

        Assert.Equal(BookingStatusSummary.Mixed, dto.Status);
        Assert.Equal(2, dto.Participations.Count);
        Assert.Equal(BookingStatus.Cancelled, dto.Participations.Single(p => p.Id == second).Status);

        // One occurrence, keyed on the TARGET participation and its own version; audit row carries the participation.
        OutboxMessage message = Assert.Single(await w.LoadOutbox());
        Assert.Equal($"booking-cancelled:{second}:1", message.IdempotencyKey);
        AppointmentAuditLog audit = Assert.Single(await w.LoadAuditLog(created.Id), l => l.ChangeType == "BookingStatus");
        Assert.Equal(second, audit.BookingSegmentParticipationId);
    }

    [Fact]
    public async Task BookingWideCancel_CancelsEveryActiveParticipation_EachWithItsOwnVersionAuditAndOccurrence()
    {
        (SchedulingWorld w, AppointmentDto created, Guid first, Guid second) = await TwoParticipations(nameof(BookingWideCancel_CancelsEveryActiveParticipation_EachWithItsOwnVersionAuditAndOccurrence));
        await using SchedulingWorld _ = w;

        BookingDto dto = await w.Bookings.CancelBooking(w.OrganizationId, w.ActorUserId, true, created.Id, w.Client.Id.Value,
            SchedulingWorld.ClientCancel("client left"));

        Assert.Equal(BookingStatusSummary.Cancelled, dto.Status);
        List<BookingSegmentParticipation> rows = await w.LoadParticipations(created.Id, w.Client);
        Assert.All(rows, p => Assert.Equal((ParticipationStatus.Cancelled, 1, "client left"), (p.Status, p.StatusVersion, p.CancellationReason)));

        Assert.Equal(
            new[] { $"booking-cancelled:{first}:1", $"booking-cancelled:{second}:1" }.OrderBy(k => k),
            (await w.LoadOutbox()).Select(m => m.IdempotencyKey).OrderBy(k => k));
        Assert.Equal(
            new[] { first, second }.OrderBy(x => x),
            (await w.LoadAuditLog(created.Id)).Where(l => l.ChangeType == "BookingStatus").Select(l => l.BookingSegmentParticipationId.Value).OrderBy(x => x));
    }

    [Fact]
    public async Task BookingWideCancel_LeavesTerminalParticipationsAsHistory_AndVersionsAdvanceIndependently()
    {
        (SchedulingWorld w, AppointmentDto created, Guid first, Guid second) = await TwoParticipations(nameof(BookingWideCancel_LeavesTerminalParticipationsAsHistory_AndVersionsAdvanceIndependently));
        await using SchedulingWorld _ = w;
        await SetParticipation(w, first, BookingStatus.Completed);    // first: v1 Completed
        await SetParticipation(w, first, BookingStatus.Confirmed);    // first: v2 Confirmed (correction)
        await SetParticipation(w, first, BookingStatus.Completed);    // first: v3 Completed

        BookingDto dto = await w.Bookings.CancelBooking(w.OrganizationId, w.ActorUserId, true, created.Id, w.Client.Id.Value, SchedulingWorld.ClientCancel());

        List<BookingSegmentParticipation> rows = await w.LoadParticipations(created.Id, w.Client);
        Assert.Equal((ParticipationStatus.Completed, 3), (rows.Single(p => p.Id == first).Status, rows.Single(p => p.Id == first).StatusVersion));
        Assert.Equal((ParticipationStatus.Cancelled, 1), (rows.Single(p => p.Id == second).Status, rows.Single(p => p.Id == second).StatusVersion));
        Assert.Equal(BookingStatusSummary.Mixed, dto.Status); // Completed + Cancelled

        // Nothing active left on a multi-participation Booking: the Booking-wide command refuses instead of guessing.
        await SchedulingAssert.BusinessRule(ErrorCodes.NoActiveParticipations /* P1 (D12) */,
            () => w.Bookings.CancelBooking(w.OrganizationId, w.ActorUserId, true, created.Id, w.Client.Id.Value, SchedulingWorld.ClientCancel()));
    }

    [Fact]
    public async Task SingleParticipationBooking_BookingWideCancel_BehavesExactlyLikeTheParticipationCommand()
    {
        await using SchedulingWorld w = await SchedulingWorld.Create(nameof(SingleParticipationBooking_BookingWideCancel_BehavesExactlyLikeTheParticipationCommand));
        AppointmentDto created = await w.CreateAppointment(SchedulingWorld.Future(10));
        Guid participationId = created.Bookings.Single().Participations.Single().Id;

        BookingDto dto = await w.Bookings.CancelBooking(w.OrganizationId, w.ActorUserId, true, created.Id, w.Client.Id.Value, SchedulingWorld.ClientCancel());
        Assert.Equal(BookingStatusSummary.Cancelled, dto.Status);
        Assert.Equal($"booking-cancelled:{participationId}:1", Assert.Single(await w.LoadOutbox()).IdempotencyKey);

        // CHANGED in P1 (D12/D13): with nothing active left the Booking-wide command returns NO_ACTIVE_PARTICIPATIONS.
        await SchedulingAssert.BusinessRule(ErrorCodes.NoActiveParticipations,
            () => w.Bookings.CancelBooking(w.OrganizationId, w.ActorUserId, true, created.Id, w.Client.Id.Value, SchedulingWorld.ClientCancel()));
        Assert.Equal(1, (await w.LoadBooking(created.Id, w.Client)).StatusVersion);
    }

    #endregion

    #region Locking

    [Fact]
    public void LockOrder_IsDistinctAndAscendingById()
    {
        Guid a = Guid.Parse("00000000-0000-0000-0000-000000000001");
        Guid b = Guid.Parse("00000000-0000-0000-0000-000000000002");
        Guid c = Guid.Parse("00000000-0000-0000-0000-000000000003");

        Assert.Equal(new[] { a, b, c }, BookingSegmentParticipationHandler.LockOrder(new[] { c, a, b, a, c }));
        Assert.Equal(BookingSegmentParticipationHandler.LockOrder(new[] { b, a }), BookingSegmentParticipationHandler.LockOrder(new[] { a, b }));
    }

    [Fact]
    public async Task TwoParticipationsOfOneBooking_AreLockedIndependently_AndTheBookingRowIsNotLocked()
    {
        (SchedulingWorld w, AppointmentDto created, Guid first, Guid second) = await TwoParticipations(nameof(TwoParticipationsOfOneBooking_AreLockedIndependently_AndTheBookingRowIsNotLocked));
        await using SchedulingWorld _ = w;
        IBookingSegmentParticipationHandler handler = w.Resolve<IBookingSegmentParticipationHandler>();
        IUnitOfWorkFactory factory = w.Resolve<IUnitOfWorkFactory>();
        Guid bookingId = created.Bookings.Single().Id;

        await using IUnitOfWork holder = await factory.Begin();
        await handler.LockForUpdate(holder, w.OrganizationId, new[] { first });

        await using IUnitOfWork other = await factory.Begin();
        await other.Context.Database.ExecuteSqlRawAsync("SET LOCAL lock_timeout = '300ms'");

        // The sibling participation and the Booking row itself are free.
        await handler.LockForUpdate(other, w.OrganizationId, new[] { second });
        await other.Context.Database.ExecuteSqlInterpolatedAsync(
            $"SELECT 1 FROM dunelight.bookings WHERE id = {bookingId} FOR UPDATE");

        // The held participation is not.
        PostgresException ex = await Assert.ThrowsAnyAsync<PostgresException>(
            () => handler.LockForUpdate(other, w.OrganizationId, new[] { first }));
        Assert.Equal(PostgresErrorCodes.LockNotAvailable, ex.SqlState);
    }

    #endregion

    #region Segment-scoped scheduling

    [Fact]
    public async Task ClientOccupancy_IsPerSegment_ACancelledSegmentDoesNotBlockWhileTheActiveOneDoes()
    {
        (SchedulingWorld w, AppointmentDto created, Guid first, Guid second) = await TwoParticipations(
            nameof(ClientOccupancy_IsPerSegment_ACancelledSegmentDoesNotBlockWhileTheActiveOneDoes), ParticipationStatus.Cancelled);
        await using SchedulingWorld _ = w;
        ISchedulingOccupancyHandler occupancy = w.Resolve<ISchedulingOccupancyHandler>();
        List<Guid> clientIds = new() { w.Client.Id.Value };

        // Segment A (10:00, Confirmed) occupies the client; segment B (14:00, Cancelled) does not — even though the SAME
        // Booking has an active participation (the old Booking-level predicate would have reported both).
        OccupancySlot atA = Assert.Single(await w.ClientsOverlapping(clientIds, SchedulingWorld.Future(10), 30));
        Assert.Contains(w.Client.Id.Value, atA.ActiveClientIds);
        Assert.Empty(await w.ClientsOverlapping(clientIds, SchedulingWorld.Future(14), 30));

        // Range read: one slot per segment; only A lists the client as active.
        List<OccupancySlot> range = await occupancy.GetForClientsInRange(
            w.OrganizationId, clientIds, SchedulingWorld.Future(0), SchedulingWorld.Future(23));
        OccupancySlot slot = Assert.Single(range);
        Assert.Equal(SchedulingWorld.Future(10), slot.Start);

        // Flip: B confirmed (artificial seed), then A cancelled through the command (which re-derives the appointment from
        // BOTH participations => still Scheduled) — only B blocks.
        await using (DatabaseContext db = w.NewDb())
        {
            BookingSegmentParticipation b = await db.BookingSegmentParticipations.SingleAsync(p => p.Id == second);
            b.Status = ParticipationStatus.Confirmed;
            b.CancellationInitiator = null; // P1: metadata always matches the status
            b.CancelledAt = null;
            await db.SaveChangesAsync();
        }
        await SetParticipation(w, first, BookingStatus.Cancelled);
        Assert.Empty(await w.ClientsOverlapping(clientIds, SchedulingWorld.Future(10), 30));
        Assert.Single(await w.ClientsOverlapping(clientIds, SchedulingWorld.Future(14), 30));
    }

    #endregion

    #region Checkout / package target one participation

    [Fact]
    public async Task Checkout_TargetsOneParticipation_AndPaymentOnItDoesNotTouchTheSibling()
    {
        (SchedulingWorld w, AppointmentDto created, Guid first, Guid second) = await TwoParticipations(nameof(Checkout_TargetsOneParticipation_AndPaymentOnItDoesNotTouchTheSibling));
        await using SchedulingWorld _ = w;
        Guid bookingId = created.Bookings.Single().Id;
        CheckoutDto checkout = await w.Checkouts.Create(w.OrganizationId, w.ActorUserId,
            new CheckoutCreateRequest { ClientId = w.Client.Id.Value, CompanyId = w.Company.Id.Value });

        // M1H: a checkout item addresses exactly one participation — the ParticipationId is required (no BookingId path).
        await SchedulingAssert.Validation(
            () => w.Checkouts.AddBookingItem(w.OrganizationId, w.ActorUserId, checkout.Id, new CheckoutAddBookingItemRequest()));

        CheckoutDto withItem = await w.Checkouts.AddBookingItem(w.OrganizationId, w.ActorUserId, checkout.Id,
            new CheckoutAddBookingItemRequest { ParticipationId = second });
        CheckoutItemDto item = Assert.Single(withItem.Items);
        Assert.Equal((second, bookingId, SecondAmount), (item.ParticipationId.Value, item.BookingId.Value, item.RetailAmount));

        await w.Checkouts.RecordPayment(w.OrganizationId, w.ActorUserId, checkout.Id,
            new CheckoutPaymentCreateRequest { Amount = SecondAmount, Method = PaymentMethod.Cash });

        BookingDto booking = Assert.Single(await w.Bookings.GetForAppointment(w.OrganizationId, created.Id));
        BookingParticipationDto a = booking.Participations.Single(p => p.Id == first);
        BookingParticipationDto b = booking.Participations.Single(p => p.Id == second);
        Assert.Equal((0m, SchedulingWorld.DefaultServicePrice, false), (a.PaidAmount, a.OutstandingAmount, a.IsPaid));
        Assert.Equal((SecondAmount, 0m, true), (b.PaidAmount, b.OutstandingAmount, b.IsPaid));
        Assert.Equal((SchedulingWorld.DefaultServicePrice + SecondAmount, SecondAmount, SchedulingWorld.DefaultServicePrice, false),
            (booking.Amount, booking.PaidAmount, booking.OutstandingAmount, booking.IsPaid));
    }

    [Fact]
    public async Task PackageConsumption_TargetsOneParticipation_AndLeavesTheSiblingUncovered()
    {
        (SchedulingWorld w, AppointmentDto created, Guid first, Guid second) = await TwoParticipations(nameof(PackageConsumption_TargetsOneParticipation_AndLeavesTheSiblingUncovered));
        await using SchedulingWorld _ = w;
        ClientPackage package = await w.AddClientPackage(w.Client, w.Service, 5, SchedulingWorld.FutureDay.AddYears(1));

        await using (IUnitOfWork uow = await w.Resolve<IUnitOfWorkFactory>().Begin())
        {
            Appointment appointment = await uow.Context.Appointments
                .Include(a => a.Segments).ThenInclude(s => s.Employees).Include(a => a.Bookings)
                .SingleAsync(a => a.Id == created.Id);
            Booking booking = appointment.Bookings.Single();
            BookingSegmentParticipation target = BookingParticipations.ById(booking, second);
            await w.Resolve<IPackageConsumptionLedgerService>().Consume(uow, w.OrganizationId, w.ActorUserId, target,
                ExecutionContextResolver.ForParticipation(appointment, booking, target), package.Id.Value, BookingStatus.Completed);
            await uow.CommitAsync();
        }

        await using DatabaseContext db = w.NewDb();
        PackageConsumption consumption = Assert.Single(await db.PackageConsumptions.Where(c => c.ClientPackageId == package.Id).ToListAsync());
        Assert.Equal(second, consumption.BookingSegmentParticipationId);
        Assert.Equal(SchedulingWorld.Future(14), consumption.ServiceStartsAt); // the TARGET participation's segment

        BookingDto booking2 = Assert.Single(await w.Bookings.GetForAppointment(w.OrganizationId, created.Id));
        Assert.True(booking2.Participations.Single(p => p.Id == second).PackageCovered);
        Assert.False(booking2.Participations.Single(p => p.Id == first).PackageCovered);
        Assert.Equal(SchedulingWorld.DefaultServicePrice, booking2.OutstandingAmount); // only the uncovered sibling owes
        Assert.Null(booking2.ClientPackageId); // participations disagree => no single Booking-level coverage
    }

    #endregion

    #region Read models and commission source

    [Fact]
    public async Task SingleParticipationReadModel_BookingFieldsEqualTheParticipation()
    {
        await using SchedulingWorld w = await SchedulingWorld.Create(nameof(SingleParticipationReadModel_BookingFieldsEqualTheParticipation));
        AppointmentDto created = await w.CreateAppointment(SchedulingWorld.Past(10));
        await w.PayBookingViaCheckout(created.Bookings.Single().Id, w.Client, 20m);
        await w.SetBookingStatus(created.Id, w.Client, BookingStatus.NoShow, cancellationReason: "sick");

        BookingDto dto = Assert.Single(await w.Bookings.GetForAppointment(w.OrganizationId, created.Id));
        BookingParticipationDto p = Assert.Single(dto.Participations);

        Assert.Equal(BookingStatusSummary.NoShow, dto.Status);
        Assert.Equal(BookingStatus.NoShow, p.Status);
        Assert.Equal((p.Amount, p.SuggestedAmount, p.IsAmountManuallyOverridden), (dto.Amount, dto.SuggestedAmount, dto.IsAmountManuallyOverridden));
        Assert.Equal((p.PaidAmount, p.IsPaid), (dto.PaidAmount, dto.IsPaid));
        // CHANGED in P1 (D5/D7): a no-show under the neutral default policy owes nothing — the 20 paid is surplus, not credit.
        // The participation carries the raw debt (-20); the Booking sums only positive debt (a surplus is never netted).
        Assert.Equal((20m, -20m, true), (p.PaidAmount, p.OutstandingAmount, p.IsPaid));
        Assert.Equal((20m, 0m, true), (dto.PaidAmount, dto.OutstandingAmount, dto.IsPaid));
        Assert.Equal((20m, 20m), (p.SurplusAmount, dto.SurplusAmount));
        Assert.Equal(0m, p.MonetaryDue);
        Assert.Equal("sick", p.NoShowReason);
        Assert.Null(dto.CancellationReason);
        Assert.Single(dto.Payments);
    }

    [Fact]
    public async Task IndividualCompletion_CommissionSourceIsTheParticipation_WithItsOwnVersion()
    {
        await using SchedulingWorld w = await SchedulingWorld.Create(nameof(IndividualCompletion_CommissionSourceIsTheParticipation_WithItsOwnVersion));
        await w.AddCommissionRule(w.Employee, w.Service, CommissionCalculationType.Percentage, 10m);
        AppointmentDto created = await w.CreateAppointment(SchedulingWorld.Future(10));
        await w.CompleteParticipations(created.Id, w.CompleteRequest(SchedulingWorld.Future(10)));

        Booking booking = await w.LoadBooking(created.Id, w.Client);
        BookingSegmentParticipation participation = booking.Participations.Single();
        CommissionEntry entry = Assert.Single(await w.LoadCommissionEntries());
        Assert.Equal(participation.Id, entry.BookingSegmentParticipationId);
        Assert.Equal(booking.Id, entry.BookingId);
        Assert.Equal(participation.StatusVersion, entry.SourceVersion);
        Assert.Equal(participation.Amount, entry.BaseAmount);
    }

    #endregion
}
