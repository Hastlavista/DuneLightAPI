#nullable disable
using System;
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
/// CHARACTERIZATION (matrix A): individual Appointment creation through AppointmentService.Create
/// (POST /api/appointments/schedule). Pins what the code does today, including the order in which structural checks
/// run, so the Appointment/Booking redesign can be diffed against it. See the class summary of SchedulingWorld for the
/// fixture conventions.
/// </summary>
public class AppointmentCreateCharacterizationTests
{
    #region Happy path — persisted shape

    [Fact]
    public async Task Create_Individual_PersistsTheSegmentAndOneConfirmedBooking()
    {
        await using SchedulingWorld w = await SchedulingWorld.Create(nameof(Create_Individual_PersistsTheSegmentAndOneConfirmedBooking));
        Room room = await w.AddRoom();

        AppointmentDto dto = await w.CreateAppointment(w.CreateRequest(SchedulingWorld.Future(10), room: room, note: "hello"));

        Appointment a = await w.LoadAppointment(dto.Id);
        Assert.Equal(w.OrganizationId, a.OrganizationId);
        Assert.Equal(w.Company.Id, a.CompanyId);
        Assert.Equal(w.Service.Id, a.ServiceId);
        Assert.Equal(w.Employee.Id, a.EmployeeId);
        Assert.Equal(room.Id, a.RoomId);
        Assert.Equal(SchedulingWorld.Future(10), a.StartsAt);
        Assert.Equal(SchedulingWorld.DefaultServiceDuration, a.DurationMinutes);
        Assert.Equal(AppointmentForm.Individual, a.Form);
        Assert.Equal(AppointmentStatus.Scheduled, a.Status);
        Assert.Equal("hello", a.Note);
        Assert.Null(a.GroupId);
        Assert.Null(a.GroupSlotId);
        Assert.Null(a.RecurrenceGroupId);
        Assert.Null(a.CancellationReason);
        Assert.Equal(w.ActorUserId, a.CreatedBy);
        Assert.Null(a.UpdatedAt);

        Booking b = Assert.Single(a.Bookings);
        Assert.Equal(w.Client.Id, b.ClientId);
        Assert.Equal(w.OrganizationId, b.OrganizationId);
        Assert.Equal(BookingStatus.Confirmed, b.Status);
        Assert.Equal(0, b.StatusVersion);
        Assert.Equal(SchedulingWorld.DefaultServicePrice, b.Amount);
        Assert.Equal(SchedulingWorld.DefaultServicePrice, b.SuggestedAmount);
        Assert.False(b.IsAmountManuallyOverridden);
        Assert.Null(b.ClientPackageId);
        Assert.Null(b.CoverageType);
        Assert.False(b.PackageCoverageApplied);
        Assert.False(b.PackageCoverageReturned);
        Assert.Null(b.IsLateCancellation);
    }

    [Fact]
    public async Task Create_ReturnedDtoMirrorsPersistedState()
    {
        await using SchedulingWorld w = await SchedulingWorld.Create(nameof(Create_ReturnedDtoMirrorsPersistedState));

        AppointmentDto dto = await w.CreateAppointment(SchedulingWorld.Future(10));

        Assert.Equal(AppointmentStatus.Scheduled, dto.Status);
        Assert.Equal(w.Employee.Id, dto.EmployeeId);
        Assert.Equal(w.Service.Id, dto.ServiceId);
        Assert.Equal(SchedulingWorld.DefaultServiceDuration, dto.DurationMinutes);
        BookingDto booking = Assert.Single(dto.Bookings);
        Assert.Equal(BookingStatusSummary.Confirmed, booking.Status);
        Assert.Equal(SchedulingWorld.DefaultServicePrice, booking.OutstandingAmount);
        Assert.False(booking.IsPaid);
        SchedulingAssert.HasNoWarnings(dto);
    }

