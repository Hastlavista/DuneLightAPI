#nullable disable
using System;
using System.Linq;
using System.Threading.Tasks;
using BlueDragon.DuneLight.Core.DTOs.Appointments;
using BlueDragon.DuneLight.Core.Enums;
using BlueDragon.DuneLight.Core.Shared;
using BlueDragon.DuneLight.Infrastructure.Domain.Models.Appointments;
using BlueDragon.DuneLight.Infrastructure.Domain.Models.Checkouts;
using BlueDragon.DuneLight.Infrastructure.Domain.Models.Clients;
using BlueDragon.DuneLight.Infrastructure.Domain.Models.Commissions;
using BlueDragon.DuneLight.Infrastructure.Domain.Models.Employees;
using ServiceEntityAlias = BlueDragon.DuneLight.Infrastructure.Domain.Models.Catalog.Service;

namespace BlueDragon.DuneLight.UnitTests.Scheduling;

/// <summary>
/// CHARACTERIZATION (matrix N and Q): administrative corrections — moving a Booking BACK to Confirmed — and what they
/// reverse. The Individual and Group paths are separate implementations with different guarantees; this file pins both
/// so the asymmetry cannot be "fixed" or lost by accident.
///
/// Individual (BookingService.ApplyIndividualCompletionCorrection / ApplyIndividualNoShowCorrection):
///   Completed → Confirmed  reverses: check-in payments (voided), package entry (returned), commission (Reversed),
///                          appointment Completed → Scheduled; REFUSES when a manual POS payment exists; keeps Booking.Amount.
///   NoShow → Confirmed     status only (+ appointment revert if it had been closed), no financial side effects.
///   Cancelled → Confirmed  not supported (validation error).
/// Group (ApplyGroupTransition, any move away from Completed):
///   voids check-in payments, returns package coverage, RESETS Amount/SuggestedAmount to 0, never checks manual payments,
///   has no commission reversal (group commission is per occurrence). Cancelled → Confirmed and NoShow → Confirmed are allowed.
///
/// Manual (POS) payments are intentionally preserved by both paths — only <c>IsCheckInGenerated</c> payments are voided.
/// </summary>
public class BookingCorrectionCharacterizationTests
{
    private static readonly DateTimeOffset LongValid = new(2035, 1, 1, 0, 0, 0, TimeSpan.Zero);

    private static async Task<(AppointmentDto Dto, Guid BookingId)> CompleteWithCash(SchedulingWorld w)
    {
        AppointmentDto created = await w.CreateAppointment(SchedulingWorld.Future(10));
        AppointmentDto completed = await w.CompleteExisting(created.Id, w.CompleteRequest(SchedulingWorld.Future(10), paymentMethod: PaymentMethod.Cash));
        return (completed, completed.Bookings.Single().Id);
    }

    #region Individual: Completed -> Confirmed

    [Fact]
    public async Task Individual_CompletedToConfirmed_RevertsTheBookingAndTheAppointment_AndAdvancesTheVersion()
    {
        await using SchedulingWorld w = await SchedulingWorld.Create(nameof(Individual_CompletedToConfirmed_RevertsTheBookingAndTheAppointment_AndAdvancesTheVersion));
        (AppointmentDto completed, _) = await CompleteWithCash(w);

        BookingDto dto = await w.SetBookingStatus(completed.Id, w.Client, BookingStatus.Confirmed);

        Assert.Equal(BookingStatus.Confirmed, dto.Status);
        Appointment a = await w.LoadAppointment(completed.Id);
        Assert.Equal(AppointmentStatus.Scheduled, a.Status); // "Completed = nothing left unresolved" is re-opened
        Assert.Equal(2, a.Bookings.Single().StatusVersion);
        Assert.Contains(await w.LoadAuditLog(completed.Id), l => l.ChangeType == "Status" && l.OldValue == "Completed" && l.NewValue == "Scheduled");
        Assert.Contains(await w.LoadAuditLog(completed.Id), l => l.ChangeType == "BookingStatus" && l.OldValue == "Completed" && l.NewValue == "Confirmed" && l.StatusVersion == 2);
    }

