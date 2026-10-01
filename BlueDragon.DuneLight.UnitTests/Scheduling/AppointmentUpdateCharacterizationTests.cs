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
using BlueDragon.DuneLight.Infrastructure.Domain.Models.Clients;
using BlueDragon.DuneLight.Infrastructure.Domain.Models.Employees;
using ServiceEntity = BlueDragon.DuneLight.Infrastructure.Domain.Models.Catalog.Service;

namespace BlueDragon.DuneLight.UnitTests.Scheduling;

/// <summary>
/// CHARACTERIZATION (matrix F): full edit, AppointmentService.Update (PUT /api/appointments/{id}).
///
/// Update is a FULL replace: the request re-states service, employee, company, room, clients, time, note and amount, and
/// the resulting state is re-validated with the same rules as Create (structural eligibility, workforce availability,
/// hard overlap with the appointment itself excluded). It re-snapshots duration from the LIVE Service and re-resolves
/// the price. Bookings are reconciled against ClientIds: non-terminal Bookings are re-priced, unlisted Confirmed Bookings
/// are hard-deleted, terminal Bookings are never touched.
///
/// Several tests below pin behaviour that looks unintended (marked "FINDING" in the test name or comment) — they document
/// today's behaviour so a refactor cannot change it silently; they are NOT endorsements. See the final report.
/// </summary>
public class AppointmentUpdateCharacterizationTests
{
    private static Task<AppointmentDto> Update(SchedulingWorld w, AppointmentDto current, Action<AppointmentUpdateRequest> mutate = null, bool hasFullScope = true, Guid? userId = null) =>
        w.Appointments.Update(w.OrganizationId, userId ?? w.ActorUserId, hasFullScope, current.Id, w.UpdateRequest(current, mutate));

    private static async Task<AppointmentDto> Reload(SchedulingWorld w, Guid id) => await w.Appointments.GetById(w.OrganizationId, id);

    #region Which fields can change

    [Fact]
    public async Task Update_CanChangeStartsAt()
    {
        await using SchedulingWorld w = await SchedulingWorld.Create(nameof(Update_CanChangeStartsAt));
        AppointmentDto created = await w.CreateAppointment(SchedulingWorld.Future(10));

        await Update(w, created, r => r.StartsAt = SchedulingWorld.Future(14));

        Appointment a = await w.LoadAppointment(created.Id);
        Assert.Equal(SchedulingWorld.Future(14), a.StartsAt);
        Assert.Equal(w.ActorUserId, a.UpdatedBy);
        Assert.NotNull(a.UpdatedAt);
        Assert.Equal(AppointmentStatus.Scheduled, a.Status);
    }

    [Fact]
    public async Task Update_ChangingTheService_IsValidatedButNotPersisted_WhileDurationAndPriceFollowTheRequestedService()
    {
        await using SchedulingWorld w = await SchedulingWorld.Create(nameof(Update_ChangingTheService_IsValidatedButNotPersisted_WhileDurationAndPriceFollowTheRequestedService));
        ServiceEntity longer = await w.AddService(45, 80m);
        await w.AssignEmployeeToService(w.Employee, longer);
        AppointmentDto created = await w.CreateAppointment(SchedulingWorld.Future(10));

        AppointmentDto updated = await Update(w, created, r => r.ServiceId = longer.Id.Value);

        // FINDING (likely defect, NOT fixed here): AppointmentService.Update validates the NEW service and re-derives
        // duration and price from it, but Appointment.ServiceId is not written — the row keeps the OLD service. The
        // appointment ends up with the old service, the new service's duration and the new service's price.
        // Cause: AppointmentHandler.GetWithBookingsForMutation loads the Service/Employee navigations, and
        // context.Appointments.Update(graph) lets the stale navigation win over the changed foreign key.
        Appointment a = await w.LoadAppointment(created.Id);
        Assert.Equal(w.Service.Id, a.ServiceId);
        Assert.Equal(w.Service.Id, updated.ServiceId);
        Assert.Equal(45, a.DurationMinutes);
        Booking b = a.Bookings.Single();
        Assert.Equal(80m, b.Amount);
        Assert.Equal(80m, b.SuggestedAmount);
    }

    [Fact]
    public async Task Update_ChangingTheService_StillRejectsAnEmployeeNotAuthorizedForTheRequestedService()
    {
        await using SchedulingWorld w = await SchedulingWorld.Create(nameof(Update_ChangingTheService_StillRejectsAnEmployeeNotAuthorizedForTheRequestedService));
        ServiceEntity other = await w.AddService(45, 80m); // the employee is NOT assigned to it
        AppointmentDto created = await w.CreateAppointment(SchedulingWorld.Future(10));

        await SchedulingAssert.BusinessRule(ErrorCodes.EmployeeNotAssignedToService,
            () => Update(w, created, r => r.ServiceId = other.Id.Value));
    }

