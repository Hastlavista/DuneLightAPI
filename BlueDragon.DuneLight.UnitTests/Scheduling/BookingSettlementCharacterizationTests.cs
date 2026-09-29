#nullable disable
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using BlueDragon.DuneLight.Core.DTOs.Appointments;
using BlueDragon.DuneLight.Core.DTOs.Checkouts;
using BlueDragon.DuneLight.Core.Enums;
using BlueDragon.DuneLight.Core.Interfaces.Appointments;
using BlueDragon.DuneLight.Core.Shared;
using BlueDragon.DuneLight.Infrastructure.Domain.Models.Appointments;
using BlueDragon.DuneLight.Infrastructure.Domain.Models.Checkouts;
using BlueDragon.DuneLight.Infrastructure.Domain.Models.Clients;
using Microsoft.EntityFrameworkCore;

namespace BlueDragon.DuneLight.UnitTests.Scheduling;

/// <summary>
/// CHARACTERIZATION (matrix P and Q): where settlement lives today.
///
/// The money chain is <c>Booking (obligation: Amount) ← CheckoutItem(BookingId) ← PaymentAllocation → Payment ← Checkout</c>.
/// A Booking stores its OBLIGATION (Amount / SuggestedAmount / override flag) and its package-coverage flags, and nothing
/// about money received: PaidAmount, OutstandingAmount and IsPaid are always DERIVED from the non-voided allocations of the
/// Booking's checkout items (BookingFinancialsCalculator), never persisted. Settlement is therefore attached to the
/// BOOKING (through checkout items), not to the Appointment.
///
/// Two payment origins exist: check-in generated (created inside completion/check-in, one auto-Checkout per payment,
/// IsCheckInGenerated = true, reversible by the correction flows) and manual POS payments (ICheckoutService, IsCheckInGenerated
/// = false, preserved by the correction flows).
/// </summary>
public class BookingSettlementCharacterizationTests
{
    private static async Task<(Guid BookingId, AppointmentDto Appointment)> UnpaidCompletedBooking(SchedulingWorld w, DateTimeOffset when, Client client = null)
    {
        AppointmentDto dto = await w.CompleteNew(w.CompleteRequest(when, client: client));
        return (dto.Bookings.Single().Id, dto);
    }

    private static Task<CheckoutDto> NewCheckout(SchedulingWorld w, Client client = null) =>
        w.Checkouts.Create(w.OrganizationId, w.ActorUserId,
            new CheckoutCreateRequest { ClientId = (client ?? w.Client).Id.Value, CompanyId = w.Company.Id.Value });

    private static Task<CheckoutDto> AddItem(SchedulingWorld w, Guid checkoutId, Guid bookingId) =>
        w.Checkouts.AddBookingItem(w.OrganizationId, w.ActorUserId, checkoutId, new CheckoutAddBookingItemRequest { BookingId = bookingId });

    private static Task<CheckoutDto> Pay(SchedulingWorld w, Guid checkoutId, decimal amount, List<CheckoutPaymentAllocationRequest> allocations = null) =>
        w.Checkouts.RecordPayment(w.OrganizationId, w.ActorUserId, checkoutId,
            new CheckoutPaymentCreateRequest { Amount = amount, Method = PaymentMethod.Cash, Allocations = allocations });

    private static async Task<BookingDto> BookingDtoOf(SchedulingWorld w, Guid appointmentId) =>
        (await w.Appointments.GetById(w.OrganizationId, appointmentId)).Bookings.Single();

    #region Nothing about money is stored on Booking or Appointment

    [Fact]
    public async Task Booking_HasNoPersistedPaidFlagPaymentMethodOrPaidAmount()
    {
        await using SchedulingWorld w = await SchedulingWorld.Create(nameof(Booking_HasNoPersistedPaidFlagPaymentMethodOrPaidAmount));
        await using var db = w.NewDb();

        string[] bookingColumns = db.Model.FindEntityType(typeof(Booking))!.GetProperties().Select(p => p.GetColumnName()).ToArray();
        string[] appointmentColumns = db.Model.FindEntityType(typeof(Appointment))!.GetProperties().Select(p => p.GetColumnName()).ToArray();

        // Money received is not a column anywhere on the scheduling aggregate.
        Assert.DoesNotContain("is_paid", bookingColumns);
        Assert.DoesNotContain("payment_method", bookingColumns);
        Assert.DoesNotContain("paid_amount", bookingColumns);
        Assert.DoesNotContain("outstanding_amount", bookingColumns);
        // The obligation and the coverage state ARE on the Booking ...
        Assert.Contains("amount", bookingColumns);
        Assert.Contains("suggested_amount", bookingColumns);
        Assert.Contains("client_package_id", bookingColumns);
        Assert.Contains("package_coverage_applied", bookingColumns);
        // ... and the Appointment carries no price at all.
        Assert.DoesNotContain(appointmentColumns, c => c.Contains("amount") || c.Contains("price") || c.Contains("paid"));
    }

