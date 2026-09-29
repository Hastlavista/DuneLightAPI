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

namespace BlueDragon.DuneLight.UnitTests.Scheduling;

/// <summary>
/// CHARACTERIZATION (matrix G): the drag-and-drop endpoint, AppointmentService.Move (PATCH /api/appointments/{id}/move).
///
/// Move is NOT a light version of Update. It loads the appointment without navigations and writes only scalar columns:
/// it changes StartsAt and, optionally, Employee / Company / Room; it never touches Service, Bookings, prices or the
/// stored duration. It re-runs structural eligibility for the EFFECTIVE employee/company, workforce availability and
/// hard overlap. Optional request members are "null = unchanged" — which is why Move cannot clear a room.
/// </summary>
public class AppointmentMoveCharacterizationTests
{
    private static Task<AppointmentDto> Move(SchedulingWorld w, Guid id, DateTimeOffset startsAt, Employee employee = null, Company company = null,
        Room room = null, bool hasFullScope = true, Guid? userId = null, bool overrideAvailability = false) =>
        w.Appointments.Move(w.OrganizationId, userId ?? w.ActorUserId, hasFullScope, id, new AppointmentMoveRequest
        {
            StartsAt = startsAt,
            EmployeeId = employee?.Id,
            CompanyId = company?.Id,
            RoomId = room?.Id,
            OverrideAvailability = overrideAvailability
        });

    #region Time move

    [Fact]
    public async Task Move_ChangesOnlyTheStartTime_ServiceEmployeeCompanyRoomAndClientsStayTheSame()
    {
        await using SchedulingWorld w = await SchedulingWorld.Create(nameof(Move_ChangesOnlyTheStartTime_ServiceEmployeeCompanyRoomAndClientsStayTheSame));
        Room room = await w.AddRoom();
        Client second = await w.AddClient("Second", "Client");
        AppointmentDto created = await w.CreateAppointment(SchedulingWorld.Future(10), room: room, extraClients: second);

        await Move(w, created.Id, SchedulingWorld.Future(15));

        Appointment a = await w.LoadAppointment(created.Id);
        Assert.Equal(SchedulingWorld.Future(15), a.StartsAt);
        Assert.Equal(w.Service.Id, a.ServiceId);
        Assert.Equal(w.Employee.Id, a.EmployeeId);
        Assert.Equal(w.Company.Id, a.CompanyId);
        Assert.Equal(room.Id, a.RoomId);
        Assert.Equal(SchedulingWorld.DefaultServiceDuration, a.DurationMinutes);
        Assert.Equal(AppointmentStatus.Scheduled, a.Status);
        Assert.Equal(2, a.Bookings.Count);
        Assert.All(a.Bookings, b =>
        {
            Assert.Equal(BookingStatus.Confirmed, b.Status);
            Assert.Equal(0, b.StatusVersion);
            Assert.Equal(50m, b.Amount);
        });
        Assert.Equal(w.ActorUserId, a.UpdatedBy);
        Assert.NotNull(a.UpdatedAt);
    }

    [Fact]
    public async Task Move_DoesNotRefreshTheDurationSnapshot_UnlikeUpdate()
    {
        await using SchedulingWorld w = await SchedulingWorld.Create(nameof(Move_DoesNotRefreshTheDurationSnapshot_UnlikeUpdate));
        AppointmentDto created = await w.CreateAppointment(SchedulingWorld.Future(10));
        await w.UpdateService(w.Service, durationMinutes: 60);

        await Move(w, created.Id, SchedulingWorld.Future(15));

        Assert.Equal(30, (await w.LoadAppointment(created.Id)).DurationMinutes);
    }

    [Fact]
    public async Task Move_DoesNotRePriceTheBookings_UnlikeUpdate()
    {
        await using SchedulingWorld w = await SchedulingWorld.Create(nameof(Move_DoesNotRePriceTheBookings_UnlikeUpdate));
        AppointmentDto created = await w.CreateAppointment(w.CreateRequest(SchedulingWorld.Future(10), amount: 35m));
        await w.AddPriceListItem(w.Service, 99m, new DateTimeOffset(2030, 1, 1, 0, 0, 0, TimeSpan.Zero), companyId: w.Company.Id);

        await Move(w, created.Id, SchedulingWorld.Future(15));

        Booking b = (await w.LoadAppointment(created.Id)).Bookings.Single();
        Assert.Equal(35m, b.Amount);
        Assert.True(b.IsAmountManuallyOverridden);
        Assert.Empty(await w.LoadAuditLog(created.Id));
    }

