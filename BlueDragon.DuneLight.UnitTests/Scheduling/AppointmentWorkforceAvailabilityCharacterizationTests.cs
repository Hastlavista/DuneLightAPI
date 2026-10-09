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

namespace BlueDragon.DuneLight.UnitTests.Scheduling;

/// <summary>
/// CHARACTERIZATION (matrix E): the "workforce availability" checks — employee working hours, company working hours,
/// employee absence, employee schedule break, company holiday — and OverrideAvailability.
///
/// What the code does today (differs from docs/poslovna-logika-pregled.md §4.4, which still says breaks and working hours
/// are only warnings): every violation is a HARD error (BusinessRuleException) unless the caller has full scope AND sets
/// OverrideAvailability, in which case the same violation comes back as a warning on the DTO and the write proceeds.
/// Error codes and warning codes are different strings for working hours (OUTSIDE_WORKING_HOURS vs
/// OUTSIDE_WORKING_HOURS_WARNING); both are pinned here.
///
/// Classification priority (AppointmentEligibilityHelper.Classify): absence &gt; break &gt; (holiday | outside hours).
///
/// The last regions pin how the checks DIFFER per flow — Create and segment time changes always check, CompleteNow checks
/// only for a future start, completing existing participations never checks (it does not schedule anything),
/// CreateRecurring reports hard conflicts as RECURRING_CONFLICT — as separate tests rather than normalizing them.
/// </summary>
public class AppointmentWorkforceAvailabilityCharacterizationTests
{
    private static readonly TimeSpan Nine = TimeSpan.FromHours(9);
    private static readonly TimeSpan Seventeen = TimeSpan.FromHours(17);

    #region Working hours (employee and company)

    [Fact]
    public async Task WorkingHours_EmployeeOutsideOwnHours_IsRejected()
    {
        await using SchedulingWorld w = await SchedulingWorld.Create(nameof(WorkingHours_EmployeeOutsideOwnHours_IsRejected));
        await w.SetWorkingHours(w.Employee, Nine, Seventeen); // company stays 08:00-20:00

        await SchedulingAssert.BusinessRule(ErrorCodes.OutsideWorkingHours,
            () => w.CreateAppointment(SchedulingWorld.Future(18)));

        Assert.Equal(0, await w.CountAppointments());
    }

    [Fact]
    public async Task WorkingHours_CompanyOutsideOwnHours_IsRejectedEvenWhenTheEmployeeWorks()
    {
        await using SchedulingWorld w = await SchedulingWorld.Create(nameof(WorkingHours_CompanyOutsideOwnHours_IsRejectedEvenWhenTheEmployeeWorks));
        await w.SetCompanyWorkingHours(w.Company, Nine, Seventeen); // employee stays 08:00-20:00

        await SchedulingAssert.BusinessRule(ErrorCodes.OutsideWorkingHours,
            () => w.CreateAppointment(SchedulingWorld.Future(18)));
    }

    [Fact]
    public async Task WorkingHours_TheIntersectionOfEmployeeAndCompanyHoursApplies()
    {
        await using SchedulingWorld w = await SchedulingWorld.Create(nameof(WorkingHours_TheIntersectionOfEmployeeAndCompanyHoursApplies));
        await w.SetWorkingHours(w.Employee, TimeSpan.FromHours(8), TimeSpan.FromHours(12));
        await w.SetCompanyWorkingHours(w.Company, TimeSpan.FromHours(10), TimeSpan.FromHours(20));

        // 09:00 is inside the employee's hours but before the company opens; 11:00 is inside both.
        await SchedulingAssert.BusinessRule(ErrorCodes.OutsideWorkingHours, () => w.CreateAppointment(SchedulingWorld.Future(9)));
        AppointmentDto ok = await w.CreateAppointment(SchedulingWorld.Future(11));

        Assert.Equal(AppointmentStatus.Scheduled, ok.Status);
    }

    [Fact]
    public async Task WorkingHours_AppointmentSpanningTheEndOfTheWorkingWindow_IsRejected()
    {
        await using SchedulingWorld w = await SchedulingWorld.Create(nameof(WorkingHours_AppointmentSpanningTheEndOfTheWorkingWindow_IsRejected));

        // 19:45 + 30 min ends at 20:15, after the 20:00 window end: the WHOLE interval must fit.
        await SchedulingAssert.BusinessRule(ErrorCodes.OutsideWorkingHours,
            () => w.CreateAppointment(SchedulingWorld.Future(19, 45)));
    }