    [Fact]
    public async Task Update_ChangingTheEmployee_IsValidatedAndAuditedButTheEmployeeIsNotPersisted()
    {
        await using SchedulingWorld w = await SchedulingWorld.Create(nameof(Update_ChangingTheEmployee_IsValidatedAndAuditedButTheEmployeeIsNotPersisted));
        Employee other = await w.AddEmployee("Other");
        AppointmentDto created = await w.CreateAppointment(SchedulingWorld.Future(10));

        AppointmentDto updated = await Update(w, created, r => r.EmployeeId = other.Id.Value);

        // FINDING (likely defect, NOT fixed here): same root cause as the Service case above — the audit log claims the
        // employee changed, availability/overlap were checked for the NEW employee, yet the row keeps the OLD one.
        Assert.Equal(w.Employee.Id, (await w.LoadAppointment(created.Id)).EmployeeId);
        Assert.Equal(w.Employee.Id, updated.EmployeeId);
        AppointmentAuditLog audit = Assert.Single(await w.LoadAuditLog(created.Id), l => l.ChangeType == "EmployeeId");
        Assert.Equal(w.Employee.Id.ToString(), audit.OldValue);
        Assert.Equal(other.Id.ToString(), audit.NewValue);
        Assert.Equal(w.ActorUserId, audit.ChangedBy);
        Assert.Null(audit.BookingId);
    }

    [Fact]
    public async Task Update_ChangingTheEmployee_ChecksAvailabilityAndOverlapOfTheRequestedEmployee()
    {
        await using SchedulingWorld w = await SchedulingWorld.Create(nameof(Update_ChangingTheEmployee_ChecksAvailabilityAndOverlapOfTheRequestedEmployee));
        Employee other = await w.AddEmployee("Other");
        Client busyClient = await w.AddClient("Busy", "Client");
        await w.CreateAppointment(SchedulingWorld.Future(10), client: busyClient, employee: other);
        AppointmentDto created = await w.CreateAppointment(SchedulingWorld.Future(10));

        // The requested employee is busy at that time, so the (never-persisted) reassignment is still rejected.
        await SchedulingAssert.BusinessRule(ErrorCodes.AppointmentOverlap, () => Update(w, created, r => r.EmployeeId = other.Id.Value));
    }

    [Fact]
    public async Task Update_WithTheSameEmployee_WritesNoEmployeeAuditRow()
    {
        await using SchedulingWorld w = await SchedulingWorld.Create(nameof(Update_WithTheSameEmployee_WritesNoEmployeeAuditRow));
        AppointmentDto created = await w.CreateAppointment(SchedulingWorld.Future(10));

        await Update(w, created, r => r.Note = "only the note changes");

        Assert.DoesNotContain(await w.LoadAuditLog(created.Id), l => l.ChangeType == "EmployeeId");
    }

    [Fact]
    public async Task Update_CanChangeCompany_WhenServiceEmployeeAndCompanyAreEligible()
    {
        await using SchedulingWorld w = await SchedulingWorld.Create(nameof(Update_CanChangeCompany_WhenServiceEmployeeAndCompanyAreEligible));
        Company other = await w.AddCompany("Second company");
        await w.MakeServiceAvailableAt(w.Service, other);
        await w.AssignEmployeeToCompany(w.Employee, other);
        AppointmentDto created = await w.CreateAppointment(SchedulingWorld.Future(10));

        await Update(w, created, r => r.CompanyId = other.Id.Value);

        Assert.Equal(other.Id, (await w.LoadAppointment(created.Id)).CompanyId);
    }

    [Fact]
    public async Task Update_CanSetAndClearTheRoom()
    {
        await using SchedulingWorld w = await SchedulingWorld.Create(nameof(Update_CanSetAndClearTheRoom));
        Room room = await w.AddRoom();
        AppointmentDto created = await w.CreateAppointment(SchedulingWorld.Future(10));

        AppointmentDto withRoom = await Update(w, created, r => r.RoomId = room.Id);
        Assert.Equal(room.Id, (await w.LoadAppointment(created.Id)).RoomId);

        // Contrast with Move: Update CAN clear the room (RoomId = null is meaningful here).
        await Update(w, withRoom, r => r.RoomId = null);
        Assert.Null((await w.LoadAppointment(created.Id)).RoomId);
    }

