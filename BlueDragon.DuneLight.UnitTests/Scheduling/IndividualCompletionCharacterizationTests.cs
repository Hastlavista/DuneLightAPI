#nullable disable
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using BlueDragon.DuneLight.Core.DTOs.Appointments;
using BlueDragon.DuneLight.Core.Enums;
using BlueDragon.DuneLight.Core.Shared;
using BlueDragon.DuneLight.Core.Shared.Exceptions;
using BlueDragon.DuneLight.Infrastructure.Domain.Models.Appointments;
using BlueDragon.DuneLight.Infrastructure.Domain.Models.Catalog;
using BlueDragon.DuneLight.Infrastructure.Domain.Models.Checkouts;
using BlueDragon.DuneLight.Infrastructure.Domain.Models.Clients;
using BlueDragon.DuneLight.Infrastructure.Domain.Models.Commissions;
using BlueDragon.DuneLight.Infrastructure.Domain.Models.Employees;
using ServiceEntityAlias = BlueDragon.DuneLight.Infrastructure.Domain.Models.Catalog.Service;

namespace BlueDragon.DuneLight.UnitTests.Scheduling;

/// <summary>
/// CHARACTERIZATION (matrix K): completing INDIVIDUAL work. M1H: there is no appointment-wide "complete existing" command any
/// more — an existing appointment is completed PER PARTICIPATION (PATCH /participations/{id}/status with the settlement), and
/// "record work already done" is the atomic POS command CompleteNow (POST /complete: ONE explicit segment + clients). Both
/// run the same participation lifecycle core inside ONE transaction that writes the status (StatusVersion + audit), package
/// consumption, check-in payment and commission together. Completion never rewrites the schedule (segment, note, room).
/// </summary>
public class IndividualCompletionCharacterizationTests
{
    private static readonly DateTimeOffset LongValid = new(2035, 1, 1, 0, 0, 0, TimeSpan.Zero);

    private static async Task<AppointmentDto> CreateAndComplete(
        SchedulingWorld w, Action<TestCompletionSpec> mutate = null, PaymentMethod? method = null, Guid? packageId = null,
        decimal? settlementAmount = null, bool isPaid = true)
    {
        AppointmentDto created = await w.CreateAppointment(SchedulingWorld.Future(10));
        TestCompletionSpec request = w.CompleteRequest(SchedulingWorld.Future(10),
            paymentMethod: method, clientPackageId: packageId, settlementAmount: settlementAmount, isPaid: isPaid);
        mutate?.Invoke(request);
        return await w.CompleteParticipations(created.Id, request);
    }

    #region Completing existing participations — state, payment, commission, audit

    [Fact]
    public async Task CompletingParticipations_MovesTheAppointmentAndItsBookingToCompleted_AtBookingVersionOne()
    {
        await using SchedulingWorld w = await SchedulingWorld.Create(nameof(CompletingParticipations_MovesTheAppointmentAndItsBookingToCompleted_AtBookingVersionOne));

        AppointmentDto dto = await CreateAndComplete(w);

        Appointment a = await w.LoadAppointment(dto.Id);
        Assert.Equal(AppointmentStatus.Closed, a.Status);
        Booking b = Assert.Single(a.Bookings);
        Assert.Equal(BookingStatus.Completed, b.Status);
        Assert.Equal(1, b.StatusVersion);
        Assert.Equal(50m, b.Amount);
        Assert.Equal(w.ActorUserId, a.UpdatedBy);
        AppointmentAuditLog audit = Assert.Single(await w.LoadAuditLog(dto.Id), l => l.ChangeType == "BookingStatus");
        Assert.Equal("Confirmed", audit.OldValue);
        Assert.Equal("Completed", audit.NewValue);
        Assert.Equal(1, audit.StatusVersion);
        Assert.Equal(b.Id, audit.BookingId);
    }