    [Fact]
    public async Task RecordingAPayment_DoesNotModifyTheBookingRow_OnlyTheDerivedFinancialsChange()
    {
        await using SchedulingWorld w = await SchedulingWorld.Create(nameof(RecordingAPayment_DoesNotModifyTheBookingRow_OnlyTheDerivedFinancialsChange));
        (Guid bookingId, AppointmentDto dto) = await UnpaidCompletedBooking(w, SchedulingWorld.Past(10));
        string before = JsonSerializer.Serialize(await w.LoadBooking(dto.Id, w.Client));

        await w.PayBookingViaCheckout(bookingId, w.Client, 50m);

        Assert.Equal(before, JsonSerializer.Serialize(await w.LoadBooking(dto.Id, w.Client)));
        BookingDto derived = await BookingDtoOf(w, dto.Id);
        Assert.Equal(50m, derived.PaidAmount);
        Assert.True(derived.IsPaid);
    }

    #endregion

    #region Partial payments and derivation

    [Fact]
    public async Task PartialPayments_AccumulateOnTheSameCheckout_UntilTheBookingIsSettled()
    {
        await using SchedulingWorld w = await SchedulingWorld.Create(nameof(PartialPayments_AccumulateOnTheSameCheckout_UntilTheBookingIsSettled));
        (Guid bookingId, AppointmentDto dto) = await UnpaidCompletedBooking(w, SchedulingWorld.Past(10));
        CheckoutDto checkout = await NewCheckout(w);
        await AddItem(w, checkout.Id, bookingId);

        await Pay(w, checkout.Id, 20m);
        BookingDto afterFirst = await BookingDtoOf(w, dto.Id);
        Assert.Equal(20m, afterFirst.PaidAmount);
        Assert.Equal(30m, afterFirst.OutstandingAmount);
        Assert.False(afterFirst.IsPaid);

        await Pay(w, checkout.Id, 30m);
        BookingDto afterSecond = await BookingDtoOf(w, dto.Id);
        Assert.Equal(50m, afterSecond.PaidAmount);
        Assert.Equal(0m, afterSecond.OutstandingAmount);
        Assert.True(afterSecond.IsPaid);
        Assert.Equal(2, afterSecond.Payments.Count);
        List<Payment> payments = await w.LoadPayments(bookingId);
        Assert.All(payments, p =>
        {
            Assert.Equal(PaymentStatus.Completed, p.Status);
            Assert.False(p.IsCheckInGenerated); // manual POS payments
        });
        CheckoutItem item = Assert.Single(await w.LoadCheckoutItems(bookingId));
        Assert.Equal(new[] { 20m, 30m }, item.Allocations.Select(a => a.Amount).OrderBy(x => x).ToArray());
    }

    [Fact]
    public async Task ACheckoutCannotBeCompleted_WhileItIsNotFullyPaid()
    {
        await using SchedulingWorld w = await SchedulingWorld.Create(nameof(ACheckoutCannotBeCompleted_WhileItIsNotFullyPaid));
        (Guid bookingId, _) = await UnpaidCompletedBooking(w, SchedulingWorld.Past(10));
        CheckoutDto checkout = await NewCheckout(w);
        await AddItem(w, checkout.Id, bookingId);
        await Pay(w, checkout.Id, 20m);

        await SchedulingAssert.BusinessRule(ErrorCodes.CheckoutOutstandingBalance,
            () => w.Checkouts.Complete(w.OrganizationId, w.ActorUserId, checkout.Id));

        await Pay(w, checkout.Id, 30m);
        CheckoutDto completed = await w.Checkouts.Complete(w.OrganizationId, w.ActorUserId, checkout.Id);
        Assert.Equal(CheckoutStatus.Completed, completed.Status);
        // Completing releases the "booking is locked in an open checkout" marker.
        Assert.False(Assert.Single(await w.LoadCheckoutItems(bookingId)).LocksBooking);
    }