    [Fact]
    public async Task Update_CanChangeTheNote()
    {
        await using SchedulingWorld w = await SchedulingWorld.Create(nameof(Update_CanChangeTheNote));
        AppointmentDto created = await w.CreateAppointment(w.CreateRequest(SchedulingWorld.Future(10), note: "before"));

        await Update(w, created, r => r.Note = "after");

        Assert.Equal("after", (await w.LoadAppointment(created.Id)).Note);
    }

    [Fact]
    public async Task Update_AllFieldsAtOnce_AreFullyRevalidated_AndEverythingExceptServiceAndEmployeeIsPersisted()
    {
        await using SchedulingWorld w = await SchedulingWorld.Create(nameof(Update_AllFieldsAtOnce_AreFullyRevalidated_AndEverythingExceptServiceAndEmployeeIsPersisted));
        Company company2 = await w.AddCompany("Second company");
        ServiceEntity service2 = await w.AddService(45, 80m, companyId: company2.Id);
        Employee employee2 = await w.AddEmployee("Second", companyId: company2.Id, serviceId: service2.Id);
        Room room2 = await w.AddRoom(company2);
        Client client2 = await w.AddClient("Second", "Client");
        AppointmentDto created = await w.CreateAppointment(SchedulingWorld.Future(10));

        await Update(w, created, r =>
        {
            r.StartsAt = SchedulingWorld.Future(15);
            r.ServiceId = service2.Id.Value;
            r.EmployeeId = employee2.Id.Value;
            r.CompanyId = company2.Id.Value;
            r.RoomId = room2.Id;
            r.ClientIds = new List<Guid> { client2.Id.Value };
            r.Note = "everything changed";
            r.Amount = 65m;
        });

        Appointment a = await w.LoadAppointment(created.Id);
        Assert.Equal(SchedulingWorld.Future(15), a.StartsAt);
        Assert.Equal(company2.Id, a.CompanyId);
        Assert.Equal(room2.Id, a.RoomId);
        Assert.Equal("everything changed", a.Note);
        Assert.Equal(45, a.DurationMinutes); // taken from the requested (but not persisted) service
        Assert.Equal(w.Service.Id, a.ServiceId); // FINDING: not persisted, see the dedicated tests
        Assert.Equal(w.Employee.Id, a.EmployeeId); // FINDING: not persisted, see the dedicated tests
        Booking b = Assert.Single(a.Bookings); // the original client's Confirmed booking was removed, the new client added
        Assert.Equal(client2.Id, b.ClientId);
        Assert.Equal(BookingStatus.Confirmed, b.Status);
        Assert.Equal(65m, b.Amount);
        Assert.Equal(80m, b.SuggestedAmount);
        Assert.True(b.IsAmountManuallyOverridden);
    }

    #endregion

    #region Snapshot behaviour

    [Fact]
    public async Task Update_ReSnapshotsTheDurationFromTheLiveService()
    {
        await using SchedulingWorld w = await SchedulingWorld.Create(nameof(Update_ReSnapshotsTheDurationFromTheLiveService));
        AppointmentDto created = await w.CreateAppointment(SchedulingWorld.Future(10));
        Assert.Equal(30, (await w.LoadAppointment(created.Id)).DurationMinutes);
        await w.UpdateService(w.Service, durationMinutes: 60);

        // Nothing about the appointment changes in the request — yet the stored duration follows the service.
        AppointmentDto updated = await Update(w, created, r => r.Note = "touch");

        Assert.Equal(60, (await w.LoadAppointment(created.Id)).DurationMinutes);
        Assert.Equal(60, updated.DurationMinutes);
    }

    [Fact]
    public async Task Update_ReResolvesThePriceOfNonTerminalBookings_AndAuditsTheAmountChange()
    {
        await using SchedulingWorld w = await SchedulingWorld.Create(nameof(Update_ReResolvesThePriceOfNonTerminalBookings_AndAuditsTheAmountChange));
        AppointmentDto created = await w.CreateAppointment(SchedulingWorld.Future(10));
        await w.AddPriceListItem(w.Service, 70m, new DateTimeOffset(2030, 1, 1, 0, 0, 0, TimeSpan.Zero), companyId: w.Company.Id);

        await Update(w, created, r => r.Note = "touch");

        Booking b = (await w.LoadAppointment(created.Id)).Bookings.Single();
        Assert.Equal(70m, b.Amount);
        Assert.Equal(70m, b.SuggestedAmount);
        AppointmentAuditLog audit = Assert.Single(await w.LoadAuditLog(created.Id), l => l.ChangeType == "Amount");
        Assert.Equal(b.Id, audit.BookingId);
        Assert.Equal(50m, decimal.Parse(audit.OldValue, System.Globalization.CultureInfo.InvariantCulture));
        Assert.Equal(70m, decimal.Parse(audit.NewValue, System.Globalization.CultureInfo.InvariantCulture));
    }

