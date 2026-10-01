#nullable disable
using System;
using System.Linq;
using System.Threading.Tasks;
using BlueDragon.DuneLight.Core.DTOs.Appointments;
using BlueDragon.DuneLight.Core.DTOs.Groups;
using BlueDragon.DuneLight.Core.Enums;
using BlueDragon.DuneLight.Core.Shared;
using BlueDragon.DuneLight.Core.Shared.Exceptions;
using BlueDragon.DuneLight.Infrastructure.Domain.Models.Appointments;
using BlueDragon.DuneLight.Infrastructure.Domain.Models.Clients;
using BlueDragon.DuneLight.Infrastructure.Domain.Models.Employees;
using BlueDragon.DuneLight.Infrastructure.Handlers.Interfaces;
using BlueDragon.DuneLight.Infrastructure.Utils;
using ServiceEntity = BlueDragon.DuneLight.Infrastructure.Domain.Models.Catalog.Service;

namespace BlueDragon.DuneLight.UnitTests.Scheduling;

/// <summary>
/// CHARACTERIZATION of the appointment "own" scope, now decided by <see cref="AppointmentOwnership"/> (S3). The caller is
/// always resolved on the backend (User → Employee in the caller's organization); since D3A "assigned" means the
/// employee assigned to the appointment's single segment (before D3A: Appointment.EmployeeId). The service-level tests (messages included) were verified against the pre-S3 code.
/// </summary>
public class AppointmentOwnershipCharacterizationTests
{
    private const string Message = "not owner";

    private static IEmployeeHandler Employees(SchedulingWorld w) => w.Resolve<IEmployeeHandler>();

    /// <summary>D3A: assignment lives on the appointment's single segment.</summary>
    private static Appointment AssignedTo(SchedulingWorld w, Guid? employeeId)
    {
        Appointment appointment = new() { Id = Guid.NewGuid(), OrganizationId = w.OrganizationId };
        AppointmentFrameMutator.NewSegment(appointment, new AppointmentFrame(Guid.NewGuid(), employeeId, null, SchedulingWorld.Future(10), 30));
        return appointment;
    }

    #region AppointmentOwnership helper (real IEmployeeHandler)

    [Fact]
    public async Task EnsureCallerIsAssigned_TheAssignedEmployee_Passes()
    {
        await using SchedulingWorld w = await SchedulingWorld.Create(nameof(EnsureCallerIsAssigned_TheAssignedEmployee_Passes));

        await AppointmentOwnership.EnsureCallerIsAssigned(
            Employees(w), w.OrganizationId, w.Employee.UserId, false, AssignedTo(w, w.Employee.Id), Message);
    }

    [Fact]
    public async Task EnsureCallerIsAssigned_AnotherEmployee_IsNotOwner_WithTheGivenMessage()
    {
        await using SchedulingWorld w = await SchedulingWorld.Create(nameof(EnsureCallerIsAssigned_AnotherEmployee_IsNotOwner_WithTheGivenMessage));
        Employee other = await w.AddEmployee("Other");

        BusinessRuleException ex = await SchedulingAssert.BusinessRule(ErrorCodes.NotOwner,
            () => AppointmentOwnership.EnsureCallerIsAssigned(
                Employees(w), w.OrganizationId, other.UserId, false, AssignedTo(w, w.Employee.Id), Message));
        Assert.Equal(Message, ex.Message);
    }

    [Fact]
    public async Task EnsureCallerIsAssigned_AUserWithoutAnEmployeeRecord_IsNotOwner()
    {
        await using SchedulingWorld w = await SchedulingWorld.Create(nameof(EnsureCallerIsAssigned_AUserWithoutAnEmployeeRecord_IsNotOwner));

        await SchedulingAssert.BusinessRule(ErrorCodes.NotOwner,
            () => AppointmentOwnership.EnsureCallerIsAssigned(
                Employees(w), w.OrganizationId, Guid.NewGuid(), false, AssignedTo(w, w.Employee.Id), Message));
    }