    [Fact]
    public async Task APaymentLargerThanTheOutstandingAmount_IsRejected()
    {
        await using SchedulingWorld w = await SchedulingWorld.Create(nameof(APaymentLargerThanTheOutstandingAmount_IsRejected));
        (Guid bookingId, _) = await UnpaidCompletedBooking(w, SchedulingWorld.Past(10));
        CheckoutDto checkout = await NewCheckout(w);
        await AddItem(w, checkout.Id, bookingId);

        await SchedulingAssert.BusinessRule(ErrorCodes.PaymentExceedsOutstandingAmount, () => Pay(w, checkout.Id, 60m));

        Assert.Empty(await w.LoadPayments(bookingId));
    }

    [Fact]
    public async Task AVoidedManualPayment_IsExcludedFromPaidAmount_ButKeptInTheHistory()
    {
        await using SchedulingWorld w = await SchedulingWorld.Create(nameof(AVoidedManualPayment_IsExcludedFromPaidAmount_ButKeptInTheHistory));
        (Guid bookingId, AppointmentDto dto) = await UnpaidCompletedBooking(w, SchedulingWorld.Past(10));
        CheckoutDto checkout = await NewCheckout(w);
        await AddItem(w, checkout.Id, bookingId);
        await Pay(w, checkout.Id, 50m);
        Payment payment = Assert.Single(await w.LoadPayments(bookingId));

        await w.Checkouts.VoidPayment(w.OrganizationId, w.ActorUserId, checkout.Id, payment.Id.Value, new CheckoutPaymentVoidRequest { Reason = "wrong card" });

        BookingDto derived = await BookingDtoOf(w, dto.Id);
        Assert.Equal(0m, derived.PaidAmount);
        Assert.Equal(50m, derived.OutstandingAmount);
        Assert.False(derived.IsPaid);
        PaymentDto history = Assert.Single(derived.Payments); // the voided payment is still listed
        Assert.Equal(PaymentStatus.Voided, history.Status);
        Assert.Equal("wrong card", history.VoidReason);
        Assert.Equal(PaymentStatus.Voided, Assert.Single(await w.LoadPayments(bookingId)).Status);
    }

    [Fact]
    public async Task VoidingAManualPayment_RequiresAReason_AndCannotBeRepeated()
    {
        await using SchedulingWorld w = await SchedulingWorld.Create(nameof(VoidingAManualPayment_RequiresAReason_AndCannotBeRepeated));
        (Guid bookingId, _) = await UnpaidCompletedBooking(w, SchedulingWorld.Past(10));
        CheckoutDto checkout = await NewCheckout(w);
        await AddItem(w, checkout.Id, bookingId);
        await Pay(w, checkout.Id, 50m);
        Guid paymentId = Assert.Single(await w.LoadPayments(bookingId)).Id.Value;

        await SchedulingAssert.Validation(() => w.Checkouts.VoidPayment(w.OrganizationId, w.ActorUserId, checkout.Id, paymentId, new CheckoutPaymentVoidRequest { Reason = " " }));
        await w.Checkouts.VoidPayment(w.OrganizationId, w.ActorUserId, checkout.Id, paymentId, new CheckoutPaymentVoidRequest { Reason = "mistake" });
        await SchedulingAssert.BusinessRule(ErrorCodes.PaymentAlreadyVoided,
            () => w.Checkouts.VoidPayment(w.OrganizationId, w.ActorUserId, checkout.Id, paymentId, new CheckoutPaymentVoidRequest { Reason = "again" }));
    }

    [Fact]
    public async Task ThePaymentListForABooking_IsExposedThroughThePaymentService_IncludingVoidedOnes()
    {
        await using SchedulingWorld w = await SchedulingWorld.Create(nameof(ThePaymentListForABooking_IsExposedThroughThePaymentService_IncludingVoidedOnes));
        AppointmentDto completed = await w.CompleteNew(w.CompleteRequest(SchedulingWorld.Past(10), paymentMethod: PaymentMethod.Cash));
        await w.SetBookingStatus(completed.Id, w.Client, BookingStatus.Confirmed); // voids the check-in payment

        List<PaymentDto> payments = await w.Resolve<IPaymentService>().GetForBooking(w.OrganizationId, completed.Id, w.Client.Id.Value);

        PaymentDto payment = Assert.Single(payments);
        Assert.Equal(PaymentStatus.Voided, payment.Status);
        Assert.True(payment.IsCheckInGenerated);
    }

