#nullable disable
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using BlueDragon.DuneLight.Core.DTOs.Appointments;
using BlueDragon.DuneLight.Core.Enums;
using BlueDragon.DuneLight.Core.Shared;
using BlueDragon.DuneLight.Core.Shared.Exceptions;
using BlueDragon.DuneLight.Infrastructure.Domain.Models.Appointments;
using BlueDragon.DuneLight.Infrastructure.Domain.Models.Catalog;
using BlueDragon.DuneLight.Infrastructure.Domain.Models.Clients;
using BlueDragon.DuneLight.Infrastructure.Domain.Models.Employees;

namespace BlueDragon.DuneLight.UnitTests.Scheduling;

/// <summary>
/// CHARACTERIZATION: recurring individual appointments (POST /api/appointments/recurring). One request creates a series of
/// independent Appointment rows that share a RecurrenceGroupId; there is no series entity. Each occurrence gets its own
/// price snapshot (resolved for that occurrence's date) and its own Confirmed Bookings. The whole series is validated
/// before anything is written (all-or-nothing). The workforce/overlap rules are the same as for a single Create, reported
/// in aggregate as RECURRING_CONFLICT — except the CLIENT overlap, which still surfaces as APPOINTMENT_OVERLAP.
/// </summary>
public class AppointmentRecurringCharacterizationTests
{
    private static RecurringAppointmentCreateRequest Series(SchedulingWorld w, RecurrenceType type, int occurrences, Client client = null, Employee employee = null, Room room = null)
    {
        int stepDays = type == RecurrenceType.Daily ? 1 : 7;
        return new RecurringAppointmentCreateRequest
        {
            RecurrenceType = type,
            ServiceId = w.Service.Id.Value,
            EmployeeId = (employee ?? w.Employee).Id.Value,
            CompanyId = w.Company.Id.Value,
            RoomId = room?.Id,
            ClientIds = new List<Guid> { (client ?? w.Client).Id.Value },
            FirstOccurrenceStartsAt = SchedulingWorld.Future(10),
            EndDate = SchedulingWorld.Day(SchedulingWorld.Future(10).AddDays(stepDays * (occurrences - 1))),
            Note = "series"
        };
    }

    [Fact]
    public async Task Weekly_CreatesOneAppointmentPerWeek_SharingARecurrenceGroupId()
    {
        await using SchedulingWorld w = await SchedulingWorld.Create(nameof(Weekly_CreatesOneAppointmentPerWeek_SharingARecurrenceGroupId));

        List<AppointmentDto> created = await w.Appointments.CreateRecurring(w.OrganizationId, w.ActorUserId, true, Series(w, RecurrenceType.Weekly, 3));

        Assert.Equal(3, created.Count);
        Assert.Equal(
            new[] { SchedulingWorld.Future(10), SchedulingWorld.Future(10).AddDays(7), SchedulingWorld.Future(10).AddDays(14) },
            created.Select(a => a.StartsAt).OrderBy(x => x).ToArray());
        Guid group = Assert.Single(created.Select(a => a.RecurrenceGroupId).Distinct()).Value;
        Assert.NotEqual(Guid.Empty, group);
        foreach (AppointmentDto dto in created)
        {
            Appointment a = await w.LoadAppointment(dto.Id);
            Assert.Equal(AppointmentStatus.Scheduled, a.Status);
            Assert.Equal(AppointmentForm.Individual, a.Form);
            Assert.Equal(group, a.RecurrenceGroupId);
            Assert.Equal("series", a.Note);
            Assert.Equal(BookingStatus.Confirmed, Assert.Single(a.Bookings).Status);
        }
    }

    [Fact]
    public async Task Daily_IncludesWeekendsWithoutSkipping()
    {
        await using SchedulingWorld w = await SchedulingWorld.Create(nameof(Daily_IncludesWeekendsWithoutSkipping));

        List<AppointmentDto> created = await w.Appointments.CreateRecurring(w.OrganizationId, w.ActorUserId, true, Series(w, RecurrenceType.Daily, 7));

        Assert.Equal(7, created.Count);
        Assert.Equal(7, created.Select(a => a.StartsAt.DayOfWeek).Distinct().Count());
    }

    [Fact]
    public async Task ASingleOccurrence_WhenTheEndDateEqualsTheFirstStart_IsAllowed()
    {
        await using SchedulingWorld w = await SchedulingWorld.Create(nameof(ASingleOccurrence_WhenTheEndDateEqualsTheFirstStart_IsAllowed));

        List<AppointmentDto> created = await w.Appointments.CreateRecurring(w.OrganizationId, w.ActorUserId, true, Series(w, RecurrenceType.Weekly, 1));

        Assert.Single(created);
    }

    [Fact]
    public async Task AnEndDateBeforeTheFirstOccurrence_IsAValidationError()
    {
        await using SchedulingWorld w = await SchedulingWorld.Create(nameof(AnEndDateBeforeTheFirstOccurrence_IsAValidationError));
        RecurringAppointmentCreateRequest request = Series(w, RecurrenceType.Weekly, 2);
        request.EndDate = SchedulingWorld.Day(request.FirstOccurrenceStartsAt.AddDays(-1));

        await SchedulingAssert.Validation(() => w.Appointments.CreateRecurring(w.OrganizationId, w.ActorUserId, true, request));
    }