    [Fact]
    public async Task Move_ToTheSameSlot_DoesNotConflictWithItself()
    {
        await using SchedulingWorld w = await SchedulingWorld.Create(nameof(Move_ToTheSameSlot_DoesNotConflictWithItself));
        AppointmentDto created = await w.CreateAppointment(SchedulingWorld.Future(10));

        AppointmentDto moved = await Move(w, created.Id, SchedulingWorld.Future(10));

        Assert.Equal(SchedulingWorld.Future(10), moved.StartsAt);
    }

    [Fact]
    public async Task Move_ReturnsTheUpdatedAppointmentDto()
    {
        await using SchedulingWorld w = await SchedulingWorld.Create(nameof(Move_ReturnsTheUpdatedAppointmentDto));
        AppointmentDto created = await w.CreateAppointment(SchedulingWorld.Future(10));

        AppointmentDto moved = await Move(w, created.Id, SchedulingWorld.Future(15));

        Assert.Equal(SchedulingWorld.Future(15), moved.StartsAt);
        Assert.Equal(created.Id, moved.Id);
    }

    #endregion

    #region Employee change

    [Fact]
    public async Task Move_CanChangeTheEmployee_ItIsPersisted_AndAnAuditRowIsWritten()
    {
        await using SchedulingWorld w = await SchedulingWorld.Create(nameof(Move_CanChangeTheEmployee_ItIsPersisted_AndAnAuditRowIsWritten));
        Employee other = await w.AddEmployee("Other");
        AppointmentDto created = await w.CreateAppointment(SchedulingWorld.Future(10));

        await Move(w, created.Id, SchedulingWorld.Future(10), employee: other);

        // Contrast with Update, which does NOT persist an employee change (see AppointmentUpdateCharacterizationTests).
        Assert.Equal(other.Id, (await w.LoadAppointment(created.Id)).EmployeeId);
        AppointmentAuditLog audit = Assert.Single(await w.LoadAuditLog(created.Id), l => l.ChangeType == "EmployeeId");
        Assert.Equal(w.Employee.Id.ToString(), audit.OldValue);
        Assert.Equal(other.Id.ToString(), audit.NewValue);
    }

    [Fact]
    public async Task Move_WithTheSameEmployee_WritesNoAuditRow()
    {
        await using SchedulingWorld w = await SchedulingWorld.Create(nameof(Move_WithTheSameEmployee_WritesNoAuditRow));
        AppointmentDto created = await w.CreateAppointment(SchedulingWorld.Future(10));

        await Move(w, created.Id, SchedulingWorld.Future(12), employee: w.Employee);

        Assert.Empty(await w.LoadAuditLog(created.Id));
    }

    [Fact]
    public async Task Move_ToAnEmployeeNotAuthorizedForTheService_IsRejected()
    {
        await using SchedulingWorld w = await SchedulingWorld.Create(nameof(Move_ToAnEmployeeNotAuthorizedForTheService_IsRejected));
        Employee unauthorized = await w.AddEmployee(assignedToService: false);
        AppointmentDto created = await w.CreateAppointment(SchedulingWorld.Future(10));

        await SchedulingAssert.BusinessRule(ErrorCodes.EmployeeNotAssignedToService,
            () => Move(w, created.Id, SchedulingWorld.Future(10), employee: unauthorized));

        Assert.Equal(w.Employee.Id, (await w.LoadAppointment(created.Id)).EmployeeId);
    }

    [Fact]
    public async Task Move_ToAnUnknownEmployee_IsNotFound()
    {
        await using SchedulingWorld w = await SchedulingWorld.Create(nameof(Move_ToAnUnknownEmployee_IsNotFound));
        AppointmentDto created = await w.CreateAppointment(SchedulingWorld.Future(10));

        await SchedulingAssert.NotFound(() => w.Appointments.Move(w.OrganizationId, w.ActorUserId, true, created.Id,
            new AppointmentMoveRequest { StartsAt = SchedulingWorld.Future(10), EmployeeId = Guid.NewGuid() }));
    }

