#nullable disable
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using BlueDragon.DuneLight.Core.DTOs.Appointments;
using BlueDragon.DuneLight.Core.DTOs.Groups;
using BlueDragon.DuneLight.Core.Enums;
using BlueDragon.DuneLight.Core.Shared;
using BlueDragon.DuneLight.Infrastructure.Domain.Contexts;
using BlueDragon.DuneLight.Infrastructure.Domain.Models.Appointments;
using BlueDragon.DuneLight.Infrastructure.Domain.Models.Catalog;
using BlueDragon.DuneLight.Infrastructure.Domain.Models.Clients;
using BlueDragon.DuneLight.Infrastructure.Domain.Models.Employees;
using Microsoft.EntityFrameworkCore;
using ServiceEntityAlias = BlueDragon.DuneLight.Infrastructure.Domain.Models.Catalog.Service;

namespace BlueDragon.DuneLight.UnitTests.Scheduling;

/// <summary>
/// CHARACTERIZATION: the read side. Every appointment read model is built around ONE service, ONE (nullable) employee and
/// ONE (nullable) room — <see cref="AppointmentScheduleCellDto"/> (calendar), <see cref="AppointmentDto"/> (detail/edit),
/// <see cref="ClientAppointmentHistoryDto"/> (client history) all expose singular EmployeeId/ServiceId/RoomId plus their
/// names, joined LIVE from Employee/Service/Room/Company at read time. Group occurrences use the same DTOs and add
/// GroupId/GroupName plus AttendanceCount/ExpectedCount. Available-slot search is per employee and looks only at the
/// employee's working hours, appointments and breaks (never rooms or clients).
/// </summary>
public class AppointmentReadModelCharacterizationTests
{
    private static AppointmentScheduleQuery Day(SchedulingWorld w, Action<AppointmentScheduleQuery> mutate = null)
    {
        AppointmentScheduleQuery q = new() { From = SchedulingWorld.FutureDay, To = SchedulingWorld.FutureDay.AddDays(1).AddTicks(-1) };
        mutate?.Invoke(q);
        return q;
    }

    #region Schedule feed

    [Fact]
    public async Task ScheduleCell_ForAnIndividualAppointment_ExposesOneServiceOneEmployeeOneRoomAndTheClientNames()
    {
        await using SchedulingWorld w = await SchedulingWorld.Create(nameof(ScheduleCell_ForAnIndividualAppointment_ExposesOneServiceOneEmployeeOneRoomAndTheClientNames));
        Room room = await w.AddRoom(capacity: 10);
        Client partner = await w.AddClient("Partner", "Client");
        AppointmentDto created = await w.CreateAppointment(SchedulingWorld.Future(10), room: room, extraClients: partner);

        AppointmentScheduleCellDto cell = Assert.Single(await w.Appointments.GetSchedule(w.OrganizationId, Day(w)));

        Assert.Equal(created.Id, cell.Id);
        Assert.Equal(SchedulingWorld.Future(10), cell.StartsAt);
        Assert.Equal(30, cell.DurationMinutes);
        Assert.Equal(w.Service.Id, cell.ServiceId);
        Assert.Equal(w.Service.Name, cell.ServiceName);
        Assert.Equal(w.Employee.Id, cell.EmployeeId);
        Assert.Equal($"{w.Employee.FirstName} {w.Employee.LastName}", cell.EmployeeName);
        Assert.Equal(w.Company.Id, cell.CompanyId);
        Assert.Equal(room.Id, cell.RoomId);
        Assert.Equal(room.Name, cell.RoomName);
        Assert.Equal(AppointmentStatus.Scheduled, cell.Status);
        Assert.False(cell.IsCancelled);
        Assert.Equal(AppointmentForm.Individual, cell.Form);
        Assert.Equal(2, cell.ClientIds.Count);
        Assert.Contains($"{partner.FirstName} {partner.LastName}", cell.ClientNames);
        Assert.Null(cell.AttendanceCount);
        Assert.Null(cell.ExpectedCount);
    }

