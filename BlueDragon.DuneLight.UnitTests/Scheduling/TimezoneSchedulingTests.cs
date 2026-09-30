#nullable disable
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using BlueDragon.DuneLight.Core.DTOs.Appointments;
using BlueDragon.DuneLight.Core.DTOs.Groups;
using BlueDragon.DuneLight.Core.DTOs.Organization;
using BlueDragon.DuneLight.Core.Enums;
using BlueDragon.DuneLight.Core.Interfaces.Organization;
using BlueDragon.DuneLight.Core.Shared;
using BlueDragon.DuneLight.Core.Shared.Exceptions;
using BlueDragon.DuneLight.Infrastructure.Domain.Models.Appointments;
using BlueDragon.DuneLight.Infrastructure.Services;
using ServiceEntity = BlueDragon.DuneLight.Infrastructure.Domain.Models.Catalog.Service;

namespace BlueDragon.DuneLight.UnitTests.Scheduling;

/// <summary>
/// Timezone foundation (F-19 fix): scheduling in an Organization whose business timezone is Europe/Zagreb. Working
/// hours are 08:00-20:00 LOCAL, absences and holidays are LOCAL calendar dates, appointments and breaks are UTC instants.
/// Every expectation is written as an explicit UTC instant, and the suite passes unchanged on UTC, Europe/Zagreb,
/// America/New_York and Asia/Tokyo hosts — the host timezone never influences a result.
/// Zagreb 2031: CET (+1) until Sun 2031-03-30, CEST (+2) until Sun 2031-10-26.
/// </summary>
public class TimezoneSchedulingTests
{
    private const string Zagreb = "Europe/Zagreb";

    private static DateTimeOffset Z(int y, int mo, int d, int h, int mi = 0) => new(y, mo, d, h, mi, 0, TimeSpan.Zero);

    /// <summary>A calendar date as a client would send it (midnight in its own offset — only the wall date matters).</summary>
    private static DateTimeOffset Day(int y, int mo, int d) => new(y, mo, d, 0, 0, 0, TimeSpan.FromHours(1));

    private static Task<SchedulingWorld> ZagrebWorld(string name) => SchedulingWorld.Create(name, Zagreb);

    private static AvailableSlotsQuery SlotsOn(SchedulingWorld w, DateTimeOffset day) => new()
    {
        ServiceId = w.Service.Id.Value, CompanyId = w.Company.Id.Value, Date = day
    };

    private static async Task<TimeSpan[]> SlotStarts(SchedulingWorld w, DateTimeOffset day) =>
        Assert.Single(await w.Appointments.GetAvailableSlots(w.OrganizationId, SlotsOn(w, day))).Slots.Select(s => s.Start).ToArray();

    #region Absences are local calendar dates

    [Fact]
    public async Task Absence_OnTheFirstDay_BlocksAnAppointmentThatLocalDay()
    {
        await using SchedulingWorld w = await ZagrebWorld(nameof(Absence_OnTheFirstDay_BlocksAnAppointmentThatLocalDay));
        await w.AddAbsence(w.Employee, Day(2031, 3, 3));

        // 10:00 local = 09:00Z (CET).
        await SchedulingAssert.BusinessRule(ErrorCodes.EmployeeAbsent, () => w.CreateAppointment(Z(2031, 3, 3, 9)));
    }

    [Fact]
    public async Task Absence_UsesTheLocalDate_NotTheUtcDate_OfTheAppointment()
    {
        await using SchedulingWorld w = await ZagrebWorld(nameof(Absence_UsesTheLocalDate_NotTheUtcDate_OfTheAppointment));
        await w.AddAbsence(w.Employee, Day(2031, 3, 4));

        // 2031-03-03 23:30Z is 00:30 on the 4th in Zagreb → the absence of the 4th applies (reported before the hours).
        await SchedulingAssert.BusinessRule(ErrorCodes.EmployeeAbsent, () => w.CreateAppointment(Z(2031, 3, 3, 23, 30)));
        // 2031-03-03 22:30Z is still the 3rd locally (23:30) → no absence, only outside working hours.
        await SchedulingAssert.BusinessRule(ErrorCodes.OutsideWorkingHours, () => w.CreateAppointment(Z(2031, 3, 3, 22, 30)));
    }