    [Fact]
    public async Task Create_WithoutRoom_LeavesRoomNull()
    {
        await using SchedulingWorld w = await SchedulingWorld.Create(nameof(Create_WithoutRoom_LeavesRoomNull));

        AppointmentDto dto = await w.CreateAppointment(SchedulingWorld.Future(10));

        Assert.Null((await w.LoadAppointment(dto.Id)).RoomId);
    }

    [Fact]
    public async Task Create_WithSeveralClients_CreatesOneBookingPerClientAllWithTheSameAmount()
    {
        await using SchedulingWorld w = await SchedulingWorld.Create(nameof(Create_WithSeveralClients_CreatesOneBookingPerClientAllWithTheSameAmount));
        Client second = await w.AddClient("Second", "Client");

        AppointmentDto dto = await w.CreateAppointment(SchedulingWorld.Future(10), extraClients: second);

        Appointment a = await w.LoadAppointment(dto.Id);
        Assert.Equal(2, a.Bookings.Count);
        Assert.All(a.Bookings, b =>
        {
            Assert.Equal(BookingStatus.Confirmed, b.Status);
            Assert.Equal(SchedulingWorld.DefaultServicePrice, b.Amount);
        });
        Assert.Equal(
            new[] { w.Client.Id, second.Id }.OrderBy(x => x).ToArray(),
            a.Bookings.Select(b => b.ClientId).OrderBy(x => x).Cast<Guid?>().ToArray());
    }

    [Fact]
    public async Task Create_DoesNotWriteAuditLogOrOutboxMessages()
    {
        await using SchedulingWorld w = await SchedulingWorld.Create(nameof(Create_DoesNotWriteAuditLogOrOutboxMessages));

        AppointmentDto dto = await w.CreateAppointment(SchedulingWorld.Future(10));

        Assert.Empty(await w.LoadAuditLog(dto.Id));
        Assert.Empty(await w.LoadOutbox());
        Assert.Empty(await w.LoadCommissionEntries());
    }

    #endregion

    #region Snapshots — duration and price

    [Fact]
    public async Task Create_SnapshotsServiceDefaultDuration_AndLaterServiceChangesDoNotRewriteIt()
    {
        await using SchedulingWorld w = await SchedulingWorld.Create(nameof(Create_SnapshotsServiceDefaultDuration_AndLaterServiceChangesDoNotRewriteIt));
        ServiceEntity forty = await w.AddService(40, 10m);
        await w.AssignEmployeeToService(w.Employee, forty);

        AppointmentDto dto = await w.CreateAppointment(w.CreateRequest(SchedulingWorld.Future(10), service: forty));
        Assert.Equal(40, (await w.LoadAppointment(dto.Id)).DurationMinutes);

        await w.UpdateService(forty, durationMinutes: 90);

        Assert.Equal(40, (await w.LoadAppointment(dto.Id)).DurationMinutes);
        Assert.Equal(40, (await w.Appointments.GetById(w.OrganizationId, dto.Id)).DurationMinutes);
    }

    [Fact]
    public async Task Create_ResolvesPriceFromServiceDefaultPrice_WhenNoPriceListRowApplies()
    {
        await using SchedulingWorld w = await SchedulingWorld.Create(nameof(Create_ResolvesPriceFromServiceDefaultPrice_WhenNoPriceListRowApplies));

        AppointmentDto dto = await w.CreateAppointment(SchedulingWorld.Future(10));

        Booking b = (await w.LoadAppointment(dto.Id)).Bookings.Single();
        Assert.Equal(SchedulingWorld.DefaultServicePrice, b.SuggestedAmount);
    }

    [Fact]
    public async Task Create_ResolvesPriceFromCompanySpecificPriceListRow_ValidOnTheAppointmentDate()
    {
        await using SchedulingWorld w = await SchedulingWorld.Create(nameof(Create_ResolvesPriceFromCompanySpecificPriceListRow_ValidOnTheAppointmentDate));
        // Pricing is resolved for Service + Company + the APPOINTMENT's StartsAt (not "today").
        await w.AddPriceListItem(w.Service, 70m, validFrom: new DateTimeOffset(2030, 1, 1, 0, 0, 0, TimeSpan.Zero), companyId: w.Company.Id);

        AppointmentDto dto = await w.CreateAppointment(SchedulingWorld.Future(10));

        Booking b = (await w.LoadAppointment(dto.Id)).Bookings.Single();
        Assert.Equal(70m, b.Amount);
        Assert.Equal(70m, b.SuggestedAmount);
    }