    [Fact]
    public async Task ScheduleCell_NamesAreReadLive_ARenamedServiceChangesAlreadyBookedCells()
    {
        await using SchedulingWorld w = await SchedulingWorld.Create(nameof(ScheduleCell_NamesAreReadLive_ARenamedServiceChangesAlreadyBookedCells));
        await w.CreateAppointment(SchedulingWorld.Future(10));
        await w.UpdateService(w.Service, name: "Renamed later");

        AppointmentScheduleCellDto cell = Assert.Single(await w.Appointments.GetSchedule(w.OrganizationId, Day(w)));

        Assert.Equal("Renamed later", cell.ServiceName); // no snapshot of the service name
    }

    [Fact]
    public async Task ScheduleCell_ListsClientsOfEveryBookingStatus_IncludingCancelledOnes()
    {
        await using SchedulingWorld w = await SchedulingWorld.Create(nameof(ScheduleCell_ListsClientsOfEveryBookingStatus_IncludingCancelledOnes));
        Client cancelled = await w.AddClient("Cancelled", "Client");
        await w.SeedAppointment(SchedulingWorld.Future(10),
            bookings: new[] { (w.Client, BookingStatus.Confirmed, 50m), (cancelled, BookingStatus.Cancelled, 50m) });

        AppointmentScheduleCellDto cell = Assert.Single(await w.Appointments.GetSchedule(w.OrganizationId, Day(w)));

        Assert.Equal(2, cell.ClientIds.Count);
    }

    [Fact]
    public async Task ScheduleCell_ForAGroupOccurrence_CarriesGroupNameAttendanceAndExpectedCounts()
    {
        await using SchedulingWorld w = await SchedulingWorld.Create(nameof(ScheduleCell_ForAGroupOccurrence_CarriesGroupNameAttendanceAndExpectedCounts));
        ServiceEntityAlias svc = await w.AddGroupService();
        GroupDto group = await w.CreateGroup(svc, capacity: 5);
        Client second = await w.AddClient("Second", "Member");
        await w.AddGroupMember(group, w.Client);
        await w.AddGroupMember(group, second);
        Appointment occurrence = await w.GenerateSingleOccurrence(group);
        await w.SetBookingStatus(occurrence.Id.Value, w.Client, BookingStatus.Completed);

        AppointmentScheduleCellDto cell = Assert.Single(await w.Appointments.GetSchedule(w.OrganizationId, Day(w)));

        Assert.Equal(AppointmentForm.Group, cell.Form);
        Assert.Equal(group.Id, cell.GroupId);
        Assert.Equal(group.Name, cell.GroupName);
        Assert.Equal(1, cell.AttendanceCount);  // Completed bookings
        Assert.Equal(2, cell.ExpectedCount);    // active roster members
    }

    [Fact]
    public async Task ScheduleCell_ForATrainerlessGroupOccurrence_HasANullEmployee()
    {
        await using SchedulingWorld w = await SchedulingWorld.Create(nameof(ScheduleCell_ForATrainerlessGroupOccurrence_HasANullEmployee));
        ServiceEntityAlias svc = await w.AddGroupService();
        GroupDto group = await w.CreateGroup(svc, capacity: 5, withTrainer: false);
        await w.GenerateSingleOccurrence(group);

        AppointmentScheduleCellDto cell = Assert.Single(await w.Appointments.GetSchedule(w.OrganizationId, Day(w)));

        Assert.Null(cell.EmployeeId);
        Assert.Null(cell.EmployeeName);
    }

    [Fact]
    public async Task Schedule_IncludesCancelledAppointments_FlaggedAsCancelled()
    {
        await using SchedulingWorld w = await SchedulingWorld.Create(nameof(Schedule_IncludesCancelledAppointments_FlaggedAsCancelled));
        AppointmentDto created = await w.CreateAppointment(SchedulingWorld.Future(10));
        await w.Appointments.Cancel(w.OrganizationId, w.ActorUserId, true, created.Id, SchedulingWorld.BusinessCancel());

        AppointmentScheduleCellDto cell = Assert.Single(await w.Appointments.GetSchedule(w.OrganizationId, Day(w)));

        Assert.True(cell.IsCancelled);
        Assert.Equal(AppointmentStatus.Cancelled, cell.Status);
    }