    [Fact]
    public async Task Update_PriceIsResolvedForTheNewStartsAt()
    {
        await using SchedulingWorld w = await SchedulingWorld.Create(nameof(Update_PriceIsResolvedForTheNewStartsAt));
        await w.AddPriceListItem(w.Service, 90m, SchedulingWorld.FutureDay.AddDays(10), companyId: w.Company.Id);
        AppointmentDto created = await w.CreateAppointment(SchedulingWorld.Future(10));
        Assert.Equal(50m, (await w.LoadAppointment(created.Id)).Bookings.Single().Amount);

        await Update(w, created, r => r.StartsAt = SchedulingWorld.Future(10).AddDays(11));

        Assert.Equal(90m, (await w.LoadAppointment(created.Id)).Bookings.Single().Amount);
    }

    [Fact]
    public async Task Update_WithoutAnExplicitAmount_DropsAPreviousManualPriceOverride()
    {
        await using SchedulingWorld w = await SchedulingWorld.Create(nameof(Update_WithoutAnExplicitAmount_DropsAPreviousManualPriceOverride));
        AppointmentDto created = await w.CreateAppointment(w.CreateRequest(SchedulingWorld.Future(10), amount: 35m));
        Assert.True((await w.LoadAppointment(created.Id)).Bookings.Single().IsAmountManuallyOverridden);

        // FINDING: the override lives only on the Booking, and a full Update that does not resend Amount silently resets
        // it to the resolved price.
        await Update(w, created, r => r.Note = "touch");

        Booking b = (await w.LoadAppointment(created.Id)).Bookings.Single();
        Assert.Equal(50m, b.Amount);
        Assert.False(b.IsAmountManuallyOverridden);
    }

    [Fact]
    public async Task Update_WithAnExplicitAmount_AppliesThatSameAmountToEveryNonTerminalBooking()
    {
        await using SchedulingWorld w = await SchedulingWorld.Create(nameof(Update_WithAnExplicitAmount_AppliesThatSameAmountToEveryNonTerminalBooking));
        Client second = await w.AddClient("Second", "Client");
        AppointmentDto created = await w.CreateAppointment(SchedulingWorld.Future(10), extraClients: second);

        await Update(w, created, r => r.Amount = 42m);

        Appointment a = await w.LoadAppointment(created.Id);
        Assert.All(a.Bookings, b =>
        {
            Assert.Equal(42m, b.Amount);
            Assert.True(b.IsAmountManuallyOverridden);
        });
    }

    #endregion

    #region Booking reconciliation

    [Fact]
    public async Task Update_AddingAClient_CreatesAConfirmedBookingWithTheCurrentPrice()
    {
        await using SchedulingWorld w = await SchedulingWorld.Create(nameof(Update_AddingAClient_CreatesAConfirmedBookingWithTheCurrentPrice));
        Client added = await w.AddClient("Added", "Client");
        AppointmentDto created = await w.CreateAppointment(SchedulingWorld.Future(10));

        await Update(w, created, r => r.ClientIds.Add(added.Id.Value));

        Appointment a = await w.LoadAppointment(created.Id);
        Assert.Equal(2, a.Bookings.Count);
        Booking b = a.Bookings.Single(x => x.ClientId == added.Id);
        Assert.Equal(BookingStatus.Confirmed, b.Status);
        Assert.Equal(0, b.StatusVersion);
        Assert.Equal(50m, b.Amount);
    }

    [Fact]
    public async Task Update_OmittingAConfirmedClient_HardDeletesThatBooking()
    {
        await using SchedulingWorld w = await SchedulingWorld.Create(nameof(Update_OmittingAConfirmedClient_HardDeletesThatBooking));
        Client second = await w.AddClient("Second", "Client");
        AppointmentDto created = await w.CreateAppointment(SchedulingWorld.Future(10), extraClients: second);

        await Update(w, created, r => r.ClientIds = new List<Guid> { w.Client.Id.Value });

        Appointment a = await w.LoadAppointment(created.Id);
        Assert.Equal(w.Client.Id, Assert.Single(a.Bookings).ClientId);
        Assert.Empty(await w.LoadAuditLog(created.Id)); // the removal itself is not audited
    }

