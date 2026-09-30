#nullable disable
using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using BlueDragon.DuneLight.Core.DTOs.Appointments;
using BlueDragon.DuneLight.Core.DTOs.ScheduleBreaks;
using BlueDragon.DuneLight.Core.Enums;
using BlueDragon.DuneLight.Core.Interfaces.ScheduleBreaks;
using BlueDragon.DuneLight.Core.Shared;
using BlueDragon.DuneLight.Core.Shared.Exceptions;
using BlueDragon.DuneLight.Infrastructure.Domain.Models.Employees;

namespace BlueDragon.DuneLight.UnitTests.Scheduling;

/// <summary>
/// CHARACTERIZATION: ScheduleBreakService's checks against the employee's existing appointments (single Create/Update
/// and CreateRecurring). Not covered by the original suite; added with the S1 occupancy seam, which these paths now read
/// through. A break never checks working hours; it only refuses to sit on top of a non-cancelled appointment (or
/// another break) of the same employee, with the same half-open interval rule as appointments.
/// </summary>
public class ScheduleBreakOverlapCharacterizationTests
{
    private static IScheduleBreakService Breaks(SchedulingWorld w) => w.Resolve<IScheduleBreakService>();

    private static ScheduleBreakCreateRequest BreakRequest(SchedulingWorld w, DateTimeOffset startsAt, int durationMinutes = 30, Employee employee = null) => new()
    {
        StartsAt = startsAt,
        EmployeeId = (employee ?? w.Employee).Id.Value,
        CompanyId = w.Company.Id.Value,
        DurationMinutes = durationMinutes
    };

    [Fact]
    public async Task Create_OverlappingAnAppointmentOfTheSameEmployee_IsRejected()
    {
        await using SchedulingWorld w = await SchedulingWorld.Create(nameof(Create_OverlappingAnAppointmentOfTheSameEmployee_IsRejected));
        await w.CreateAppointment(SchedulingWorld.Future(10)); // 10:00-10:30

        BusinessRuleException ex = await SchedulingAssert.BusinessRule(ErrorCodes.AppointmentOverlap,
            () => Breaks(w).Create(w.OrganizationId, w.ActorUserId, true, BreakRequest(w, SchedulingWorld.Future(10, 15))));

        Assert.Equal("Trener već ima termin u ovom vremenskom razdoblju.", ex.Message);
    }

    [Fact]
    public async Task Create_AdjacentToAnAppointment_IsAllowed()
    {
        await using SchedulingWorld w = await SchedulingWorld.Create(nameof(Create_AdjacentToAnAppointment_IsAllowed));
        await w.CreateAppointment(SchedulingWorld.Future(10)); // 10:00-10:30

        ScheduleBreakDto before = await Breaks(w).Create(w.OrganizationId, w.ActorUserId, true, BreakRequest(w, SchedulingWorld.Future(9, 30)));
        ScheduleBreakDto after = await Breaks(w).Create(w.OrganizationId, w.ActorUserId, true, BreakRequest(w, SchedulingWorld.Future(10, 30)));

        Assert.Equal(SchedulingWorld.Future(9, 30), before.StartsAt);
        Assert.Equal(SchedulingWorld.Future(10, 30), after.StartsAt);
    }

    [Fact]
    public async Task Create_OverACancelledAppointment_IsAllowed()
    {
        await using SchedulingWorld w = await SchedulingWorld.Create(nameof(Create_OverACancelledAppointment_IsAllowed));
        await w.SeedAppointment(SchedulingWorld.Future(10), status: AppointmentStatus.Cancelled);

        ScheduleBreakDto created = await Breaks(w).Create(w.OrganizationId, w.ActorUserId, true, BreakRequest(w, SchedulingWorld.Future(10)));

        Assert.Equal(SchedulingWorld.Future(10), created.StartsAt);
    }

