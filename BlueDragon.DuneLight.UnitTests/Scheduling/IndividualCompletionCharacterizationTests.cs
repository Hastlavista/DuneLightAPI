#nullable disable
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using BlueDragon.DuneLight.Core.DTOs.Appointments;
using BlueDragon.DuneLight.Core.Enums;
using BlueDragon.DuneLight.Core.Shared;
using BlueDragon.DuneLight.Infrastructure.Domain.Models.Appointments;
using BlueDragon.DuneLight.Infrastructure.Domain.Models.Catalog;
using BlueDragon.DuneLight.Infrastructure.Domain.Models.Checkouts;
using BlueDragon.DuneLight.Infrastructure.Domain.Models.Clients;
using BlueDragon.DuneLight.Infrastructure.Domain.Models.Commissions;
using BlueDragon.DuneLight.Infrastructure.Domain.Models.Employees;
using ServiceEntityAlias = BlueDragon.DuneLight.Infrastructure.Domain.Models.Catalog.Service;

namespace BlueDragon.DuneLight.UnitTests.Scheduling;

/// <summary>
/// CHARACTERIZATION (matrix K): completing an INDIVIDUAL appointment. Individual bookings cannot be completed one by one
/// (SetStatus rejects it); the appointment is completed as a whole with a per-client settlement list, either as
/// "record work already done" (CompleteNew, POST /complete) or by closing an existing Scheduled appointment
/// (CompleteExisting, PATCH /{id}/complete). Both run inside ONE transaction that writes Booking status, package
/// deduction, check-in payment, commission and audit together.
///
/// CompleteExisting is also a full replace of the appointment frame (service, employee, company, room, start, duration,
/// note) and reconciles Bookings against the client list — see the reconciliation tests and the asymmetry region.
/// </summary>
public class IndividualCompletionCharacterizationTests
{
    private static readonly DateTimeOffset LongValid = new(2035, 1, 1, 0, 0, 0, TimeSpan.Zero);

    private static async Task<AppointmentDto> CreateAndComplete(
        SchedulingWorld w, Action<AppointmentCompleteRequest> mutate = null, PaymentMethod? method = null, Guid? packageId = null,
        decimal? settlementAmount = null, bool isPaid = true)
    {
        AppointmentDto created = await w.CreateAppointment(SchedulingWorld.Future(10));
        AppointmentCompleteRequest request = w.CompleteRequest(SchedulingWorld.Future(10),
            paymentMethod: method, clientPackageId: packageId, settlementAmount: settlementAmount, isPaid: isPaid);
        mutate?.Invoke(request);
        return await w.CompleteExisting(created.Id, request);
    }

    #region CompleteExisting — state, payment, commission, audit