    [Fact]
    public async Task Move_ToAnEmployeeWhoIsBusyAtTheNewTime_IsRejected()
    {
        await using SchedulingWorld w = await SchedulingWorld.Create(nameof(Move_ToAnEmployeeWhoIsBusyAtTheNewTime_IsRejected));
        Employee other = await w.AddEmployee("Other");
        Client otherClient = await w.AddClient("Other", "Client");
        AppointmentDto created = await w.CreateAppointment(SchedulingWorld.Future(10));
        await w.CreateAppointment(SchedulingWorld.Future(14), client: otherClient, employee: other);

        await SchedulingAssert.BusinessRule(ErrorCodes.AppointmentOverlap,
            () => Move(w, created.Id, SchedulingWorld.Future(14), employee: other));
    }

    #endregion

    #region Company change

    [Fact]
    public async Task Move_CanChangeTheCompany_WhenTheEmployeeAndServiceAreEligibleThere()
    {
        await using SchedulingWorld w = await SchedulingWorld.Create(nameof(Move_CanChangeTheCompany_WhenTheEmployeeAndServiceAreEligibleThere));
        Company other = await w.AddCompany("Second company");
        await w.MakeServiceAvailableAt(w.Service, other);
        await w.AssignEmployeeToCompany(w.Employee, other);
        AppointmentDto created = await w.CreateAppointment(SchedulingWorld.Future(10));

        await Move(w, created.Id, SchedulingWorld.Future(10), company: other);

        Appointment a = await w.LoadAppointment(created.Id);
        Assert.Equal(other.Id, a.CompanyId);
        Assert.Equal(w.Service.Id, a.ServiceId);
    }

    [Fact]
    public async Task Move_ToACompanyWhereTheServiceIsNotOffered_IsRejected()
    {
        await using SchedulingWorld w = await SchedulingWorld.Create(nameof(Move_ToACompanyWhereTheServiceIsNotOffered_IsRejected));
        Company other = await w.AddCompany("Second company");
        await w.AssignEmployeeToCompany(w.Employee, other); // service NOT made available there
        AppointmentDto created = await w.CreateAppointment(SchedulingWorld.Future(10));

        await SchedulingAssert.BusinessRule(ErrorCodes.ServiceNotAvailableAtCompany,
            () => Move(w, created.Id, SchedulingWorld.Future(10), company: other));
    }

    [Fact]
    public async Task Move_ToACompanyWhereTheEmployeeDoesNotWork_IsRejected()
    {
        await using SchedulingWorld w = await SchedulingWorld.Create(nameof(Move_ToACompanyWhereTheEmployeeDoesNotWork_IsRejected));
        Company other = await w.AddCompany("Second company");
        await w.MakeServiceAvailableAt(w.Service, other); // employee NOT assigned there
        AppointmentDto created = await w.CreateAppointment(SchedulingWorld.Future(10));

        await SchedulingAssert.BusinessRule(ErrorCodes.EmployeeNotAssignedToCompany,
            () => Move(w, created.Id, SchedulingWorld.Future(10), company: other));
    }

    [Fact]
    public async Task Move_ToAnInactiveCompany_IsRejected()
    {
        await using SchedulingWorld w = await SchedulingWorld.Create(nameof(Move_ToAnInactiveCompany_IsRejected));
        Company other = await w.AddCompany("Second company", isActive: false);
        AppointmentDto created = await w.CreateAppointment(SchedulingWorld.Future(10));

        await SchedulingAssert.BusinessRule(ErrorCodes.InactiveCompany,
            () => Move(w, created.Id, SchedulingWorld.Future(10), company: other));
    }

    [Fact]
    public async Task Move_ToAnUnknownCompany_IsNotFound()
    {
        await using SchedulingWorld w = await SchedulingWorld.Create(nameof(Move_ToAnUnknownCompany_IsNotFound));
        AppointmentDto created = await w.CreateAppointment(SchedulingWorld.Future(10));

        await SchedulingAssert.NotFound(() => w.Appointments.Move(w.OrganizationId, w.ActorUserId, true, created.Id,
            new AppointmentMoveRequest { StartsAt = SchedulingWorld.Future(10), CompanyId = Guid.NewGuid() }));
    }