    [Fact]
    public async Task Schedule_FiltersByEmployeeRoomServiceCompanyStatusAndExecutionMode()
    {
        await using SchedulingWorld w = await SchedulingWorld.Create(nameof(Schedule_FiltersByEmployeeRoomServiceCompanyStatusAndExecutionMode));
        Employee otherEmployee = await w.AddEmployee("Other");
        Client otherClient = await w.AddClient("Other", "Client");
        Room room = await w.AddRoom();
        ServiceEntityAlias groupService = await w.AddGroupService();
        AppointmentDto mine = await w.CreateAppointment(SchedulingWorld.Future(10), room: room);
        AppointmentDto theirs = await w.CreateAppointment(SchedulingWorld.Future(10), client: otherClient, employee: otherEmployee);
        AppointmentDto cancelled = await w.CreateAppointment(SchedulingWorld.Future(12));
        await w.Appointments.Cancel(w.OrganizationId, w.ActorUserId, true, cancelled.Id, SchedulingWorld.BusinessCancel());
        GroupDto group = await w.CreateGroup(groupService, capacity: 4, slots: (SchedulingWorld.FutureDay.DayOfWeek, TimeSpan.FromHours(15)));
        Appointment occurrence = await w.GenerateSingleOccurrence(group);

        async Task<List<Guid>> Ids(Action<AppointmentScheduleQuery> f) =>
            (await w.Appointments.GetSchedule(w.OrganizationId, Day(w, f))).Select(c => c.Id).ToList();

        Assert.Equal(4, (await Ids(_ => { })).Count);
        Assert.Equal(new[] { theirs.Id }, await Ids(q => q.EmployeeId = otherEmployee.Id));
        Assert.Equal(new[] { mine.Id }, await Ids(q => q.RoomId = room.Id));
        Assert.Equal(new[] { occurrence.Id.Value }, await Ids(q => q.ServiceId = groupService.Id));
        Assert.Equal(new[] { occurrence.Id.Value }, await Ids(q => q.ExecutionMode = ServiceExecutionMode.Group));
        Assert.Equal(new[] { cancelled.Id }, await Ids(q => q.Status = AppointmentStatus.Cancelled));
        Assert.Empty(await Ids(q => q.CompanyId = Guid.NewGuid()));
    }

    [Fact]
    public async Task Schedule_TheRangeIsInclusiveOnBothEnds_OnTheStartTimeOfTheAppointment()
    {
        await using SchedulingWorld w = await SchedulingWorld.Create(nameof(Schedule_TheRangeIsInclusiveOnBothEnds_OnTheStartTimeOfTheAppointment));
        AppointmentDto created = await w.CreateAppointment(SchedulingWorld.Future(10));

        Assert.Single(await w.Appointments.GetSchedule(w.OrganizationId, new AppointmentScheduleQuery { From = SchedulingWorld.Future(10), To = SchedulingWorld.Future(10) }));
        Assert.Empty(await w.Appointments.GetSchedule(w.OrganizationId, new AppointmentScheduleQuery { From = SchedulingWorld.Future(10).AddSeconds(1), To = SchedulingWorld.Future(11) }));
        Assert.Empty(await w.Appointments.GetSchedule(w.OrganizationId, new AppointmentScheduleQuery { From = SchedulingWorld.Future(8), To = SchedulingWorld.Future(10).AddSeconds(-1) }));
        Assert.NotNull(created);
    }