    [Fact]
    public async Task WorkingHours_AppointmentTouchingBothWindowBoundaries_IsAllowed()
    {
        await using SchedulingWorld w = await SchedulingWorld.Create(nameof(WorkingHours_AppointmentTouchingBothWindowBoundaries_IsAllowed));
        Client other = await w.AddClient("Other", "Client");
        Employee secondEmployee = await w.AddEmployee("Second");

        // Starts exactly at opening; ends exactly at closing. Inclusive on both edges.
        AppointmentDto opening = await w.CreateAppointment(SchedulingWorld.Future(8));
        AppointmentDto closing = await w.CreateAppointment(SchedulingWorld.Future(19, 30), client: other, employee: secondEmployee);

        Assert.Equal(AppointmentStatus.Scheduled, opening.Status);
        Assert.Equal(AppointmentStatus.Scheduled, closing.Status);
    }

    [Fact]
    public async Task WorkingHours_EmployeeWithoutAnyWorkingHoursTemplate_CanNeverBeBooked()
    {
        await using SchedulingWorld w = await SchedulingWorld.Create(nameof(WorkingHours_EmployeeWithoutAnyWorkingHoursTemplate_CanNeverBeBooked));
        Employee noHours = await w.AddEmployee("NoHours", withWorkingHours: false);

        // A missing template is "no availability", not "always available" (WorkingHoursCalculator returns no intervals).
        await SchedulingAssert.BusinessRule(ErrorCodes.OutsideWorkingHours,
            () => w.CreateAppointment(SchedulingWorld.Future(10), employee: noHours));
    }

    [Fact]
    public async Task WorkingHours_OutsideHours_WithFullScopeOverride_SucceedsWithAWarning()
    {
        await using SchedulingWorld w = await SchedulingWorld.Create(nameof(WorkingHours_OutsideHours_WithFullScopeOverride_SucceedsWithAWarning));

        AppointmentDto dto = await w.CreateAppointment(w.CreateRequest(SchedulingWorld.Future(22), overrideAvailability: true));

        SchedulingAssert.HasWarning(dto, WarningCodes.OutsideWorkingHours);
        Assert.Equal("OUTSIDE_WORKING_HOURS_WARNING", WarningCodes.OutsideWorkingHours);
        Assert.Equal(1, await w.CountAppointments());
    }

    #endregion

    #region Absence, break, holiday

    [Fact]
    public async Task Absence_EmployeeAbsentOnTheDay_IsRejected()
    {
        await using SchedulingWorld w = await SchedulingWorld.Create(nameof(Absence_EmployeeAbsentOnTheDay_IsRejected));
        await w.AddAbsence(w.Employee, SchedulingWorld.FutureDay);

        await SchedulingAssert.BusinessRule(ErrorCodes.EmployeeAbsent,
            () => w.CreateAppointment(SchedulingWorld.Future(10)));

        Assert.Equal(0, await w.CountAppointments());
    }

    [Fact]
    public async Task Absence_OnAnotherDay_DoesNotAffectTheAppointment()
    {
        await using SchedulingWorld w = await SchedulingWorld.Create(nameof(Absence_OnAnotherDay_DoesNotAffectTheAppointment));
        await w.AddAbsence(w.Employee, SchedulingWorld.FutureDay.AddDays(1));

        AppointmentDto dto = await w.CreateAppointment(SchedulingWorld.Future(10));

        Assert.Equal(AppointmentStatus.Scheduled, dto.Status);
    }

    [Fact]
    public async Task Absence_OfAnotherEmployee_DoesNotAffectTheAppointment()
    {
        await using SchedulingWorld w = await SchedulingWorld.Create(nameof(Absence_OfAnotherEmployee_DoesNotAffectTheAppointment));
        Employee other = await w.AddEmployee("Other");
        await w.AddAbsence(other, SchedulingWorld.FutureDay);

        AppointmentDto dto = await w.CreateAppointment(SchedulingWorld.Future(10));

        Assert.Equal(AppointmentStatus.Scheduled, dto.Status);
    }