    [Fact]
    public async Task Create_PriceListRowNotYetValidOnTheAppointmentDate_FallsBackToDefaultPrice()
    {
        await using SchedulingWorld w = await SchedulingWorld.Create(nameof(Create_PriceListRowNotYetValidOnTheAppointmentDate_FallsBackToDefaultPrice));
        await w.AddPriceListItem(w.Service, 99m, validFrom: new DateTimeOffset(2040, 1, 1, 0, 0, 0, TimeSpan.Zero), companyId: w.Company.Id);

        AppointmentDto dto = await w.CreateAppointment(SchedulingWorld.Future(10));

        Assert.Equal(SchedulingWorld.DefaultServicePrice, (await w.LoadAppointment(dto.Id)).Bookings.Single().Amount);
    }

    [Fact]
    public async Task Create_PriceIsSnapshottedOnTheBooking_LaterPriceChangesDoNotRewriteIt()
    {
        await using SchedulingWorld w = await SchedulingWorld.Create(nameof(Create_PriceIsSnapshottedOnTheBooking_LaterPriceChangesDoNotRewriteIt));

        AppointmentDto dto = await w.CreateAppointment(SchedulingWorld.Future(10));
        await w.UpdateService(w.Service, defaultPrice: 999m);
        await w.AddPriceListItem(w.Service, 888m, validFrom: new DateTimeOffset(2030, 1, 1, 0, 0, 0, TimeSpan.Zero));

        Booking b = (await w.LoadAppointment(dto.Id)).Bookings.Single();
        Assert.Equal(SchedulingWorld.DefaultServicePrice, b.Amount);
        Assert.Equal(SchedulingWorld.DefaultServicePrice, b.SuggestedAmount);
    }

    [Fact]
    public async Task Create_ExplicitAmountDifferentFromSuggested_IsStoredAndFlaggedAsManualOverride()
    {
        await using SchedulingWorld w = await SchedulingWorld.Create(nameof(Create_ExplicitAmountDifferentFromSuggested_IsStoredAndFlaggedAsManualOverride));

        AppointmentDto dto = await w.CreateAppointment(w.CreateRequest(SchedulingWorld.Future(10), amount: 35m));

        Booking b = (await w.LoadAppointment(dto.Id)).Bookings.Single();
        Assert.Equal(35m, b.Amount);
        Assert.Equal(SchedulingWorld.DefaultServicePrice, b.SuggestedAmount);
        Assert.True(b.IsAmountManuallyOverridden);
    }

    [Fact]
    public async Task Create_ExplicitAmountEqualToSuggested_IsNotFlaggedAsOverride()
    {
        await using SchedulingWorld w = await SchedulingWorld.Create(nameof(Create_ExplicitAmountEqualToSuggested_IsNotFlaggedAsOverride));

        AppointmentDto dto = await w.CreateAppointment(w.CreateRequest(SchedulingWorld.Future(10), amount: SchedulingWorld.DefaultServicePrice));

        Assert.False((await w.LoadAppointment(dto.Id)).Bookings.Single().IsAmountManuallyOverridden);
    }

    [Fact]
    public async Task Create_ExplicitZeroAmount_IsAValidFreeAppointment()
    {
        await using SchedulingWorld w = await SchedulingWorld.Create(nameof(Create_ExplicitZeroAmount_IsAValidFreeAppointment));

        AppointmentDto dto = await w.CreateAppointment(w.CreateRequest(SchedulingWorld.Future(10), amount: 0m));

        Booking b = (await w.LoadAppointment(dto.Id)).Bookings.Single();
        Assert.Equal(0m, b.Amount);
        Assert.True(b.IsAmountManuallyOverridden);
        // Amount 0 is "settled" by definition (see BookingFinancialsCalculator.CalculateOutstanding).
        Assert.True(Assert.Single(dto.Bookings).IsPaid);
    }