    #endregion

    #region Multiple allocations

    [Fact]
    public async Task OnePayment_AcrossTwoBookings_IsAllocatedFifoInTheOrderTheItemsWereAdded()
    {
        await using SchedulingWorld w = await SchedulingWorld.Create(nameof(OnePayment_AcrossTwoBookings_IsAllocatedFifoInTheOrderTheItemsWereAdded));
        (Guid first, AppointmentDto firstDto) = await UnpaidCompletedBooking(w, SchedulingWorld.Past(10));
        (Guid second, AppointmentDto secondDto) = await UnpaidCompletedBooking(w, SchedulingWorld.Past(12));
        CheckoutDto checkout = await NewCheckout(w);
        await AddItem(w, checkout.Id, first);
        await AddItem(w, checkout.Id, second);

        CheckoutDto paid = await Pay(w, checkout.Id, 70m);

        Payment payment = Assert.Single(paid.Payments.Select(p => new Payment { Id = p.Id, Amount = p.Amount }));
        Assert.Equal(70m, payment.Amount);
        Assert.Equal(50m, (await BookingDtoOf(w, firstDto.Id)).PaidAmount);
        Assert.Equal(20m, (await BookingDtoOf(w, secondDto.Id)).PaidAmount);
        // ONE payment, TWO allocations (one per checkout item).
        CheckoutItem firstItem = Assert.Single(await w.LoadCheckoutItems(first));
        CheckoutItem secondItem = Assert.Single(await w.LoadCheckoutItems(second));
        Assert.Equal(50m, Assert.Single(firstItem.Allocations).Amount);
        Assert.Equal(20m, Assert.Single(secondItem.Allocations).Amount);
        Assert.Equal(firstItem.Allocations.Single().PaymentId, secondItem.Allocations.Single().PaymentId);
        Assert.Equal(30m, paid.Totals.OutstandingAmount);
    }

    [Fact]
    public async Task ExplicitAllocations_MustSumToThePaymentAmount_AndCannotExceedTheItemOutstanding()
    {
        await using SchedulingWorld w = await SchedulingWorld.Create(nameof(ExplicitAllocations_MustSumToThePaymentAmount_AndCannotExceedTheItemOutstanding));
        (Guid first, AppointmentDto firstDto) = await UnpaidCompletedBooking(w, SchedulingWorld.Past(10));
        (Guid second, AppointmentDto secondDto) = await UnpaidCompletedBooking(w, SchedulingWorld.Past(12));
        CheckoutDto checkout = await NewCheckout(w);
        await AddItem(w, checkout.Id, first);
        CheckoutDto withBoth = await AddItem(w, checkout.Id, second);
        CheckoutItemDto item1 = withBoth.Items.Single(i => i.BookingId == first);
        CheckoutItemDto item2 = withBoth.Items.Single(i => i.BookingId == second);

        await SchedulingAssert.BusinessRule(ErrorCodes.AllocationAmountMismatch,
            () => Pay(w, checkout.Id, 30m, new() { new() { CheckoutItemId = item1.Id, Amount = 10m } }));
        await SchedulingAssert.BusinessRule(ErrorCodes.AllocationExceedsItemOutstanding,
            () => Pay(w, checkout.Id, 60m, new() { new() { CheckoutItemId = item1.Id, Amount = 60m } }));

        // The valid split targets the SECOND item first — explicit allocation overrides FIFO order.
        await Pay(w, checkout.Id, 40m, new()
        {
            new() { CheckoutItemId = item2.Id, Amount = 30m },
            new() { CheckoutItemId = item1.Id, Amount = 10m }
        });

        Assert.Equal(10m, (await BookingDtoOf(w, firstDto.Id)).PaidAmount);
        Assert.Equal(30m, (await BookingDtoOf(w, secondDto.Id)).PaidAmount);
    }

    #endregion

    #region Checkout locking and eligibility