    [Fact]
    public async Task CompletingParticipations_WithACashSettlement_CreatesOneCheckInGeneratedPaymentInAnAutoCheckout()
    {
        await using SchedulingWorld w = await SchedulingWorld.Create(nameof(CompletingParticipations_WithACashSettlement_CreatesOneCheckInGeneratedPaymentInAnAutoCheckout));

        AppointmentDto dto = await CreateAndComplete(w, method: PaymentMethod.Cash);

        Booking b = (await w.LoadAppointment(dto.Id)).Bookings.Single();
        Payment payment = Assert.Single(await w.LoadPayments(b.Id.Value));
        Assert.Equal(50m, payment.Amount);
        Assert.Equal(PaymentMethod.Cash, payment.Method);
        Assert.Equal(PaymentStatus.Completed, payment.Status);
        Assert.True(payment.IsCheckInGenerated);
        CheckoutItem item = Assert.Single(await w.LoadCheckoutItems(b.Id.Value));
        Assert.Equal(CheckoutItemType.Booking, item.Type);
        Assert.Equal(50m, item.Amount);
        Assert.False(item.LocksParticipation); // D3B3B: the marker is per participation
        Assert.Equal(50m, Assert.Single(item.Allocations).Amount);
        Checkout checkout = await w.LoadCheckout(item.CheckoutId);
        Assert.Equal(CheckoutStatus.Completed, checkout.Status);
        Assert.Equal(w.Client.Id, checkout.ClientId);
        Assert.Equal(w.Company.Id, checkout.CompanyId);
        Assert.Single(await w.LoadAuditLog(dto.Id), l => l.ChangeType == "PaymentCreated");
        BookingDto bookingDto = Assert.Single(dto.Bookings);
        Assert.Equal(50m, bookingDto.PaidAmount);
        Assert.Equal(0m, bookingDto.OutstandingAmount);
        Assert.True(bookingDto.IsPaid);
    }

    [Fact]
    public async Task CompletingParticipations_WithoutAPaymentMethod_LeavesTheBookingCompletedButUnpaid()
    {
        await using SchedulingWorld w = await SchedulingWorld.Create(nameof(CompletingParticipations_WithoutAPaymentMethod_LeavesTheBookingCompletedButUnpaid));

        AppointmentDto dto = await CreateAndComplete(w);

        Booking b = (await w.LoadAppointment(dto.Id)).Bookings.Single();
        Assert.Equal(BookingStatus.Completed, b.Status);
        Assert.Empty(await w.LoadPayments(b.Id.Value));
        Assert.Empty(await w.LoadCheckoutItems(b.Id.Value));
        BookingDto bookingDto = Assert.Single(dto.Bookings);
        Assert.Equal(0m, bookingDto.PaidAmount);
        Assert.Equal(50m, bookingDto.OutstandingAmount);
        Assert.False(bookingDto.IsPaid);
    }

    [Fact]
    public async Task CompletingParticipations_WithAMethodButIsPaidFalse_RecordsNoPayment()
    {
        await using SchedulingWorld w = await SchedulingWorld.Create(nameof(CompletingParticipations_WithAMethodButIsPaidFalse_RecordsNoPayment));

        AppointmentDto dto = await CreateAndComplete(w, method: PaymentMethod.Card, isPaid: false);

        Assert.Empty(await w.LoadPayments((await w.LoadAppointment(dto.Id)).Bookings.Single().Id.Value));
        Assert.False(Assert.Single(dto.Bookings).IsPaid);
    }

    [Fact]
    public async Task CompletingParticipations_ForAFreeBooking_NeverCreatesAPayment_AndIsSettledByDefinition()
    {
        await using SchedulingWorld w = await SchedulingWorld.Create(nameof(CompletingParticipations_ForAFreeBooking_NeverCreatesAPayment_AndIsSettledByDefinition));

        AppointmentDto dto = await CreateAndComplete(w, method: PaymentMethod.Cash, settlementAmount: 0m);

        Booking b = (await w.LoadAppointment(dto.Id)).Bookings.Single();
        Assert.Equal(0m, b.Amount);
        Assert.Empty(await w.LoadPayments(b.Id.Value));
        Assert.True(Assert.Single(dto.Bookings).IsPaid);
    }