    [Fact]
    public async Task Schedule_NeverShowsAnotherOrganizationsAppointments()
    {
        await using SchedulingWorld w = await SchedulingWorld.Create(nameof(Schedule_NeverShowsAnotherOrganizationsAppointments) + "-A");
        await using SchedulingWorld other = await SchedulingWorld.Create(nameof(Schedule_NeverShowsAnotherOrganizationsAppointments) + "-B");
        await other.CreateAppointment(SchedulingWorld.Future(10));

        Assert.Empty(await w.Appointments.GetSchedule(w.OrganizationId, Day(w)));
        Assert.Single(await other.Appointments.GetSchedule(other.OrganizationId, Day(other)));
    }

    [Fact]
    public async Task GetById_OfAnotherOrganizationsAppointment_IsNotFound()
    {
        await using SchedulingWorld w = await SchedulingWorld.Create(nameof(GetById_OfAnotherOrganizationsAppointment_IsNotFound) + "-A");
        await using SchedulingWorld other = await SchedulingWorld.Create(nameof(GetById_OfAnotherOrganizationsAppointment_IsNotFound) + "-B");
        AppointmentDto foreign = await other.CreateAppointment(SchedulingWorld.Future(10));

        await SchedulingAssert.NotFound(() => w.Appointments.GetById(w.OrganizationId, foreign.Id));
    }

    #endregion

    #region Detail DTO

    [Fact]
    public async Task AppointmentDto_CarriesOneBookingDtoPerClient_WithDerivedFinancialsAndPayments()
    {
        await using SchedulingWorld w = await SchedulingWorld.Create(nameof(AppointmentDto_CarriesOneBookingDtoPerClient_WithDerivedFinancialsAndPayments));
        Client partner = await w.AddClient("Partner", "Client");
        AppointmentDto created = await w.CreateAppointment(SchedulingWorld.Future(10), extraClients: partner);
        await w.PayBookingViaCheckout(created.Bookings.Single(b => b.ClientId == partner.Id).Id, partner, 20m);

        AppointmentDto dto = await w.Appointments.GetById(w.OrganizationId, created.Id);

        Assert.Equal(2, dto.Bookings.Count);
        BookingDto paying = dto.Bookings.Single(b => b.ClientId == partner.Id);
        Assert.Equal(20m, paying.PaidAmount);
        Assert.Equal(30m, paying.OutstandingAmount);
        Assert.Single(paying.Payments);
        BookingDto other = dto.Bookings.Single(b => b.ClientId == w.Client.Id);
        Assert.Equal(0m, other.PaidAmount);
        Assert.Empty(other.Payments);
        Assert.NotNull(paying.ClientName);
    }

    #endregion

    #region Client history and employee history

    [Fact]
    public async Task ClientHistory_ListsOnlyTheClientsOwnBooking_NewestFirst_IncludingCancelledAndNoShow()
    {
        await using SchedulingWorld w = await SchedulingWorld.Create(nameof(ClientHistory_ListsOnlyTheClientsOwnBooking_NewestFirst_IncludingCancelledAndNoShow));
        Client partner = await w.AddClient("Partner", "Client");
        AppointmentDto first = await w.CreateAppointment(SchedulingWorld.Past(10), extraClients: partner);
        AppointmentDto second = await w.CreateAppointment(SchedulingWorld.Future(12));
        await w.SetBookingStatus(first.Id, w.Client, BookingStatus.NoShow);
        await w.SetBookingStatus(second.Id, w.Client, BookingStatus.Cancelled);

        PagedResult<ClientAppointmentHistoryDto> page = await w.Appointments.GetByClient(w.OrganizationId, w.Client.Id.Value, new PagedRequest());

        Assert.Equal(2, page.TotalCount);
        Assert.Equal(new[] { second.Id, first.Id }, page.Items.Select(i => i.Id).ToArray()); // newest StartsAt first
        Assert.Equal(new[] { BookingStatusSummary.Cancelled, BookingStatusSummary.NoShow }, page.Items.Select(i => i.BookingStatus).ToArray());
        Assert.All(page.Items, i => Assert.Equal(w.Client.Id.Value.ToString(), w.Client.Id.Value.ToString()));
        // The partner's booking on the shared appointment is not exposed through this client's history.
        Assert.DoesNotContain(page.Items, i => i.BookingId == first.Bookings.Single(b => b.ClientId == partner.Id).Id);
    }