    #endregion

    #region Structural eligibility failures — one rule per test

    [Fact]
    public async Task Create_InactiveClient_IsRejected()
    {
        await using SchedulingWorld w = await SchedulingWorld.Create(nameof(Create_InactiveClient_IsRejected));
        Client inactive = await w.AddClient("Inactive", "Client", isActive: false);

        await SchedulingAssert.BusinessRule(ErrorCodes.InactiveClient,
            () => w.CreateAppointment(w.CreateRequest(SchedulingWorld.Future(10), client: inactive)));

        Assert.Equal(0, await w.CountAppointments());
    }

    [Fact]
    public async Task Create_AnonymizedClient_IsRejected()
    {
        await using SchedulingWorld w = await SchedulingWorld.Create(nameof(Create_AnonymizedClient_IsRejected));
        Client anonymized = await w.AddClient("Anon", "Client", isAnonymized: true);

        await SchedulingAssert.BusinessRule(ErrorCodes.ClientAnonymized,
            () => w.CreateAppointment(w.CreateRequest(SchedulingWorld.Future(10), client: anonymized)));

        Assert.Equal(0, await w.CountAppointments());
    }

    [Fact]
    public async Task Create_UnknownClient_IsNotFound()
    {
        await using SchedulingWorld w = await SchedulingWorld.Create(nameof(Create_UnknownClient_IsNotFound));
        TestAppointmentSpec request = w.CreateRequest(SchedulingWorld.Future(10));
        request.ClientIds = new() { Guid.NewGuid() };

        await SchedulingAssert.NotFound(() => w.CreateAppointment(request));

        Assert.Equal(0, await w.CountAppointments());
    }

    [Fact]
    public async Task Create_InactiveService_IsRejected()
    {
        await using SchedulingWorld w = await SchedulingWorld.Create(nameof(Create_InactiveService_IsRejected));
        await w.SetServiceActive(w.Service, false);

        await SchedulingAssert.BusinessRule(ErrorCodes.InactiveService,
            () => w.CreateAppointment(w.CreateRequest(SchedulingWorld.Future(10))));

        Assert.Equal(0, await w.CountAppointments());
    }

    [Fact]
    public async Task Create_UnknownService_IsNotFound()
    {
        await using SchedulingWorld w = await SchedulingWorld.Create(nameof(Create_UnknownService_IsNotFound));
        TestAppointmentSpec request = w.CreateRequest(SchedulingWorld.Future(10));
        request.ServiceId = Guid.NewGuid();

        await SchedulingAssert.NotFound(() => w.CreateAppointment(request));
    }

    [Fact]
    public async Task Create_InactiveCompany_IsRejected()
    {
        await using SchedulingWorld w = await SchedulingWorld.Create(nameof(Create_InactiveCompany_IsRejected));
        await w.SetCompanyActive(w.Company, false);

        await SchedulingAssert.BusinessRule(ErrorCodes.InactiveCompany,
            () => w.CreateAppointment(w.CreateRequest(SchedulingWorld.Future(10))));

        Assert.Equal(0, await w.CountAppointments());
    }

    [Fact]
    public async Task Create_ServiceNotOfferedAtTheCompany_IsRejected()
    {
        await using SchedulingWorld w = await SchedulingWorld.Create(nameof(Create_ServiceNotOfferedAtTheCompany_IsRejected));
        ServiceEntity notOffered = await w.AddService(30, 10m, availableAtCompany: false);
        await w.AssignEmployeeToService(w.Employee, notOffered);

        await SchedulingAssert.BusinessRule(ErrorCodes.ServiceNotAvailableAtCompany,
            () => w.CreateAppointment(w.CreateRequest(SchedulingWorld.Future(10), service: notOffered)));

        Assert.Equal(0, await w.CountAppointments());
    }

