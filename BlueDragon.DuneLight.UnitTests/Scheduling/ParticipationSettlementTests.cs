#nullable disable
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using BlueDragon.DuneLight.Core.DTOs.Appointments;
using BlueDragon.DuneLight.Core.DTOs.Checkouts;
using BlueDragon.DuneLight.Core.DTOs.Dashboard;
using BlueDragon.DuneLight.Core.Enums;
using BlueDragon.DuneLight.Core.Interfaces.Appointments;
using BlueDragon.DuneLight.Core.Interfaces.Checkouts;
using BlueDragon.DuneLight.Core.Interfaces.Dashboard;
using BlueDragon.DuneLight.Core.Shared;
using BlueDragon.DuneLight.Core.Shared.Exceptions;
using BlueDragon.DuneLight.Infrastructure.Domain.Contexts;
using BlueDragon.DuneLight.Infrastructure.Domain.Models.Appointments;
using BlueDragon.DuneLight.Infrastructure.Domain.Models.Checkouts;
using BlueDragon.DuneLight.Infrastructure.Domain.Models.Clients;
using BlueDragon.DuneLight.Infrastructure.Utils;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace BlueDragon.DuneLight.UnitTests.Scheduling;

/// <summary>
/// Phase D3B3B — BookingSegmentParticipation is the monetary settlement boundary: a service CheckoutItem references the
/// participation (tenant-safe composite FK, RESTRICT); PaymentAllocation -> CheckoutItem -> Payment is unchanged; settled /
/// outstanding are DERIVED (ParticipationSettlement) from active allocations across all the participation's items, never
/// exceeding the monetary due. A package is not money: an active PackageConsumption makes the monetary due 0 (current
/// all-or-nothing rule) and the package/money exclusivity is one policy. Settlement and lifecycle are independent.
/// Settlement history makes a participation non-untouched (F-09).
/// </summary>
public class ParticipationSettlementTests
{
    private static readonly DateTimeOffset LongValid = new(2035, 1, 1, 0, 0, 0, TimeSpan.Zero);
    private static DateTimeOffset Z(int h) => SchedulingWorld.Future(h);

    private static async Task<CheckoutDto> NewCheckout(SchedulingWorld w, Client client = null) =>
        await w.Checkouts.Create(w.OrganizationId, w.ActorUserId,
            new CheckoutCreateRequest { ClientId = (client ?? w.Client).Id.Value, CompanyId = w.Company.Id.Value });

    private static Task<CheckoutDto> AddService(SchedulingWorld w, Guid checkoutId, Guid bookingId) =>
        w.Checkouts.AddBookingItem(w.OrganizationId, w.ActorUserId, checkoutId, new CheckoutAddBookingItemRequest { BookingId = bookingId });

    private static Task<CheckoutDto> Pay(SchedulingWorld w, Guid checkoutId, decimal amount, List<CheckoutPaymentAllocationRequest> allocations = null) =>
        w.Checkouts.RecordPayment(w.OrganizationId, w.ActorUserId, checkoutId,
            new CheckoutPaymentCreateRequest { Amount = amount, Method = PaymentMethod.Cash, Allocations = allocations });

    private static async Task<BookingDto> BookingOf(SchedulingWorld w, Guid appointmentId) =>
        (await w.Appointments.GetById(w.OrganizationId, appointmentId)).Bookings.Single();

    private static async Task<ParticipationSettlement> SettlementOf(SchedulingWorld w, Guid bookingId)
    {
        await using DatabaseContext db = w.NewDb();
        BookingSegmentParticipation p = await db.BookingSegmentParticipations.AsNoTracking()
            .Include(x => x.CheckoutItems).ThenInclude(i => i.Allocations).ThenInclude(a => a.Payment)
            .SingleAsync(x => x.BookingId == bookingId);
        return ParticipationSettlement.Of(p);
    }

    #region Relationship / schema