    [Fact]
    public async Task CompletingParticipations_SettlementAmountOverride_IsStoredAsTheBookingAmount_PaidInFull_AndAudited()
    {
        await using SchedulingWorld w = await SchedulingWorld.Create(nameof(CompletingParticipations_SettlementAmountOverride_IsStoredAsTheBookingAmount_PaidInFull_AndAudited));

        AppointmentDto dto = await CreateAndComplete(w, method: PaymentMethod.Cash, settlementAmount: 40m);

        Booking b = (await w.LoadAppointment(dto.Id)).Bookings.Single();
        Assert.Equal(40m, b.Amount);
        Assert.Equal(50m, b.SuggestedAmount);
        Assert.True(b.IsAmountManuallyOverridden);
        Assert.Equal(40m, Assert.Single(await w.LoadPayments(b.Id.Value)).Amount);
        Assert.Single(await w.LoadAuditLog(dto.Id), l => l.ChangeType == "Amount");
    }

    [Fact]
    public async Task CompletingParticipations_GeneratesOneEarnedCommissionEntryPerBooking_FromTheBookingAmount()
    {
        await using SchedulingWorld w = await SchedulingWorld.Create(nameof(CompletingParticipations_GeneratesOneEarnedCommissionEntryPerBooking_FromTheBookingAmount));
        await w.AddCommissionRule(w.Employee, w.Service, CommissionCalculationType.Percentage, 10m);

        AppointmentDto dto = await CreateAndComplete(w);

        CommissionEntry entry = Assert.Single(await w.LoadCommissionEntries());
        Assert.Equal(CommissionSourceType.IndividualService, entry.SourceType);
        Assert.Equal(CommissionEntryStatus.Earned, entry.Status);
        Assert.Equal(w.Employee.Id, entry.EmployeeId);
        Assert.Equal(w.Company.Id, entry.CompanyId);
        Assert.Equal(dto.Id, entry.AppointmentId);
        Assert.Equal(dto.Bookings.Single().Id, entry.BookingId);
        Assert.Equal(50m, entry.BaseAmount);
        Assert.Equal(5m, entry.CommissionAmount);
        Assert.Equal(1, entry.SourceVersion); // = Booking.StatusVersion after the transition
    }

    [Fact]
    public async Task CompletingParticipations_WithoutACommissionRule_CreatesNoCommissionEntry()
    {
        await using SchedulingWorld w = await SchedulingWorld.Create(nameof(CompletingParticipations_WithoutACommissionRule_CreatesNoCommissionEntry));

        await CreateAndComplete(w);

        Assert.Empty(await w.LoadCommissionEntries());
    }

    [Fact]
    public async Task CompletingParticipations_DoesNotEmitOutboxEventsOrNotifications()
    {
        await using SchedulingWorld w = await SchedulingWorld.Create(nameof(CompletingParticipations_DoesNotEmitOutboxEventsOrNotifications));

        await CreateAndComplete(w, method: PaymentMethod.Cash);

        Assert.Empty(await w.LoadOutbox());
        Assert.Empty(await w.LoadNotifications());
    }

    [Fact]
    public async Task CompletingParticipations_ARepeatedCompletion_IsIdempotent()
    {
        await using SchedulingWorld w = await SchedulingWorld.Create(nameof(CompletingParticipations_ARepeatedCompletion_IsIdempotent));
        AppointmentDto dto = await CreateAndComplete(w, method: PaymentMethod.Cash);

        // CHANGED in M1H: completion is per participation and idempotent (the removed appointment-wide command answered
        // ALREADY_COMPLETED) — a repeat changes nothing.
        await w.CompleteParticipations(dto.Id, w.CompleteRequest(SchedulingWorld.Future(10), paymentMethod: PaymentMethod.Cash));

        Assert.Single(await w.LoadPayments(dto.Bookings.Single().Id)); // no second payment
        Assert.Equal(1, (await w.LoadBooking(dto.Id, w.Client)).StatusVersion);
    }