    [Fact]
    public async Task Create_InactiveEmployee_IsRejected()
    {
        await using SchedulingWorld w = await SchedulingWorld.Create(nameof(Create_InactiveEmployee_IsRejected));
        Employee inactive = await w.AddEmployee(isActive: false);

        await SchedulingAssert.BusinessRule(ErrorCodes.InactiveEmployee,
            () => w.CreateAppointment(w.CreateRequest(SchedulingWorld.Future(10), employee: inactive)));

        Assert.Equal(0, await w.CountAppointments());
    }

    [Fact]
    public async Task Create_EmployeeNotAssignedToTheCompany_IsRejected()
    {
        await using SchedulingWorld w = await SchedulingWorld.Create(nameof(Create_EmployeeNotAssignedToTheCompany_IsRejected));
        Employee elsewhere = await w.AddEmployee(assignedToCompany: false);

        await SchedulingAssert.BusinessRule(ErrorCodes.EmployeeNotAssignedToCompany,
            () => w.CreateAppointment(w.CreateRequest(SchedulingWorld.Future(10), employee: elsewhere)));

        Assert.Equal(0, await w.CountAppointments());
    }

    [Fact]
    public async Task Create_EmployeeNotAuthorizedForTheService_IsRejected()
    {
        await using SchedulingWorld w = await SchedulingWorld.Create(nameof(Create_EmployeeNotAuthorizedForTheService_IsRejected));
        Employee unauthorized = await w.AddEmployeeRestrictedToAnotherService();

        await SchedulingAssert.BusinessRule(ErrorCodes.EmployeeNotAssignedToService,
            () => w.CreateAppointment(w.CreateRequest(SchedulingWorld.Future(10), employee: unauthorized)));

        Assert.Equal(0, await w.CountAppointments());
    }

    [Fact]
    public async Task Create_UnknownEmployee_IsNotFound()
    {
        await using SchedulingWorld w = await SchedulingWorld.Create(nameof(Create_UnknownEmployee_IsNotFound));
        TestAppointmentSpec request = w.CreateRequest(SchedulingWorld.Future(10));
        request.EmployeeId = Guid.NewGuid();

        // Full-scope callers skip the ownership lookup, so the first failure is the missing Employee.
        await SchedulingAssert.NotFound(() => w.CreateAppointment(request));
    }

    [Fact]
    public async Task Create_UnknownRoom_IsNotFound()
    {
        await using SchedulingWorld w = await SchedulingWorld.Create(nameof(Create_UnknownRoom_IsNotFound));
        TestAppointmentSpec request = w.CreateRequest(SchedulingWorld.Future(10));
        request.RoomId = Guid.NewGuid();

        await SchedulingAssert.NotFound(() => w.CreateAppointment(request));

        Assert.Equal(0, await w.CountAppointments());
    }

    [Fact]
    public async Task Create_InactiveRoom_IsRejected()
    {
        await using SchedulingWorld w = await SchedulingWorld.Create(nameof(Create_InactiveRoom_IsRejected));
        Room inactive = await w.AddRoom(isActive: false);

        await SchedulingAssert.BusinessRule(ErrorCodes.InactiveRoom,
            () => w.CreateAppointment(w.CreateRequest(SchedulingWorld.Future(10), room: inactive)));

        Assert.Equal(0, await w.CountAppointments());
    }

    [Fact]
    public async Task Create_RoomBelongingToAnotherCompany_IsRejected()
    {
        await using SchedulingWorld w = await SchedulingWorld.Create(nameof(Create_RoomBelongingToAnotherCompany_IsRejected));
        Company other = await w.AddCompany("Other company");
        Room otherRoom = await w.AddRoom(other);

        await SchedulingAssert.BusinessRule(ErrorCodes.RoomCompanyMismatch,
            () => w.CreateAppointment(w.CreateRequest(SchedulingWorld.Future(10), room: otherRoom)));

        Assert.Equal(0, await w.CountAppointments());
    }

    #endregion

    #region Order of the structural checks (observed, pinned)