    [Fact]
    public async Task Create_OverAnotherEmployeesAppointment_IsAllowed()
    {
        await using SchedulingWorld w = await SchedulingWorld.Create(nameof(Create_OverAnotherEmployeesAppointment_IsAllowed));
        Employee other = await w.AddEmployee("Other");
        await w.CreateAppointment(SchedulingWorld.Future(10), employee: other);

        ScheduleBreakDto created = await Breaks(w).Create(w.OrganizationId, w.ActorUserId, true, BreakRequest(w, SchedulingWorld.Future(10)));

        Assert.Equal(SchedulingWorld.Future(10), created.StartsAt);
    }

    [Fact]
    public async Task Update_OntoAnAppointment_IsRejected()
    {
        await using SchedulingWorld w = await SchedulingWorld.Create(nameof(Update_OntoAnAppointment_IsRejected));
        await w.CreateAppointment(SchedulingWorld.Future(10));
        ScheduleBreakDto created = await Breaks(w).Create(w.OrganizationId, w.ActorUserId, true, BreakRequest(w, SchedulingWorld.Future(14)));

        await SchedulingAssert.BusinessRule(ErrorCodes.AppointmentOverlap,
            () => Breaks(w).Update(w.OrganizationId, w.ActorUserId, true, created.Id, new ScheduleBreakUpdateRequest
            {
                StartsAt = SchedulingWorld.Future(10, 15),
                EmployeeId = w.Employee.Id.Value,
                CompanyId = w.Company.Id.Value,
                DurationMinutes = 30
            }));
    }

    [Fact]
    public async Task CreateRecurring_ReportsEveryOccurrenceThatHitsAnAppointment()
    {
        await using SchedulingWorld w = await SchedulingWorld.Create(nameof(CreateRecurring_ReportsEveryOccurrenceThatHitsAnAppointment));
        await w.CreateAppointment(SchedulingWorld.Future(10).AddDays(1)); // second day only

        BusinessRuleException ex = await SchedulingAssert.BusinessRule(ErrorCodes.RecurringConflict,
            () => Breaks(w).CreateRecurring(w.OrganizationId, w.ActorUserId, true, new RecurringScheduleBreakCreateRequest
            {
                RecurrenceType = RecurrenceType.Daily,
                EmployeeId = w.Employee.Id.Value,
                CompanyId = w.Company.Id.Value,
                FirstOccurrenceStartsAt = SchedulingWorld.Future(10, 15),
                DurationMinutes = 30,
                EndDate = SchedulingWorld.Future(10, 15).AddDays(2)
            }));

        Assert.Equal(new List<string> { ErrorCodes.RecurringConflictReasonAppointment }, SchedulingAssert.ConflictReasons(ex));
        object conflicts = ex.Details.GetType().GetProperty("conflicts").GetValue(ex.Details);
        Assert.Equal(SchedulingWorld.Future(10, 15).AddDays(1), Assert.Single((IEnumerable<RecurringConflictDetail>)conflicts).Date);
    }

    [Fact]
    public async Task CreateRecurring_AdjacentToAppointments_IsAllowed()
    {
        await using SchedulingWorld w = await SchedulingWorld.Create(nameof(CreateRecurring_AdjacentToAppointments_IsAllowed));
        await w.CreateAppointment(SchedulingWorld.Future(10));
        await w.CreateAppointment(SchedulingWorld.Future(10).AddDays(1));

        List<ScheduleBreakDto> created = await Breaks(w).CreateRecurring(w.OrganizationId, w.ActorUserId, true, new RecurringScheduleBreakCreateRequest
        {
            RecurrenceType = RecurrenceType.Daily,
            EmployeeId = w.Employee.Id.Value,
            CompanyId = w.Company.Id.Value,
            FirstOccurrenceStartsAt = SchedulingWorld.Future(10, 30),
            DurationMinutes = 30,
            EndDate = SchedulingWorld.Future(10, 30).AddDays(1)
        });

        Assert.Equal(2, created.Count);
    }
}