    [Fact]
    public async Task Absence_MultiDayRange_IsInclusiveOnBothLocalDates()
    {
        await using SchedulingWorld w = await ZagrebWorld(nameof(Absence_MultiDayRange_IsInclusiveOnBothLocalDates));
        await w.AddAbsenceRange(w.Employee, Day(2031, 3, 3), Day(2031, 3, 5));

        await SchedulingAssert.BusinessRule(ErrorCodes.EmployeeAbsent, () => w.CreateAppointment(Z(2031, 3, 3, 9)));
        await SchedulingAssert.BusinessRule(ErrorCodes.EmployeeAbsent, () => w.CreateAppointment(Z(2031, 3, 5, 9)));
        AppointmentDto before = await w.CreateAppointment(Z(2031, 3, 2, 9));
        AppointmentDto after = await w.CreateAppointment(Z(2031, 3, 6, 9));

        Assert.Equal(AppointmentStatus.Scheduled, before.Status);
        Assert.Equal(AppointmentStatus.Scheduled, after.Status);
    }

    #endregion

    #region Working hours are local wall-clock times

    [Fact]
    public async Task WorkingHours_StandardTime_BoundariesAreLocal()
    {
        await using SchedulingWorld w = await ZagrebWorld(nameof(WorkingHours_StandardTime_BoundariesAreLocal));

        // 08:00 local = 07:00Z and 19:30-20:00 local = 18:30Z: both exactly inside.
        await w.CreateAppointment(Z(2031, 3, 3, 7));
        await w.CreateAppointment(Z(2031, 3, 3, 18, 30));

        // 07:30 local (06:30Z) starts before the day; 19:31 local (18:31Z) ends after 20:00.
        await SchedulingAssert.BusinessRule(ErrorCodes.OutsideWorkingHours, () => w.CreateAppointment(Z(2031, 3, 4, 6, 30)));
        await SchedulingAssert.BusinessRule(ErrorCodes.OutsideWorkingHours, () => w.CreateAppointment(Z(2031, 3, 4, 18, 31)));
    }

    [Fact]
    public async Task WorkingHours_SummerTime_BoundariesAreLocal()
    {
        await using SchedulingWorld w = await ZagrebWorld(nameof(WorkingHours_SummerTime_BoundariesAreLocal));

        // CEST: 08:00 local = 06:00Z, 19:30 local = 17:30Z.
        await w.CreateAppointment(Z(2031, 7, 14, 6));
        await w.CreateAppointment(Z(2031, 7, 14, 17, 30));

        // 18:00Z would be inside 08-20 in UTC terms but is 20:00 local — outside.
        await SchedulingAssert.BusinessRule(ErrorCodes.OutsideWorkingHours, () => w.CreateAppointment(Z(2031, 7, 15, 18)));
        await SchedulingAssert.BusinessRule(ErrorCodes.OutsideWorkingHours, () => w.CreateAppointment(Z(2031, 7, 15, 5, 30)));
    }

    [Fact]
    public async Task WorkingHours_OnTheSpringForwardDay_UseTheSameLocalWindow()
    {
        await using SchedulingWorld w = await ZagrebWorld(nameof(WorkingHours_OnTheSpringForwardDay_UseTheSameLocalWindow));

        // Sunday 2031-03-30 is already CEST from 03:00: 08:00 local = 06:00Z.
        await w.CreateAppointment(Z(2031, 3, 30, 6));
        await SchedulingAssert.BusinessRule(ErrorCodes.OutsideWorkingHours, () => w.CreateAppointment(Z(2031, 3, 30, 5, 30)));
    }

    #endregion

    #region Holidays are local calendar dates

    [Fact]
    public async Task Holiday_IsEvaluatedOnTheLocalDate()
    {
        await using SchedulingWorld w = await ZagrebWorld(nameof(Holiday_IsEvaluatedOnTheLocalDate));
        await w.AddCompanyHoliday(w.Company, Day(2031, 3, 4));

        await SchedulingAssert.BusinessRule(ErrorCodes.CompanyClosedHoliday, () => w.CreateAppointment(Z(2031, 3, 4, 9)));
        // 00:30 on the 4th locally (23:30Z on the 3rd) is on the holiday as well.
        await SchedulingAssert.BusinessRule(ErrorCodes.CompanyClosedHoliday, () => w.CreateAppointment(Z(2031, 3, 3, 23, 30)));
        // The day before is an ordinary day.
        AppointmentDto dayBefore = await w.CreateAppointment(Z(2031, 3, 3, 9));
        Assert.Equal(AppointmentStatus.Scheduled, dayBefore.Status);
    }