    [Fact]
    public async Task Individual_CompletedToConfirmed_VoidsTheCheckInPayment_AndTheAutoCheckout_KeepingTheAmount()
    {
        await using SchedulingWorld w = await SchedulingWorld.Create(nameof(Individual_CompletedToConfirmed_VoidsTheCheckInPayment_AndTheAutoCheckout_KeepingTheAmount));
        (AppointmentDto completed, Guid bookingId) = await CompleteWithCash(w);

        await w.SetBookingStatus(completed.Id, w.Client, BookingStatus.Confirmed);

        Payment payment = Assert.Single(await w.LoadPayments(bookingId));
        Assert.Equal(PaymentStatus.Voided, payment.Status); // voided, never deleted
        Assert.Equal("Poništen check-in (korekcija Completed -> Confirmed)", payment.VoidReason);
        Assert.Equal(w.ActorUserId, payment.VoidedBy);
        Assert.NotNull(payment.VoidedAt);
        CheckoutItem item = Assert.Single(await w.LoadCheckoutItems(bookingId));
        Assert.Equal(CheckoutStatus.Voided, (await w.LoadCheckout(item.CheckoutId)).Status);
        Assert.Contains(await w.LoadAuditLog(completed.Id), l => l.ChangeType == "PaymentVoided");
        // Individual keeps the price (contrast with Group, which resets it to 0): the booking is unpaid again, not free.
        Booking b = await w.LoadBooking(completed.Id, w.Client);
        Assert.Equal(50m, b.Amount);
        Assert.Equal(50m, b.SuggestedAmount);
        BookingDto refreshed = (await w.Appointments.GetById(w.OrganizationId, completed.Id)).Bookings.Single();
        Assert.Equal(0m, refreshed.PaidAmount);
        Assert.Equal(50m, refreshed.OutstandingAmount);
        Assert.False(refreshed.IsPaid);
    }

    [Fact]
    public async Task Individual_CompletedToConfirmed_ReturnsThePackageEntry_ButKeepsTheClientPackageLink()
    {
        await using SchedulingWorld w = await SchedulingWorld.Create(nameof(Individual_CompletedToConfirmed_ReturnsThePackageEntry_ButKeepsTheClientPackageLink));
        ClientPackage package = await w.AddClientPackage(w.Client, w.Service, 5, LongValid);
        AppointmentDto created = await w.CreateAppointment(SchedulingWorld.Future(10));
        await w.CompleteExisting(created.Id, w.CompleteRequest(SchedulingWorld.Future(10), clientPackageId: package.Id));
        Assert.Equal(4, (await w.LoadClientPackage(package.Id.Value)).ServiceEntries.Single().RemainingEntries);

        await w.SetBookingStatus(created.Id, w.Client, BookingStatus.Confirmed);

        Booking b = await w.LoadBooking(created.Id, w.Client);
        Assert.Equal(package.Id, b.ClientPackageId);   // history: which package was used
        Assert.True(b.PackageCoverageApplied);
        Assert.True(b.PackageCoverageReturned);         // ... and that it was given back
        Assert.NotNull(b.PackageCoverageReturnedAt);
        Assert.Equal(w.ActorUserId, b.PackageCoverageReturnedBy);
        Assert.Equal(5, (await w.LoadClientPackage(package.Id.Value)).ServiceEntries.Single().RemainingEntries);
        Assert.Contains(await w.LoadAuditLog(created.Id), l => l.ChangeType == "BookingPackageCoverageReturned");
        // Returned coverage no longer settles the obligation.
        BookingDto refreshed = (await w.Appointments.GetById(w.OrganizationId, created.Id)).Bookings.Single();
        Assert.Equal(50m, refreshed.OutstandingAmount);
    }

