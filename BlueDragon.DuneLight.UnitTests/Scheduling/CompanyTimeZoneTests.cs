#nullable disable
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using BlueDragon.DuneLight.Core.DTOs.Appointments;
using BlueDragon.DuneLight.Core.DTOs.Catalog;
using BlueDragon.DuneLight.Core.DTOs.Dashboard;
using BlueDragon.DuneLight.Core.DTOs.Groups;
using BlueDragon.DuneLight.Core.DTOs.Organization;
using BlueDragon.DuneLight.Core.DTOs.ScheduleBreaks;
using BlueDragon.DuneLight.Core.Enums;
using BlueDragon.DuneLight.Core.Interfaces.Catalog;
using BlueDragon.DuneLight.Core.Interfaces.Dashboard;
using BlueDragon.DuneLight.Core.Interfaces.Organization;
using BlueDragon.DuneLight.Core.Interfaces.ScheduleBreaks;
using BlueDragon.DuneLight.Core.Shared;
using BlueDragon.DuneLight.Core.Shared.Exceptions;
using BlueDragon.DuneLight.Infrastructure.Domain.Contexts;
using BlueDragon.DuneLight.Infrastructure.Domain.Models.Catalog;
using BlueDragon.DuneLight.Infrastructure.Services;
using BlueDragon.DuneLight.Infrastructure.Utils;
using Microsoft.EntityFrameworkCore;
using ServiceEntity = BlueDragon.DuneLight.Infrastructure.Domain.Models.Catalog.Service;

namespace BlueDragon.DuneLight.UnitTests.Scheduling;

/// <summary>
/// Timezone foundation — Company override. A Company's effective timezone is Company.TimeZone ?? Organization.TimeZone
/// (NULL = inherit, never a copy of the Organization value) and every Company-scoped scheduling rule evaluates in it.
/// The Organization is Europe/Zagreb in every test; overrides use America/New_York (EST -5 until Sun 2031-03-09, then
/// EDT -4) and Asia/Tokyo (+9, no DST). Expectations are explicit UTC instants and hold on any host timezone.
/// </summary>
public class CompanyTimeZoneTests
{
    private const string Zagreb = "Europe/Zagreb";
    private const string NewYork = "America/New_York";
    private const string Tokyo = "Asia/Tokyo";

    private static DateTimeOffset Z(int y, int mo, int d, int h, int mi = 0) => new(y, mo, d, h, mi, 0, TimeSpan.Zero);

    /// <summary>A calendar date as a client would send it (only the wall date matters).</summary>
    private static DateTimeOffset Day(int y, int mo, int d) => new(y, mo, d, 0, 0, 0, TimeSpan.FromHours(1));

    private static Task<SchedulingWorld> ZagrebWorld(string name) => SchedulingWorld.Create(name, Zagreb);

    private static async Task SetTimeZone(SchedulingWorld w, Company company, string timeZone)
    {
        await using DatabaseContext db = w.NewDb();
        Company tracked = await db.Companies.SingleAsync(c => c.Id == company.Id);
        tracked.TimeZone = timeZone;
        await db.SaveChangesAsync();
    }

    private static async Task<string> StoredTimeZone(SchedulingWorld w, Guid companyId)
    {
        await using DatabaseContext db = w.NewDb();
        return await db.Companies.Where(c => c.Id == companyId).Select(c => c.TimeZone).SingleAsync();
    }

    private static CompanyUpdateRequest UpdateOf(CompanyDto company, string timeZone) => new()
    {
        Name = company.Name, Address = company.Address, Phone = company.Phone, ColorHex = company.ColorHex,
        Country = company.Country, Note = company.Note, SortOrder = company.SortOrder, TimeZone = timeZone
    };

    #region Company API: inheritance, override, clearing

    [Fact]
    public async Task NewCompany_WithoutTimeZone_InheritsTheOrganizationZone_AndStoresNull()
    {
        await using SchedulingWorld w = await ZagrebWorld(nameof(NewCompany_WithoutTimeZone_InheritsTheOrganizationZone_AndStoresNull));
        ICompanyService companies = w.Resolve<ICompanyService>();

        CompanyDto created = await companies.Create(w.OrganizationId, w.ActorUserId, new CompanyCreateRequest { Name = "Inheriting", Country = "HR" });

        Assert.Null(created.TimeZone);
        Assert.Equal(Zagreb, created.EffectiveTimeZone);
        Assert.Null(await StoredTimeZone(w, created.Id)); // the Organization value is not duplicated into the row
        Assert.Equal(Zagreb, (await companies.GetById(w.OrganizationId, created.Id)).EffectiveTimeZone);
    }