    [Fact]
    public async Task CompleteExisting_MovesTheAppointmentAndItsBookingToCompleted_AtBookingVersionOne()
    {
        await using SchedulingWorld w = await SchedulingWorld.Create(nameof(CompleteExisting_MovesTheAppointmentAndItsBookingToCompleted_AtBookingVersionOne));

        AppointmentDto dto = await CreateAndComplete(w);

        Appointment a = await w.LoadAppointment(dto.Id);
        Assert.Equal(AppointmentStatus.Completed, a.Status);
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
    public async Task CompleteExisting_WithACashSettlement_CreatesOneCheckInGeneratedPaymentInAnAutoCheckout()
    {
        await using SchedulingWorld w = await SchedulingWorld.Create(nameof(CompleteExisting_WithACashSettlement_CreatesOneCheckInGeneratedPaymentInAnAutoCheckout));

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
    public async Task CompleteExisting_WithoutAPaymentMethod_LeavesTheBookingCompletedButUnpaid()
    {
        await using SchedulingWorld w = await SchedulingWorld.Create(nameof(CompleteExisting_WithoutAPaymentMethod_LeavesTheBookingCompletedButUnpaid));

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
    public async Task CompleteExisting_WithAMethodButIsPaidFalse_RecordsNoPayment()
    {
        await using SchedulingWorld w = await SchedulingWorld.Create(nameof(CompleteExisting_WithAMethodButIsPaidFalse_RecordsNoPayment));

        AppointmentDto dto = await CreateAndComplete(w, method: PaymentMethod.Card, isPaid: false);

        Assert.Empty(await w.LoadPayments((await w.LoadAppointment(dto.Id)).Bookings.Single().Id.Value));
        Assert.False(Assert.Single(dto.Bookings).IsPaid);
    }

    [Fact]
    public async Task CompleteExisting_ForAFreeBooking_NeverCreatesAPayment_AndIsSettledByDefinition()
    {
        await using SchedulingWorld w = await SchedulingWorld.Create(nameof(CompleteExisting_ForAFreeBooking_NeverCreatesAPayment_AndIsSettledByDefinition));

        AppointmentDto dto = await CreateAndComplete(w, method: PaymentMethod.Cash, settlementAmount: 0m);

        Booking b = (await w.LoadAppointment(dto.Id)).Bookings.Single();
        Assert.Equal(0m, b.Amount);
        Assert.Empty(await w.LoadPayments(b.Id.Value));
        Assert.True(Assert.Single(dto.Bookings).IsPaid);
    }

    [Fact]
    public async Task CompleteExisting_SettlementAmountOverride_IsStoredAsTheBookingAmount_PaidInFull_AndAudited()
    {
        await using SchedulingWorld w = await SchedulingWorld.Create(nameof(CompleteExisting_SettlementAmountOverride_IsStoredAsTheBookingAmount_PaidInFull_AndAudited));

        AppointmentDto dto = await CreateAndComplete(w, method: PaymentMethod.Cash, settlementAmount: 40m);

        Booking b = (await w.LoadAppointment(dto.Id)).Bookings.Single();
        Assert.Equal(40m, b.Amount);
        Assert.Equal(50m, b.SuggestedAmount);
        Assert.True(b.IsAmountManuallyOverridden);
        Assert.Equal(40m, Assert.Single(await w.LoadPayments(b.Id.Value)).Amount);
        Assert.Single(await w.LoadAuditLog(dto.Id), l => l.ChangeType == "Amount");
    }

    [Fact]
    public async Task CompleteExisting_GeneratesOneEarnedCommissionEntryPerBooking_FromTheBookingAmount()
    {
        await using SchedulingWorld w = await SchedulingWorld.Create(nameof(CompleteExisting_GeneratesOneEarnedCommissionEntryPerBooking_FromTheBookingAmount));
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
    public async Task CompleteExisting_WithoutACommissionRule_CreatesNoCommissionEntry()
    {
        await using SchedulingWorld w = await SchedulingWorld.Create(nameof(CompleteExisting_WithoutACommissionRule_CreatesNoCommissionEntry));

        await CreateAndComplete(w);

        Assert.Empty(await w.LoadCommissionEntries());
    }

    [Fact]
    public async Task CompleteExisting_DoesNotEmitOutboxEventsOrNotifications()
    {
        await using SchedulingWorld w = await SchedulingWorld.Create(nameof(CompleteExisting_DoesNotEmitOutboxEventsOrNotifications));

        await CreateAndComplete(w, method: PaymentMethod.Cash);

        Assert.Empty(await w.LoadOutbox());
        Assert.Empty(await w.LoadNotifications());
    }

    [Fact]
    public async Task CompleteExisting_ARepeatedCompletion_IsRejected()
    {
        await using SchedulingWorld w = await SchedulingWorld.Create(nameof(CompleteExisting_ARepeatedCompletion_IsRejected));
        AppointmentDto dto = await CreateAndComplete(w, method: PaymentMethod.Cash);

        await SchedulingAssert.BusinessRule(ErrorCodes.AlreadyCompleted,
            () => w.CompleteExisting(dto.Id, w.CompleteRequest(SchedulingWorld.Future(10), paymentMethod: PaymentMethod.Cash)));

        Assert.Single(await w.LoadPayments(dto.Bookings.Single().Id)); // no second payment
    }

    [Fact]
    public async Task SetStatusCompleted_OnAnIndividualBooking_IsRejected_TheAppointmentIsCompletedAsAWhole()
    {
        await using SchedulingWorld w = await SchedulingWorld.Create(nameof(SetStatusCompleted_OnAnIndividualBooking_IsRejected_TheAppointmentIsCompletedAsAWhole));
        AppointmentDto created = await w.CreateAppointment(SchedulingWorld.Future(10));

        await SchedulingAssert.Validation(() => w.SetBookingStatus(created.Id, w.Client, BookingStatus.Completed));

        Assert.Equal(BookingStatus.Confirmed, (await w.LoadBooking(created.Id, w.Client)).Status);
    }

    #endregion

    #region CompleteExisting — full replace of the frame

    [Fact]
    public async Task CompleteExisting_RewritesTheFrameFromTheRequest_IncludingServiceAndEmployee_UnlikeUpdate()
    {
        await using SchedulingWorld w = await SchedulingWorld.Create(nameof(CompleteExisting_RewritesTheFrameFromTheRequest_IncludingServiceAndEmployee_UnlikeUpdate));
        ServiceEntityAlias service2 = await w.AddService(45, 80m);
        Employee employee2 = await w.AddEmployee("Second", serviceId: service2.Id);
        await w.AssignEmployeeToService(w.Employee, service2);
        AppointmentDto created = await w.CreateAppointment(SchedulingWorld.Future(10));

        AppointmentCompleteRequest request = w.CompleteRequest(SchedulingWorld.Future(14), employee: employee2, service: service2);
        AppointmentDto dto = await w.CompleteExisting(created.Id, request);

        // Contrast: the same change through Update is validated but NOT persisted for Service/Employee.
        Appointment a = await w.LoadAppointment(dto.Id);
        Assert.Equal(service2.Id, a.ServiceId);
        Assert.Equal(employee2.Id, a.EmployeeId);
        Assert.Equal(SchedulingWorld.Future(14), a.StartsAt);
        Assert.Equal(45, a.DurationMinutes);
        Assert.Equal(80m, a.Bookings.Single().Amount);
        Assert.DoesNotContain(await w.LoadAuditLog(dto.Id), l => l.ChangeType == "EmployeeId"); // and no employee audit row here
    }

    [Fact]
    public async Task CompleteExisting_WithoutARoomInTheRequest_ClearsTheAppointmentsRoom()
    {
        await using SchedulingWorld w = await SchedulingWorld.Create(nameof(CompleteExisting_WithoutARoomInTheRequest_ClearsTheAppointmentsRoom));
        Room room = await w.AddRoom();
        AppointmentDto created = await w.CreateAppointment(SchedulingWorld.Future(10), room: room);

        await w.CompleteExisting(created.Id, w.CompleteRequest(SchedulingWorld.Future(10))); // room omitted

        Assert.Null((await w.LoadAppointment(created.Id)).RoomId);
    }

    [Fact]
    public async Task CompleteExisting_ReplacesTheNoteWithTheRequestNote()
    {
        await using SchedulingWorld w = await SchedulingWorld.Create(nameof(CompleteExisting_ReplacesTheNoteWithTheRequestNote));
        AppointmentDto created = await w.CreateAppointment(w.CreateRequest(SchedulingWorld.Future(10), note: "booked note"));

        await w.CompleteExisting(created.Id, w.CompleteRequest(SchedulingWorld.Future(10)));

        Assert.Null((await w.LoadAppointment(created.Id)).Note);
    }

    #endregion

    #region CompleteNew

    [Fact]
    public async Task CompleteNew_CreatesACompletedAppointmentAndCompletedBookingsInOneStep()
    {
        await using SchedulingWorld w = await SchedulingWorld.Create(nameof(CompleteNew_CreatesACompletedAppointmentAndCompletedBookingsInOneStep));

        AppointmentDto dto = await w.CompleteNew(w.CompleteRequest(SchedulingWorld.Past(10), paymentMethod: PaymentMethod.Card));

        Appointment a = await w.LoadAppointment(dto.Id);
        Assert.Equal(AppointmentStatus.Completed, a.Status);
        Assert.Equal(AppointmentForm.Individual, a.Form);
        Assert.Equal(w.ActorUserId, a.CreatedBy);
        Booking b = Assert.Single(a.Bookings);
        Assert.Equal(BookingStatus.Completed, b.Status);
        Payment payment = Assert.Single(await w.LoadPayments(b.Id.Value));
        Assert.Equal(PaymentMethod.Card, payment.Method);
        Assert.True(payment.IsCheckInGenerated);
        // No status transition was audited: CompleteNew creates the Booking already Completed, there was no transition.
        Assert.DoesNotContain(await w.LoadAuditLog(dto.Id), l => l.ChangeType == "BookingStatus");
    }

    [Fact]
    public async Task CompleteNew_GeneratesCommissionAtSourceVersionZero()
    {
        await using SchedulingWorld w = await SchedulingWorld.Create(nameof(CompleteNew_GeneratesCommissionAtSourceVersionZero));
        await w.AddCommissionRule(w.Employee, w.Service, CommissionCalculationType.Fixed, 7m);

        await w.CompleteNew(w.CompleteRequest(SchedulingWorld.Past(10)));

        CommissionEntry entry = Assert.Single(await w.LoadCommissionEntries());
        Assert.Equal(7m, entry.CommissionAmount);
        Assert.Equal(0, entry.SourceVersion);
    }

    [Fact]
    public async Task CompleteNew_WithoutAnySettlementForTheClient_IsAValidationError()
    {
        await using SchedulingWorld w = await SchedulingWorld.Create(nameof(CompleteNew_WithoutAnySettlementForTheClient_IsAValidationError));
        AppointmentCompleteRequest request = w.CompleteRequest(SchedulingWorld.Past(10));
        request.Settlements.Clear();

        await SchedulingAssert.Validation(() => w.CompleteNew(request));

        Assert.Equal(0, await w.CountAppointments());
    }

    [Fact]
    public async Task CompleteNew_WithASettlementForAClientNotOnTheAppointment_IsAValidationError()
    {
        await using SchedulingWorld w = await SchedulingWorld.Create(nameof(CompleteNew_WithASettlementForAClientNotOnTheAppointment_IsAValidationError));
        Client stranger = await w.AddClient("Stranger", "Client");
        AppointmentCompleteRequest request = w.CompleteRequest(SchedulingWorld.Past(10));
        request.Settlements[0].ClientId = stranger.Id.Value;

        await SchedulingAssert.Validation(() => w.CompleteNew(request));
    }

    [Fact]
    public async Task CompleteNew_WithTwoSettlementsForTheSameClient_IsAValidationError()
    {
        await using SchedulingWorld w = await SchedulingWorld.Create(nameof(CompleteNew_WithTwoSettlementsForTheSameClient_IsAValidationError));
        AppointmentCompleteRequest request = w.CompleteRequest(SchedulingWorld.Past(10));
        request.Settlements.Add(new AppointmentClientSettlement { ClientId = w.Client.Id.Value });

        await SchedulingAssert.Validation(() => w.CompleteNew(request));
    }

    [Fact]
    public async Task CompleteNew_MixedSettlementPerClient_OneOnPackageOneOnCard()
    {
        await using SchedulingWorld w = await SchedulingWorld.Create(nameof(CompleteNew_MixedSettlementPerClient_OneOnPackageOneOnCard));
        Client payer = await w.AddClient("Payer", "Client");
        ClientPackage package = await w.AddClientPackage(w.Client, w.Service, 5, LongValid);
        await w.AddCommissionRule(w.Employee, w.Service, CommissionCalculationType.Percentage, 10m);

        AppointmentDto dto = await w.CompleteNew(w.CompleteRequest(SchedulingWorld.Past(10), settlements: new[]
        {
            new AppointmentClientSettlement { ClientId = w.Client.Id.Value, ClientPackageId = package.Id },
            new AppointmentClientSettlement { ClientId = payer.Id.Value, PaymentMethod = PaymentMethod.Card }
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
    public async Task CompleteExisting_WithAnEligiblePackage_AppliesCoverage_DeductsOneEntry_AndCreatesNoPayment()
    {
        await using SchedulingWorld w = await SchedulingWorld.Create(nameof(CompleteExisting_WithAnEligiblePackage_AppliesCoverage_DeductsOneEntry_AndCreatesNoPayment));
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
        // FINDING: unlike CompleteNew and the group check-in, CompleteExisting writes NO "BookingPackageCoverageApplied"
        // audit row — the coverage is only visible through the Booking columns and the package counter.
        Assert.DoesNotContain(await w.LoadAuditLog(dto.Id), l => l.ChangeType == "BookingPackageCoverageApplied");
    }

    [Fact]
    public async Task CompleteNew_WithAnEligiblePackage_AuditsTheCoverageApplication()
    {
        await using SchedulingWorld w = await SchedulingWorld.Create(nameof(CompleteNew_WithAnEligiblePackage_AuditsTheCoverageApplication));
        ClientPackage package = await w.AddClientPackage(w.Client, w.Service, 5, LongValid);

        AppointmentDto dto = await w.CompleteNew(w.CompleteRequest(SchedulingWorld.Past(10), clientPackageId: package.Id));

        AppointmentAuditLog audit = Assert.Single(await w.LoadAuditLog(dto.Id), l => l.ChangeType == "BookingPackageCoverageApplied");
        Assert.Equal(package.Id.ToString(), audit.NewValue);
        Assert.Equal(dto.Bookings.Single().Id, audit.BookingId);
        Assert.Equal(4, (await w.LoadClientPackage(package.Id.Value)).ServiceEntries.Single().RemainingEntries);
    }

    [Fact]
    public async Task CompleteExisting_PackageCoveredBooking_StillEarnsCommissionOnTheRetailAmount()
    {
        await using SchedulingWorld w = await SchedulingWorld.Create(nameof(CompleteExisting_PackageCoveredBooking_StillEarnsCommissionOnTheRetailAmount));
        ClientPackage package = await w.AddClientPackage(w.Client, w.Service, 5, LongValid);
        await w.AddCommissionRule(w.Employee, w.Service, CommissionCalculationType.Percentage, 10m);

        await CreateAndComplete(w, packageId: package.Id);

        Assert.Equal(5m, Assert.Single(await w.LoadCommissionEntries()).CommissionAmount);
    }

    #endregion

    #region Reconciliation and the Individual/Group asymmetry (matrix T)

    [Fact]
    public async Task CompleteExisting_HardDeletesConfirmedBookingsOfClientsOmittedFromTheRequest()
    {
        await using SchedulingWorld w = await SchedulingWorld.Create(nameof(CompleteExisting_HardDeletesConfirmedBookingsOfClientsOmittedFromTheRequest));
        Client omitted = await w.AddClient("Omitted", "Client");
        AppointmentDto created = await w.CreateAppointment(SchedulingWorld.Future(10), extraClients: omitted);

        // Only the default client is listed: the still-Confirmed booking of the other client is not "left unresolved" —
        // it is deleted. (Contrast: a GROUP occurrence closes without touching unresolved bookings, see below.)
        AppointmentDto dto = await w.CompleteExisting(created.Id, w.CompleteRequest(SchedulingWorld.Future(10)));

        Appointment a = await w.LoadAppointment(dto.Id);
        Assert.Equal(AppointmentStatus.Completed, a.Status);
        Assert.Equal(w.Client.Id, Assert.Single(a.Bookings).ClientId);
        Assert.Empty(await w.LoadOutbox()); // and the deletion emits nothing
    }

    [Fact]
    public async Task CompleteExisting_ForAGroupOccurrence_IsRejected_GroupsCloseThroughCompleteGroup()
    {
        await using SchedulingWorld w = await SchedulingWorld.Create(nameof(CompleteExisting_ForAGroupOccurrence_IsRejected_GroupsCloseThroughCompleteGroup));
        Appointment occurrence = await w.SeedAppointment(SchedulingWorld.Future(10), form: AppointmentForm.Group, employee: w.Employee,
            bookings: (w.Client, BookingStatus.Confirmed, 15m));

        await SchedulingAssert.Validation(() => w.CompleteExisting(occurrence.Id.Value, w.CompleteRequest(SchedulingWorld.Future(10))));
    }

    [Fact]
    public async Task CompleteGroupAppointment_ForAnIndividualAppointment_IsRejected()
    {
        await using SchedulingWorld w = await SchedulingWorld.Create(nameof(CompleteGroupAppointment_ForAnIndividualAppointment_IsRejected));
        AppointmentDto created = await w.CreateAppointment(SchedulingWorld.Future(10));

        await SchedulingAssert.Validation(() => w.Appointments.CompleteGroupAppointment(w.OrganizationId, w.ActorUserId, true, created.Id));
    }

    [Fact]
    public async Task CompleteGroupAppointment_WithUnresolvedConfirmedBookings_StillCompletes_AndOnlyWarns()
    {
        await using SchedulingWorld w = await SchedulingWorld.Create(nameof(CompleteGroupAppointment_WithUnresolvedConfirmedBookings_StillCompletes_AndOnlyWarns));
        Client second = await w.AddClient("Second", "Client");
        Appointment occurrence = await w.SeedAppointment(SchedulingWorld.Future(10), form: AppointmentForm.Group, employee: w.Employee,
            bookings: new[] { (w.Client, BookingStatus.Confirmed, 15m), (second, BookingStatus.Completed, 15m) });

        AppointmentDto dto = await w.Appointments.CompleteGroupAppointment(w.OrganizationId, w.ActorUserId, true, occurrence.Id.Value);

        // The occurrence is closed although one Booking was never resolved; it stays Confirmed and a warning lists it.
        Appointment a = await w.LoadAppointment(occurrence.Id.Value);
        Assert.Equal(AppointmentStatus.Completed, a.Status);
        Assert.Equal(BookingStatus.Confirmed, a.Bookings.Single(b => b.ClientId == w.Client.Id).Status);
        Assert.Equal(BookingStatus.Completed, a.Bookings.Single(b => b.ClientId == second.Id).Status);
        AppointmentDto warning = dto;
        WarningDto unresolved = Assert.Single(warning.Warnings, x => x.Code == WarningCodes.GroupAppointmentUnresolvedBookings);
        WarningUnresolvedBookingsDetails details = Assert.IsType<WarningUnresolvedBookingsDetails>(unresolved.Details);
        Assert.Equal(w.Client.Id.Value, Assert.Single(details.ClientIds));
        Assert.Single(await w.LoadAuditLog(occurrence.Id.Value), l => l.ChangeType == "Status" && l.NewValue == "Completed");
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
    public async Task CompleteGroupAppointment_AlreadyCompleted_OrCancelled_IsRejected()
    {
        await using SchedulingWorld w = await SchedulingWorld.Create(nameof(CompleteGroupAppointment_AlreadyCompleted_OrCancelled_IsRejected));
        Appointment completed = await w.SeedAppointment(SchedulingWorld.Future(10), AppointmentStatus.Completed, AppointmentForm.Group, employee: w.Employee);
        Appointment cancelled = await w.SeedAppointment(SchedulingWorld.Future(12), AppointmentStatus.Cancelled, AppointmentForm.Group, employee: w.Employee);

        await SchedulingAssert.BusinessRule(ErrorCodes.AlreadyCompleted,
            () => w.Appointments.CompleteGroupAppointment(w.OrganizationId, w.ActorUserId, true, completed.Id.Value));
        await SchedulingAssert.BusinessRule(ErrorCodes.AppointmentNotMovable,
            () => w.Appointments.CompleteGroupAppointment(w.OrganizationId, w.ActorUserId, true, cancelled.Id.Value));
    }

    [Fact]
    public async Task CompleteExisting_OnACancelledIndividualAppointment_IsCurrentlyAllowed_AndResurrectsItsCancelledBookings()
    {
        await using SchedulingWorld w = await SchedulingWorld.Create(nameof(CompleteExisting_OnACancelledIndividualAppointment_IsCurrentlyAllowed_AndResurrectsItsCancelledBookings));
        await w.AddCommissionRule(w.Employee, w.Service, CommissionCalculationType.Percentage, 10m);
        AppointmentDto created = await w.CreateAppointment(SchedulingWorld.Future(10));
        await w.Appointments.Cancel(w.OrganizationId, w.ActorUserId, true, created.Id, new AppointmentCancelRequest { CancellationReason = "cancelled" });

        // FINDING: CompleteExisting only refuses an already-Completed appointment. A Cancelled one is completed:
        // the Cancelled Booking flips straight to Completed (version 1 -> 2), a payment and a commission are generated.
        AppointmentDto dto = await w.CompleteExisting(created.Id, w.CompleteRequest(SchedulingWorld.Future(10), paymentMethod: PaymentMethod.Cash));

        Appointment a = await w.LoadAppointment(created.Id);
        Assert.Equal(AppointmentStatus.Completed, a.Status);
        Booking b = a.Bookings.Single();
        Assert.Equal(BookingStatus.Completed, b.Status);
        Assert.Equal(2, b.StatusVersion);
        Assert.Single(await w.LoadPayments(b.Id.Value));
        Assert.Single(await w.LoadCommissionEntries());
        Assert.Equal(AppointmentStatus.Completed, dto.Status);
    }

    #endregion

    #region Authorization

    [Fact]
    public async Task CompleteExisting_OwnScopeCaller_CannotCompleteAnotherEmployeesAppointment()
    {
        await using SchedulingWorld w = await SchedulingWorld.Create(nameof(CompleteExisting_OwnScopeCaller_CannotCompleteAnotherEmployeesAppointment));
        Employee other = await w.AddEmployee("Other");
        AppointmentDto created = await w.CreateAppointment(SchedulingWorld.Future(10));

        await SchedulingAssert.BusinessRule(ErrorCodes.NotOwner,
            () => w.Appointments.CompleteExisting(w.OrganizationId, other.UserId, false, created.Id, w.CompleteRequest(SchedulingWorld.Future(10))));

        Assert.Equal(AppointmentStatus.Scheduled, (await w.LoadAppointment(created.Id)).Status);
    }

    #endregion
}