    [Theory]
    [InlineData(BookingStatus.Completed)]
    [InlineData(BookingStatus.Cancelled)]
    [InlineData(BookingStatus.NoShow)]
    public async Task Update_OmittingATerminalClient_PreservesThatBooking(BookingStatus terminal)
    {
        await using SchedulingWorld w = await SchedulingWorld.Create($"{nameof(Update_OmittingATerminalClient_PreservesThatBooking)}-{terminal}");
        Client active = await w.AddClient("Active", "Client");
        Appointment seeded = await w.SeedAppointment(SchedulingWorld.Future(10),
            bookings: new[] { (w.Client, terminal, 50m), (active, BookingStatus.Confirmed, 50m) });
        AppointmentDto current = await w.Appointments.GetById(w.OrganizationId, seeded.Id.Value);

        // The request lists only the still-active client; the terminal Booking is history and survives.
        await Update(w, current, r => r.ClientIds = new List<Guid> { active.Id.Value });

        Appointment a = await w.LoadAppointment(seeded.Id.Value);
        Assert.Equal(2, a.Bookings.Count);
        Assert.Equal(terminal, a.Bookings.Single(b => b.ClientId == w.Client.Id).Status);
    }

    [Fact]
    public async Task Update_DoesNotRePriceTerminalBookings_ButDoesRePriceTheConfirmedOnes()
    {
        await using SchedulingWorld w = await SchedulingWorld.Create(nameof(Update_DoesNotRePriceTerminalBookings_ButDoesRePriceTheConfirmedOnes));
        Client active = await w.AddClient("Active", "Client");
        Appointment seeded = await w.SeedAppointment(SchedulingWorld.Future(10),
            bookings: new[] { (w.Client, BookingStatus.Completed, 50m), (active, BookingStatus.Confirmed, 50m) });
        AppointmentDto current = await w.Appointments.GetById(w.OrganizationId, seeded.Id.Value);

        await Update(w, current, r => r.Amount = 20m);

        Appointment a = await w.LoadAppointment(seeded.Id.Value);
        Assert.Equal(50m, a.Bookings.Single(b => b.ClientId == w.Client.Id).Amount);
        Assert.Equal(20m, a.Bookings.Single(b => b.ClientId == active.Id).Amount);
        // Only the re-priced Booking is audited.
        Assert.Single(await w.LoadAuditLog(seeded.Id.Value), l => l.ChangeType == "Amount");
    }

    [Fact]
    public async Task Update_DoesNotChangeBookingStatusesOrStatusVersions()
    {
        await using SchedulingWorld w = await SchedulingWorld.Create(nameof(Update_DoesNotChangeBookingStatusesOrStatusVersions));
        AppointmentDto created = await w.CreateAppointment(SchedulingWorld.Future(10));

        await Update(w, created, r => r.StartsAt = SchedulingWorld.Future(12));

        Booking b = (await w.LoadAppointment(created.Id)).Bookings.Single();
        Assert.Equal(BookingStatus.Confirmed, b.Status);
        Assert.Equal(0, b.StatusVersion);
        Assert.Empty(await w.LoadOutbox());
        Assert.Empty(await w.LoadCommissionEntries());
    }

    #endregion

    #region Package / payment data survives an Update

    [Fact]
    public async Task Update_OnACompletedPackageCoveredAppointment_PreservesCoverageAndDoesNotTouchThePackage()
    {
        await using SchedulingWorld w = await SchedulingWorld.Create(nameof(Update_OnACompletedPackageCoveredAppointment_PreservesCoverageAndDoesNotTouchThePackage));
        ClientPackage package = await w.AddClientPackage(w.Client, w.Service, entries: 5, expiry: new DateTimeOffset(2035, 1, 1, 0, 0, 0, TimeSpan.Zero));
        AppointmentDto completed = await w.CompleteNew(w.CompleteRequest(SchedulingWorld.Past(10), clientPackageId: package.Id));
        Assert.Equal(4, (await w.LoadClientPackage(package.Id.Value)).ServiceEntries.Single().RemainingEntries);

        await Update(w, completed, r => r.Note = "edited after completion");

        Booking b = (await w.LoadAppointment(completed.Id)).Bookings.Single();
        Assert.Equal(BookingStatus.Completed, b.Status);
        Assert.Equal(package.Id, b.ClientPackageId);
        Assert.True(b.PackageCoverageApplied);
        Assert.False(b.PackageCoverageReturned);
        Assert.Equal(4, (await w.LoadClientPackage(package.Id.Value)).ServiceEntries.Single().RemainingEntries);
    }

