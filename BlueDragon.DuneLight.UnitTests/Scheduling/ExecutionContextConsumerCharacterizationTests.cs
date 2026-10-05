#nullable disable
using System;
using System.Linq;
using System.Threading.Tasks;
using BlueDragon.DuneLight.Core.DTOs.Appointments;
using BlueDragon.DuneLight.Core.DTOs.Checkouts;
using BlueDragon.DuneLight.Core.Enums;
using BlueDragon.DuneLight.Infrastructure.Domain.Models.Checkouts;
using BlueDragon.DuneLight.Infrastructure.Domain.Models.Clients;
using BlueDragon.DuneLight.Infrastructure.Domain.Models.Commissions;
using BlueDragon.DuneLight.Infrastructure.Domain.Models.Employees;
using ServiceEntity = BlueDragon.DuneLight.Infrastructure.Domain.Models.Catalog.Service;

namespace BlueDragon.DuneLight.UnitTests.Scheduling;

/// <summary>
/// CHARACTERIZATION of consumers moved onto the S2 execution context that the original suite did not pin directly:
/// the checkout item description, and completion reading the CURRENT segment (after segment commands changed its service /
/// employees) for commission and package deduction — never the values the appointment was booked with.
/// </summary>
public class ExecutionContextConsumerCharacterizationTests
{
    private static readonly DateTimeOffset LongValid = new(2035, 1, 1, 0, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task CheckoutBookingItem_IsDescribedByTheAppointmentsServiceName_AndPricedFromTheBooking()
    {
        await using SchedulingWorld w = await SchedulingWorld.Create(nameof(CheckoutBookingItem_IsDescribedByTheAppointmentsServiceName_AndPricedFromTheBooking));
        AppointmentDto created = await w.CreateAppointment(SchedulingWorld.Future(10));

        CheckoutDto checkout = await w.Checkouts.Create(w.OrganizationId, w.ActorUserId,
            new CheckoutCreateRequest { ClientId = w.Client.Id.Value, CompanyId = w.Company.Id.Value });
        await w.Checkouts.AddBookingItem(w.OrganizationId, w.ActorUserId, checkout.Id,
            new CheckoutAddBookingItemRequest { ParticipationId = Assert.Single(created.Bookings.Single().Participations).Id });

        CheckoutItem item = Assert.Single(await w.LoadCheckoutItems(created.Bookings.Single().Id));
        Assert.Equal(w.Service.Name, item.Description);
        Assert.Equal(SchedulingWorld.DefaultServicePrice, item.Amount);
    }

    [Fact]
    public async Task Completion_AfterTheSegmentsServiceAndEmployeeChanged_EarnsCommissionForTheNewPair()
    {
        await using SchedulingWorld w = await SchedulingWorld.Create(nameof(Completion_AfterTheSegmentsServiceAndEmployeeChanged_EarnsCommissionForTheNewPair));
        ServiceEntity service2 = await w.AddService(45, 80m);
        Employee employee2 = await w.AddEmployee("Second", serviceId: service2.Id);
        await w.AddCommissionRule(w.Employee, w.Service, CommissionCalculationType.Fixed, 7m); // original pair — must NOT apply
        await w.AddCommissionRule(employee2, service2, CommissionCalculationType.Percentage, 10m);
        await w.AssignEmployeeToService(w.Employee, service2);
        AppointmentDto created = await w.CreateAppointment(SchedulingWorld.Future(10));
        Guid segmentId = created.Segments[0].Id;
        await w.Appointments.ChangeSegmentService(w.OrganizationId, w.ActorUserId, true, segmentId,
            new AppointmentSegmentServiceChangeRequest { ServiceId = service2.Id.Value, UseServiceDuration = true });
        await w.Appointments.ChangeSegmentEmployees(w.OrganizationId, w.ActorUserId, true, segmentId,
            new AppointmentSegmentEmployeesChangeRequest { EmployeeIds = new List<Guid> { employee2.Id.Value } });

        await w.CompleteParticipations(created.Id, w.CompleteRequest(SchedulingWorld.Future(10)));

        CommissionEntry entry = Assert.Single(await w.LoadCommissionEntries());
        Assert.Equal(employee2.Id, entry.EmployeeId);
        Assert.Equal(created.Id, entry.AppointmentId);
        Assert.Equal(80m, entry.BaseAmount);
        Assert.Equal(8m, entry.CommissionAmount);
    }

    [Fact]
    public async Task Completion_AfterTheSegmentsServiceChanged_DeductsThePackageEntryOfTheNewService()
    {
        await using SchedulingWorld w = await SchedulingWorld.Create(nameof(Completion_AfterTheSegmentsServiceChanged_DeductsThePackageEntryOfTheNewService));
        ServiceEntity service2 = await w.AddService(45, 80m);
        await w.AssignEmployeeToService(w.Employee, service2);
        ClientPackage package = await w.AddClientPackage(w.Client, service2, 3, LongValid);
        AppointmentDto created = await w.CreateAppointment(SchedulingWorld.Future(10));
        await w.Appointments.ChangeSegmentService(w.OrganizationId, w.ActorUserId, true, created.Segments[0].Id,
            new AppointmentSegmentServiceChangeRequest { ServiceId = service2.Id.Value, UseServiceDuration = true });

        await w.CompleteParticipations(created.Id, w.CompleteRequest(SchedulingWorld.Future(10), clientPackageId: package.Id));

        ClientPackage reloaded = await w.LoadClientPackage(package.Id.Value);
        Assert.Equal(2, reloaded.ServiceEntries.Single(e => e.ServiceId == service2.Id).RemainingEntries);
    }
}