    [Fact]
    public async Task Company_Override_IsPersistedAndExposed_AndClearingRestoresInheritance()
    {
        await using SchedulingWorld w = await ZagrebWorld(nameof(Company_Override_IsPersistedAndExposed_AndClearingRestoresInheritance));
        ICompanyService companies = w.Resolve<ICompanyService>();
        IOrganizationCalendarService calendars = w.Resolve<IOrganizationCalendarService>();

        CompanyDto created = await companies.Create(w.OrganizationId, w.ActorUserId,
            new CompanyCreateRequest { Name = "Tokyo studio", Country = "HR", TimeZone = Tokyo });

        CompanyDto read = await companies.GetById(w.OrganizationId, created.Id);
        Assert.Equal(Tokyo, read.TimeZone);
        Assert.Equal(Tokyo, read.EffectiveTimeZone);
        Assert.Equal(Tokyo, await StoredTimeZone(w, created.Id));
        Assert.Equal(new TimeSpan(18, 0, 0), (await calendars.GetCompanyCalendar(w.OrganizationId, created.Id)).LocalTimeOfDay(Z(2031, 3, 3, 9)));

        CompanyDto switched = await companies.Update(w.OrganizationId, w.ActorUserId, created.Id, UpdateOf(read, NewYork));
        Assert.Equal(NewYork, switched.EffectiveTimeZone);

        CompanyDto cleared = await companies.Update(w.OrganizationId, w.ActorUserId, created.Id, UpdateOf(switched, null));
        Assert.Null(cleared.TimeZone);
        Assert.Equal(Zagreb, cleared.EffectiveTimeZone);
        Assert.Null(await StoredTimeZone(w, created.Id));
        Assert.Equal(new TimeSpan(10, 0, 0), (await calendars.GetCompanyCalendar(w.OrganizationId, created.Id)).LocalTimeOfDay(Z(2031, 3, 3, 9)));

        // Re-overriding after re-inheriting works the same way.
        CompanyDto again = await companies.Update(w.OrganizationId, w.ActorUserId, created.Id, UpdateOf(cleared, Tokyo));
        Assert.Equal(Tokyo, again.EffectiveTimeZone);
    }