    [Fact]
    public async Task Move_ToAnotherCompany_WithoutAResendRoom_RevalidatesTheExistingRoomAgainstTheNewCompany()
    {
        await using SchedulingWorld w = await SchedulingWorld.Create(nameof(Move_ToAnotherCompany_WithoutAResendRoom_RevalidatesTheExistingRoomAgainstTheNewCompany));
        Company other = await w.AddCompany("Second company");
        await w.MakeServiceAvailableAt(w.Service, other);
        await w.AssignEmployeeToCompany(w.Employee, other);
        Room oldRoom = await w.AddRoom();
        AppointmentDto created = await w.CreateAppointment(SchedulingWorld.Future(10), room: oldRoom);

        // The stored room belongs to the OLD company, so moving to the new company without naming a new room is rejected.
        await SchedulingAssert.BusinessRule(ErrorCodes.RoomCompanyMismatch,
            () => Move(w, created.Id, SchedulingWorld.Future(10), company: other));

        Assert.Equal(w.Company.Id, (await w.LoadAppointment(created.Id)).CompanyId);
    }

    [Fact]
    public async Task Move_ToAnotherCompany_WithANewRoomOfThatCompany_Succeeds()
    {
        await using SchedulingWorld w = await SchedulingWorld.Create(nameof(Move_ToAnotherCompany_WithANewRoomOfThatCompany_Succeeds));
        Company other = await w.AddCompany("Second company");
        await w.MakeServiceAvailableAt(w.Service, other);
        await w.AssignEmployeeToCompany(w.Employee, other);
        Room oldRoom = await w.AddRoom();
        Room newRoom = await w.AddRoom(other);
        AppointmentDto created = await w.CreateAppointment(SchedulingWorld.Future(10), room: oldRoom);

        await Move(w, created.Id, SchedulingWorld.Future(10), company: other, room: newRoom);

        Appointment a = await w.LoadAppointment(created.Id);
        Assert.Equal(other.Id, a.CompanyId);
        Assert.Equal(newRoom.Id, a.RoomId);
    }

    #endregion

    #region Room behaviour

    [Fact]
    public async Task Move_CanAssignARoomToAnAppointmentWithoutOne()
    {
        await using SchedulingWorld w = await SchedulingWorld.Create(nameof(Move_CanAssignARoomToAnAppointmentWithoutOne));
        Room room = await w.AddRoom();
        AppointmentDto created = await w.CreateAppointment(SchedulingWorld.Future(10));

        await Move(w, created.Id, SchedulingWorld.Future(10), room: room);

        Assert.Equal(room.Id, (await w.LoadAppointment(created.Id)).RoomId);
    }

    [Fact]
    public async Task Move_CanSwitchToAnotherRoom()
    {
        await using SchedulingWorld w = await SchedulingWorld.Create(nameof(Move_CanSwitchToAnotherRoom));
        Room first = await w.AddRoom();
        Room second = await w.AddRoom();
        AppointmentDto created = await w.CreateAppointment(SchedulingWorld.Future(10), room: first);

        await Move(w, created.Id, SchedulingWorld.Future(10), room: second);

        Assert.Equal(second.Id, (await w.LoadAppointment(created.Id)).RoomId);
    }

    [Fact]
    public async Task Move_CannotClearTheRoom_NullMeansUnchanged()
    {
        await using SchedulingWorld w = await SchedulingWorld.Create(nameof(Move_CannotClearTheRoom_NullMeansUnchanged));
        Room room = await w.AddRoom();
        AppointmentDto created = await w.CreateAppointment(SchedulingWorld.Future(10), room: room);

        // FINDING (confirmed): AppointmentMoveRequest.RoomId is a nullable "unchanged" marker, so there is no way to
        // express "remove the room" through Move. Only Update can clear a room.
        await Move(w, created.Id, SchedulingWorld.Future(12), room: null);

        Assert.Equal(room.Id, (await w.LoadAppointment(created.Id)).RoomId);
    }