    #endregion

    #region Available slots are local business time

    [Fact]
    public async Task AvailableSlots_WithAnAppointment_AreLocalTimes_InWinterAndSummer()
    {
        await using SchedulingWorld w = await ZagrebWorld(nameof(AvailableSlots_WithAnAppointment_AreLocalTimes_InWinterAndSummer));
        await w.CreateAppointment(Z(2031, 3, 3, 9));  // 10:00-10:30 local (CET)
        await w.CreateAppointment(Z(2031, 7, 14, 8)); // 10:00-10:30 local (CEST)

        foreach (DateTimeOffset day in new[] { Day(2031, 3, 3), Day(2031, 7, 14) })
        {
            TimeSpan[] starts = await SlotStarts(w, day);

            Assert.Equal(new TimeSpan(8, 0, 0), starts.First());
            Assert.Equal(new TimeSpan(19, 30, 0), starts.Last());
            Assert.Contains(new TimeSpan(9, 30, 0), starts);
            Assert.DoesNotContain(new TimeSpan(9, 45, 0), starts);
            Assert.DoesNotContain(new TimeSpan(10, 0, 0), starts);
            Assert.DoesNotContain(new TimeSpan(10, 15, 0), starts);
            Assert.Contains(new TimeSpan(10, 30, 0), starts);
        }
    }

    [Fact]
    public async Task AvailableSlots_WithABreak_ExcludeTheLocalBreakWindow()
    {
        await using SchedulingWorld w = await ZagrebWorld(nameof(AvailableSlots_WithABreak_ExcludeTheLocalBreakWindow));
        await w.AddScheduleBreak(w.Employee, Z(2031, 3, 3, 13), 60); // 14:00-15:00 local

        TimeSpan[] starts = await SlotStarts(w, Day(2031, 3, 3));

        Assert.Contains(new TimeSpan(13, 30, 0), starts);
        Assert.DoesNotContain(new TimeSpan(13, 45, 0), starts);
        Assert.DoesNotContain(new TimeSpan(14, 30, 0), starts);
        Assert.Contains(new TimeSpan(15, 0, 0), starts);
    }

    [Fact]
    public async Task AvailableSlots_OnDstTransitionDays_UseLocalTimeOfThatDay()
    {
        await using SchedulingWorld w = await ZagrebWorld(nameof(AvailableSlots_OnDstTransitionDays_UseLocalTimeOfThatDay));
        await w.CreateAppointment(Z(2031, 3, 30, 8));  // spring-forward Sunday: 10:00 local (CEST)
        await w.CreateAppointment(Z(2031, 10, 26, 9)); // fall-back Sunday: 10:00 local (CET again)

        foreach (DateTimeOffset day in new[] { Day(2031, 3, 30), Day(2031, 10, 26) })
        {
            TimeSpan[] starts = await SlotStarts(w, day);

            Assert.Equal(new TimeSpan(8, 0, 0), starts.First());
            Assert.Contains(new TimeSpan(9, 30, 0), starts);
            Assert.DoesNotContain(new TimeSpan(10, 0, 0), starts);
            Assert.Contains(new TimeSpan(10, 30, 0), starts);
        }
    }

    [Fact]
    public async Task AvailableSlots_OnAnAbsenceDay_AreEmpty()
    {
        await using SchedulingWorld w = await ZagrebWorld(nameof(AvailableSlots_OnAnAbsenceDay_AreEmpty));
        await w.AddAbsence(w.Employee, Day(2031, 3, 3));

        Assert.Empty(await SlotStarts(w, Day(2031, 3, 3)));
        Assert.NotEmpty(await SlotStarts(w, Day(2031, 3, 4)));
    }

    #endregion

    #region Recurrence keeps the local wall clock across DST