    [Fact]
    public async Task Individual_CompletedToConfirmed_ReversesTheEarnedCommission()
    {
        await using SchedulingWorld w = await SchedulingWorld.Create(nameof(Individual_CompletedToConfirmed_ReversesTheEarnedCommission));
        await w.AddCommissionRule(w.Employee, w.Service, CommissionCalculationType.Percentage, 10m);
        (AppointmentDto completed, _) = await CompleteWithCash(w);

        await w.SetBookingStatus(completed.Id, w.Client, BookingStatus.Confirmed);

        CommissionEntry entry = Assert.Single(await w.LoadCommissionEntries());
        Assert.Equal(CommissionEntryStatus.Reversed, entry.Status);
        Assert.Equal(w.ActorUserId, entry.ReversedBy);
        Assert.NotNull(entry.ReversedAt);
        Assert.Equal(5m, entry.CommissionAmount); // the row is kept, only its status changes
    }

    [Fact]
    public async Task Individual_CompletedToConfirmed_IsRefusedWhileAManualPosPaymentExists_AndChangesNothing()
    {
        await using SchedulingWorld w = await SchedulingWorld.Create(nameof(Individual_CompletedToConfirmed_IsRefusedWhileAManualPosPaymentExists_AndChangesNothing));
        await w.AddCommissionRule(w.Employee, w.Service, CommissionCalculationType.Percentage, 10m);
        AppointmentDto created = await w.CreateAppointment(SchedulingWorld.Future(10));
        AppointmentDto completed = await w.CompleteExisting(created.Id, w.CompleteRequest(SchedulingWorld.Future(10))); // unpaid
        Guid bookingId = completed.Bookings.Single().Id;
        await w.PayBookingViaCheckout(bookingId, w.Client, 20m); // a manual POS payment

        await SchedulingAssert.BusinessRule(ErrorCodes.BookingHasNonReversiblePayment,
            () => w.SetBookingStatus(completed.Id, w.Client, BookingStatus.Confirmed));

        // The whole correction is rolled back: status, version, appointment, commission and payment are unchanged.
        Booking b = await w.LoadBooking(completed.Id, w.Client);
        Assert.Equal(BookingStatus.Completed, b.Status);
        Assert.Equal(1, b.StatusVersion);
        Assert.Equal(AppointmentStatus.Completed, (await w.LoadAppointment(completed.Id)).Status);
        Assert.Equal(CommissionEntryStatus.Earned, Assert.Single(await w.LoadCommissionEntries()).Status);
        Assert.Equal(PaymentStatus.Completed, Assert.Single(await w.LoadPayments(bookingId)).Status);
    }

    [Fact]
    public async Task Individual_CompletedToConfirmed_PreservesAManualPaymentOnASiblingBooking()
    {
        await using SchedulingWorld w = await SchedulingWorld.Create(nameof(Individual_CompletedToConfirmed_PreservesAManualPaymentOnASiblingBooking));
        Client partner = await w.AddClient("Partner", "Client");
        AppointmentDto created = await w.CreateAppointment(SchedulingWorld.Future(10), extraClients: partner);
        AppointmentDto completed = await w.CompleteExisting(created.Id, w.CompleteRequest(SchedulingWorld.Future(10), settlements: new[]
        {
            new AppointmentClientSettlement { ClientId = w.Client.Id.Value, PaymentMethod = PaymentMethod.Cash },
            new AppointmentClientSettlement { ClientId = partner.Id.Value }
        }));
        Guid partnerBookingId = completed.Bookings.Single(b => b.ClientId == partner.Id).Id;
        await w.PayBookingViaCheckout(partnerBookingId, partner, 20m);

        // Correcting ONE client's booking is scoped to that booking: the partner's manual payment does not block it and is untouched.
        await w.SetBookingStatus(created.Id, w.Client, BookingStatus.Confirmed);

        Assert.Equal(PaymentStatus.Completed, Assert.Single(await w.LoadPayments(partnerBookingId)).Status);
        Assert.Equal(BookingStatus.Completed, (await w.LoadBooking(created.Id, partner)).Status);
    }