    [Fact]
    public async Task Move_KeepsCheckingTheExistingExclusiveRoomAtTheNewTime()
    {
        await using SchedulingWorld w = await SchedulingWorld.Create(nameof(Move_KeepsCheckingTheExistingExclusiveRoomAtTheNewTime));
        Room room = await w.AddRoom(allowConcurrent: false);
        Client otherClient = await w.AddClient("Other", "Client");
        Employee otherEmployee = await w.AddEmployee("Other");
        AppointmentDto created = await w.CreateAppointment(SchedulingWorld.Future(10), room: room);
        await w.CreateAppointment(SchedulingWorld.Future(14), client: otherClient, employee: otherEmployee, room: room);

        // The request names no room at all; the appointment's own (unchanged) room is still checked for the new slot.
        BusinessRuleExceptionHolder holder = await BusinessRuleExceptionHolder.Capture(
            () => Move(w, created.Id, SchedulingWorld.Future(14)));

        Assert.Equal(ErrorCodes.AppointmentOverlap, holder.Code);
        Assert.Contains("Prostorija", holder.Message);
    }

    [Fact]
    public async Task Move_ToARoomOfAnotherCompany_IsRejected()
    {
        await using SchedulingWorld w = await SchedulingWorld.Create(nameof(Move_ToARoomOfAnotherCompany_IsRejected));
        Company other = await w.AddCompany("Other");
        Room foreign = await w.AddRoom(other);
        AppointmentDto created = await w.CreateAppointment(SchedulingWorld.Future(10));

        await SchedulingAssert.BusinessRule(ErrorCodes.RoomCompanyMismatch,
            () => Move(w, created.Id, SchedulingWorld.Future(10), room: foreign));
    }

    #endregion

    #region Clients and overlap

    [Fact]
    public async Task Move_IntoASlotWhereAnActiveClientIsBusy_IsRejected()
    {
        await using SchedulingWorld w = await SchedulingWorld.Create(nameof(Move_IntoASlotWhereAnActiveClientIsBusy_IsRejected));
        Employee other = await w.AddEmployee("Other");
        AppointmentDto created = await w.CreateAppointment(SchedulingWorld.Future(10));
        Client busyElsewhere = w.Client;
        await w.SeedAppointment(SchedulingWorld.Future(14), employee: other, bookings: (busyElsewhere, BookingStatus.Confirmed, 50m));

        await SchedulingAssert.BusinessRule(ErrorCodes.AppointmentOverlap, () => Move(w, created.Id, SchedulingWorld.Future(14)));
    }

    [Fact]
    public async Task Move_OnlyConsidersClientsWhoseBookingIsStillActive()
    {
        await using SchedulingWorld w = await SchedulingWorld.Create(nameof(Move_OnlyConsidersClientsWhoseBookingIsStillActive));
        Client cancelledClient = await w.AddClient("Cancelled", "Client");
        Employee other = await w.AddEmployee("Other");
        Appointment seeded = await w.SeedAppointment(SchedulingWorld.Future(10),
            bookings: new[] { (w.Client, BookingStatus.Confirmed, 50m), (cancelledClient, BookingStatus.Cancelled, 50m) });
        await w.SeedAppointment(SchedulingWorld.Future(14), employee: other, bookings: (cancelledClient, BookingStatus.Confirmed, 50m));

        // The Cancelled Booking's client is busy at the target time, but that Booking is excluded from the client check.
        AppointmentDto moved = await Move(w, seeded.Id.Value, SchedulingWorld.Future(14));

        Assert.Equal(SchedulingWorld.Future(14), moved.StartsAt);
    }

    #endregion

    #region Lifecycle

    [Fact]
    public async Task Move_ACancelledAppointment_IsRejected()
    {
        await using SchedulingWorld w = await SchedulingWorld.Create(nameof(Move_ACancelledAppointment_IsRejected));
        AppointmentDto created = await w.CreateAppointment(SchedulingWorld.Future(10));
        await w.Appointments.Cancel(w.OrganizationId, w.ActorUserId, true, created.Id, new AppointmentCancelRequest());

        await SchedulingAssert.BusinessRule(ErrorCodes.AppointmentNotMovable, () => Move(w, created.Id, SchedulingWorld.Future(12)));

        Assert.Equal(SchedulingWorld.Future(10), (await w.LoadAppointment(created.Id)).StartsAt);
    }