    [Fact]
    public async Task ABookingInAnOpenCheckout_CannotBeAddedToAnotherOpenCheckout_UntilTheFirstIsCancelled()
    {
        await using SchedulingWorld w = await SchedulingWorld.Create(nameof(ABookingInAnOpenCheckout_CannotBeAddedToAnotherOpenCheckout_UntilTheFirstIsCancelled));
        (Guid bookingId, _) = await UnpaidCompletedBooking(w, SchedulingWorld.Past(10));
        CheckoutDto first = await NewCheckout(w);
        await AddItem(w, first.Id, bookingId);
        CheckoutDto second = await NewCheckout(w);

        await SchedulingAssert.BusinessRule(ErrorCodes.BookingAlreadyInOpenCheckout, () => AddItem(w, second.Id, bookingId));

        await w.Checkouts.Cancel(w.OrganizationId, w.ActorUserId, first.Id);
        CheckoutDto moved = await AddItem(w, second.Id, bookingId);
        Assert.Single(moved.Items);
    }

    [Fact]
    public async Task ACancelledBooking_CannotBeAddedToACheckout()
    {
        await using SchedulingWorld w = await SchedulingWorld.Create(nameof(ACancelledBooking_CannotBeAddedToACheckout));
        AppointmentDto created = await w.CreateAppointment(SchedulingWorld.Future(10));
        await w.SetBookingStatus(created.Id, w.Client, BookingStatus.Cancelled);
        CheckoutDto checkout = await NewCheckout(w);

        await SchedulingAssert.BusinessRule(ErrorCodes.CheckoutItemNotEligible, () => AddItem(w, checkout.Id, created.Bookings.Single().Id));
    }

    [Fact]
    public async Task ABooking_CannotBeAddedToAnotherClientsCheckout()
    {
        await using SchedulingWorld w = await SchedulingWorld.Create(nameof(ABooking_CannotBeAddedToAnotherClientsCheckout));
        Client other = await w.AddClient("Other", "Client");
        (Guid bookingId, _) = await UnpaidCompletedBooking(w, SchedulingWorld.Past(10));
        CheckoutDto othersCheckout = await NewCheckout(w, other);

        await SchedulingAssert.BusinessRule(ErrorCodes.CheckoutItemClientMismatch, () => AddItem(w, othersCheckout.Id, bookingId));
    }

    [Fact]
    public async Task AConfirmedFutureBooking_CanAlreadyBePaidThroughCheckout_BeforeTheServiceIsDelivered()
    {
        await using SchedulingWorld w = await SchedulingWorld.Create(nameof(AConfirmedFutureBooking_CanAlreadyBePaidThroughCheckout_BeforeTheServiceIsDelivered));
        AppointmentDto created = await w.CreateAppointment(SchedulingWorld.Future(10));
        Guid bookingId = created.Bookings.Single().Id;

        await w.PayBookingViaCheckout(bookingId, w.Client, 50m);

        BookingDto derived = await BookingDtoOf(w, created.Id);
        Assert.Equal(BookingStatus.Confirmed, derived.Status);
        Assert.True(derived.IsPaid); // settlement is independent of the participation/completion status
    }

    #endregion

    #region The two calculators can disagree after a re-price (hidden coupling)

    [Fact]
    public async Task ARePriceAfterACheckoutItemWasCreated_LeavesTheItemAtTheOldAmount_WhileTheBookingUsesTheNewOne()
    {
        await using SchedulingWorld w = await SchedulingWorld.Create(nameof(ARePriceAfterACheckoutItemWasCreated_LeavesTheItemAtTheOldAmount_WhileTheBookingUsesTheNewOne));
        AppointmentDto created = await w.CreateAppointment(SchedulingWorld.Future(10));
        Guid bookingId = created.Bookings.Single().Id;
        CheckoutDto checkout = await NewCheckout(w);
        await AddItem(w, checkout.Id, bookingId);
        await Pay(w, checkout.Id, 20m);

        // A full Update re-prices the still-Confirmed booking to 30 ...
        await w.Appointments.Update(w.OrganizationId, w.ActorUserId, true, created.Id, w.UpdateRequest(created, r => r.Amount = 30m));

        // FINDING: ... but the CheckoutItem snapshot (UnitPrice/Amount) stays 50. The booking says 10 outstanding, the
        // checkout says 30 outstanding: two calculators reading two different "amount" sources for one obligation.
        BookingDto booking = await BookingDtoOf(w, created.Id);
        Assert.Equal(30m, booking.Amount);
        Assert.Equal(10m, booking.OutstandingAmount);
        CheckoutDto refreshed = await w.Checkouts.GetById(w.OrganizationId, checkout.Id);
        Assert.Equal(50m, refreshed.Items.Single().RetailAmount);
        Assert.Equal(30m, refreshed.Totals.OutstandingAmount);
    }

    #endregion
}
