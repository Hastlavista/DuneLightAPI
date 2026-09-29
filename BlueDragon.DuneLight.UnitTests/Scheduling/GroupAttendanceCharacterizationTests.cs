#nullable disable
using System;
using System.Linq;
using System.Threading.Tasks;
using BlueDragon.DuneLight.Core.DTOs.Groups;
using BlueDragon.DuneLight.Core.Enums;
using BlueDragon.DuneLight.Core.Shared;
using BlueDragon.DuneLight.Infrastructure.Domain.Contexts;
using BlueDragon.DuneLight.Infrastructure.Domain.Models.Appointments;
using BlueDragon.DuneLight.Infrastructure.Domain.Models.Clients;
using BlueDragon.DuneLight.Infrastructure.Domain.Models.Employees;
using Microsoft.EntityFrameworkCore;
using ServiceEntityAlias = BlueDragon.DuneLight.Infrastructure.Domain.Models.Catalog.Service;

namespace BlueDragon.DuneLight.UnitTests.Scheduling;

/// <summary>
/// CHARACTERIZATION: how GROUP ATTENDANCE is represented today. There is no attendance entity and no "arrived" state:
/// attendance IS the Booking status. The legacy endpoint /api/groups/appointments/{id}/attendance is a thin adapter over
/// IBookingService.SetStatus that maps <c>Attended = true</c> to Completed and <c>Attended = false</c> to NoShow, and reads back
/// Confirmed → null (not yet recorded), Completed → true, NoShow / Cancelled → false.
///
/// "Expected" (active members without a Booking row) is a legacy shape: since generation now creates a Confirmed Booking per
/// active member, it is normally empty.
/// </summary>
public class GroupAttendanceCharacterizationTests
{
    private static async Task<(SchedulingWorld W, GroupDto Group, Appointment Occurrence)> Arrange(string name, int capacity = 4)
    {
        SchedulingWorld w = await SchedulingWorld.Create(name);
        ServiceEntityAlias svc = await w.AddGroupService();
        GroupDto group = await w.CreateGroup(svc, capacity);
        await w.AddGroupMember(group, w.Client);
        return (w, group, await w.GenerateSingleOccurrence(group));
    }

    private static Task<GroupAttendanceListDto> Set(SchedulingWorld w, Guid appointmentId, Client client, bool attended, bool hasFullScope = true, Guid? userId = null,
        decimal? amount = null, PaymentMethod? method = null, bool isPaid = true) =>
        w.GroupAttendance.SetAttendance(w.OrganizationId, userId ?? w.ActorUserId, hasFullScope, appointmentId,
            new SetGroupAttendanceRequest { ClientId = client.Id.Value, Attended = attended, Amount = amount, PaymentMethod = method, IsPaid = isPaid });

    #region Reading attendance

    [Fact]
    public async Task Attendance_BeforeAnyCheckIn_ListsEveryMemberAsRecordedWithAttendedNull_AndNothingExpected()
    {
        (SchedulingWorld w, _, Appointment occurrence) = await Arrange(nameof(Attendance_BeforeAnyCheckIn_ListsEveryMemberAsRecordedWithAttendedNull_AndNothingExpected));
        await using SchedulingWorld _w = w;

        GroupAttendanceListDto list = await w.GroupAttendance.GetAttendance(w.OrganizationId, occurrence.Id.Value);

        Assert.Empty(list.Expected);
        GroupAttendanceEntryDto entry = Assert.Single(list.Recorded);
        Assert.Null(entry.Attended);
        Assert.True(entry.IsMember);
        Assert.Equal(15m, entry.Amount);
        Assert.False(entry.IsPaid);
    }