    [Fact]
    public async Task Move_ACompletedAppointment_IsCurrentlyAllowed()
    {
        await using SchedulingWorld w = await SchedulingWorld.Create(nameof(Move_ACompletedAppointment_IsCurrentlyAllowed));
        AppointmentDto completed = await w.CompleteNew(w.CompleteRequest(SchedulingWorld.Past(10), paymentMethod: PaymentMethod.Cash));

        // FINDING (confirmed): only Cancelled blocks a move; a Completed (paid, commissioned) appointment can still be dragged.
        AppointmentDto moved = await Move(w, completed.Id, SchedulingWorld.Past(15));

        Appointment a = await w.LoadAppointment(completed.Id);
        Assert.Equal(AppointmentStatus.Completed, a.Status);
        Assert.Equal(SchedulingWorld.Past(15), a.StartsAt);
        Assert.Equal(AppointmentStatus.Completed, moved.Status);
    }

    [Fact]
    public async Task Move_ACompletedAppointment_LeavesBookingsPaymentsPackageAndCommissionUntouched()
    {
        await using SchedulingWorld w = await SchedulingWorld.Create(nameof(Move_ACompletedAppointment_LeavesBookingsPaymentsPackageAndCommissionUntouched));
        await w.AddCommissionRule(w.Employee, w.Service, CommissionCalculationType.Percentage, 10m);
        ClientPackage package = await w.AddClientPackage(w.Client, w.Service, 5, new DateTimeOffset(2035, 1, 1, 0, 0, 0, TimeSpan.Zero));
        Client payer = await w.AddClient("Payer", "Client");
        Employee otherEmployee = await w.AddEmployee("Other");
        AppointmentDto completed = await w.CompleteNew(w.CompleteRequest(SchedulingWorld.Past(10), settlements: new[]
        {
            new AppointmentClientSettlement { ClientId = w.Client.Id.Value, ClientPackageId = package.Id },
            new AppointmentClientSettlement { ClientId = payer.Id.Value, PaymentMethod = PaymentMethod.Card, IsPaid = true }
        }));
        int commissionsBefore = (await w.LoadCommissionEntries()).Count;

        await Move(w, completed.Id, SchedulingWorld.Past(15), employee: otherEmployee);

        Appointment a = await w.LoadAppointment(completed.Id);
        Booking packageBooking = a.Bookings.Single(b => b.ClientId == w.Client.Id);
        Booking cardBooking = a.Bookings.Single(b => b.ClientId == payer.Id);
        Assert.True(packageBooking.PackageCoverageApplied);
        Assert.Equal(package.Id, packageBooking.ClientPackageId);
        Assert.Equal(BookingStatus.Completed, cardBooking.Status);
        Assert.Single(await w.LoadPayments(cardBooking.Id.Value));
        Assert.Equal(4, (await w.LoadClientPackage(package.Id.Value)).ServiceEntries.Single().RemainingEntries);
        // Commission entries are keyed on the Booking (and store the employee at earn-time): moving to another employee
        // neither re-earns nor reverses them.
        Assert.Equal(commissionsBefore, (await w.LoadCommissionEntries()).Count);
        Assert.All(await w.LoadCommissionEntries(), e => Assert.Equal(w.Employee.Id, e.EmployeeId));
    }

    [Fact]
    public async Task Move_UnknownAppointment_IsNotFound()
    {
        await using SchedulingWorld w = await SchedulingWorld.Create(nameof(Move_UnknownAppointment_IsNotFound));

        await SchedulingAssert.NotFound(() => Move(w, Guid.NewGuid(), SchedulingWorld.Future(10)));
    }

    #endregion

    #region Group occurrences

    [Fact]
    public async Task Move_ATrainerlessGroupOccurrenceWithoutAnEmployeeInTheRequest_IsCurrentlyNotFound()
    {
        await using SchedulingWorld w = await SchedulingWorld.Create(nameof(Move_ATrainerlessGroupOccurrenceWithoutAnEmployeeInTheRequest_IsCurrentlyNotFound));
        Appointment occurrence = await w.SeedAppointment(SchedulingWorld.Future(10), form: AppointmentForm.Group, employee: null,
            bookings: (w.Client, BookingStatus.Confirmed, 50m));

        // FINDING: the effective employee falls back to Guid.Empty for an occurrence with no trainer, and structural
        // eligibility then fails on "Employee not found". A trainerless group occurrence cannot be re-timed unless a
        // trainer is assigned in the same request.
        await SchedulingAssert.NotFound(() => Move(w, occurrence.Id.Value, SchedulingWorld.Future(12)));

        Assert.Equal(SchedulingWorld.Future(10), (await w.LoadAppointment(occurrence.Id.Value)).StartsAt);
    }