    [Fact]
    public async Task Create_InactiveServiceIsReportedBeforeAnInactiveEmployee()
    {
        await using SchedulingWorld w = await SchedulingWorld.Create(nameof(Create_InactiveServiceIsReportedBeforeAnInactiveEmployee));
        Employee inactive = await w.AddEmployee(isActive: false);
        await w.SetServiceActive(w.Service, false);

        // EnsureStructuralEligibility: Service.IsActive is the first check, ahead of Company/Employee checks.
        await SchedulingAssert.BusinessRule(ErrorCodes.InactiveService,
            () => w.CreateAppointment(w.CreateRequest(SchedulingWorld.Future(10), employee: inactive)));
    }

    [Fact]
    public async Task Create_StructuralEligibilityIsCheckedBeforeTheRoomAndClientChecks()
    {
        await using SchedulingWorld w = await SchedulingWorld.Create(nameof(Create_StructuralEligibilityIsCheckedBeforeTheRoomAndClientChecks));
        Client inactiveClient = await w.AddClient(isActive: false);
        Room inactiveRoom = await w.AddRoom(isActive: false);
        Employee inactiveEmployee = await w.AddEmployee(isActive: false);

        await SchedulingAssert.BusinessRule(ErrorCodes.InactiveEmployee,
            () => w.CreateAppointment(w.CreateRequest(SchedulingWorld.Future(10), client: inactiveClient, employee: inactiveEmployee, room: inactiveRoom)));
    }

    #endregion

    #region Authorization scope on Create

    [Fact]
    public async Task Create_OwnScopeCaller_CanCreateForTheirOwnEmployeeRecord()
    {
        await using SchedulingWorld w = await SchedulingWorld.Create(nameof(Create_OwnScopeCaller_CanCreateForTheirOwnEmployeeRecord));

        AppointmentDto dto = await w.Appointments.Create(
            w.OrganizationId, w.Employee.UserId, hasFullScope: false, w.CreateRequest(SchedulingWorld.Future(10)).ToTarget());

        Assert.Equal(w.Employee.Id, dto.EmployeeId);
    }

    [Fact]
    public async Task Create_OwnScopeCaller_CannotCreateForAnotherEmployee()
    {
        await using SchedulingWorld w = await SchedulingWorld.Create(nameof(Create_OwnScopeCaller_CannotCreateForAnotherEmployee));
        Employee other = await w.AddEmployee();

        await SchedulingAssert.BusinessRule(ErrorCodes.NotOwner,
            () => w.Appointments.Create(
                w.OrganizationId, w.Employee.UserId, hasFullScope: false, w.CreateRequest(SchedulingWorld.Future(10), employee: other).ToTarget()));

        Assert.Equal(0, await w.CountAppointments());
    }

    [Fact]
    public async Task Create_OwnScopeCaller_WithoutAnEmployeeRecord_IsNotOwner()
    {
        await using SchedulingWorld w = await SchedulingWorld.Create(nameof(Create_OwnScopeCaller_WithoutAnEmployeeRecord_IsNotOwner));

        await SchedulingAssert.BusinessRule(ErrorCodes.NotOwner,
            () => w.Appointments.Create(
                w.OrganizationId, w.ActorUserId, hasFullScope: false, w.CreateRequest(SchedulingWorld.Future(10)).ToTarget()));
    }

    [Fact]
    public async Task Create_OverrideAvailabilityIsIgnoredForOwnScopeCallers()
    {
        await using SchedulingWorld w = await SchedulingWorld.Create(nameof(Create_OverrideAvailabilityIsIgnoredForOwnScopeCallers));

        // 22:00 is outside the seeded 08:00-20:00 working hours. overrideAvailability = request flag AND full scope.
        await SchedulingAssert.BusinessRule(ErrorCodes.OutsideWorkingHours,
            () => w.Appointments.Create(
                w.OrganizationId, w.Employee.UserId, hasFullScope: false,
                w.CreateRequest(SchedulingWorld.Future(22), overrideAvailability: true).ToTarget()));
    }

    #endregion
}
