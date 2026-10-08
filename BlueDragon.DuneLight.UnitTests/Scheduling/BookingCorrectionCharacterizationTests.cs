#nullable disable
using System;
using System.Collections.Generic;
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
/// reverse. CHANGED in P1 (ADR-0018, D12): Individual and Group share ONE transition matrix (BookingService
/// ApplyTransitionInTransaction / ReversePreviousStateEffects).
///
///   Completed → anything   voids check-in payments, returns the completion package entry, reverses the individual
///                          completion commission (group commission is per occurrence); the price is KEPT (Group no longer
///                          resets it to 0); a manual POS payment no longer blocks the correction — it stays as settlement.
///   NoShow/Cancelled → …   reverses only the active policy consequence (none under the neutral default policy).
///   Same status            a true no-op.
///
/// Manual (POS) payments are intentionally preserved — only <c>IsCheckInGenerated</c> payments are voided.
/// </summary>
public class BookingCorrectionCharacterizationTests
{
    private static readonly DateTimeOffset LongValid = new(2035, 1, 1, 0, 0, 0, TimeSpan.Zero);

    private static async Task<(AppointmentDto Dto, Guid BookingId)> CompleteWithCash(SchedulingWorld w)
    {
        AppointmentDto created = await w.CreateAppointment(SchedulingWorld.Future(10));
        AppointmentDto completed = await w.CompleteParticipations(created.Id, w.CompleteRequest(SchedulingWorld.Future(10), paymentMethod: PaymentMethod.Cash));
        return (completed, completed.Bookings.Single().Id);
    }

    #region Individual: Completed -> Confirmed