    [Fact]
    public async Task EnsureCallerIsAssigned_ATrainerlessAppointment_BelongsToNobodyInOwnScope()
    {
        await using SchedulingWorld w = await SchedulingWorld.Create(nameof(EnsureCallerIsAssigned_ATrainerlessAppointment_BelongsToNobodyInOwnScope));

        await SchedulingAssert.BusinessRule(ErrorCodes.NotOwner,
            () => AppointmentOwnership.EnsureCallerIsAssigned(
                Employees(w), w.OrganizationId, w.Employee.UserId, false, AssignedTo(w, null), Message));
    }

    [Fact]
    public async Task EnsureCallerIsAssigned_FullScope_PassesForAnyone_WithoutResolvingTheCaller()
    {
        await using SchedulingWorld w = await SchedulingWorld.Create(nameof(EnsureCallerIsAssigned_FullScope_PassesForAnyone_WithoutResolvingTheCaller));

        await AppointmentOwnership.EnsureCallerIsAssigned(Employees(w), w.OrganizationId, Guid.NewGuid(), true, AssignedTo(w, null), Message);
        await AppointmentOwnership.EnsureCallerIsAssigned(null, w.OrganizationId, Guid.NewGuid(), true, AssignedTo(w, Guid.NewGuid()), Message);
    }

    [Fact]
    public async Task EnsureCallerIsAssigned_TheCallerIsResolvedOnlyInsideTheRequestedOrganization()
    {
        await using SchedulingWorld w = await SchedulingWorld.Create(nameof(EnsureCallerIsAssigned_TheCallerIsResolvedOnlyInsideTheRequestedOrganization));
        await using SchedulingWorld other = await SchedulingWorld.Create(nameof(EnsureCallerIsAssigned_TheCallerIsResolvedOnlyInsideTheRequestedOrganization) + "-other");
        Appointment claimed = AssignedTo(w, other.Employee.Id); // an appointment claiming another tenant's employee

        await SchedulingAssert.BusinessRule(ErrorCodes.NotOwner,
            () => AppointmentOwnership.EnsureCallerIsAssigned(Employees(w), w.OrganizationId, other.Employee.UserId, false, claimed, Message));

        // Control: in its own organization the same user does resolve to that employee.
        await AppointmentOwnership.EnsureCallerIsAssigned(Employees(w), other.OrganizationId, other.Employee.UserId, false, claimed, Message);
    }

    [Fact]
    public async Task EnsureCallerIsEmployee_OwnScopeMayOnlySchedulePerItself()
    {
        await using SchedulingWorld w = await SchedulingWorld.Create(nameof(EnsureCallerIsEmployee_OwnScopeMayOnlySchedulePerItself));
        Employee other = await w.AddEmployee("Other");
        IEmployeeHandler employees = Employees(w);

        await AppointmentOwnership.EnsureCallerIsEmployee(employees, w.OrganizationId, w.Employee.UserId, false, w.Employee.Id.Value, Message);
        await AppointmentOwnership.EnsureCallerIsEmployee(employees, w.OrganizationId, Guid.NewGuid(), true, other.Id.Value, Message);

        BusinessRuleException ex = await SchedulingAssert.BusinessRule(ErrorCodes.NotOwner,
            () => AppointmentOwnership.EnsureCallerIsEmployee(employees, w.OrganizationId, w.Employee.UserId, false, other.Id.Value, Message));
        Assert.Equal(Message, ex.Message);
        await SchedulingAssert.BusinessRule(ErrorCodes.NotOwner,
            () => AppointmentOwnership.EnsureCallerIsEmployee(employees, w.OrganizationId, Guid.NewGuid(), false, other.Id.Value, Message));
    }

    #endregion

    #region Services (behaviour and messages unchanged by S3)

    [Fact]
    public async Task AppointmentCancel_OwnScope_AssignedTrainerMayCancel_OthersGetTheAppointmentMessage()
    {
        await using SchedulingWorld w = await SchedulingWorld.Create(nameof(AppointmentCancel_OwnScope_AssignedTrainerMayCancel_OthersGetTheAppointmentMessage));
        Employee other = await w.AddEmployee("Other");
        AppointmentDto mine = await w.CreateAppointment(SchedulingWorld.Future(10));
        AppointmentDto alsoMine = await w.CreateAppointment(SchedulingWorld.Future(12));

        BusinessRuleException ex = await SchedulingAssert.BusinessRule(ErrorCodes.NotOwner,
            () => w.Appointments.Cancel(w.OrganizationId, other.UserId, false, mine.Id, new AppointmentCancelRequest()));
        Assert.Equal("Trener smije upravljati samo svojim vlastitim terminima.", ex.Message);

        AppointmentDto cancelled = await w.Appointments.Cancel(w.OrganizationId, w.Employee.UserId, false, alsoMine.Id, new AppointmentCancelRequest());
        Assert.Equal(AppointmentStatus.Cancelled, cancelled.Status);
    }