    [Fact]
    public async Task Individual_CorrectionOfOneBooking_ReopensTheAppointment_EvenWhileASiblingStaysCompleted()
    {
        await using SchedulingWorld w = await SchedulingWorld.Create(nameof(Individual_CorrectionOfOneBooking_ReopensTheAppointment_EvenWhileASiblingStaysCompleted));
        Client partner = await w.AddClient("Partner", "Client");
        AppointmentDto created = await w.CreateAppointment(SchedulingWorld.Future(10), extraClients: partner);
        await w.CompleteExisting(created.Id, w.CompleteRequest(SchedulingWorld.Future(10), settlements: new[]
        {
            new AppointmentClientSettlement { ClientId = w.Client.Id.Value },
            new AppointmentClientSettlement { ClientId = partner.Id.Value }
        }));

        await w.SetBookingStatus(created.Id, w.Client, BookingStatus.Confirmed);

        Appointment a = await w.LoadAppointment(created.Id);
        Assert.Equal(AppointmentStatus.Scheduled, a.Status);
        Assert.Equal(BookingStatus.Completed, a.Bookings.Single(b => b.ClientId == partner.Id).Status);
        Assert.Equal(BookingStatus.Confirmed, a.Bookings.Single(b => b.ClientId == w.Client.Id).Status);
    }

    [Fact]
    public async Task Individual_ConfirmingAnAlreadyConfirmedBooking_IsANoOp_WithNoSideEffects()
    {
        await using SchedulingWorld w = await SchedulingWorld.Create(nameof(Individual_ConfirmingAnAlreadyConfirmedBooking_IsANoOp_WithNoSideEffects));
        AppointmentDto created = await w.CreateAppointment(SchedulingWorld.Future(10));

        BookingDto dto = await w.SetBookingStatus(created.Id, w.Client, BookingStatus.Confirmed);

        Assert.Equal(BookingStatus.Confirmed, dto.Status);
        Assert.Equal(0, (await w.LoadBooking(created.Id, w.Client)).StatusVersion);
        Assert.Empty(await w.LoadAuditLog(created.Id));
    }

    [Fact]
    public async Task Individual_ConfirmingAConfirmedBookingThatHasAManualPartialPayment_IsCurrentlyRefused()
    {
        await using SchedulingWorld w = await SchedulingWorld.Create(nameof(Individual_ConfirmingAConfirmedBookingThatHasAManualPartialPayment_IsCurrentlyRefused));
        AppointmentDto created = await w.CreateAppointment(SchedulingWorld.Future(10));
        await w.PayBookingViaCheckout(created.Bookings.Single().Id, w.Client, 20m);

        // FINDING: the idempotent "confirm again" retry runs the same correction body as a real Completed -> Confirmed, so a
        // plain Confirmed booking with a manual payment cannot even be re-confirmed (the non-reversible-payment guard fires).
        await SchedulingAssert.BusinessRule(ErrorCodes.BookingHasNonReversiblePayment,
            () => w.SetBookingStatus(created.Id, w.Client, BookingStatus.Confirmed));
    }

    #endregion

    #region Individual: NoShow -> Confirmed and Cancelled

    [Fact]
    public async Task Individual_NoShowToConfirmed_ChangesStatusOnly_AndAdvancesTheVersion()
    {
        await using SchedulingWorld w = await SchedulingWorld.Create(nameof(Individual_NoShowToConfirmed_ChangesStatusOnly_AndAdvancesTheVersion));
        AppointmentDto created = await w.CreateAppointment(SchedulingWorld.Future(10));
        await w.SetBookingStatus(created.Id, w.Client, BookingStatus.NoShow, "absent");

        BookingDto dto = await w.SetBookingStatus(created.Id, w.Client, BookingStatus.Confirmed);

        Assert.Equal(BookingStatus.Confirmed, dto.Status);
        Booking b = await w.LoadBooking(created.Id, w.Client);
        Assert.Equal(2, b.StatusVersion);
        Assert.Equal("absent", b.CancellationReason); // the previous reason is not cleared
        Assert.Equal(AppointmentStatus.Scheduled, (await w.LoadAppointment(created.Id)).Status);
    }