    [Fact]
    public async Task RecurringAppointments_AcrossSpringForward_StayAtTheSameLocalTime()
    {
        await using SchedulingWorld w = await ZagrebWorld(nameof(RecurringAppointments_AcrossSpringForward_StayAtTheSameLocalTime));

        List<AppointmentDto> created = await w.Appointments.CreateRecurring(w.OrganizationId, w.ActorUserId, true, new RecurringAppointmentCreateRequest
        {
            RecurrenceType = RecurrenceType.Weekly,
            ServiceId = w.Service.Id.Value,
            EmployeeId = w.Employee.Id.Value,
            CompanyId = w.Company.Id.Value,
            ClientIds = new List<Guid> { w.Client.Id.Value },
            FirstOccurrenceStartsAt = Z(2031, 3, 24, 9), // Monday 10:00 CET
            EndDate = Z(2031, 4, 7, 12)
        });

        Assert.Equal(new[] { Z(2031, 3, 24, 9), Z(2031, 3, 31, 8), Z(2031, 4, 7, 8) },
            created.Select(a => a.StartsAt).OrderBy(s => s).ToArray());
    }

    [Fact]
    public async Task GroupOccurrences_AcrossSpringForward_StayAtTheSlotsLocalTime()
    {
        await using SchedulingWorld w = await ZagrebWorld(nameof(GroupOccurrences_AcrossSpringForward_StayAtTheSlotsLocalTime));
        ServiceEntity groupService = await w.AddGroupService();
        GroupDto group = await w.CreateGroup(groupService, capacity: 5); // Monday 10:00 slot

        GenerateGroupAppointmentsResult result = await w.GenerateOccurrences(group, Day(2031, 3, 24), Day(2031, 4, 7));

        Assert.Equal(new[] { Z(2031, 3, 24, 9), Z(2031, 3, 31, 8), Z(2031, 4, 7, 8) },
            result.Created.Select(c => c.StartsAt).OrderBy(s => s).ToArray());
        Assert.All(await Task.WhenAll(result.Created.Select(c => w.LoadAppointment(c.Id))),
            a => Assert.Equal(TimeSpan.Zero, a.StartsAt.Offset)); // persisted and read back as UTC instants
    }

    #endregion

    #region Organization timezone setting

    [Fact]
    public async Task Settings_ExposeAndUpdateTheIanaTimeZone_AndTheCalendarFollows()
    {
        await using SchedulingWorld w = await ZagrebWorld(nameof(Settings_ExposeAndUpdateTheIanaTimeZone_AndTheCalendarFollows));
        IOrganizationSettingsService settings = w.Resolve<IOrganizationSettingsService>();
        IOrganizationCalendarService calendars = w.Resolve<IOrganizationCalendarService>();

        Assert.Equal(Zagreb, (await settings.GetSettings(w.OrganizationId)).TimeZone);
        Assert.Equal(new TimeSpan(10, 0, 0), (await calendars.GetCalendar(w.OrganizationId)).LocalTimeOfDay(Z(2031, 3, 3, 9)));

        OrganizationSettingsDto updated = await settings.UpdateTimeZone(w.OrganizationId, w.ActorUserId,
            new OrganizationTimeZoneUpdateRequest { TimeZone = "America/New_York" });

        Assert.Equal("America/New_York", updated.TimeZone);
        Assert.Equal(new TimeSpan(4, 0, 0), (await calendars.GetCalendar(w.OrganizationId)).LocalTimeOfDay(Z(2031, 3, 3, 9)));
    }

    [Theory]
    [InlineData("Central European Standard Time")]
    [InlineData("Europe/Atlantis")]
    [InlineData("")]
    public async Task Settings_RejectAnUnknownOrNonIanaTimeZone(string timeZone)
    {
        await using SchedulingWorld w = await ZagrebWorld($"{nameof(Settings_RejectAnUnknownOrNonIanaTimeZone)}-{timeZone.Length}");
        IOrganizationSettingsService settings = w.Resolve<IOrganizationSettingsService>();

        await Assert.ThrowsAsync<ValidationAppException>(() => settings.UpdateTimeZone(w.OrganizationId, w.ActorUserId,
            new OrganizationTimeZoneUpdateRequest { TimeZone = timeZone }));
        Assert.Equal(Zagreb, (await settings.GetSettings(w.OrganizationId)).TimeZone);
    }

    #endregion
}