    [Fact]
    public async Task BookingSetStatus_OwnScope_AssignedTrainerMayCancel_OthersGetTheBookingMessage()
    {
        await using SchedulingWorld w = await SchedulingWorld.Create(nameof(BookingSetStatus_OwnScope_AssignedTrainerMayCancel_OthersGetTheBookingMessage));
        Employee other = await w.AddEmployee("Other");
        AppointmentDto created = await w.CreateAppointment(SchedulingWorld.Future(10));

        BusinessRuleException ex = await SchedulingAssert.BusinessRule(ErrorCodes.NotOwner,
            () => w.SetBookingStatus(created.Id, w.Client, BookingStatus.Cancelled, hasFullScope: false, userId: other.UserId));
        Assert.Equal("Trener smije upravljati samo bookinzima na svojim vlastitim terminima.", ex.Message);

        BookingDto cancelled = await w.SetBookingStatus(created.Id, w.Client, BookingStatus.Cancelled, hasFullScope: false, userId: w.Employee.UserId);
        Assert.Equal(BookingStatusSummary.Cancelled, cancelled.Status);
    }

    [Fact]
    public async Task BookingSetStatus_OwnScope_OnATrainerlessOccurrence_IsNotOwnerForEveryone()
    {
        await using SchedulingWorld w = await SchedulingWorld.Create(nameof(BookingSetStatus_OwnScope_OnATrainerlessOccurrence_IsNotOwnerForEveryone));
        Appointment occurrence = await w.SeedAppointment(SchedulingWorld.Future(10), form: AppointmentForm.Group, employee: null,
            bookings: (w.Client, BookingStatus.Confirmed, 15m));

        await SchedulingAssert.BusinessRule(ErrorCodes.NotOwner,
            () => w.SetBookingStatus(occurrence.Id.Value, w.Client, BookingStatus.Cancelled, hasFullScope: false, userId: w.Employee.UserId));

        BookingDto withFullScope = await w.SetBookingStatus(occurrence.Id.Value, w.Client, BookingStatus.Cancelled, hasFullScope: true);
        Assert.Equal(BookingStatusSummary.Cancelled, withFullScope.Status);
    }

    [Fact]
    public async Task WaitlistJoin_OwnScope_AssignedTrainerMayJoin_OthersGetTheWaitlistMessage()
    {
        await using SchedulingWorld w = await SchedulingWorld.Create(nameof(WaitlistJoin_OwnScope_AssignedTrainerMayJoin_OthersGetTheWaitlistMessage));
        Employee other = await w.AddEmployee("Other");
        ServiceEntity groupService = await w.AddGroupService();
        GroupDto group = await w.CreateGroup(groupService, capacity: 1);
        await w.AddGroupMember(group, w.Client); // the only seat is taken
        Appointment occurrence = await w.GenerateSingleOccurrence(group);
        Client waiter = await w.AddClient("Waiter", "Client");

        BusinessRuleException ex = await SchedulingAssert.BusinessRule(ErrorCodes.NotOwner,
            () => w.Waitlist.Join(w.OrganizationId, other.UserId, false, occurrence.Id.Value, new WaitlistJoinRequest { ClientId = waiter.Id.Value }));
        Assert.Equal("Trener smije upravljati samo listom čekanja na svojim vlastitim terminima.", ex.Message);

        WaitlistEntryDto joined = await w.Waitlist.Join(
            w.OrganizationId, w.Employee.UserId, false, occurrence.Id.Value, new WaitlistJoinRequest { ClientId = waiter.Id.Value });
        Assert.Equal(waiter.Id.Value, joined.ClientId);
        Assert.Single((await w.LoadWaitlist(occurrence.Id.Value)).Where(e => e.ClientId == waiter.Id));
    }

    #endregion
}