    [Fact]
    public async Task Attendance_AMemberWithoutABookingRow_AppearsUnderExpected()
    {
        (SchedulingWorld w, _, Appointment occurrence) = await Arrange(nameof(Attendance_AMemberWithoutABookingRow_AppearsUnderExpected));
        await using SchedulingWorld _w = w;
        await using (DatabaseContext db = w.NewDb())
        {
            Booking row = await db.Bookings.SingleAsync(b => b.AppointmentId == occurrence.Id);
            db.Bookings.Remove(row); // the legacy / pre-migration shape
            await db.SaveChangesAsync();
        }

        GroupAttendanceListDto list = await w.GroupAttendance.GetAttendance(w.OrganizationId, occurrence.Id.Value);

        Assert.Empty(list.Recorded);
        GroupAttendanceEntryDto expected = Assert.Single(list.Expected);
        Assert.Equal(w.Client.Id, expected.ClientId);
        Assert.Null(expected.Attended);
        Assert.True(expected.IsMember);
    }

    [Fact]
    public async Task Attendance_OnAnIndividualAppointment_IsNotFound()
    {
        await using SchedulingWorld w = await SchedulingWorld.Create(nameof(Attendance_OnAnIndividualAppointment_IsNotFound));
        var created = await w.CreateAppointment(SchedulingWorld.Future(10));

        await SchedulingAssert.NotFound(() => w.GroupAttendance.GetAttendance(w.OrganizationId, created.Id));
    }

    #endregion

    #region Writing attendance

    [Fact]
    public async Task SetAttendance_True_CompletesTheBooking_False_MarksNoShow_AndTheListMirrorsIt()
    {
        (SchedulingWorld w, _, Appointment occurrence) = await Arrange(nameof(SetAttendance_True_CompletesTheBooking_False_MarksNoShow_AndTheListMirrorsIt));
        await using SchedulingWorld _w = w;

        GroupAttendanceListDto attended = await Set(w, occurrence.Id.Value, w.Client, attended: true);
        Assert.True(Assert.Single(attended.Recorded).Attended);
        Assert.Equal(BookingStatus.Completed, (await w.LoadBooking(occurrence.Id.Value, w.Client)).Status);

        GroupAttendanceListDto absent = await Set(w, occurrence.Id.Value, w.Client, attended: false);
        Assert.False(Assert.Single(absent.Recorded).Attended);
        Booking b = await w.LoadBooking(occurrence.Id.Value, w.Client);
        Assert.Equal(BookingStatus.NoShow, b.Status);
        Assert.Equal(2, b.StatusVersion); // Confirmed(0) -> Completed(1) -> NoShow(2): toggling is allowed for groups
    }

    [Fact]
    public async Task SetAttendance_ACancelledBooking_IsReadBackAsNotAttended()
    {
        (SchedulingWorld w, _, Appointment occurrence) = await Arrange(nameof(SetAttendance_ACancelledBooking_IsReadBackAsNotAttended));
        await using SchedulingWorld _w = w;
        await w.SetBookingStatus(occurrence.Id.Value, w.Client, BookingStatus.Cancelled);

        GroupAttendanceListDto list = await w.GroupAttendance.GetAttendance(w.OrganizationId, occurrence.Id.Value);

        Assert.False(Assert.Single(list.Recorded).Attended); // Cancelled is folded into "false" by the adapter
    }

    [Fact]
    public async Task SetAttendance_PassesTheSettlementFieldsThrough_ToTheBookingCheckIn()
    {
        (SchedulingWorld w, _, Appointment occurrence) = await Arrange(nameof(SetAttendance_PassesTheSettlementFieldsThrough_ToTheBookingCheckIn));
        await using SchedulingWorld _w = w;

        GroupAttendanceListDto list = await Set(w, occurrence.Id.Value, w.Client, attended: true, amount: 12m, method: PaymentMethod.Cash);

        GroupAttendanceEntryDto entry = Assert.Single(list.Recorded);
        Assert.Equal(12m, entry.Amount);
        Assert.Equal(15m, entry.SuggestedAmount);
        Assert.Equal(12m, entry.PaidAmount);
        Assert.True(entry.IsPaid);
        Assert.Equal(AttendanceCoverageType.SinglePaid, entry.CoverageType);
    }