    [Fact]
    public async Task Absence_MultiDayRange_CoversEveryDayInclusive()
    {
        await using SchedulingWorld w = await SchedulingWorld.Create(nameof(Absence_MultiDayRange_CoversEveryDayInclusive));
        await w.AddAbsenceRange(w.Employee, SchedulingWorld.FutureDay, SchedulingWorld.FutureDay.AddDays(2));

        await SchedulingAssert.BusinessRule(ErrorCodes.EmployeeAbsent, () => w.CreateAppointment(SchedulingWorld.Future(10)));
        await SchedulingAssert.BusinessRule(ErrorCodes.EmployeeAbsent,
            () => w.CreateAppointment(w.CreateRequest(SchedulingWorld.Future(10).AddDays(2))));
        AppointmentDto dayAfter = await w.CreateAppointment(SchedulingWorld.Future(10).AddDays(3));

        Assert.Equal(AppointmentStatus.Scheduled, dayAfter.Status);
    }

    [Fact]
    public async Task Absence_WithoutAnEndDate_IsOpenEnded()
    {
        await using SchedulingWorld w = await SchedulingWorld.Create(nameof(Absence_WithoutAnEndDate_IsOpenEnded));
        await w.AddOpenEndedAbsence(w.Employee, SchedulingWorld.FutureDay);

        // DateTo == null means "absent from DateFrom onwards", so a date far after DateFrom is still blocked,
        // while a date before DateFrom is not.
        await SchedulingAssert.BusinessRule(ErrorCodes.EmployeeAbsent,
            () => w.CreateAppointment(w.CreateRequest(SchedulingWorld.Future(10).AddDays(365))));
        AppointmentDto before = await w.CreateAppointment(SchedulingWorld.Future(10).AddDays(-1));

        Assert.Equal(AppointmentStatus.Scheduled, before.Status);
    }

    [Fact]
    public async Task Absence_WithFullScopeOverride_SucceedsWithAWarning()
    {
        await using SchedulingWorld w = await SchedulingWorld.Create(nameof(Absence_WithFullScopeOverride_SucceedsWithAWarning));
        await w.AddAbsence(w.Employee, SchedulingWorld.FutureDay);

        AppointmentDto dto = await w.CreateAppointment(w.CreateRequest(SchedulingWorld.Future(10), overrideAvailability: true));

        SchedulingAssert.HasWarning(dto, WarningCodes.EmployeeAbsent);
    }

    [Fact]
    public async Task Break_OverlappingTheAppointment_IsRejected()
    {
        await using SchedulingWorld w = await SchedulingWorld.Create(nameof(Break_OverlappingTheAppointment_IsRejected));
        await w.AddScheduleBreak(w.Employee, SchedulingWorld.Future(10), 60);

        // Docs say a break is only a warning; the code treats it as a hard block.
        await SchedulingAssert.BusinessRule(ErrorCodes.EmployeeOnBreak,
            () => w.CreateAppointment(SchedulingWorld.Future(10, 30)));

        Assert.Equal(0, await w.CountAppointments());
    }

    [Fact]
    public async Task Break_AdjacentToTheAppointment_DoesNotBlock()
    {
        await using SchedulingWorld w = await SchedulingWorld.Create(nameof(Break_AdjacentToTheAppointment_DoesNotBlock));
        await w.AddScheduleBreak(w.Employee, SchedulingWorld.Future(10), 60); // 10:00-11:00

        AppointmentDto after = await w.CreateAppointment(SchedulingWorld.Future(11));

        Assert.Equal(AppointmentStatus.Scheduled, after.Status);
    }

    [Fact]
    public async Task Break_WithFullScopeOverride_SucceedsWithAWarning()
    {
        await using SchedulingWorld w = await SchedulingWorld.Create(nameof(Break_WithFullScopeOverride_SucceedsWithAWarning));
        await w.AddScheduleBreak(w.Employee, SchedulingWorld.Future(10), 60);

        AppointmentDto dto = await w.CreateAppointment(w.CreateRequest(SchedulingWorld.Future(10), overrideAvailability: true));

        SchedulingAssert.HasWarning(dto, WarningCodes.EmployeeOnBreak);
    }

    [Fact]
    public async Task Holiday_CompanyClosed_IsRejected()
    {
        await using SchedulingWorld w = await SchedulingWorld.Create(nameof(Holiday_CompanyClosed_IsRejected));
        await w.AddCompanyHoliday(w.Company, SchedulingWorld.FutureDay);

        await SchedulingAssert.BusinessRule(ErrorCodes.CompanyClosedHoliday,
            () => w.CreateAppointment(SchedulingWorld.Future(10)));

        Assert.Equal(0, await w.CountAppointments());
    }