    [Fact]
    public async Task AServiceItem_ReferencesTheParticipation_AndBookingHasNoSettlementLink()
    {
        await using SchedulingWorld w = await SchedulingWorld.Create(nameof(AServiceItem_ReferencesTheParticipation_AndBookingHasNoSettlementLink));
        AppointmentDto created = await w.CreateAppointment(Z(10));
        Guid bookingId = created.Bookings.Single().Id;
        CheckoutDto checkout = await AddService(w, (await NewCheckout(w)).Id, bookingId);

        await using DatabaseContext db = w.NewDb();
        CheckoutItem item = await db.CheckoutItems.AsNoTracking().SingleAsync(i => i.CheckoutId == checkout.Id);
        Guid participationId = await db.BookingSegmentParticipations.Where(p => p.BookingId == bookingId).Select(p => p.Id.Value).SingleAsync();
        Assert.Equal(participationId, item.BookingSegmentParticipationId);
        Assert.Equal(bookingId, Assert.Single(checkout.Items).BookingId); // API contract unchanged (derived through the participation)

        Assert.Null(typeof(Booking).GetProperty("CheckoutItems"));
        Assert.Null(typeof(CheckoutItem).GetProperty("BookingId"));
        List<string> columns = await db.Database.SqlQueryRaw<string>(@"
            SELECT column_name AS ""Value"" FROM information_schema.columns
             WHERE table_schema = 'dunelight' AND table_name = 'checkout_items' AND column_name IN ('booking_id', 'locks_booking')").ToListAsync();
        Assert.Empty(columns);

        string fk = Assert.Single(await db.Database.SqlQueryRaw<string>(@"
            SELECT pg_get_constraintdef(oid) AS ""Value"" FROM pg_constraint
             WHERE conrelid = 'dunelight.checkout_items'::regclass AND conname = 'fk_checkout_items_participation_organization'").ToListAsync());
        Assert.Equal("FOREIGN KEY (booking_segment_participation_id, organization_id) REFERENCES dunelight.booking_segment_participations(id, organization_id) ON DELETE RESTRICT", fk);
        List<string> indexes = await db.Database.SqlQueryRaw<string>(@"
            SELECT indexname AS ""Value"" FROM pg_indexes WHERE schemaname = 'dunelight' AND tablename = 'checkout_items'").ToListAsync();
        Assert.Contains("ix_checkout_items_participation_id", indexes);
        Assert.Contains("ux_checkout_items_locks_participation", indexes);
        Assert.Empty(await db.Database.SqlQueryRaw<string>(
            "SELECT trigger_name AS \"Value\" FROM information_schema.triggers WHERE trigger_schema = 'dunelight'").ToListAsync());
    }

    [Fact]
    public async Task ACrossOrganizationSettlementLink_IsRejected_ByServiceAndDatabase()
    {
        await using SchedulingWorld w = await SchedulingWorld.Create(nameof(ACrossOrganizationSettlementLink_IsRejected_ByServiceAndDatabase));
        await using SchedulingWorld other = await SchedulingWorld.Create($"{nameof(ACrossOrganizationSettlementLink_IsRejected_ByServiceAndDatabase)}-other");
        AppointmentDto theirs = await other.CreateAppointment(Z(10));
        CheckoutDto mine = await NewCheckout(w);

        await SchedulingAssert.NotFound(() => AddService(w, mine.Id, theirs.Bookings.Single().Id));

        await using DatabaseContext db = w.NewDb();
        Guid theirParticipation = await db.BookingSegmentParticipations.Where(p => p.BookingId == theirs.Bookings.Single().Id).Select(p => p.Id.Value).SingleAsync();
        db.CheckoutItems.Add(new CheckoutItem
        {
            Id = Guid.NewGuid(), OrganizationId = w.OrganizationId, CheckoutId = mine.Id, Type = CheckoutItemType.Booking,
            Description = "x", UnitPrice = 50m, Quantity = 1, Amount = 50m, BookingSegmentParticipationId = theirParticipation,
            CreatedAt = DateTimeOffset.UtcNow
        });
        DbUpdateException ex = await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync());
        Assert.Contains("fk_checkout_items_participation_organization", ex.InnerException?.Message);
    }

    #endregion

    #region Financials: derived, independent of lifecycle, never over-settled

    [Fact]
    public async Task Settlement_IsDerivedFromActiveAllocations_PartialSplitFullAndVoided()
    {
        await using SchedulingWorld w = await SchedulingWorld.Create(nameof(Settlement_IsDerivedFromActiveAllocations_PartialSplitFullAndVoided));
        AppointmentDto created = await w.CreateAppointment(Z(10)); // Confirmed, price 50
        Guid bookingId = created.Bookings.Single().Id;
        CheckoutDto checkout = await AddService(w, (await NewCheckout(w)).Id, bookingId);

        ParticipationSettlement none = await SettlementOf(w, bookingId);
        Assert.Equal((50m, 50m, 0m, 50m, false), (none.FinalPrice, none.MonetaryDue, none.SettledAmount, none.OutstandingAmount, none.FullySettled));

        await Pay(w, checkout.Id, 20m);                     // partial
        CheckoutDto split = await Pay(w, checkout.Id, 10m); // split: second payment on the same participation
        Assert.Equal((30m, 20m), ((await SettlementOf(w, bookingId)).SettledAmount, (await SettlementOf(w, bookingId)).OutstandingAmount));

        await Pay(w, checkout.Id, 20m);                     // fully paid while still Confirmed (prepayment)
        ParticipationSettlement full = await SettlementOf(w, bookingId);
        Assert.True(full.FullySettled);
        BookingDto booking = await BookingOf(w, created.Id);
        Assert.Equal((BookingStatusSummary.Confirmed, 50m, 0m, true), (booking.Status, booking.PaidAmount, booking.OutstandingAmount, booking.IsPaid));

        // A voided payment no longer counts as settled.
        await w.Checkouts.VoidPayment(w.OrganizationId, w.ActorUserId, checkout.Id, split.Payments.First(p => p.Amount == 10m).Id,
            new CheckoutPaymentVoidRequest { Reason = "mistake" });
        Assert.Equal((40m, 10m), ((await SettlementOf(w, bookingId)).SettledAmount, (await SettlementOf(w, bookingId)).OutstandingAmount));
        Assert.Equal(50m, (await SettlementOf(w, bookingId)).FinalPrice); // price never changes because of settlement
    }

    [Fact]
    public async Task Overpayment_IsRejected_AlsoAcrossDifferentCheckoutsOfTheSameParticipation()
    {
        await using SchedulingWorld w = await SchedulingWorld.Create(nameof(Overpayment_IsRejected_AlsoAcrossDifferentCheckoutsOfTheSameParticipation));
        AppointmentDto created = await w.CreateAppointment(Z(10));
        Guid bookingId = created.Bookings.Single().Id;
        CheckoutDto first = await AddService(w, (await NewCheckout(w)).Id, bookingId);

        await SchedulingAssert.BusinessRule(ErrorCodes.PaymentExceedsOutstandingAmount, () => Pay(w, first.Id, 60m));
        await Pay(w, first.Id, 50m);
        await w.Checkouts.Complete(w.OrganizationId, w.ActorUserId, first.Id);

        // A second checkout for the already-settled participation shows no debt and accepts no money.
        CheckoutDto second = await AddService(w, (await NewCheckout(w)).Id, bookingId);
        Assert.Equal(0m, second.Totals.OutstandingAmount);
        await SchedulingAssert.BusinessRule(ErrorCodes.PaymentExceedsOutstandingAmount, () => Pay(w, second.Id, 10m));
        Assert.Equal(50m, (await SettlementOf(w, bookingId)).SettledAmount);
    }

    [Fact]
    public async Task Completed_WithOutstandingDebt_IsAllowed()
    {
        await using SchedulingWorld w = await SchedulingWorld.Create(nameof(Completed_WithOutstandingDebt_IsAllowed));
        AppointmentDto created = await w.CreateAppointment(Z(10));

        AppointmentDto completed = await w.CompleteExisting(created.Id, w.CompleteRequest(Z(10), isPaid: false));

        BookingDto booking = Assert.Single(completed.Bookings);
        Assert.Equal((BookingStatusSummary.Completed, 0m, 50m, false), (booking.Status, booking.PaidAmount, booking.OutstandingAmount, booking.IsPaid));
    }

    [Fact]
    public async Task CheckInPayment_SettlesOnlyTheRemainder_AfterAPrepayment()
    {
        await using SchedulingWorld w = await SchedulingWorld.Create(nameof(CheckInPayment_SettlesOnlyTheRemainder_AfterAPrepayment));
        AppointmentDto created = await w.CreateAppointment(Z(10));
        Guid bookingId = created.Bookings.Single().Id;
        await w.PayBookingViaCheckout(bookingId, w.Client, 50m, complete: true); // fully prepaid

        AppointmentDto completed = await w.CompleteExisting(created.Id, w.CompleteRequest(Z(10), paymentMethod: PaymentMethod.Cash));

        Assert.Single(await w.LoadPayments(bookingId)); // nothing left to settle => no check-in payment
        Assert.Equal((50m, 0m), (Assert.Single(completed.Bookings).PaidAmount, Assert.Single(completed.Bookings).OutstandingAmount));
    }

    #endregion

    #region Checkout behaviour preserved

    [Fact]
    public async Task Checkout_WithServiceAndPackageSale_FifoAndExplicitAllocations_CompletesOnlyWhenSettled()
    {
        await using SchedulingWorld w = await SchedulingWorld.Create(nameof(Checkout_WithServiceAndPackageSale_FifoAndExplicitAllocations_CompletesOnlyWhenSettled));
        ClientPackage owned = await w.AddClientPackage(w.Client, w.Service, 5, LongValid); // only to get a catalog package (price 100)
        AppointmentDto created = await w.CreateAppointment(Z(10));
        CheckoutDto checkout = await AddService(w, (await NewCheckout(w)).Id, created.Bookings.Single().Id);
        checkout = await w.Checkouts.AddPackageItem(w.OrganizationId, w.ActorUserId, checkout.Id, new CheckoutAddPackageItemRequest { PackageId = owned.PackageId });
        CheckoutItemDto service = checkout.Items.Single(i => i.Type == CheckoutItemType.Booking);
        CheckoutItemDto packageSale = checkout.Items.Single(i => i.Type == CheckoutItemType.Package);

        CheckoutDto afterFifo = await Pay(w, checkout.Id, 60m); // FIFO: service 50, then 10 to the package sale
        Assert.Equal((50m, 10m), (afterFifo.Items.Single(i => i.Id == service.Id).PaidAmount, afterFifo.Items.Single(i => i.Id == packageSale.Id).PaidAmount));
        await SchedulingAssert.BusinessRule(ErrorCodes.CheckoutOutstandingBalance,
            () => w.Checkouts.Complete(w.OrganizationId, w.ActorUserId, checkout.Id));
        await SchedulingAssert.BusinessRule(ErrorCodes.AllocationExceedsItemOutstanding, () => Pay(w, checkout.Id, 10m,
            new List<CheckoutPaymentAllocationRequest> { new() { CheckoutItemId = service.Id, Amount = 10m } }));

        await Pay(w, checkout.Id, 90m, new List<CheckoutPaymentAllocationRequest> { new() { CheckoutItemId = packageSale.Id, Amount = 90m } });
        CheckoutDto done = await w.Checkouts.Complete(w.OrganizationId, w.ActorUserId, checkout.Id);

        Assert.Equal(CheckoutStatus.Completed, done.Status);
        Assert.NotNull(done.Items.Single(i => i.Id == packageSale.Id).ClientPackageId); // package sale still issues the entitlement
        Assert.True((await BookingOf(w, created.Id)).IsPaid);
    }

    #endregion

    #region Package is not money

    [Fact]
    public async Task ActivePackageConsumption_MakesTheMonetaryDueZero_WithoutPaymentOrAllocation_AndReversalReopensIt()
    {
        await using SchedulingWorld w = await SchedulingWorld.Create(nameof(ActivePackageConsumption_MakesTheMonetaryDueZero_WithoutPaymentOrAllocation_AndReversalReopensIt));
        ClientPackage package = await w.AddClientPackage(w.Client, w.Service, 5, LongValid);
        AppointmentDto created = await w.CreateAppointment(Z(10));
        Guid bookingId = created.Bookings.Single().Id;
        await w.CompleteExisting(created.Id, w.CompleteRequest(Z(10), clientPackageId: package.Id));

        ParticipationSettlement covered = await SettlementOf(w, bookingId);
        Assert.Equal((50m, true, 0m, 0m, 0m), (covered.FinalPrice, covered.EntitlementCovered, covered.MonetaryDue, covered.SettledAmount, covered.OutstandingAmount));
        Assert.Empty(await w.LoadPayments(bookingId));
        await using (DatabaseContext db = w.NewDb())
            Assert.False(await db.CheckoutItems.AnyAsync(i => i.Participation.BookingId == bookingId)); // no fake allocation

        // Package then money stays rejected: nothing is monetarily due (checkout-level check, as before D3B3B), and the
        // check-in path refuses money on a covered participation (SettlementExclusivityPolicy).
        CheckoutDto checkout = await AddService(w, (await NewCheckout(w)).Id, bookingId);
        Assert.Equal(0m, checkout.Totals.OutstandingAmount);
        await SchedulingAssert.BusinessRule(ErrorCodes.PaymentExceedsOutstandingAmount, () => Pay(w, checkout.Id, 10m,
            new List<CheckoutPaymentAllocationRequest> { new() { CheckoutItemId = checkout.Items.Single().Id, Amount = 10m } }));
        await w.Checkouts.Cancel(w.OrganizationId, w.ActorUserId, checkout.Id);

        // Reversing the consumption (correction) makes the monetary due available again; the price never changed.
        await w.SetBookingStatus(created.Id, w.Client, BookingStatus.Confirmed);
        ParticipationSettlement reopened = await SettlementOf(w, bookingId);
        Assert.Equal((50m, false, 50m, 50m), (reopened.FinalPrice, reopened.EntitlementCovered, reopened.MonetaryDue, reopened.OutstandingAmount));
    }

    [Fact]
    public async Task MoneyThenPackage_StaysRejected_ThroughTheSinglePolicy()
    {
        await using SchedulingWorld w = await SchedulingWorld.Create(nameof(MoneyThenPackage_StaysRejected_ThroughTheSinglePolicy));
        ClientPackage package = await w.AddClientPackage(w.Client, w.Service, 5, LongValid);
        AppointmentDto created = await w.CreateAppointment(Z(10));
        await w.PayBookingViaCheckout(created.Bookings.Single().Id, w.Client, 20m);

        await SchedulingAssert.BusinessRule(ErrorCodes.BookingAlreadyHasMonetaryPayment,
            () => w.CompleteExisting(created.Id, w.CompleteRequest(Z(10), clientPackageId: package.Id)));

        Assert.Equal(5, (await w.LoadClientPackage(package.Id.Value)).ServiceEntries.Single().RemainingEntries);
    }

    #endregion

    #region History (F-09)

    [Fact]
    public async Task SettlementHistory_MakesAParticipationNonUntouched_WithADomainError()
    {
        await using SchedulingWorld w = await SchedulingWorld.Create(nameof(SettlementHistory_MakesAParticipationNonUntouched_WithADomainError));
        Client second = await w.AddClient("Second", "Client");
        Client third = await w.AddClient("Third", "Client");
        AppointmentDto created = await w.CreateAppointment(Z(10), extraClients: new[] { second, third });
        BookingDto onCheckout = created.Bookings.Single(b => b.ClientId == second.Id);
        await AddService(w, (await NewCheckout(w, second)).Id, onCheckout.Id);          // checkout item, no payment
        await w.PayBookingViaCheckout(created.Bookings.Single(b => b.ClientId == third.Id).Id, third, 20m); // payment

        foreach (Client omitted in new[] { second, third })
        {
            AppointmentDto current = await w.Appointments.GetById(w.OrganizationId, created.Id);
            await SchedulingAssert.BusinessRule(ErrorCodes.ReferencedCannotDelete, () => w.Appointments.Update(
                w.OrganizationId, w.ActorUserId, true, created.Id,
                w.UpdateRequest(current, r => r.ClientIds = current.Bookings.Select(b => b.ClientId).Where(c => c != omitted.Id).ToList())));
        }
        await SchedulingAssert.BusinessRule(ErrorCodes.ReferencedCannotDelete,
            () => w.Appointments.Delete(w.OrganizationId, w.ActorUserId, created.Id));
        Assert.Equal(3, (await w.LoadAppointment(created.Id)).Bookings.Count);

        // A participation with no lifecycle/package/settlement history is still removable.
        AppointmentDto now = await w.Appointments.GetById(w.OrganizationId, created.Id);
        await w.Appointments.Update(w.OrganizationId, w.ActorUserId, true, created.Id,
            w.UpdateRequest(now, r => r.ClientIds = new List<Guid> { second.Id.Value, third.Id.Value }));
        Assert.Equal(2, (await w.LoadAppointment(created.Id)).Bookings.Count);
    }

    #endregion

    #region Concurrency

    [Fact]
    public async Task ConcurrentCheckoutPaymentAndCheckInPayment_CannotOverSettleOneParticipation()
    {
        await using SchedulingWorld w = await SchedulingWorld.Create(nameof(ConcurrentCheckoutPaymentAndCheckInPayment_CannotOverSettleOneParticipation));
        AppointmentDto created = await w.CreateAppointment(Z(10));
        Guid bookingId = created.Bookings.Single().Id;
        CheckoutDto checkout = await AddService(w, (await NewCheckout(w)).Id, bookingId);

        using IServiceScope scopeA = SchedulingTestHost.CreateScope();
        using IServiceScope scopeB = SchedulingTestHost.CreateScope();
        Task<bool> viaCheckout = Task.Run(async () =>
        {
            try
            {
                await scopeA.ServiceProvider.GetRequiredService<ICheckoutService>().RecordPayment(w.OrganizationId, w.ActorUserId, checkout.Id,
                    new CheckoutPaymentCreateRequest { Amount = 50m, Method = PaymentMethod.Card });
                return true;
            }
            catch (BusinessRuleException ex) when (ex.Code == ErrorCodes.PaymentExceedsOutstandingAmount)
            {
                return false;
            }
        });
        Task viaCheckIn = Task.Run(() => scopeB.ServiceProvider.GetRequiredService<IAppointmentService>().CompleteExisting(
            w.OrganizationId, w.ActorUserId, true, created.Id, w.CompleteRequest(Z(10), paymentMethod: PaymentMethod.Cash)));

        await Task.WhenAll(viaCheckout, viaCheckIn);

        // Whichever ran second saw the first under the participation lock: exactly 50 is settled, never 100.
        Assert.Equal(50m, (await SettlementOf(w, bookingId)).SettledAmount);
        Assert.Single(await w.LoadPayments(bookingId), p => p.Status == PaymentStatus.Completed);
    }

    #endregion

    #region Dashboard

    [Fact]
    public async Task Dashboard_RevenueIsActualPayments_OutstandingIsParticipationSettlement_PackageIsNotRevenue()
    {
        await using SchedulingWorld w = await SchedulingWorld.Create(nameof(Dashboard_RevenueIsActualPayments_OutstandingIsParticipationSettlement_PackageIsNotRevenue));
        Client second = await w.AddClient("Second", "Client");
        ClientPackage package = await w.AddClientPackage(second, w.Service, 5, LongValid);
        AppointmentDto paid = await w.CreateAppointment(Z(10));
        AppointmentDto covered = await w.CreateAppointment(Z(12), client: second);
        await w.PayBookingViaCheckout(paid.Bookings.Single().Id, w.Client, 20m, complete: false);
        await w.CompleteExisting(covered.Id, w.CompleteRequest(Z(12), client: second, clientPackageId: package.Id));
        IOperationalDashboardService dashboard = w.Resolve<IOperationalDashboardService>();

        DashboardFinancialDto onServiceDay = (await dashboard.GetDashboard(w.OrganizationId, w.Company.Id.Value, Z(10))).Financial;
        Assert.Equal(30m, onServiceDay.OutstandingAmount); // 50 - 20 on the paid one; the package-covered one owes nothing
        Assert.Equal(1, onServiceDay.UnpaidBookingCount);

        DashboardFinancialDto today = (await dashboard.GetDashboard(w.OrganizationId, w.Company.Id.Value, DateTimeOffset.UtcNow)).Financial;
        Assert.Equal(20m, today.TodayRevenue); // only real Payments — the package consumption is not cash
    }

    #endregion
}