    [Fact]
    public async Task Individual_CompletedToConfirmed_RevertsTheBookingAndTheAppointment_AndAdvancesTheVersion()
    {
        await using SchedulingWorld w = await SchedulingWorld.Create(nameof(Individual_CompletedToConfirmed_RevertsTheBookingAndTheAppointment_AndAdvancesTheVersion));
        (AppointmentDto completed, _) = await CompleteWithCash(w);

        BookingDto dto = await w.SetBookingStatus(completed.Id, w.Client, BookingStatus.Confirmed);

        Assert.Equal(BookingStatusSummary.Confirmed, dto.Status);
        Appointment a = await w.LoadAppointment(completed.Id);
        Assert.Equal(AppointmentStatus.Scheduled, a.Status); // M1A: a Confirmed participation re-derives Closed -> Scheduled
        Assert.Equal(2, a.Bookings.Single().StatusVersion);
        Assert.Contains(await w.LoadAuditLog(completed.Id), l => l.ChangeType == "Status" && l.OldValue == "Closed" && l.NewValue == "Scheduled");
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
        await w.CompleteParticipations(created.Id, w.CompleteRequest(SchedulingWorld.Future(10), clientPackageId: package.Id));
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
    public async Task Individual_CompletedToConfirmed_WithAManualPosPayment_ProceedsAndKeepsTheMoneyAsSettlement()
    {
        // CHANGED in P1 (D7/D12, intentional): a manual payment used to block the correction (BOOKING_HAS_NON_REVERSIBLE_PAYMENT).
        await using SchedulingWorld w = await SchedulingWorld.Create(nameof(Individual_CompletedToConfirmed_WithAManualPosPayment_ProceedsAndKeepsTheMoneyAsSettlement));
        await w.AddCommissionRule(w.Employee, w.Service, CommissionCalculationType.Percentage, 10m);
        AppointmentDto created = await w.CreateAppointment(SchedulingWorld.Future(10));
        AppointmentDto completed = await w.CompleteParticipations(created.Id, w.CompleteRequest(SchedulingWorld.Future(10))); // unpaid
        Guid bookingId = completed.Bookings.Single().Id;
        await w.PayBookingViaCheckout(bookingId, w.Client, 20m); // a manual POS payment

        await w.SetBookingStatus(completed.Id, w.Client, BookingStatus.Confirmed);

        Booking b = await w.LoadBooking(completed.Id, w.Client);
        Assert.Equal(BookingStatus.Confirmed, b.Status);
        Assert.Equal(2, b.StatusVersion);
        Assert.Equal(AppointmentStatus.Scheduled, (await w.LoadAppointment(completed.Id)).Status);
        Assert.Equal(CommissionEntryStatus.Reversed, Assert.Single(await w.LoadCommissionEntries()).Status);
        Assert.Equal(PaymentStatus.Completed, Assert.Single(await w.LoadPayments(bookingId)).Status); // money never moves
        BookingDto dto = (await w.Appointments.GetById(w.OrganizationId, completed.Id)).Bookings.Single();
        Assert.Equal(20m, dto.PaidAmount);      // kept as a prepayment
        Assert.Equal(30m, dto.OutstandingAmount);
    }

    [Fact]
    public async Task Individual_CompletedToConfirmed_WithBothAManualAndACheckInPayment_VoidsOnlyTheCheckInPayment()
    {
        await using SchedulingWorld w = await SchedulingWorld.Create(nameof(Individual_CompletedToConfirmed_WithBothAManualAndACheckInPayment_VoidsOnlyTheCheckInPayment));
        await w.AddCommissionRule(w.Employee, w.Service, CommissionCalculationType.Percentage, 10m);
        ClientPackage untouchedPackage = await w.AddClientPackage(w.Client, w.Service, 5, LongValid); // must stay at 5: no package is applied here
        AppointmentDto created = await w.CreateAppointment(SchedulingWorld.Future(10));
        Guid bookingId = created.Bookings.Single().Id;
        await w.PayBookingViaCheckout(bookingId, w.Client, 20m); // manual POS payment (IsCheckInGenerated = false)
        // D3B3B (changed): the check-in cash settlement pays only the participation's REMAINING 30 — the manual 20 is
        // counted. Before: it charged the full 50 and over-settled the obligation (20 + 50 against 50).
        AppointmentDto completed = await w.CompleteParticipations(created.Id, w.CompleteRequest(SchedulingWorld.Future(10), paymentMethod: PaymentMethod.Cash));
        List<Payment> before = await w.LoadPayments(bookingId);
        Assert.Equal(2, before.Count);
        Assert.Equal(30m, Assert.Single(before, p => p.IsCheckInGenerated).Amount);
        Assert.Equal(1, before.Count(p => !p.IsCheckInGenerated));
        BookingDto settled = (await w.Appointments.GetById(w.OrganizationId, completed.Id)).Bookings.Single();
        Assert.Equal(50m, settled.PaidAmount);       // 20 manual + 30 check-in against a 50 obligation
        Assert.Equal(0m, settled.OutstandingAmount);

        // CHANGED in P1 (D12): the manual payment no longer blocks — only the check-in payment is voided, the manual one stays.
        await w.SetBookingStatus(completed.Id, w.Client, BookingStatus.Confirmed);

        Booking b = await w.LoadBooking(completed.Id, w.Client);
        Assert.Equal(BookingStatus.Confirmed, b.Status);
        Assert.Equal(2, b.StatusVersion);
        Assert.Equal(50m, b.Amount);
        List<Payment> after = await w.LoadPayments(bookingId);
        Assert.Equal(PaymentStatus.Voided, Assert.Single(after, p => p.IsCheckInGenerated).Status);
        Assert.Equal(PaymentStatus.Completed, Assert.Single(after, p => !p.IsCheckInGenerated).Status);
        Assert.Equal(CommissionEntryStatus.Reversed, Assert.Single(await w.LoadCommissionEntries()).Status);
        Assert.Equal(5, (await w.LoadClientPackage(untouchedPackage.Id.Value)).ServiceEntries.Single().RemainingEntries);
        BookingDto dto = (await w.Appointments.GetById(w.OrganizationId, completed.Id)).Bookings.Single();
        Assert.Equal(20m, dto.PaidAmount);
        Assert.Equal(30m, dto.OutstandingAmount);
    }

    [Fact]
    public async Task Individual_CompletedToConfirmed_PreservesAManualPaymentOnASiblingBooking()
    {
        await using SchedulingWorld w = await SchedulingWorld.Create(nameof(Individual_CompletedToConfirmed_PreservesAManualPaymentOnASiblingBooking));
        Client partner = await w.AddClient("Partner", "Client");
        AppointmentDto created = await w.CreateAppointment(SchedulingWorld.Future(10), extraClients: partner);
        AppointmentDto completed = await w.CompleteParticipations(created.Id, w.CompleteRequest(SchedulingWorld.Future(10), settlements: new[]
        {
            new AppointmentCompletedClientRequest { ClientId = w.Client.Id.Value, PaymentMethod = PaymentMethod.Cash },
            new AppointmentCompletedClientRequest { ClientId = partner.Id.Value }
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
        await w.CompleteParticipations(created.Id, w.CompleteRequest(SchedulingWorld.Future(10), settlements: new[]
        {
            new AppointmentCompletedClientRequest { ClientId = w.Client.Id.Value },
            new AppointmentCompletedClientRequest { ClientId = partner.Id.Value }
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

        Assert.Equal(BookingStatusSummary.Confirmed, dto.Status);
        Assert.Equal(0, (await w.LoadBooking(created.Id, w.Client)).StatusVersion);
        Assert.Empty(await w.LoadAuditLog(created.Id));
    }

    [Fact]
    public async Task Individual_ConfirmingAConfirmedBookingThatHasAManualPartialPayment_IsANoOp()
    {
        // CHANGED in P1 (D12): the same-status retry is a true no-op (it used to run the correction body and be refused).
        await using SchedulingWorld w = await SchedulingWorld.Create(nameof(Individual_ConfirmingAConfirmedBookingThatHasAManualPartialPayment_IsANoOp));
        AppointmentDto created = await w.CreateAppointment(SchedulingWorld.Future(10));
        await w.PayBookingViaCheckout(created.Bookings.Single().Id, w.Client, 20m);

        await w.SetBookingStatus(created.Id, w.Client, BookingStatus.Confirmed);

        Assert.Equal(0, (await w.LoadBooking(created.Id, w.Client)).StatusVersion);
        Assert.Equal(PaymentStatus.Completed, Assert.Single(await w.LoadPayments(created.Bookings.Single().Id)).Status);
    }

    #endregion

    #region Individual: NoShow -> Confirmed and Cancelled

    [Fact]
    public async Task Individual_NoShowToConfirmed_ChangesStatusOnly_AndAdvancesTheVersion()
    {
        await using SchedulingWorld w = await SchedulingWorld.Create(nameof(Individual_NoShowToConfirmed_ChangesStatusOnly_AndAdvancesTheVersion));
        AppointmentDto created = await w.CreateAppointment(SchedulingWorld.Past(10));
        await w.SetBookingStatus(created.Id, w.Client, BookingStatus.NoShow, "absent");

        BookingDto dto = await w.SetBookingStatus(created.Id, w.Client, BookingStatus.Confirmed);

        Assert.Equal(BookingStatusSummary.Confirmed, dto.Status);
        Booking b = await w.LoadBooking(created.Id, w.Client);
        Assert.Equal(2, b.StatusVersion);
        // CHANGED in P1 (D12): metadata always matches the status — the no-show stamp and reason are cleared (history is in
        // the audit log).
        Assert.Null(b.Participations.Single().NoShowReason);
        Assert.Null(b.Participations.Single().NoShowAt);
        Assert.Equal(AppointmentStatus.Scheduled, (await w.LoadAppointment(created.Id)).Status);
    }

    [Fact]
    public async Task Individual_NoShowToConfirmed_ReopensAnAppointmentClosedByASibling()
    {
        await using SchedulingWorld w = await SchedulingWorld.Create(nameof(Individual_NoShowToConfirmed_ReopensAnAppointmentClosedByASibling));
        Client sibling = await w.AddClient("Sibling", "Client");
        // Shape produced by real flows: one booking no-showed, then a sibling was completed via CompleteExisting.
        AppointmentDto created = await w.CreateAppointment(SchedulingWorld.Past(10), extraClients: sibling);
        await w.SetBookingStatus(created.Id, w.Client, BookingStatus.NoShow);
        await w.CompleteParticipations(created.Id, w.CompleteRequest(SchedulingWorld.Past(10), client: sibling));
        Assert.Equal(AppointmentStatus.Closed, (await w.LoadAppointment(created.Id)).Status);

        await w.SetBookingStatus(created.Id, w.Client, BookingStatus.Confirmed);

        Assert.Equal(AppointmentStatus.Scheduled, (await w.LoadAppointment(created.Id)).Status);
    }

    [Fact]
    public async Task Individual_NoShowToConfirmed_DoesNotRunTheFinancialReversal_SoAnUnrelatedManualPaymentDoesNotBlockIt()
    {
        await using SchedulingWorld w = await SchedulingWorld.Create(nameof(Individual_NoShowToConfirmed_DoesNotRunTheFinancialReversal_SoAnUnrelatedManualPaymentDoesNotBlockIt));
        AppointmentDto created = await w.CreateAppointment(SchedulingWorld.Past(10));
        Guid bookingId = created.Bookings.Single().Id;
        await w.PayBookingViaCheckout(bookingId, w.Client, 20m);
        await w.SetBookingStatus(created.Id, w.Client, BookingStatus.NoShow);

        BookingDto dto = await w.SetBookingStatus(created.Id, w.Client, BookingStatus.Confirmed);

        Assert.Equal(BookingStatusSummary.Confirmed, dto.Status);
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
    public async Task Group_CompletedToConfirmed_VoidsTheCheckInPayment_AndKeepsThePrice()
    {
        await using SchedulingWorld w = await SchedulingWorld.Create(nameof(Group_CompletedToConfirmed_VoidsTheCheckInPayment_AndKeepsThePrice));
        var svc = await w.AddGroupService();
        (Appointment occurrence, Client member) = await GroupOccurrence(w, svc);
        await w.SetBookingStatus(occurrence.Id.Value, member, BookingStatus.Completed, paymentMethod: PaymentMethod.Cash);
        Guid bookingId = (await w.LoadBooking(occurrence.Id.Value, member)).Id.Value;
        Assert.Equal(PaymentStatus.Completed, Assert.Single(await w.LoadPayments(bookingId)).Status);

        await w.SetBookingStatus(occurrence.Id.Value, member, BookingStatus.Confirmed);

        Payment payment = Assert.Single(await w.LoadPayments(bookingId));
        Assert.Equal(PaymentStatus.Voided, payment.Status);
        Assert.Equal("Poništen check-in (korekcija Completed -> Confirmed)", payment.VoidReason); // one matrix, one reason
        Booking b = await w.LoadBooking(occurrence.Id.Value, member);
        Assert.Equal(BookingStatus.Confirmed, b.Status);
        Assert.Equal(2, b.StatusVersion);
        // CHANGED in P1 (D12, intentional): Group un-check-in no longer zeroes the price — same as Individual.
        Assert.Equal(15m, b.Amount);
        Assert.Equal(15m, b.SuggestedAmount);
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
    public async Task Group_CompletedToConfirmed_KeepsManualPaymentsAsPrepayment_OnTheKeptPrice()
    {
        await using SchedulingWorld w = await SchedulingWorld.Create(nameof(Group_CompletedToConfirmed_KeepsManualPaymentsAsPrepayment_OnTheKeptPrice));
        var svc = await w.AddGroupService();
        (Appointment occurrence, Client member) = await GroupOccurrence(w, svc);
        await w.SetBookingStatus(occurrence.Id.Value, member, BookingStatus.Completed); // checked in, unpaid
        Guid bookingId = (await w.LoadBooking(occurrence.Id.Value, member)).Id.Value;
        await w.PayBookingViaCheckout(bookingId, member, 5m); // a manual partial payment

        // P1 (D12): manual payments never block a correction on either form.
        await w.SetBookingStatus(occurrence.Id.Value, member, BookingStatus.Confirmed);

        Booking b = await w.LoadBooking(occurrence.Id.Value, member);
        Assert.Equal(15m, b.Amount); // P1: the price is kept
        Payment payment = Assert.Single(await w.LoadPayments(bookingId));
        Assert.Equal(PaymentStatus.Completed, payment.Status); // preserved (manual payments are never voided by a correction)
        Assert.Equal(5m, payment.Amount);
        BookingDto dto = (await w.Appointments.GetById(w.OrganizationId, occurrence.Id.Value)).Bookings.Single();
        Assert.Equal(5m, dto.PaidAmount);
        Assert.Equal(10m, dto.OutstandingAmount); // the kept price still owes the rest
    }

    [Fact]
    public async Task Group_NoShowToConfirmed_IsAllowed_AndAdvancesTheVersion()
    {
        await using SchedulingWorld w = await SchedulingWorld.Create(nameof(Group_NoShowToConfirmed_IsAllowed_AndAdvancesTheVersion));
        var svc = await w.AddGroupService();
        (Appointment occurrence, Client member) = await GroupOccurrence(w, svc);
        await w.MoveToPast(occurrence.Id.Value); // P1: a no-show needs a started segment
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
        // M1A: corrections re-open AUTOMATICALLY on every path — the member is Confirmed again, so the occurrence derives
        // Scheduled (it used to stay "Completed" because only the Individual path re-opened). The commission is untouched.
        Assert.Equal(AppointmentStatus.Scheduled, (await w.LoadAppointment(occurrence.Id.Value)).Status);
    }

    #endregion
}