    [Fact]
    public async Task Holiday_WithFullScopeOverride_SucceedsWithAWarning()
    {
        await using SchedulingWorld w = await SchedulingWorld.Create(nameof(Holiday_WithFullScopeOverride_SucceedsWithAWarning));
        await w.AddCompanyHoliday(w.Company, SchedulingWorld.FutureDay);

        AppointmentDto dto = await w.CreateAppointment(w.CreateRequest(SchedulingWorld.Future(10), overrideAvailability: true));

        SchedulingAssert.HasWarning(dto, WarningCodes.CompanyClosedHoliday);
    }

    [Fact]
    public async Task Holiday_OfAnotherCompany_DoesNotAffectTheAppointment()
    {
        await using SchedulingWorld w = await SchedulingWorld.Create(nameof(Holiday_OfAnotherCompany_DoesNotAffectTheAppointment));
        Company other = await w.AddCompany("Other");
        await w.AddCompanyHoliday(other, SchedulingWorld.FutureDay);

        AppointmentDto dto = await w.CreateAppointment(SchedulingWorld.Future(10));

        Assert.Equal(AppointmentStatus.Scheduled, dto.Status);
    }

    #endregion

    #region Classification priority (only one violation is ever reported)

    [Fact]
    public async Task Priority_AbsenceIsReportedBeforeABreak()
    {
        await using SchedulingWorld w = await SchedulingWorld.Create(nameof(Priority_AbsenceIsReportedBeforeABreak));
        await w.AddAbsence(w.Employee, SchedulingWorld.FutureDay);
        await w.AddScheduleBreak(w.Employee, SchedulingWorld.Future(10), 60);

        await SchedulingAssert.BusinessRule(ErrorCodes.EmployeeAbsent, () => w.CreateAppointment(SchedulingWorld.Future(10)));
    }

    [Fact]
    public async Task Priority_ABreakIsReportedBeforeOutsideWorkingHours()
    {
        await using SchedulingWorld w = await SchedulingWorld.Create(nameof(Priority_ABreakIsReportedBeforeOutsideWorkingHours));
        await w.AddScheduleBreak(w.Employee, SchedulingWorld.Future(22), 60);

        await SchedulingAssert.BusinessRule(ErrorCodes.EmployeeOnBreak, () => w.CreateAppointment(SchedulingWorld.Future(22)));
    }

    [Fact]
    public async Task Priority_AHolidayIsReportedInsteadOfOutsideWorkingHours()
    {
        await using SchedulingWorld w = await SchedulingWorld.Create(nameof(Priority_AHolidayIsReportedInsteadOfOutsideWorkingHours));
        await w.AddCompanyHoliday(w.Company, SchedulingWorld.FutureDay);

        // A holiday empties the company's working intervals, so the appointment is also "outside hours" — the holiday wins.
        await SchedulingAssert.BusinessRule(ErrorCodes.CompanyClosedHoliday, () => w.CreateAppointment(SchedulingWorld.Future(22)));
    }

    [Fact]
    public async Task Priority_AnAbsenceHidesTheOutsideHoursViolation()
    {
        await using SchedulingWorld w = await SchedulingWorld.Create(nameof(Priority_AnAbsenceHidesTheOutsideHoursViolation));
        await w.AddAbsence(w.Employee, SchedulingWorld.FutureDay);

        await SchedulingAssert.BusinessRule(ErrorCodes.EmployeeAbsent, () => w.CreateAppointment(SchedulingWorld.Future(22)));
    }

    [Fact]
    public async Task Override_OnlyOneWarningIsProducedPerCall()
    {
        await using SchedulingWorld w = await SchedulingWorld.Create(nameof(Override_OnlyOneWarningIsProducedPerCall));
        await w.AddAbsence(w.Employee, SchedulingWorld.FutureDay);
        await w.AddScheduleBreak(w.Employee, SchedulingWorld.Future(22), 60);

        AppointmentDto dto = await w.CreateAppointment(w.CreateRequest(SchedulingWorld.Future(22), overrideAvailability: true));

        Assert.Equal(WarningCodes.EmployeeAbsent, Assert.Single(dto.Warnings).Code);
    }