    [Fact]
    public async Task GroupAttendancePath_OnAnIndividualAppointment_IsRejected_IndividualWorkIsCompletedPerParticipation()
    {
        await using SchedulingWorld w = await SchedulingWorld.Create(nameof(GroupAttendancePath_OnAnIndividualAppointment_IsRejected_IndividualWorkIsCompletedPerParticipation));
        AppointmentDto created = await w.CreateAppointment(SchedulingWorld.Future(10));

        await SchedulingAssert.Validation(() => w.Bookings.SetStatusOnSegment(w.OrganizationId, w.ActorUserId, true, created.Id, w.Client.Id.Value,
            new BookingSetStatusRequest { Status = BookingStatus.Completed, SegmentId = created.Segments[0].Id }));

        Assert.Equal(BookingStatus.Confirmed, (await w.LoadBooking(created.Id, w.Client)).Status);
    }

    #endregion

    #region CompleteNow

    [Fact]
    public async Task CompleteNow_CreatesACompletedAppointmentAndCompletedBookingsInOneStep()
    {
        await using SchedulingWorld w = await SchedulingWorld.Create(nameof(CompleteNow_CreatesACompletedAppointmentAndCompletedBookingsInOneStep));

        AppointmentDto dto = await w.CompleteNew(w.CompleteRequest(SchedulingWorld.Past(10), paymentMethod: PaymentMethod.Card));

        Appointment a = await w.LoadAppointment(dto.Id);
        Assert.Equal(AppointmentStatus.Closed, a.Status);
        Assert.Equal(AppointmentForm.Individual, a.Form);
        Assert.Equal(w.ActorUserId, a.CreatedBy);
        Booking b = Assert.Single(a.Bookings);
        Assert.Equal(BookingStatus.Completed, b.Status);
        Payment payment = Assert.Single(await w.LoadPayments(b.Id.Value));
        Assert.Equal(PaymentMethod.Card, payment.Method);
        Assert.True(payment.IsCheckInGenerated);
        // CHANGED in M1H: CompleteNow runs the participation lifecycle core — one audited Confirmed -> Completed transition
        // (StatusVersion 1), exactly like completing an existing participation.
        Assert.Equal(1, b.StatusVersion);
        AppointmentAuditLog status = Assert.Single(await w.LoadAuditLog(dto.Id), l => l.ChangeType == "BookingStatus");
        Assert.Equal(("Confirmed", "Completed"), (status.OldValue, status.NewValue));
    }

    [Fact]
    public async Task CompleteNow_GeneratesCommissionAtSourceVersionOne_ThroughTheParticipationLifecycle()
    {
        await using SchedulingWorld w = await SchedulingWorld.Create(nameof(CompleteNow_GeneratesCommissionAtSourceVersionOne_ThroughTheParticipationLifecycle));
        await w.AddCommissionRule(w.Employee, w.Service, CommissionCalculationType.Fixed, 7m);

        await w.CompleteNew(w.CompleteRequest(SchedulingWorld.Past(10)));

        CommissionEntry entry = Assert.Single(await w.LoadCommissionEntries());
        Assert.Equal(7m, entry.CommissionAmount);
        Assert.Equal(1, entry.SourceVersion); // CHANGED in M1H (was 0: the participation was created already Completed)
    }

    [Fact]
    public async Task CompleteNow_WithoutAnySettlementForTheClient_IsAValidationError()
    {
        await using SchedulingWorld w = await SchedulingWorld.Create(nameof(CompleteNow_WithoutAnySettlementForTheClient_IsAValidationError));
        TestCompletionSpec request = w.CompleteRequest(SchedulingWorld.Past(10));
        request.Settlements.Clear();

        await SchedulingAssert.Validation(() => w.CompleteNew(request));

        Assert.Equal(0, await w.CountAppointments());
    }