    [Fact]
    public async Task Individual_NoShowToConfirmed_ReopensACompletedAppointmentClosedByASibling()
    {
        await using SchedulingWorld w = await SchedulingWorld.Create(nameof(Individual_NoShowToConfirmed_ReopensACompletedAppointmentClosedByASibling));
        Client sibling = await w.AddClient("Sibling", "Client");
        // Shape produced by real flows: one booking no-showed, then a sibling was completed via CompleteExisting.
        AppointmentDto created = await w.CreateAppointment(SchedulingWorld.Future(10), extraClients: sibling);
        await w.SetBookingStatus(created.Id, w.Client, BookingStatus.NoShow);
        await w.CompleteExisting(created.Id, w.CompleteRequest(SchedulingWorld.Future(10), client: sibling));
        Assert.Equal(AppointmentStatus.Completed, (await w.LoadAppointment(created.Id)).Status);

        await w.SetBookingStatus(created.Id, w.Client, BookingStatus.Confirmed);

        Assert.Equal(AppointmentStatus.Scheduled, (await w.LoadAppointment(created.Id)).Status);
    }

    [Fact]
    public async Task Individual_NoShowToConfirmed_DoesNotRunTheFinancialReversal_SoAnUnrelatedManualPaymentDoesNotBlockIt()
    {
        await using SchedulingWorld w = await SchedulingWorld.Create(nameof(Individual_NoShowToConfirmed_DoesNotRunTheFinancialReversal_SoAnUnrelatedManualPaymentDoesNotBlockIt));
        AppointmentDto created = await w.CreateAppointment(SchedulingWorld.Future(10));
        Guid bookingId = created.Bookings.Single().Id;
        await w.PayBookingViaCheckout(bookingId, w.Client, 20m);
        await w.SetBookingStatus(created.Id, w.Client, BookingStatus.NoShow);

        BookingDto dto = await w.SetBookingStatus(created.Id, w.Client, BookingStatus.Confirmed);

        Assert.Equal(BookingStatus.Confirmed, dto.Status);
        Assert.Equal(PaymentStatus.Completed, Assert.Single(await w.LoadPayments(bookingId)).Status);
    }

    #endregion

    #region Group corrections

    private static async Task<(Appointment Occurrence, Client Member)> GroupOccurrence(SchedulingWorld w, ServiceEntityAlias service, int capacity = 3)
    {
        var group = await w.CreateGroup(service, capacity);
        await w.AddGroupMember(group, w.Client);
        return (await w.GenerateSingleOccurrence(group), w.Client);
    }

    [Fact]
    public async Task Group_CompletedToConfirmed_VoidsTheCheckInPayment_AndResetsAmountAndSuggestedAmountToZero()
    {
        await using SchedulingWorld w = await SchedulingWorld.Create(nameof(Group_CompletedToConfirmed_VoidsTheCheckInPayment_AndResetsAmountAndSuggestedAmountToZero));
        var svc = await w.AddGroupService();
        (Appointment occurrence, Client member) = await GroupOccurrence(w, svc);
        await w.SetBookingStatus(occurrence.Id.Value, member, BookingStatus.Completed, paymentMethod: PaymentMethod.Cash);
        Guid bookingId = (await w.LoadBooking(occurrence.Id.Value, member)).Id.Value;
        Assert.Equal(PaymentStatus.Completed, Assert.Single(await w.LoadPayments(bookingId)).Status);

        await w.SetBookingStatus(occurrence.Id.Value, member, BookingStatus.Confirmed);

        Payment payment = Assert.Single(await w.LoadPayments(bookingId));
        Assert.Equal(PaymentStatus.Voided, payment.Status);
        Assert.Equal("Poništen check-in", payment.VoidReason);
        Booking b = await w.LoadBooking(occurrence.Id.Value, member);
        Assert.Equal(BookingStatus.Confirmed, b.Status);
        Assert.Equal(2, b.StatusVersion);
        // FINDING (asymmetry): Group resets the price to 0 on un-check-in; Individual keeps it.
        Assert.Equal(0m, b.Amount);
        Assert.Equal(0m, b.SuggestedAmount);
        Assert.False(b.IsAmountManuallyOverridden);
    }