    #endregion

    #region Override is NOT a general bypass

    [Fact]
    public async Task Override_DoesNotBypassStructuralEligibility()
    {
        await using SchedulingWorld w = await SchedulingWorld.Create(nameof(Override_DoesNotBypassStructuralEligibility));
        Employee unauthorized = await w.AddEmployeeRestrictedToAnotherService();

        await SchedulingAssert.BusinessRule(ErrorCodes.EmployeeNotAssignedToService,
            () => w.CreateAppointment(w.CreateRequest(SchedulingWorld.Future(10), employee: unauthorized, overrideAvailability: true)));
    }

    [Fact]
    public async Task Override_IgnoredWithoutFullScope_EvenWhenRequested()
    {
        await using SchedulingWorld w = await SchedulingWorld.Create(nameof(Override_IgnoredWithoutFullScope_EvenWhenRequested));
        await w.AddAbsence(w.Employee, SchedulingWorld.FutureDay);

        await SchedulingAssert.BusinessRule(ErrorCodes.EmployeeAbsent,
            () => w.Appointments.Create(w.OrganizationId, w.Employee.UserId, hasFullScope: false,
                w.CreateRequest(SchedulingWorld.Future(10), overrideAvailability: true).ToTarget()));
    }

    #endregion

    #region Per-flow differences (segment time change / CompleteNow / CreateRecurring)

    [Fact]
    public async Task SegmentTimeChange_ChecksWorkforceAvailabilityForTheNewSlot()
    {
        await using SchedulingWorld w = await SchedulingWorld.Create(nameof(SegmentTimeChange_ChecksWorkforceAvailabilityForTheNewSlot));
        AppointmentDto created = await w.CreateAppointment(SchedulingWorld.Future(10));

        await SchedulingAssert.BusinessRule(ErrorCodes.OutsideWorkingHours,
            () => w.Appointments.ChangeSegmentTime(w.OrganizationId, w.ActorUserId, true, created.Segments[0].Id,
                new AppointmentSegmentTimeChangeRequest { PlannedStart = SchedulingWorld.Future(22) }));

        Assert.Equal(SchedulingWorld.Future(10), (await w.LoadAppointment(created.Id)).StartsAt);
    }

    [Fact]
    public async Task SegmentTimeChange_WithFullScopeOverride_MovesToTheViolatingSlotWithAWarning()
    {
        await using SchedulingWorld w = await SchedulingWorld.Create(nameof(SegmentTimeChange_WithFullScopeOverride_MovesToTheViolatingSlotWithAWarning));
        AppointmentDto created = await w.CreateAppointment(SchedulingWorld.Future(10));

        AppointmentDto moved = await w.Appointments.ChangeSegmentTime(w.OrganizationId, w.ActorUserId, true, created.Segments[0].Id,
            new AppointmentSegmentTimeChangeRequest { PlannedStart = SchedulingWorld.Future(22), OverrideAvailability = true });

        SchedulingAssert.HasWarning(moved, WarningCodes.OutsideWorkingHours);
        Assert.Equal(SchedulingWorld.Future(22), (await w.LoadAppointment(created.Id)).StartsAt);
    }

    [Fact]
    public async Task CompleteNew_ForAFutureStart_ChecksWorkforceAvailability()
    {
        await using SchedulingWorld w = await SchedulingWorld.Create(nameof(CompleteNew_ForAFutureStart_ChecksWorkforceAvailability));

        await SchedulingAssert.BusinessRule(ErrorCodes.OutsideWorkingHours,
            () => w.CompleteNew(w.CompleteRequest(SchedulingWorld.Future(22))));

        Assert.Equal(0, await w.CountAppointments());
    }

    [Fact]
    public async Task CompleteNew_ForAPastStart_ChecksWorkforceAvailabilityLikeTheFuture()
    {
        await using SchedulingWorld w = await SchedulingWorld.Create(nameof(CompleteNew_ForAPastStart_ChecksWorkforceAvailabilityLikeTheFuture));
        await w.AddAbsence(w.Employee, SchedulingWorld.PastDay);

        // CHANGED in K1 (K1-1, ADR-0008): logging past work is validated against the workforce like planning — refused without
        // override, recorded with a warning with it.
        await SchedulingAssert.BusinessRule(ErrorCodes.EmployeeAbsent, () => w.CompleteNew(w.CompleteRequest(SchedulingWorld.Past(22))));

        AppointmentDto dto = await w.CompleteNew(w.CompleteRequest(SchedulingWorld.Past(22), overrideAvailability: true));
        Assert.Equal(AppointmentStatus.Closed, dto.Status);
        Assert.Contains(dto.Warnings, x => x.Code == WarningCodes.EmployeeAbsent);
    }