    [Fact]
    public async Task Update_OnACompletedPaidAppointment_PreservesPaymentsAndCommissionAndAmount()
    {
        await using SchedulingWorld w = await SchedulingWorld.Create(nameof(Update_OnACompletedPaidAppointment_PreservesPaymentsAndCommissionAndAmount));
        await w.AddCommissionRule(w.Employee, w.Service, CommissionCalculationType.Percentage, 10m);
        AppointmentDto completed = await w.CompleteNew(w.CompleteRequest(SchedulingWorld.Past(10), paymentMethod: PaymentMethod.Cash));
        Guid bookingId = completed.Bookings.Single().Id;
        int paymentsBefore = (await w.LoadPayments(bookingId)).Count;
        int commissionsBefore = (await w.LoadCommissionEntries()).Count;
        Assert.Equal(1, paymentsBefore);
        Assert.Equal(1, commissionsBefore);

        await Update(w, completed, r => r.Amount = 5m);

        Booking b = (await w.LoadAppointment(completed.Id)).Bookings.Single();
        Assert.Equal(50m, b.Amount); // terminal Booking: not re-priced
        Assert.Equal(paymentsBefore, (await w.LoadPayments(bookingId)).Count);
        Assert.Equal(commissionsBefore, (await w.LoadCommissionEntries()).Count);
        Assert.Equal(PaymentStatus.Completed, (await w.LoadPayments(bookingId)).Single().Status);
    }

    [Fact]
    public async Task Update_RePricesAConfirmedBookingThatAlreadyHasAPartialManualPayment_KeepingThePayment()
    {
        await using SchedulingWorld w = await SchedulingWorld.Create(nameof(Update_RePricesAConfirmedBookingThatAlreadyHasAPartialManualPayment_KeepingThePayment));
        AppointmentDto created = await w.CreateAppointment(SchedulingWorld.Future(10));
        Guid bookingId = created.Bookings.Single().Id;
        await w.PayBookingViaCheckout(bookingId, w.Client, 20m);

        // FINDING: nothing stops a re-price of a Booking that already has money against it; the payment is kept and the
        // outstanding amount is simply recomputed from the new Amount.
        AppointmentDto updated = await Update(w, created, r => r.Amount = 30m);

        BookingDto booking = Assert.Single(updated.Bookings);
        Assert.Equal(30m, booking.Amount);
        Assert.Equal(20m, booking.PaidAmount);
        Assert.Equal(10m, booking.OutstandingAmount);
        Assert.Equal(20m, (await w.LoadPayments(bookingId)).Single().Amount);
    }

    #endregion

    #region Full revalidation

    [Fact]
    public async Task Update_MovingIntoAnEmployeeConflict_IsRejectedAndNothingIsChanged()
    {
        await using SchedulingWorld w = await SchedulingWorld.Create(nameof(Update_MovingIntoAnEmployeeConflict_IsRejectedAndNothingIsChanged));
        Client other = await w.AddClient("Other", "Client");
        AppointmentDto first = await w.CreateAppointment(SchedulingWorld.Future(10));
        await w.CreateAppointment(SchedulingWorld.Future(12), client: other);

        await SchedulingAssert.BusinessRule(ErrorCodes.AppointmentOverlap,
            () => Update(w, first, r => r.StartsAt = SchedulingWorld.Future(12, 15)));

        Assert.Equal(SchedulingWorld.Future(10), (await w.LoadAppointment(first.Id)).StartsAt);
    }

    [Fact]
    public async Task Update_TheAppointmentDoesNotConflictWithItself()
    {
        await using SchedulingWorld w = await SchedulingWorld.Create(nameof(Update_TheAppointmentDoesNotConflictWithItself));
        AppointmentDto created = await w.CreateAppointment(SchedulingWorld.Future(10));

        AppointmentDto updated = await Update(w, created, r => r.StartsAt = SchedulingWorld.Future(10, 15));

        Assert.Equal(SchedulingWorld.Future(10, 15), updated.StartsAt);
    }

    [Fact]
    public async Task Update_ANewEmployeeNotAuthorizedForTheService_IsRejected()
    {
        await using SchedulingWorld w = await SchedulingWorld.Create(nameof(Update_ANewEmployeeNotAuthorizedForTheService_IsRejected));
        Employee unauthorized = await w.AddEmployee(assignedToService: false);
        AppointmentDto created = await w.CreateAppointment(SchedulingWorld.Future(10));

        await SchedulingAssert.BusinessRule(ErrorCodes.EmployeeNotAssignedToService,
            () => Update(w, created, r => r.EmployeeId = unauthorized.Id.Value));

        Assert.Equal(w.Employee.Id, (await w.LoadAppointment(created.Id)).EmployeeId);
    }