    [Fact]
    public async Task EachOccurrence_ResolvesItsOwnPriceSnapshotForItsOwnDate()
    {
        await using SchedulingWorld w = await SchedulingWorld.Create(nameof(EachOccurrence_ResolvesItsOwnPriceSnapshotForItsOwnDate));
        await w.AddPriceListItem(w.Service, 80m, SchedulingWorld.Day(SchedulingWorld.FutureDay.AddDays(10)), companyId: w.Company.Id); // effective from week 3

        List<AppointmentDto> created = await w.Appointments.CreateRecurring(w.OrganizationId, w.ActorUserId, true, Series(w, RecurrenceType.Weekly, 3));

        Assert.Equal(
            new[] { 50m, 50m, 80m },
            created.OrderBy(a => a.StartsAt).Select(a => a.Bookings.Single().Amount).ToArray());
    }

    [Fact]
    public async Task ARecurringSeries_HasNoAmountOverride_EveryBookingIsAtTheSuggestedPrice()
    {
        await using SchedulingWorld w = await SchedulingWorld.Create(nameof(ARecurringSeries_HasNoAmountOverride_EveryBookingIsAtTheSuggestedPrice));

        List<AppointmentDto> created = await w.Appointments.CreateRecurring(w.OrganizationId, w.ActorUserId, true, Series(w, RecurrenceType.Weekly, 2));

        Assert.All(created, a =>
        {
            BookingDto b = a.Bookings.Single();
            Assert.Equal(b.SuggestedAmount, b.Amount);
            Assert.False(b.IsAmountManuallyOverridden);
        });
    }

    [Fact]
    public async Task AnEmployeeConflictOnAnyOccurrence_AbortsTheWholeSeries_WithTheConflictingDate()
    {
        await using SchedulingWorld w = await SchedulingWorld.Create(nameof(AnEmployeeConflictOnAnyOccurrence_AbortsTheWholeSeries_WithTheConflictingDate));
        Client other = await w.AddClient("Other", "Client");
        await w.CreateAppointment(SchedulingWorld.Future(10).AddDays(7), client: other); // collides with occurrence #2

        BusinessRuleException ex = await SchedulingAssert.BusinessRule(ErrorCodes.RecurringConflict,
            () => w.Appointments.CreateRecurring(w.OrganizationId, w.ActorUserId, true, Series(w, RecurrenceType.Weekly, 3)));

        Assert.Equal(new[] { ErrorCodes.RecurringConflictReasonAppointment }, SchedulingAssert.ConflictReasons(ex).ToArray());
        Assert.Equal(1, await w.CountAppointments()); // only the pre-existing one
    }

    [Fact]
    public async Task ARoomConflictOnAnyOccurrence_AbortsTheWholeSeries()
    {
        await using SchedulingWorld w = await SchedulingWorld.Create(nameof(ARoomConflictOnAnyOccurrence_AbortsTheWholeSeries));
        Room room = await w.AddRoom();
        Client other = await w.AddClient("Other", "Client");
        Employee otherEmployee = await w.AddEmployee("Other");
        await w.CreateAppointment(SchedulingWorld.Future(10).AddDays(7), client: other, employee: otherEmployee, room: room);

        BusinessRuleException ex = await SchedulingAssert.BusinessRule(ErrorCodes.RecurringConflict,
            () => w.Appointments.CreateRecurring(w.OrganizationId, w.ActorUserId, true, Series(w, RecurrenceType.Weekly, 3, room: room)));

        Assert.Contains(ErrorCodes.RecurringConflictReasonRoom, SchedulingAssert.ConflictReasons(ex));
    }

    [Fact]
    public async Task AClientConflictOnAnyOccurrence_IsReportedAsAppointmentOverlap_NotRecurringConflict()
    {
        await using SchedulingWorld w = await SchedulingWorld.Create(nameof(AClientConflictOnAnyOccurrence_IsReportedAsAppointmentOverlap_NotRecurringConflict));
        Employee otherEmployee = await w.AddEmployee("Other");
        await w.CreateAppointment(SchedulingWorld.Future(10).AddDays(7), client: w.Client, employee: otherEmployee);

        // The same client is already busy at occurrence #2 with a different employee: the client rule is a separate,
        // single-error check (unlike the employee/room/workforce rules which aggregate into RECURRING_CONFLICT).
        await SchedulingAssert.BusinessRule(ErrorCodes.AppointmentOverlap,
            () => w.Appointments.CreateRecurring(w.OrganizationId, w.ActorUserId, true, Series(w, RecurrenceType.Weekly, 3)));

        Assert.Equal(1, await w.CountAppointments());
    }

    [Fact]
    public async Task ARecurringSeries_IsOwnershipCheckedAgainstTheRequestedEmployee()
    {
        await using SchedulingWorld w = await SchedulingWorld.Create(nameof(ARecurringSeries_IsOwnershipCheckedAgainstTheRequestedEmployee));
        Employee other = await w.AddEmployee("Other");

        // CHANGED in T1: 403 OutOfScope (bilo 409 NOT_OWNER)
        await SchedulingAssert.OutOfScope(() => w.Appointments.CreateRecurring(w.OrganizationId, other.UserId, false, Series(w, RecurrenceType.Weekly, 2)));

        Assert.Equal(0, await w.CountAppointments());
    }
}