    [Fact]
    public async Task SetAttendance_ForAGuestBeforeTheOccurrenceStarts_IsRejected()
    {
        (SchedulingWorld w, _, Appointment occurrence) = await Arrange(nameof(SetAttendance_ForAGuestBeforeTheOccurrenceStarts_IsRejected));
        await using SchedulingWorld _w = w;
        Client guest = await w.AddClient("Guest", "Client");

        await SchedulingAssert.BusinessRule(ErrorCodes.AttendanceBeforeStart, () => Set(w, occurrence.Id.Value, guest, attended: true));

        Assert.DoesNotContain((await w.LoadAppointment(occurrence.Id.Value)).Bookings, b => b.ClientId == guest.Id);
    }

    [Fact]
    public async Task SetAttendance_ForAGuestAfterTheStart_CreatesTheBookingOnTheFly_EvenWhenTheOccurrenceIsFull()
    {
        await using SchedulingWorld w = await SchedulingWorld.Create(nameof(SetAttendance_ForAGuestAfterTheStart_CreatesTheBookingOnTheFly_EvenWhenTheOccurrenceIsFull));
        ServiceEntityAlias svc = await w.AddGroupService();
        GroupDto group = await w.CreateGroup(svc, capacity: 1);
        Client member = await w.AddClient("Member", "Client");
        Client guest = await w.AddClient("Guest", "Client");
        Appointment past = await w.SeedAppointment(SchedulingWorld.Past(10), AppointmentStatus.Scheduled, AppointmentForm.Group,
            employee: w.Employee, service: svc, groupId: group.Id, durationMinutes: 60,
            bookings: (member, BookingStatus.Confirmed, 15m));

        GroupAttendanceListDto list = await Set(w, past.Id.Value, guest, attended: true);

        // Capacity is enforced when a Booking takes a Confirmed seat; a guest recorded straight as attended never does.
        GroupAttendanceEntryDto entry = list.Recorded.Single(e => e.ClientId == guest.Id);
        Assert.True(entry.Attended);
        Assert.False(entry.IsMember);
        Assert.Equal(2, (await w.LoadAppointment(past.Id.Value)).Bookings.Count); // 2 bookings on a capacity-1 group
    }

    #endregion

    #region Authorization scope

    [Fact]
    public async Task SetAttendance_OwnScopeTrainer_CanRecordOnTheirOwnOccurrence()
    {
        (SchedulingWorld w, _, Appointment occurrence) = await Arrange(nameof(SetAttendance_OwnScopeTrainer_CanRecordOnTheirOwnOccurrence));
        await using SchedulingWorld _w = w;

        GroupAttendanceListDto list = await Set(w, occurrence.Id.Value, w.Client, attended: true, hasFullScope: false, userId: w.Employee.UserId);

        Assert.True(Assert.Single(list.Recorded).Attended);
    }

    [Fact]
    public async Task SetAttendance_OwnScopeTrainer_CannotRecordOnAnotherTrainersOccurrence()
    {
        (SchedulingWorld w, _, Appointment occurrence) = await Arrange(nameof(SetAttendance_OwnScopeTrainer_CannotRecordOnAnotherTrainersOccurrence));
        await using SchedulingWorld _w = w;
        Employee other = await w.AddEmployee("Other");

        await SchedulingAssert.BusinessRule(ErrorCodes.NotOwner,
            () => Set(w, occurrence.Id.Value, w.Client, attended: true, hasFullScope: false, userId: other.UserId));
    }

    [Fact]
    public async Task SetAttendance_OwnScopeCaller_CannotRecordOnATrainerlessOccurrence()
    {
        await using SchedulingWorld w = await SchedulingWorld.Create(nameof(SetAttendance_OwnScopeCaller_CannotRecordOnATrainerlessOccurrence));
        ServiceEntityAlias svc = await w.AddGroupService();
        GroupDto group = await w.CreateGroup(svc, capacity: 4, withTrainer: false);
        await w.AddGroupMember(group, w.Client);
        Appointment occurrence = await w.GenerateSingleOccurrence(group);

        // There is no owning employee to compare with, so only full-scope callers can operate the occurrence.
        await SchedulingAssert.BusinessRule(ErrorCodes.NotOwner,
            () => Set(w, occurrence.Id.Value, w.Client, attended: true, hasFullScope: false, userId: w.Employee.UserId));
    }

    #endregion
}