    [Fact]
    public async Task Group_CompletedToConfirmed_ReturnsThePackageCoverage_AndAReCheckInAppliesItAgain()
    {
        await using SchedulingWorld w = await SchedulingWorld.Create(nameof(Group_CompletedToConfirmed_ReturnsThePackageCoverage_AndAReCheckInAppliesItAgain));
        var svc = await w.AddGroupService();
        ClientPackage package = await w.AddClientPackage(w.Client, svc, 5, LongValid);
        (Appointment occurrence, Client member) = await GroupOccurrence(w, svc);

        await w.SetBookingStatus(occurrence.Id.Value, member, BookingStatus.Completed, clientPackageId: package.Id);
        Assert.Equal(4, (await w.LoadClientPackage(package.Id.Value)).ServiceEntries.Single().RemainingEntries);

        await w.SetBookingStatus(occurrence.Id.Value, member, BookingStatus.Confirmed);
        Booking corrected = await w.LoadBooking(occurrence.Id.Value, member);
        Assert.True(corrected.PackageCoverageReturned);
        Assert.Equal(5, (await w.LoadClientPackage(package.Id.Value)).ServiceEntries.Single().RemainingEntries);

        await w.SetBookingStatus(occurrence.Id.Value, member, BookingStatus.Completed, clientPackageId: package.Id);
        Booking again = await w.LoadBooking(occurrence.Id.Value, member);
        Assert.True(again.PackageCoverageApplied);
        Assert.False(again.PackageCoverageReturned); // the returned flag is cleared by the fresh coverage
        Assert.Equal(4, (await w.LoadClientPackage(package.Id.Value)).ServiceEntries.Single().RemainingEntries);
        Assert.Equal(3, again.StatusVersion);
    }

    [Fact]
    public async Task Group_CompletedToConfirmed_DoesNotCheckForManualPayments_AndLeavesThemOnAZeroPricedBooking()
    {
        await using SchedulingWorld w = await SchedulingWorld.Create(nameof(Group_CompletedToConfirmed_DoesNotCheckForManualPayments_AndLeavesThemOnAZeroPricedBooking));
        var svc = await w.AddGroupService();
        (Appointment occurrence, Client member) = await GroupOccurrence(w, svc);
        await w.SetBookingStatus(occurrence.Id.Value, member, BookingStatus.Completed); // checked in, unpaid
        Guid bookingId = (await w.LoadBooking(occurrence.Id.Value, member)).Id.Value;
        await w.PayBookingViaCheckout(bookingId, member, 5m); // a manual partial payment

        // FINDING (asymmetry): the Individual path refuses here (BOOKING_HAS_NON_REVERSIBLE_PAYMENT); the Group path proceeds.
        await w.SetBookingStatus(occurrence.Id.Value, member, BookingStatus.Confirmed);

        Booking b = await w.LoadBooking(occurrence.Id.Value, member);
        Assert.Equal(0m, b.Amount);
        Payment payment = Assert.Single(await w.LoadPayments(bookingId));
        Assert.Equal(PaymentStatus.Completed, payment.Status); // preserved (manual payments are never voided by a correction)
        Assert.Equal(5m, payment.Amount);
        BookingDto dto = (await w.Appointments.GetById(w.OrganizationId, occurrence.Id.Value)).Bookings.Single();
        Assert.Equal(5m, dto.PaidAmount);
        Assert.Equal(0m, dto.OutstandingAmount); // money received against an obligation that was reset to 0
    }