    [Fact]
    public async Task CompletingExistingParticipations_NeverChecksWorkforceAvailability()
    {
        await using SchedulingWorld w = await SchedulingWorld.Create(nameof(CompletingExistingParticipations_NeverChecksWorkforceAvailability));
        AppointmentDto created = await w.CreateAppointment(SchedulingWorld.Future(10));
        await w.AddAbsence(w.Employee, SchedulingWorld.FutureDay);

        // M1H: completion is a participation lifecycle transition — it schedules nothing, so the (now absent) employee's
        // availability is not re-checked.
        AppointmentDto completed = await w.CompleteParticipations(created.Id, w.CompleteRequest(SchedulingWorld.Future(10)));

        Assert.Equal(AppointmentStatus.Closed, completed.Status);
        SchedulingAssert.HasNoWarnings(completed);
    }

    [Fact]
    public async Task CompleteNow_StillEnforcesHardOverlap()
    {
        await using SchedulingWorld w = await SchedulingWorld.Create(nameof(CompleteNow_StillEnforcesHardOverlap));
        Client other = await w.AddClient("Other", "Client");
        await w.CreateAppointment(SchedulingWorld.Future(10));

        await SchedulingAssert.BusinessRule(ErrorCodes.AppointmentOverlap,
            () => w.CompleteNew(w.CompleteRequest(SchedulingWorld.Future(10), client: other)));
    }

    [Fact]
    public async Task CreateRecurring_AnUnavailableOccurrence_AbortsTheWholeSeriesWithRecurringConflict()
    {
        await using SchedulingWorld w = await SchedulingWorld.Create(nameof(CreateRecurring_AnUnavailableOccurrence_AbortsTheWholeSeriesWithRecurringConflict));
        await w.AddAbsence(w.Employee, SchedulingWorld.FutureDay.AddDays(1)); // second occurrence of a daily series

        BusinessRuleExceptionHolder holder = await BusinessRuleExceptionHolder.Capture(
            () => w.Appointments.CreateRecurring(w.OrganizationId, w.ActorUserId, true, DailySeries(w, days: 3)));

        // Recurring reports RECURRING_CONFLICT (409) with per-date reasons instead of the per-violation error code.
        Assert.Equal(ErrorCodes.RecurringConflict, holder.Code);
        Assert.Equal(0, await w.CountAppointments()); // nothing saved — all-or-nothing
    }

    [Fact]
    public async Task CreateRecurring_WithFullScopeOverride_CreatesTheWholeSeriesAndReportsPerOccurrenceWarnings()
    {
        await using SchedulingWorld w = await SchedulingWorld.Create(nameof(CreateRecurring_WithFullScopeOverride_CreatesTheWholeSeriesAndReportsPerOccurrenceWarnings));
        await w.AddAbsence(w.Employee, SchedulingWorld.FutureDay.AddDays(1));
        RecurringAppointmentCreateRequest request = DailySeries(w, days: 3);
        request.OverrideAvailability = true;

        List<AppointmentDto> created = await w.Appointments.CreateRecurring(w.OrganizationId, w.ActorUserId, true, request);

        Assert.Equal(3, created.Count);
        Assert.Equal(3, await w.CountAppointments());
        Assert.Single(created, a => a.Warnings.Any(x => x.Code == WarningCodes.EmployeeAbsent));
        Assert.Single(created.Select(a => a.RecurrenceGroupId).Distinct());
    }

    #endregion

    private static RecurringAppointmentCreateRequest DailySeries(SchedulingWorld w, int days) => new()
    {
        RecurrenceType = RecurrenceType.Daily,
        ServiceId = w.Service.Id.Value,
        EmployeeId = w.Employee.Id.Value,
        CompanyId = w.Company.Id.Value,
        ClientIds = new List<Guid> { w.Client.Id.Value },
        FirstOccurrenceStartsAt = SchedulingWorld.Future(10),
        EndDate = SchedulingWorld.Future(10).AddDays(days - 1)
    };
}