    [Fact]
    public async Task CompleteNow_WithTwoSettlementsForTheSameClient_IsAValidationError()
    {
        await using SchedulingWorld w = await SchedulingWorld.Create(nameof(CompleteNow_WithTwoSettlementsForTheSameClient_IsAValidationError));
        TestCompletionSpec request = w.CompleteRequest(SchedulingWorld.Past(10));
        request.Settlements.Add(new AppointmentCompletedClientRequest { ClientId = w.Client.Id.Value });

        await SchedulingAssert.Validation(() => w.CompleteNew(request));
    }

    [Fact]
    public async Task CompleteNow_MixedSettlementPerClient_OneOnPackageOneOnCard()
    {
        await using SchedulingWorld w = await SchedulingWorld.Create(nameof(CompleteNow_MixedSettlementPerClient_OneOnPackageOneOnCard));
        Client payer = await w.AddClient("Payer", "Client");
        ClientPackage package = await w.AddClientPackage(w.Client, w.Service, 5, LongValid);
        await w.AddCommissionRule(w.Employee, w.Service, CommissionCalculationType.Percentage, 10m);

        AppointmentDto dto = await w.CompleteNew(w.CompleteRequest(SchedulingWorld.Past(10), settlements: new[]
        {
            new AppointmentCompletedClientRequest { ClientId = w.Client.Id.Value, ClientPackageId = package.Id },
            new AppointmentCompletedClientRequest { ClientId = payer.Id.Value, PaymentMethod = PaymentMethod.Card }
        }));

        Appointment a = await w.LoadAppointment(dto.Id);
        Booking packageBooking = a.Bookings.Single(b => b.ClientId == w.Client.Id);
        Booking cardBooking = a.Bookings.Single(b => b.ClientId == payer.Id);
        Assert.True(packageBooking.PackageCoverageApplied);
        Assert.Empty(await w.LoadPayments(packageBooking.Id.Value)); // a package is an entitlement, never a Payment
        Assert.False(cardBooking.PackageCoverageApplied);
        Assert.Equal(50m, Assert.Single(await w.LoadPayments(cardBooking.Id.Value)).Amount);
        // Settlement is per Booking: same appointment, two different settlement kinds, one commission entry each.
        Assert.Equal(2, (await w.LoadCommissionEntries()).Count);
        Assert.All(dto.Bookings, b => Assert.True(b.IsPaid));
    }

    #endregion

    #region Package settlement

    [Fact]
    public async Task CompletingParticipations_WithAnEligiblePackage_AppliesCoverage_DeductsOneEntry_AndCreatesNoPayment()
    {
        await using SchedulingWorld w = await SchedulingWorld.Create(nameof(CompletingParticipations_WithAnEligiblePackage_AppliesCoverage_DeductsOneEntry_AndCreatesNoPayment));
        ClientPackage package = await w.AddClientPackage(w.Client, w.Service, 5, LongValid);

        // A payment method sent alongside a package is IGNORED (the package settles the obligation).
        AppointmentDto dto = await CreateAndComplete(w, method: PaymentMethod.Cash, packageId: package.Id);

        Booking b = (await w.LoadAppointment(dto.Id)).Bookings.Single();
        Assert.Equal(package.Id, b.ClientPackageId);
        Assert.True(b.PackageCoverageApplied);
        Assert.False(b.PackageCoverageReturned);
        Assert.Null(b.CoverageType); // CoverageType is only populated by the group check-in path
        Assert.Equal(50m, b.Amount); // retail value stays; the obligation is settled by the entitlement, not zeroed
        Assert.Empty(await w.LoadPayments(b.Id.Value));
        Assert.Equal(4, (await w.LoadClientPackage(package.Id.Value)).ServiceEntries.Single().RemainingEntries);
        BookingDto bookingDto = Assert.Single(dto.Bookings);
        Assert.Equal(0m, bookingDto.OutstandingAmount);
        Assert.True(bookingDto.IsPaid);
        Assert.Equal(0m, bookingDto.PaidAmount); // paid amount counts money only
        // The participation lifecycle audits the coverage application (same as CompleteNow and the group check-in).
        Assert.Single(await w.LoadAuditLog(dto.Id), l => l.ChangeType == "BookingPackageCoverageApplied");
    }