    [Fact]
    public async Task ClientHistory_FinancialFieldsAreDerivedFromTheClientsOwnBooking()
    {
        await using SchedulingWorld w = await SchedulingWorld.Create(nameof(ClientHistory_FinancialFieldsAreDerivedFromTheClientsOwnBooking));
        AppointmentDto completed = await w.CompleteNew(w.CompleteRequest(SchedulingWorld.Past(10), paymentMethod: PaymentMethod.Cash));

        ClientAppointmentHistoryDto item = Assert.Single(
            (await w.Appointments.GetByClient(w.OrganizationId, w.Client.Id.Value, new PagedRequest())).Items);

        Assert.Equal(50m, item.Amount);
        Assert.Equal(50m, item.PaidAmount);
        Assert.Equal(0m, item.OutstandingAmount);
        Assert.True(item.IsPaid);
        Assert.Equal(completed.Bookings.Single().Id, item.BookingId);
        Assert.Equal(AppointmentStatus.Closed, item.Status);
    }

    [Fact]
    public async Task EmployeeHistory_ListsOnlyCompletedAppointments_Paged()
    {
        await using SchedulingWorld w = await SchedulingWorld.Create(nameof(EmployeeHistory_ListsOnlyCompletedAppointments_Paged));
        Client c2 = await w.AddClient("Second", "Client");
        Client c3 = await w.AddClient("Third", "Client");
        await w.CompleteNew(w.CompleteRequest(SchedulingWorld.Past(9)));
        await w.CompleteNew(w.CompleteRequest(SchedulingWorld.Past(11), client: c2));
        await w.CreateAppointment(SchedulingWorld.Future(10), client: c3); // Scheduled: not part of the history

        PagedResult<AppointmentDto> firstPage = await w.Appointments.GetByEmployee(w.OrganizationId, w.Employee.Id.Value, new PagedRequest { Page = 1, PageSize = 1 });
        PagedResult<AppointmentDto> secondPage = await w.Appointments.GetByEmployee(w.OrganizationId, w.Employee.Id.Value, new PagedRequest { Page = 2, PageSize = 1 });

        Assert.Equal(2, firstPage.TotalCount);
        Assert.Equal(SchedulingWorld.Past(11), Assert.Single(firstPage.Items).StartsAt); // newest first
        Assert.Equal(SchedulingWorld.Past(9), Assert.Single(secondPage.Items).StartsAt);
    }

    #endregion

    #region Available slots (per employee)

    private static AvailableSlotsQuery SlotsFor(SchedulingWorld w, Guid? employeeId = null) => new()
    {
        ServiceId = w.Service.Id.Value, CompanyId = w.Company.Id.Value, Date = SchedulingWorld.Day(SchedulingWorld.FutureDay), EmployeeId = employeeId
    };

    [Fact]
    public async Task AvailableSlots_AreOfferedOnA15MinuteGridInsideWorkingHours_ForEachCapableEmployee()
    {
        await using SchedulingWorld w = await SchedulingWorld.Create(nameof(AvailableSlots_AreOfferedOnA15MinuteGridInsideWorkingHours_ForEachCapableEmployee));

        EmployeeAvailableSlotsDto row = Assert.Single(await w.Appointments.GetAvailableSlots(w.OrganizationId, SlotsFor(w)));

        Assert.Equal(w.Employee.Id, row.EmployeeId);
        // 08:00-20:00 window, 30-minute service, 15-minute step: first 08:00, last 19:30 -> 47 slots.
        Assert.Equal(47, row.Slots.Count);
        // CHANGED in T1: slot Start/End su TimeOnly ("HH:mm:ss"), ne TimeSpan.
        Assert.Equal(new TimeOnly(8, 0), row.Slots.First().Start);
        Assert.Equal(new TimeOnly(8, 30), row.Slots.First().End);
        Assert.Equal(new TimeOnly(19, 30), row.Slots.Last().Start);
    }