    [Theory]
    [InlineData("Central European Standard Time")] // Windows id
    [InlineData("Europe/Atlantis")]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(" Europe/London")]
    [InlineData("Europe/London ")]
    public async Task Company_RejectsAnInvalidTimeZone_OnCreateAndUpdate(string timeZone)
    {
        await using SchedulingWorld w = await ZagrebWorld($"{nameof(Company_RejectsAnInvalidTimeZone_OnCreateAndUpdate)}-{timeZone.GetHashCode():x}");
        ICompanyService companies = w.Resolve<ICompanyService>();
        CompanyDto existing = await companies.GetById(w.OrganizationId, w.Company.Id.Value);

        await Assert.ThrowsAsync<ValidationAppException>(() => companies.Create(w.OrganizationId, w.ActorUserId,
            new CompanyCreateRequest { Name = "Invalid", Country = "HR", TimeZone = timeZone }));
        await Assert.ThrowsAsync<ValidationAppException>(() => companies.Update(w.OrganizationId, w.ActorUserId,
            existing.Id, UpdateOf(existing, timeZone)));

        Assert.Null(await StoredTimeZone(w, existing.Id));
    }

    [Fact]
    public async Task TwoCompanies_InDifferentZones_ResolveDifferentCalendars()
    {
        await using SchedulingWorld w = await ZagrebWorld(nameof(TwoCompanies_InDifferentZones_ResolveDifferentCalendars));
        Company tokyo = await w.AddCompany("Tokyo");
        await SetTimeZone(w, tokyo, Tokyo);
        IOrganizationCalendarService calendars = w.Resolve<IOrganizationCalendarService>();

        Dictionary<Guid, OrganizationCalendar> byCompany = await calendars.GetCompanyCalendars(
            w.OrganizationId, new[] { w.Company.Id.Value, tokyo.Id.Value });

        DateTimeOffset instant = Z(2031, 3, 3, 20);
        Assert.Equal(Zagreb, byCompany[w.Company.Id.Value].TimeZoneId);
        Assert.Equal(Tokyo, byCompany[tokyo.Id.Value].TimeZoneId);
        Assert.Equal(new DateOnly(2031, 3, 3), byCompany[w.Company.Id.Value].LocalDate(instant)); // 21:00 on the 3rd
        Assert.Equal(new DateOnly(2031, 3, 4), byCompany[tokyo.Id.Value].LocalDate(instant)); // 05:00 on the 4th
    }

    [Fact]
    public async Task OrganizationZoneChange_AffectsOnlyCompaniesWithoutAnOverride()
    {
        await using SchedulingWorld w = await ZagrebWorld(nameof(OrganizationZoneChange_AffectsOnlyCompaniesWithoutAnOverride));
        Company tokyo = await w.AddCompany("Tokyo");
        await SetTimeZone(w, tokyo, Tokyo);
        ICompanyService companies = w.Resolve<ICompanyService>();
        IOrganizationCalendarService calendars = w.Resolve<IOrganizationCalendarService>();

        await w.Resolve<IOrganizationSettingsService>().UpdateTimeZone(w.OrganizationId, w.ActorUserId,
            new OrganizationTimeZoneUpdateRequest { TimeZone = NewYork });

        Assert.Equal(NewYork, (await companies.GetById(w.OrganizationId, w.Company.Id.Value)).EffectiveTimeZone);
        Assert.Equal(Tokyo, (await companies.GetById(w.OrganizationId, tokyo.Id.Value)).EffectiveTimeZone);
        Assert.Null(await StoredTimeZone(w, w.Company.Id.Value));
        Assert.Equal(new TimeSpan(4, 0, 0), (await calendars.GetCompanyCalendar(w.OrganizationId, w.Company.Id.Value)).LocalTimeOfDay(Z(2031, 3, 3, 9)));
        Assert.Equal(new TimeSpan(18, 0, 0), (await calendars.GetCompanyCalendar(w.OrganizationId, tokyo.Id.Value)).LocalTimeOfDay(Z(2031, 3, 3, 9)));
    }

    #endregion

    #region Scheduling in the Company's effective zone

    [Fact]
    public async Task WorkingHours_AreTheCompanysLocalHours()
    {
        await using SchedulingWorld w = await ZagrebWorld(nameof(WorkingHours_AreTheCompanysLocalHours));
        await SetTimeZone(w, w.Company, NewYork);

        // 08:00-20:00 EST = 13:00Z-01:00Z. 09:00Z is 10:00 in Zagreb but 04:00 in New York.
        await SchedulingAssert.BusinessRule(ErrorCodes.OutsideWorkingHours, () => w.CreateAppointment(Z(2031, 3, 3, 9)));
        AppointmentDto evening = await w.CreateAppointment(Z(2031, 3, 4, 0)); // 19:00 EST on the 3rd

        Assert.Equal(AppointmentStatus.Scheduled, evening.Status);
    }

    [Fact]
    public async Task SameInstant_IsInsideHoursInOneCompany_AndOutsideInAnother()
    {
        await using SchedulingWorld w = await ZagrebWorld(nameof(SameInstant_IsInsideHoursInOneCompany_AndOutsideInAnother));
        Company newYork = await w.AddCompany("New York");
        await SetTimeZone(w, newYork, NewYork);
        await w.MakeServiceAvailableAt(w.Service, newYork);
        await w.AssignEmployeeToCompany(w.Employee, newYork);

        AppointmentDto inZagreb = await w.CreateAppointment(w.CreateRequest(Z(2031, 3, 3, 9))); // 10:00 CET
        await SchedulingAssert.BusinessRule(ErrorCodes.OutsideWorkingHours,
            () => w.CreateAppointment(w.CreateRequest(Z(2031, 3, 5, 9), company: newYork))); // 04:00 EST

        Assert.Equal(AppointmentStatus.Scheduled, inZagreb.Status);
    }

    [Fact]
    public async Task Absence_IsTheCompanysLocalDate()
    {
        await using SchedulingWorld w = await ZagrebWorld(nameof(Absence_IsTheCompanysLocalDate));
        await SetTimeZone(w, w.Company, NewYork);
        await w.AddAbsence(w.Employee, Day(2031, 3, 3));

        // 2031-03-04 00:00Z is 19:00 on the 3rd in New York (01:00 on the 4th in Zagreb) → the absence of the 3rd applies.
        await SchedulingAssert.BusinessRule(ErrorCodes.EmployeeAbsent, () => w.CreateAppointment(Z(2031, 3, 4, 0)));
        // 2031-03-04 14:00Z is 09:00 on the 4th in New York → no absence.
        AppointmentDto nextDay = await w.CreateAppointment(Z(2031, 3, 4, 14));

        Assert.Equal(AppointmentStatus.Scheduled, nextDay.Status);
    }

    [Fact]
    public async Task Holiday_IsTheCompanysLocalDate()
    {
        await using SchedulingWorld w = await ZagrebWorld(nameof(Holiday_IsTheCompanysLocalDate));
        await SetTimeZone(w, w.Company, NewYork);
        await w.AddCompanyHoliday(w.Company, Day(2031, 3, 3));

        // 19:00 EST on the 3rd (already the 4th in Zagreb) falls on the Company's holiday.
        await SchedulingAssert.BusinessRule(ErrorCodes.CompanyClosedHoliday, () => w.CreateAppointment(Z(2031, 3, 4, 0)));
    }

    [Fact]
    public async Task AvailableSlots_AreTheCompanysLocalTimes()
    {
        await using SchedulingWorld w = await ZagrebWorld(nameof(AvailableSlots_AreTheCompanysLocalTimes));
        await SetTimeZone(w, w.Company, NewYork);
        await w.CreateAppointment(Z(2031, 3, 3, 15)); // 10:00 EST

        EmployeeAvailableSlotsDto slots = Assert.Single(await w.Appointments.GetAvailableSlots(w.OrganizationId,
            new AvailableSlotsQuery { ServiceId = w.Service.Id.Value, CompanyId = w.Company.Id.Value, Date = Day(2031, 3, 3) }));
        TimeSpan[] starts = slots.Slots.Select(s => s.Start).ToArray();

        Assert.Equal(new TimeSpan(8, 0, 0), starts.First());
        Assert.Equal(new TimeSpan(19, 30, 0), starts.Last());
        Assert.DoesNotContain(new TimeSpan(10, 0, 0), starts);
        Assert.Contains(new TimeSpan(10, 30, 0), starts);
    }

    [Fact]
    public async Task RecurringAppointments_KeepTheCompanysLocalTime_AcrossItsDst()
    {
        await using SchedulingWorld w = await ZagrebWorld(nameof(RecurringAppointments_KeepTheCompanysLocalTime_AcrossItsDst));
        await SetTimeZone(w, w.Company, NewYork);

        List<AppointmentDto> created = await w.Appointments.CreateRecurring(w.OrganizationId, w.ActorUserId, true, new RecurringAppointmentCreateRequest
        {
            RecurrenceType = RecurrenceType.Weekly,
            ServiceId = w.Service.Id.Value,
            EmployeeId = w.Employee.Id.Value,
            CompanyId = w.Company.Id.Value,
            ClientIds = new List<Guid> { w.Client.Id.Value },
            FirstOccurrenceStartsAt = Z(2031, 3, 3, 15), // Monday 10:00 EST
            EndDate = Z(2031, 3, 17, 20)
        });

        // US DST starts Sun 2031-03-09: 10:00 EDT = 14:00Z (Zagreb is still on CET, so its zone would not shift).
        Assert.Equal(new[] { Z(2031, 3, 3, 15), Z(2031, 3, 10, 14), Z(2031, 3, 17, 14) },
            created.Select(a => a.StartsAt).OrderBy(s => s).ToArray());
    }

    [Fact]
    public async Task RecurringBreaks_KeepTheOrganizationsLocalTime_AcrossDst_WhenInheriting()
    {
        await using SchedulingWorld w = await ZagrebWorld(nameof(RecurringBreaks_KeepTheOrganizationsLocalTime_AcrossDst_WhenInheriting));

        List<ScheduleBreakDto> created = await w.Resolve<IScheduleBreakService>().CreateRecurring(w.OrganizationId, w.ActorUserId, true,
            new RecurringScheduleBreakCreateRequest
            {
                RecurrenceType = RecurrenceType.Weekly,
                EmployeeId = w.Employee.Id.Value,
                CompanyId = w.Company.Id.Value,
                FirstOccurrenceStartsAt = Z(2031, 3, 24, 11), // Monday 12:00 CET
                DurationMinutes = 30,
                EndDate = Z(2031, 4, 7, 20)
            });

        // Zagreb switches to CEST on Sun 2031-03-30: 12:00 CEST = 10:00Z.
        Assert.Equal(new[] { Z(2031, 3, 24, 11), Z(2031, 3, 31, 10), Z(2031, 4, 7, 10) },
            created.Select(b => b.StartsAt).OrderBy(s => s).ToArray());
    }

    [Fact]
    public async Task RecurringBreaks_KeepTheCompanysLocalTime_AcrossItsDst()
    {
        await using SchedulingWorld w = await ZagrebWorld(nameof(RecurringBreaks_KeepTheCompanysLocalTime_AcrossItsDst));
        await SetTimeZone(w, w.Company, NewYork);

        List<ScheduleBreakDto> created = await w.Resolve<IScheduleBreakService>().CreateRecurring(w.OrganizationId, w.ActorUserId, true,
            new RecurringScheduleBreakCreateRequest
            {
                RecurrenceType = RecurrenceType.Daily,
                EmployeeId = w.Employee.Id.Value,
                CompanyId = w.Company.Id.Value,
                FirstOccurrenceStartsAt = Z(2031, 3, 8, 17), // Saturday 12:00 EST
                DurationMinutes = 30,
                EndDate = Z(2031, 3, 10, 20)
            });

        Assert.Equal(new[] { Z(2031, 3, 8, 17), Z(2031, 3, 9, 16), Z(2031, 3, 10, 16) },
            created.Select(b => b.StartsAt).OrderBy(s => s).ToArray());
    }

    [Fact]
    public async Task GroupOccurrences_UseTheGroupCompanysZone()
    {
        await using SchedulingWorld w = await ZagrebWorld(nameof(GroupOccurrences_UseTheGroupCompanysZone));
        await SetTimeZone(w, w.Company, NewYork);
        ServiceEntity groupService = await w.AddGroupService();
        GroupDto group = await w.CreateGroup(groupService, capacity: 5); // Monday 10:00 slot

        GenerateGroupAppointmentsResult result = await w.GenerateOccurrences(group, Day(2031, 3, 3), Day(2031, 3, 17));

        Assert.Equal(new[] { Z(2031, 3, 3, 15), Z(2031, 3, 10, 14), Z(2031, 3, 17, 14) },
            result.Created.Select(c => c.StartsAt).OrderBy(s => s).ToArray());
    }

    [Fact]
    public async Task Dashboard_DayBoundaries_AreTheCompanysLocalMidnights()
    {
        await using SchedulingWorld w = await ZagrebWorld(nameof(Dashboard_DayBoundaries_AreTheCompanysLocalMidnights));
        await SetTimeZone(w, w.Company, NewYork);
        AppointmentDto morning = await w.CreateAppointment(Z(2031, 3, 3, 14)); // 09:00 EST on the 3rd
        AppointmentDto evening = await w.CreateAppointment(Z(2031, 3, 4, 0)); // 19:00 EST on the 3rd (01:00 on the 4th in Zagreb)
        AppointmentDto nextDay = await w.CreateAppointment(Z(2031, 3, 4, 14)); // 09:00 EST on the 4th

        OperationalDashboardDto dashboard = await w.Resolve<IOperationalDashboardService>()
            .GetDashboard(w.OrganizationId, w.Company.Id.Value, Day(2031, 3, 3));

        Assert.Equal(new[] { morning.Id, evening.Id }, dashboard.Schedule.OrderBy(s => s.StartsAt).Select(s => s.AppointmentId).ToArray());
        Assert.DoesNotContain(dashboard.Schedule, s => s.AppointmentId == nextDay.Id);
        Assert.Equal(Z(2031, 3, 3, 0), dashboard.Date);
    }

    [Fact]
    public async Task Dashboard_OnTheSpringForwardDay_CoversTheShort23HourLocalDay()
    {
        await using SchedulingWorld w = await ZagrebWorld(nameof(Dashboard_OnTheSpringForwardDay_CoversTheShort23HourLocalDay));
        // Zagreb 2031-03-30 runs from 03-29 23:00Z to 03-30 22:00Z.
        AppointmentDto first = await w.CreateAppointment(w.CreateRequest(Z(2031, 3, 29, 23, 0), overrideAvailability: true)); // 00:00 CET
        AppointmentDto last = await w.CreateAppointment(w.CreateRequest(Z(2031, 3, 30, 21, 30), overrideAvailability: true)); // 23:30 CEST
        AppointmentDto before = await w.CreateAppointment(w.CreateRequest(Z(2031, 3, 29, 22, 30), overrideAvailability: true)); // 23:30 CET on the 29th
        AppointmentDto after = await w.CreateAppointment(w.CreateRequest(Z(2031, 3, 30, 22, 0), overrideAvailability: true)); // 00:00 CEST on the 31st

        OperationalDashboardDto dashboard = await w.Resolve<IOperationalDashboardService>()
            .GetDashboard(w.OrganizationId, w.Company.Id.Value, Day(2031, 3, 30));

        Assert.Equal(new[] { first.Id, last.Id }, dashboard.Schedule.OrderBy(s => s.StartsAt).Select(s => s.AppointmentId).ToArray());
        Assert.DoesNotContain(dashboard.Schedule, s => s.AppointmentId == before.Id || s.AppointmentId == after.Id);
    }

    #endregion
}