    [Fact]
    public async Task CompleteNow_WithAnEligiblePackage_AuditsTheCoverageApplication()
    {
        await using SchedulingWorld w = await SchedulingWorld.Create(nameof(CompleteNow_WithAnEligiblePackage_AuditsTheCoverageApplication));
        ClientPackage package = await w.AddClientPackage(w.Client, w.Service, 5, LongValid);

        AppointmentDto dto = await w.CompleteNew(w.CompleteRequest(SchedulingWorld.Past(10), clientPackageId: package.Id));

        AppointmentAuditLog audit = Assert.Single(await w.LoadAuditLog(dto.Id), l => l.ChangeType == "BookingPackageCoverageApplied");
        Assert.Equal(package.Id.ToString(), audit.NewValue);
        Assert.Equal(dto.Bookings.Single().Id, audit.BookingId);
        Assert.Equal(4, (await w.LoadClientPackage(package.Id.Value)).ServiceEntries.Single().RemainingEntries);
    }

    [Fact]
    public async Task CompletingParticipations_PackageCoveredBooking_StillEarnsCommissionOnTheRetailAmount()
    {
        await using SchedulingWorld w = await SchedulingWorld.Create(nameof(CompletingParticipations_PackageCoveredBooking_StillEarnsCommissionOnTheRetailAmount));
        ClientPackage package = await w.AddClientPackage(w.Client, w.Service, 5, LongValid);
        await w.AddCommissionRule(w.Employee, w.Service, CommissionCalculationType.Percentage, 10m);

        await CreateAndComplete(w, packageId: package.Id);

        // CHANGED in T1 (T1-10): sesija pokrivena paketom i dalje zarađuje proviziju, ali od plaćene cijene paketa po jedinici
        // (100 € / 5 = 20 € → 10 % = 2 €), ne od maloprodajne cijene sesije (prije: 50 € → 5 €).
        Assert.Equal(2m, Assert.Single(await w.LoadCommissionEntries()).CommissionAmount);
    }

    #endregion

    #region Individual vs Group completion (matrix T)

    [Fact]
    public async Task CompleteGroupAppointment_ForAnIndividualAppointment_IsRejected()
    {
        await using SchedulingWorld w = await SchedulingWorld.Create(nameof(CompleteGroupAppointment_ForAnIndividualAppointment_IsRejected));
        AppointmentDto created = await w.CreateAppointment(SchedulingWorld.Future(10));

        await SchedulingAssert.Validation(() => w.Appointments.CompleteGroupAppointment(w.OrganizationId, w.ActorUserId, true, created.Id));
    }