    [Fact]
    public async Task Update_ARoomOfAnotherCompany_IsRejected()
    {
        await using SchedulingWorld w = await SchedulingWorld.Create(nameof(Update_ARoomOfAnotherCompany_IsRejected));
        Company other = await w.AddCompany("Other");
        Room foreign = await w.AddRoom(other);
        AppointmentDto created = await w.CreateAppointment(SchedulingWorld.Future(10));

        await SchedulingAssert.BusinessRule(ErrorCodes.RoomCompanyMismatch, () => Update(w, created, r => r.RoomId = foreign.Id));
    }

    [Fact]
    public async Task Update_AddingAnInactiveClient_IsRejected()
    {
        await using SchedulingWorld w = await SchedulingWorld.Create(nameof(Update_AddingAnInactiveClient_IsRejected));
        Client inactive = await w.AddClient("Inactive", "Client", isActive: false);
        AppointmentDto created = await w.CreateAppointment(SchedulingWorld.Future(10));

        await SchedulingAssert.BusinessRule(ErrorCodes.InactiveClient, () => Update(w, created, r => r.ClientIds.Add(inactive.Id.Value)));

        Assert.Single((await w.LoadAppointment(created.Id)).Bookings);
    }

    [Fact]
    public async Task Update_AddingAClientWhoIsBusyElsewhere_IsRejected()
    {
        await using SchedulingWorld w = await SchedulingWorld.Create(nameof(Update_AddingAClientWhoIsBusyElsewhere_IsRejected));
        Client busy = await w.AddClient("Busy", "Client");
        Employee other = await w.AddEmployee("Other");
        await w.CreateAppointment(SchedulingWorld.Future(10), client: busy, employee: other);
        AppointmentDto created = await w.CreateAppointment(SchedulingWorld.Future(10));

        await SchedulingAssert.BusinessRule(ErrorCodes.AppointmentOverlap, () => Update(w, created, r => r.ClientIds.Add(busy.Id.Value)));
    }

    [Fact]
    public async Task Update_UnknownAppointment_IsNotFound()
    {
        await using SchedulingWorld w = await SchedulingWorld.Create(nameof(Update_UnknownAppointment_IsNotFound));
        AppointmentDto created = await w.CreateAppointment(SchedulingWorld.Future(10));

        await SchedulingAssert.NotFound(() => w.Appointments.Update(
            w.OrganizationId, w.ActorUserId, true, Guid.NewGuid(), w.UpdateRequest(created)));
    }

    #endregion

    #region FINDING: Update has no lifecycle guard (audit finding confirmed)

    [Fact]
    public async Task Update_OnACompletedAppointment_IsAllowed_AndKeepsItCompleted()
    {
        await using SchedulingWorld w = await SchedulingWorld.Create(nameof(Update_OnACompletedAppointment_IsAllowed_AndKeepsItCompleted));
        AppointmentDto completed = await w.CompleteNew(w.CompleteRequest(SchedulingWorld.Past(10)));

        // FINDING: no status guard — a Completed (already paid/commissioned) appointment can be re-timed and its note edited.
        AppointmentDto updated = await Update(w, completed, r =>
        {
            r.StartsAt = SchedulingWorld.Past(14);
            r.Note = "edited after completion";
        });

        Appointment a = await w.LoadAppointment(completed.Id);
        Assert.Equal(AppointmentStatus.Closed, a.Status);
        Assert.Equal(SchedulingWorld.Past(14), a.StartsAt);
        Assert.Equal("edited after completion", a.Note);
        Assert.Equal(AppointmentStatus.Closed, updated.Status);
    }

    [Fact]
    public async Task Update_OnACancelledAppointment_IsAllowed_AndKeepsItCancelled()
    {
        await using SchedulingWorld w = await SchedulingWorld.Create(nameof(Update_OnACancelledAppointment_IsAllowed_AndKeepsItCancelled));
        AppointmentDto created = await w.CreateAppointment(SchedulingWorld.Future(10));
        AppointmentDto cancelled = await w.Appointments.Cancel(w.OrganizationId, w.ActorUserId, true, created.Id,
            new AppointmentCancelRequest { CancellationReason = "client cancelled" });

        // FINDING: Move rejects Cancelled appointments (APPOINTMENT_NOT_MOVABLE); Update does not.
        AppointmentDto updated = await Update(w, cancelled, r => r.StartsAt = SchedulingWorld.Future(16));

        Appointment a = await w.LoadAppointment(created.Id);
        Assert.Equal(AppointmentStatus.Cancelled, a.Status);
        Assert.Equal(SchedulingWorld.Future(16), a.StartsAt);
        Assert.Equal("client cancelled", a.CancellationReason);
        Assert.Equal(BookingStatus.Cancelled, a.Bookings.Single().Status);
        Assert.Equal(AppointmentStatus.Cancelled, updated.Status);
    }