    [Fact]
    public async Task AvailableSlots_ExcludeSlotsThatOverlapTheEmployeesAppointmentsAndBreaks()
    {
        await using SchedulingWorld w = await SchedulingWorld.Create(nameof(AvailableSlots_ExcludeSlotsThatOverlapTheEmployeesAppointmentsAndBreaks));
        await w.CreateAppointment(SchedulingWorld.Future(10));                          // 10:00-10:30
        await w.AddScheduleBreak(w.Employee, SchedulingWorld.Future(14), 60);           // 14:00-15:00

        EmployeeAvailableSlotsDto row = Assert.Single(await w.Appointments.GetAvailableSlots(w.OrganizationId, SlotsFor(w)));

        TimeOnly[] starts = row.Slots.Select(s => s.Start).ToArray(); // CHANGED in T1: TimeOnly umjesto TimeSpan
        Assert.DoesNotContain(new TimeOnly(9, 45), starts);   // ends 10:15 -> overlaps the appointment
        Assert.DoesNotContain(new TimeOnly(10, 0), starts);
        Assert.DoesNotContain(new TimeOnly(10, 15), starts);
        Assert.Contains(new TimeOnly(9, 30), starts);         // ends exactly at 10:00
        Assert.Contains(new TimeOnly(10, 30), starts);        // starts exactly at 10:30
        Assert.DoesNotContain(new TimeOnly(13, 45), starts);  // overlaps the break
        Assert.DoesNotContain(new TimeOnly(14, 30), starts);
        Assert.Contains(new TimeOnly(15, 0), starts);
    }

    [Fact]
    public async Task AvailableSlots_DoNotConsiderRoomsOrClients_OnlyTheEmployee()
    {
        await using SchedulingWorld w = await SchedulingWorld.Create(nameof(AvailableSlots_DoNotConsiderRoomsOrClients_OnlyTheEmployee));
        Employee otherEmployee = await w.AddEmployee("Other");
        Room room = await w.AddRoom();
        await w.CreateAppointment(SchedulingWorld.Future(10), employee: otherEmployee, room: room);

        List<EmployeeAvailableSlotsDto> rows = await w.Appointments.GetAvailableSlots(w.OrganizationId, SlotsFor(w));

        // CHANGED in T1: slot Start je TimeOnly.
        // The main employee still offers 10:00 (the room/client being used elsewhere is invisible to this search),
        // while the busy employee does not.
        Assert.Contains(new TimeOnly(10, 0), rows.Single(r => r.EmployeeId == w.Employee.Id).Slots.Select(s => s.Start));
        Assert.DoesNotContain(new TimeOnly(10, 0), rows.Single(r => r.EmployeeId == otherEmployee.Id).Slots.Select(s => s.Start));
    }

    [Fact]
    public async Task AvailableSlots_OmitEmployeesWhoCannotPerformTheServiceOrAreInactive_AndEmployeesOnAbsence()
    {
        await using SchedulingWorld w = await SchedulingWorld.Create(nameof(AvailableSlots_OmitEmployeesWhoCannotPerformTheServiceOrAreInactive_AndEmployeesOnAbsence));
        await w.AddEmployeeRestrictedToAnotherService("Incapable");
        await w.AddEmployee("Inactive", isActive: false);
        Employee absent = await w.AddEmployee("Absent");
        await w.AddAbsence(absent, SchedulingWorld.FutureDay);

        List<EmployeeAvailableSlotsDto> rows = await w.Appointments.GetAvailableSlots(w.OrganizationId, SlotsFor(w));

        Assert.Equal(2, rows.Count); // the default employee + the absent one
        Assert.NotEmpty(rows.Single(r => r.EmployeeId == w.Employee.Id).Slots);
        Assert.Empty(rows.Single(r => r.EmployeeId == absent.Id).Slots); // present in the list, but with no slots
    }