    [Fact]
    public async Task Move_ATrainerlessGroupOccurrence_CanBeGivenATrainerWhileMoving_AndTheAssignmentIsAudited()
    {
        await using SchedulingWorld w = await SchedulingWorld.Create(nameof(Move_ATrainerlessGroupOccurrence_CanBeGivenATrainerWhileMoving_AndTheAssignmentIsAudited));
        Appointment occurrence = await w.SeedAppointment(SchedulingWorld.Future(10), form: AppointmentForm.Group, employee: null,
            bookings: (w.Client, BookingStatus.Confirmed, 50m));

        await Move(w, occurrence.Id.Value, SchedulingWorld.Future(12), employee: w.Employee);

        Assert.Equal(w.Employee.Id, (await w.LoadAppointment(occurrence.Id.Value)).EmployeeId);
        AppointmentAuditLog audit = Assert.Single(await w.LoadAuditLog(occurrence.Id.Value), l => l.ChangeType == "EmployeeId");
        Assert.Null(audit.OldValue);
        Assert.Equal(w.Employee.Id.ToString(), audit.NewValue);
    }

    [Fact]
    public async Task Move_AGroupOccurrence_KeepsItsGroupLinksAndBookings()
    {
        await using SchedulingWorld w = await SchedulingWorld.Create(nameof(Move_AGroupOccurrence_KeepsItsGroupLinksAndBookings));
        Appointment occurrence = await w.SeedAppointment(SchedulingWorld.Future(10), form: AppointmentForm.Group, employee: w.Employee,
            bookings: (w.Client, BookingStatus.Confirmed, 50m));

        await Move(w, occurrence.Id.Value, SchedulingWorld.Future(12));

        Appointment a = await w.LoadAppointment(occurrence.Id.Value);
        Assert.Equal(AppointmentForm.Group, a.Form);
        Assert.Equal(SchedulingWorld.Future(12), a.StartsAt);
        Assert.Single(a.Bookings);
    }

    #endregion

    #region Authorization scope on Move

    [Fact]
    public async Task Move_OwnScopeCaller_CanMoveTheirOwnAppointment()
    {
        await using SchedulingWorld w = await SchedulingWorld.Create(nameof(Move_OwnScopeCaller_CanMoveTheirOwnAppointment));
        AppointmentDto created = await w.CreateAppointment(SchedulingWorld.Future(10));

        AppointmentDto moved = await Move(w, created.Id, SchedulingWorld.Future(12), hasFullScope: false, userId: w.Employee.UserId);

        Assert.Equal(SchedulingWorld.Future(12), moved.StartsAt);
    }

    [Fact]
    public async Task Move_OwnScopeCaller_CannotMoveAnotherEmployeesAppointment()
    {
        await using SchedulingWorld w = await SchedulingWorld.Create(nameof(Move_OwnScopeCaller_CannotMoveAnotherEmployeesAppointment));
        Employee other = await w.AddEmployee("Other");
        AppointmentDto created = await w.CreateAppointment(SchedulingWorld.Future(10));

        await SchedulingAssert.BusinessRule(ErrorCodes.NotOwner,
            () => Move(w, created.Id, SchedulingWorld.Future(12), hasFullScope: false, userId: other.UserId));
    }

    [Fact]
    public async Task Move_OwnScopeCaller_CanHandTheirAppointmentToAnotherEmployee()
    {
        await using SchedulingWorld w = await SchedulingWorld.Create(nameof(Move_OwnScopeCaller_CanHandTheirAppointmentToAnotherEmployee));
        Employee other = await w.AddEmployee("Other");
        AppointmentDto created = await w.CreateAppointment(SchedulingWorld.Future(10));

        // FINDING: like Update, ownership is checked against the CURRENT employee only, not the target employee.
        await Move(w, created.Id, SchedulingWorld.Future(10), employee: other, hasFullScope: false, userId: w.Employee.UserId);

        Assert.Equal(other.Id, (await w.LoadAppointment(created.Id)).EmployeeId);
    }

    #endregion
}