    [Fact]
    public async Task Update_OnACancelledAppointment_StillRunsTheFullRevalidation()
    {
        await using SchedulingWorld w = await SchedulingWorld.Create(nameof(Update_OnACancelledAppointment_StillRunsTheFullRevalidation));
        Client other = await w.AddClient("Other", "Client");
        AppointmentDto created = await w.CreateAppointment(SchedulingWorld.Future(10));
        AppointmentDto cancelled = await w.Appointments.Cancel(w.OrganizationId, w.ActorUserId, true, created.Id, new AppointmentCancelRequest());
        await w.CreateAppointment(SchedulingWorld.Future(12), client: other);

        // A CANCELLED appointment is re-timed onto a slot occupied by a live one: rejected by the overlap rule.
        await SchedulingAssert.BusinessRule(ErrorCodes.AppointmentOverlap,
            () => Update(w, cancelled, r => r.StartsAt = SchedulingWorld.Future(12)));
    }

    [Fact]
    public async Task Update_OnAGroupOccurrence_HardDeletesConfirmedMemberBookingsMissingFromClientIds()
    {
        await using SchedulingWorld w = await SchedulingWorld.Create(nameof(Update_OnAGroupOccurrence_HardDeletesConfirmedMemberBookingsMissingFromClientIds));
        Client member = await w.AddClient("Member", "Client");
        Appointment occurrence = await w.SeedAppointment(SchedulingWorld.Future(10), form: AppointmentForm.Group, employee: w.Employee,
            bookings: new[] { (w.Client, BookingStatus.Confirmed, 50m), (member, BookingStatus.Confirmed, 50m) });
        AppointmentDto current = await w.Appointments.GetById(w.OrganizationId, occurrence.Id.Value);

        // FINDING: the individual-appointment edit reconciles Bookings by ClientIds with no Form guard, so on a Group
        // occurrence it deletes the group's Confirmed Bookings that the request omits (no cancellation, no outbox event,
        // no waitlist promotion, no audit row).
        await Update(w, current, r => r.ClientIds = new List<Guid> { w.Client.Id.Value });

        Appointment a = await w.LoadAppointment(occurrence.Id.Value);
        Assert.Equal(AppointmentForm.Group, a.Form);
        Assert.Equal(w.Client.Id, Assert.Single(a.Bookings).ClientId);
        Assert.Empty(await w.LoadOutbox());
    }

    #endregion

    #region Authorization scope on Update

    [Fact]
    public async Task Update_OwnScopeCaller_CanEditTheirOwnAppointment()
    {
        await using SchedulingWorld w = await SchedulingWorld.Create(nameof(Update_OwnScopeCaller_CanEditTheirOwnAppointment));
        AppointmentDto created = await w.CreateAppointment(SchedulingWorld.Future(10));

        AppointmentDto updated = await Update(w, created, r => r.Note = "mine", hasFullScope: false, userId: w.Employee.UserId);

        Assert.Equal("mine", updated.Note);
    }

    [Fact]
    public async Task Update_OwnScopeCaller_CannotEditAnotherEmployeesAppointment()
    {
        await using SchedulingWorld w = await SchedulingWorld.Create(nameof(Update_OwnScopeCaller_CannotEditAnotherEmployeesAppointment));
        Employee other = await w.AddEmployee("Other");
        AppointmentDto created = await w.CreateAppointment(SchedulingWorld.Future(10));

        await SchedulingAssert.BusinessRule(ErrorCodes.NotOwner,
            () => Update(w, created, r => r.Note = "not mine", hasFullScope: false, userId: other.UserId));
    }

    [Fact]
    public async Task Update_OwnScopeCaller_IsNotOwnershipCheckedAgainstTheRequestedNewEmployee()
    {
        await using SchedulingWorld w = await SchedulingWorld.Create(nameof(Update_OwnScopeCaller_IsNotOwnershipCheckedAgainstTheRequestedNewEmployee));
        Employee other = await w.AddEmployee("Other");
        AppointmentDto created = await w.CreateAppointment(SchedulingWorld.Future(10));

        // FINDING: ownership is checked against the CURRENT employee only. An own-scope caller may submit a reassignment
        // to someone else without NOT_OWNER (the reassignment itself is then dropped by the persistence defect above,
        // but the audit row is written).
        await Update(w, created, r => r.EmployeeId = other.Id.Value, hasFullScope: false, userId: w.Employee.UserId);

        Assert.Single(await w.LoadAuditLog(created.Id), l => l.ChangeType == "EmployeeId");
    }

    #endregion
}