    [Fact]
    public async Task CompleteGroupAppointment_WithUnresolvedConfirmedBookings_IsRecorded_ButTheOccurrenceStaysScheduled_AndWarns()
    {
        await using SchedulingWorld w = await SchedulingWorld.Create(nameof(CompleteGroupAppointment_WithUnresolvedConfirmedBookings_IsRecorded_ButTheOccurrenceStaysScheduled_AndWarns));
        Client second = await w.AddClient("Second", "Client");
        Appointment occurrence = await w.SeedAppointment(SchedulingWorld.Past(10), form: AppointmentForm.Group, employee: w.Employee,
            bookings: new[] { (w.Client, BookingStatus.Confirmed, 15m), (second, BookingStatus.Completed, 15m) });

        AppointmentDto dto = await w.Appointments.CompleteGroupAppointment(w.OrganizationId, w.ActorUserId, true, occurrence.Id.Value);

        // M1A: the close-out records that the session happened but does NOT set a status: one participation is still
        // Confirmed, so the occurrence derives Scheduled (it used to be forced to Completed). The warning still lists it.
        Appointment a = await w.LoadAppointment(occurrence.Id.Value);
        Assert.Equal(AppointmentStatus.Scheduled, a.Status);
        Assert.Equal(BookingStatus.Confirmed, a.Bookings.Single(b => b.ClientId == w.Client.Id).Status);
        Assert.Equal(BookingStatus.Completed, a.Bookings.Single(b => b.ClientId == second.Id).Status);
        AppointmentDto warning = dto;
        WarningDto unresolved = Assert.Single(warning.Warnings, x => x.Code == WarningCodes.GroupAppointmentUnresolvedBookings);
        WarningUnresolvedBookingsDetails details = Assert.IsType<WarningUnresolvedBookingsDetails>(unresolved.Details);
        Assert.Equal(w.Client.Id.Value, Assert.Single(details.ClientIds));
        Assert.DoesNotContain(await w.LoadAuditLog(occurrence.Id.Value), l => l.ChangeType == "Status");

        // Resolving the last Confirmed participation closes the occurrence through derivation.
        await w.SetBookingStatus(occurrence.Id.Value, w.Client, BookingStatus.NoShow);
        Assert.Equal(AppointmentStatus.Closed, (await w.LoadAppointment(occurrence.Id.Value)).Status);
        Assert.Single(await w.LoadAuditLog(occurrence.Id.Value), l => l.ChangeType == "Status" && l.OldValue == "Scheduled" && l.NewValue == "Closed");
    }

    [Fact]
    public async Task CompleteGroupAppointment_WithNoUnresolvedBookings_ReturnsNoWarning()
    {
        await using SchedulingWorld w = await SchedulingWorld.Create(nameof(CompleteGroupAppointment_WithNoUnresolvedBookings_ReturnsNoWarning));
        Appointment occurrence = await w.SeedAppointment(SchedulingWorld.Future(10), form: AppointmentForm.Group, employee: w.Employee,
            bookings: (w.Client, BookingStatus.Completed, 15m));

        AppointmentDto dto = await w.Appointments.CompleteGroupAppointment(w.OrganizationId, w.ActorUserId, true, occurrence.Id.Value);

        SchedulingAssert.HasNoWarnings(dto);
    }

    [Fact]
    public async Task CompleteGroupAppointment_DoesNotTouchAnyBooking_NoPaymentPackageOrVersionChange()
    {
        await using SchedulingWorld w = await SchedulingWorld.Create(nameof(CompleteGroupAppointment_DoesNotTouchAnyBooking_NoPaymentPackageOrVersionChange));
        Appointment occurrence = await w.SeedAppointment(SchedulingWorld.Future(10), form: AppointmentForm.Group, employee: w.Employee,
            bookings: (w.Client, BookingStatus.Confirmed, 15m));

        await w.Appointments.CompleteGroupAppointment(w.OrganizationId, w.ActorUserId, true, occurrence.Id.Value);

        Booking b = await w.LoadBooking(occurrence.Id.Value, w.Client);
        Assert.Equal(BookingStatus.Confirmed, b.Status);
        Assert.Equal(0, b.StatusVersion);
        Assert.Empty(await w.LoadPayments(b.Id.Value));
    }