    [Fact]
    public async Task Group_NoShowToConfirmed_IsAllowed_AndAdvancesTheVersion()
    {
        await using SchedulingWorld w = await SchedulingWorld.Create(nameof(Group_NoShowToConfirmed_IsAllowed_AndAdvancesTheVersion));
        var svc = await w.AddGroupService();
        (Appointment occurrence, Client member) = await GroupOccurrence(w, svc);
        await w.SetBookingStatus(occurrence.Id.Value, member, BookingStatus.NoShow);

        await w.SetBookingStatus(occurrence.Id.Value, member, BookingStatus.Confirmed);

        Booking b = await w.LoadBooking(occurrence.Id.Value, member);
        Assert.Equal(BookingStatus.Confirmed, b.Status);
        Assert.Equal(2, b.StatusVersion);
        Assert.Equal(15m, b.Amount); // an un-no-show never touches the price (only leaving Completed resets it)
    }

    [Fact]
    public async Task Group_CancelledToConfirmed_IsAllowed_UnlikeIndividual()
    {
        await using SchedulingWorld w = await SchedulingWorld.Create(nameof(Group_CancelledToConfirmed_IsAllowed_UnlikeIndividual));
        var svc = await w.AddGroupService();
        (Appointment occurrence, Client member) = await GroupOccurrence(w, svc);
        await w.SetBookingStatus(occurrence.Id.Value, member, BookingStatus.Cancelled);

        await w.SetBookingStatus(occurrence.Id.Value, member, BookingStatus.Confirmed);

        Assert.Equal(BookingStatus.Confirmed, (await w.LoadBooking(occurrence.Id.Value, member)).Status);
    }

    [Fact]
    public async Task Group_CompletedToCancelled_ReturnsThePackageEntryAutomatically_UnlikeIndividualWhichIsOptIn()
    {
        await using SchedulingWorld w = await SchedulingWorld.Create(nameof(Group_CompletedToCancelled_ReturnsThePackageEntryAutomatically_UnlikeIndividualWhichIsOptIn));
        var svc = await w.AddGroupService();
        ClientPackage package = await w.AddClientPackage(w.Client, svc, 5, LongValid);
        (Appointment occurrence, Client member) = await GroupOccurrence(w, svc);
        await w.SetBookingStatus(occurrence.Id.Value, member, BookingStatus.Completed, clientPackageId: package.Id);

        await w.SetBookingStatus(occurrence.Id.Value, member, BookingStatus.Cancelled); // no ReturnPackageEntry flag needed

        Assert.Equal(5, (await w.LoadClientPackage(package.Id.Value)).ServiceEntries.Single().RemainingEntries);
        Assert.True((await w.LoadBooking(occurrence.Id.Value, member)).PackageCoverageReturned);
    }

    [Fact]
    public async Task Group_CorrectionDoesNotReverseTheOccurrenceCommission_ItIsOwnedByTheAppointment()
    {
        await using SchedulingWorld w = await SchedulingWorld.Create(nameof(Group_CorrectionDoesNotReverseTheOccurrenceCommission_ItIsOwnedByTheAppointment));
        var svc = await w.AddGroupService();
        await w.AddCommissionRule(w.Employee, svc, CommissionCalculationType.Fixed, 20m);
        (Appointment occurrence, Client member) = await GroupOccurrence(w, svc);
        await w.SetBookingStatus(occurrence.Id.Value, member, BookingStatus.Completed);
        await w.Appointments.CompleteGroupAppointment(w.OrganizationId, w.ActorUserId, true, occurrence.Id.Value);

        await w.SetBookingStatus(occurrence.Id.Value, member, BookingStatus.Confirmed);

        CommissionEntry entry = Assert.Single(await w.LoadCommissionEntries());
        Assert.Equal(CommissionSourceType.GroupService, entry.SourceType);
        Assert.Equal(CommissionEntryStatus.Earned, entry.Status);
        // ... and the appointment frame stays Completed (only the Individual path re-opens it).
        Assert.Equal(AppointmentStatus.Completed, (await w.LoadAppointment(occurrence.Id.Value)).Status);
    }

    #endregion
}