    [Fact]
    public async Task AvailableSlots_ForAPastDay_AreEmpty_AndForAnInactiveOrUnofferedService_AreEmpty()
    {
        await using SchedulingWorld w = await SchedulingWorld.Create(nameof(AvailableSlots_ForAPastDay_AreEmpty_AndForAnInactiveOrUnofferedService_AreEmpty));
        AvailableSlotsQuery past = SlotsFor(w);
        past.Date = SchedulingWorld.Day(SchedulingWorld.PastDay);
        Assert.Empty(await w.Appointments.GetAvailableSlots(w.OrganizationId, past));

        ServiceEntityAlias unoffered = await w.AddService(30, 10m, availableAtCompany: false);
        AvailableSlotsQuery notOffered = SlotsFor(w);
        notOffered.ServiceId = unoffered.Id.Value;
        Assert.Empty(await w.Appointments.GetAvailableSlots(w.OrganizationId, notOffered));

        await w.SetServiceActive(w.Service, false);
        Assert.Empty(await w.Appointments.GetAvailableSlots(w.OrganizationId, SlotsFor(w)));
    }

    #endregion

    #region Delete

    [Fact]
    public async Task Delete_OfAnAppointmentCreatedToday_HardDeletesItAndItsBookings()
    {
        await using SchedulingWorld w = await SchedulingWorld.Create(nameof(Delete_OfAnAppointmentCreatedToday_HardDeletesItAndItsBookings));
        AppointmentDto created = await w.CreateAppointment(SchedulingWorld.Future(10));

        await w.Appointments.Delete(w.OrganizationId, w.ActorUserId, created.Id);

        Assert.Equal(0, await w.CountAppointments());
        await using DatabaseContext db = w.NewDb();
        Assert.False(await db.Bookings.AnyAsync(b => b.AppointmentId == created.Id));
    }

    [Fact]
    public async Task Delete_OfAnAppointmentCreatedOnAnEarlierDay_IsRefused_CancelInstead()
    {
        await using SchedulingWorld w = await SchedulingWorld.Create(nameof(Delete_OfAnAppointmentCreatedOnAnEarlierDay_IsRefused_CancelInstead));
        AppointmentDto created = await w.CreateAppointment(SchedulingWorld.Future(10));
        await using (DatabaseContext db = w.NewDb())
        {
            Appointment tracked = await db.Appointments.SingleAsync(a => a.Id == created.Id);
            tracked.CreatedAt = TestClock.UtcNow.AddDays(-1);
            await db.SaveChangesAsync();
        }

        await SchedulingAssert.BusinessRule(ErrorCodes.SameDayOnly, () => w.Appointments.Delete(w.OrganizationId, w.ActorUserId, created.Id));

        Assert.Equal(1, await w.CountAppointments());
    }

    [Fact]
    public async Task Delete_OfAnAppointmentWhoseBookingWasAddedToACheckout_IsRefusedWithADomainError()
    {
        await using SchedulingWorld w = await SchedulingWorld.Create(nameof(Delete_OfAnAppointmentWhoseBookingWasAddedToACheckout_IsRefusedWithADomainError));
        AppointmentDto created = await w.CreateAppointment(SchedulingWorld.Future(10));
        await w.PayBookingViaCheckout(created.Bookings.Single().Id, w.Client, 20m);

        // F-09 FIXED (D3B3B): settlement history (a checkout item / payment on the participation) is business history, so
        // the participation is not "untouched" — the delete is refused with REFERENCED_CANNOT_DELETE instead of the raw
        // checkout_items foreign-key error (before: DbUpdateException).
        await SchedulingAssert.BusinessRule(ErrorCodes.ReferencedCannotDelete,
            () => w.Appointments.Delete(w.OrganizationId, w.ActorUserId, created.Id));

        Assert.Equal(1, await w.CountAppointments());
    }

    [Fact]
    public async Task Delete_UnknownAppointment_IsNotFound()
    {
        await using SchedulingWorld w = await SchedulingWorld.Create(nameof(Delete_UnknownAppointment_IsNotFound));

        await SchedulingAssert.NotFound(() => w.Appointments.Delete(w.OrganizationId, w.ActorUserId, Guid.NewGuid()));
    }

    #endregion
}