    [Fact]
    public async Task CompleteGroupAppointment_Repeated_IsIdempotent_AndACancelledOccurrenceIsRejected()
    {
        await using SchedulingWorld w = await SchedulingWorld.Create(nameof(CompleteGroupAppointment_Repeated_IsIdempotent_AndACancelledOccurrenceIsRejected));
        await w.AddCommissionRule(w.Employee, w.Service, CommissionCalculationType.Fixed, 20m);
        Appointment closed = await w.SeedAppointment(SchedulingWorld.Future(10), AppointmentStatus.Closed, AppointmentForm.Group, employee: w.Employee,
            bookings: (w.Client, BookingStatus.Completed, 15m));
        Appointment cancelled = await w.SeedAppointment(SchedulingWorld.Future(12), AppointmentStatus.Cancelled, AppointmentForm.Group, employee: w.Employee);

        // M1A: there is no "Completed" appointment to refuse a second close-out (and a Closed occurrence — e.g. every member
        // already NoShow — must still be closable, see the zero-attendee commission test). The close-out is idempotent:
        // the per-occurrence commission is earned exactly once; the status stays derived (Closed).
        await w.Appointments.CompleteGroupAppointment(w.OrganizationId, w.ActorUserId, true, closed.Id.Value);
        await w.Appointments.CompleteGroupAppointment(w.OrganizationId, w.ActorUserId, true, closed.Id.Value);
        Assert.Single(await w.LoadCommissionEntries());
        Assert.Equal(AppointmentStatus.Closed, (await w.LoadAppointment(closed.Id.Value)).Status);

        await SchedulingAssert.BusinessRule(ErrorCodes.AppointmentNotMovable,
            () => w.Appointments.CompleteGroupAppointment(w.OrganizationId, w.ActorUserId, true, cancelled.Id.Value));
    }

    [Fact]
    public async Task CompletingACancelledParticipation_IsACorrection_ThatEarnsAndPaysLikeAnyCompletion()
    {
        await using SchedulingWorld w = await SchedulingWorld.Create(nameof(CompletingACancelledParticipation_IsACorrection_ThatEarnsAndPaysLikeAnyCompletion));
        await w.AddCommissionRule(w.Employee, w.Service, CommissionCalculationType.Percentage, 10m);
        AppointmentDto created = await w.CreateAppointment(SchedulingWorld.Future(10));
        await w.Appointments.Cancel(w.OrganizationId, w.ActorUserId, true, created.Id, SchedulingWorld.BusinessCancel("cancelled"));

        // CHANGED in P1 (D12, intentional): one matrix — Cancelled -> Completed is a correction (re-checks the schedule claim,
        // then the normal completion effects with the new source version). It used to have no path at all.
        await w.CompleteParticipations(created.Id, w.CompleteRequest(SchedulingWorld.Future(10), paymentMethod: PaymentMethod.Cash));

        Booking b = (await w.LoadAppointment(created.Id)).Bookings.Single();
        Assert.Equal((BookingStatus.Completed, 2), (b.Status, b.StatusVersion));
        Assert.Null(b.Participations.Single().CancellationInitiator); // metadata matches the status
        Assert.Equal(PaymentStatus.Completed, Assert.Single(await w.LoadPayments(b.Id.Value)).Status);
        CommissionEntry entry = Assert.Single(await w.LoadCommissionEntries());
        Assert.Equal((CommissionEntryStatus.Earned, 2), (entry.Status, entry.SourceVersion));
        Assert.Equal(AppointmentStatus.Closed, (await w.LoadAppointment(created.Id)).Status);
    }

    #endregion

    #region Authorization

    [Fact]
    public async Task CompletingAParticipation_OwnScopeCaller_CannotCompleteAnotherEmployeesAppointment()
    {
        await using SchedulingWorld w = await SchedulingWorld.Create(nameof(CompletingAParticipation_OwnScopeCaller_CannotCompleteAnotherEmployeesAppointment));
        Employee other = await w.AddEmployee("Other");
        AppointmentDto created = await w.CreateAppointment(SchedulingWorld.Future(10));

        // CHANGED in T1: 403 OutOfScope (bilo 409 NOT_OWNER)
        await SchedulingAssert.OutOfScope(() => w.SetBookingStatus(created.Id, w.Client, BookingStatus.Completed, hasFullScope: false, userId: other.UserId));

        Assert.Equal(AppointmentStatus.Scheduled, (await w.LoadAppointment(created.Id)).Status);
    }

    #endregion
}
