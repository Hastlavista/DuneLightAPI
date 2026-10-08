#nullable disable
using System;
using System.Threading.Tasks;
using BlueDragon.DuneLight.Core.DTOs.Appointments;
using BlueDragon.DuneLight.Core.DTOs.Checkouts;
using BlueDragon.DuneLight.Core.Enums;
using BlueDragon.DuneLight.Infrastructure.Domain.Models.Appointments;
using BlueDragon.DuneLight.Infrastructure.Domain.Models.Catalog;
using BlueDragon.DuneLight.Infrastructure.Domain.Models.Clients;
using BlueDragon.DuneLight.Infrastructure.Domain.Models.Employees;
using ServiceEntityAlias = BlueDragon.DuneLight.Infrastructure.Domain.Models.Catalog.Service;

namespace BlueDragon.DuneLight.UnitTests.Scheduling;

/// <summary>
/// CHARACTERIZATION: tenant isolation of the scheduling write paths. Every id the caller supplies (client, employee, service,
/// room, appointment, booking) is resolved inside the caller's organization; an id from another tenant behaves exactly like a
/// non-existent one (NotFound) — never as a validation or authorization error that would confirm it exists.
/// Each test builds two independent worlds (two organizations) and passes the FOREIGN id to the first world's service.
/// </summary>
public class TenantIsolationCharacterizationTests
{
    private static async Task<(SchedulingWorld Mine, SchedulingWorld Foreign)> TwoTenants(string name) =>
        (await SchedulingWorld.Create(name + "-mine"), await SchedulingWorld.Create(name + "-foreign"));

    [Fact]
    public async Task Create_WithAClientOfAnotherOrganization_IsNotFound()
    {
        (SchedulingWorld mine, SchedulingWorld foreign) = await TwoTenants(nameof(Create_WithAClientOfAnotherOrganization_IsNotFound));
        await using SchedulingWorld a = mine;
        await using SchedulingWorld b = foreign;
        TestAppointmentSpec request = mine.CreateRequest(SchedulingWorld.Future(10));
        request.ClientIds = new() { foreign.Client.Id.Value };

        await SchedulingAssert.NotFound(() => mine.CreateAppointment(request));

        Assert.Equal(0, await mine.CountAppointments());
    }

    [Fact]
    public async Task Create_WithAnEmployeeOfAnotherOrganization_IsNotFound()
    {
        (SchedulingWorld mine, SchedulingWorld foreign) = await TwoTenants(nameof(Create_WithAnEmployeeOfAnotherOrganization_IsNotFound));
        await using SchedulingWorld a = mine;
        await using SchedulingWorld b = foreign;
        TestAppointmentSpec request = mine.CreateRequest(SchedulingWorld.Future(10));
        request.EmployeeId = foreign.Employee.Id.Value;

        await SchedulingAssert.NotFound(() => mine.CreateAppointment(request));
    }

    [Fact]
    public async Task Create_WithAServiceOfAnotherOrganization_IsNotFound()
    {
        (SchedulingWorld mine, SchedulingWorld foreign) = await TwoTenants(nameof(Create_WithAServiceOfAnotherOrganization_IsNotFound));
        await using SchedulingWorld a = mine;
        await using SchedulingWorld b = foreign;
        TestAppointmentSpec request = mine.CreateRequest(SchedulingWorld.Future(10));
        request.ServiceId = foreign.Service.Id.Value;

        await SchedulingAssert.NotFound(() => mine.CreateAppointment(request));
    }

    [Fact]
    public async Task Create_WithACompanyOfAnotherOrganization_IsNotFound()
    {
        (SchedulingWorld mine, SchedulingWorld foreign) = await TwoTenants(nameof(Create_WithACompanyOfAnotherOrganization_IsNotFound));
        await using SchedulingWorld a = mine;
        await using SchedulingWorld b = foreign;
        TestAppointmentSpec request = mine.CreateRequest(SchedulingWorld.Future(10));
        request.CompanyId = foreign.Company.Id.Value;

        await SchedulingAssert.NotFound(() => mine.CreateAppointment(request));
    }

    [Fact]
    public async Task Create_WithARoomOfAnotherOrganization_IsNotFound()
    {
        (SchedulingWorld mine, SchedulingWorld foreign) = await TwoTenants(nameof(Create_WithARoomOfAnotherOrganization_IsNotFound));
        await using SchedulingWorld a = mine;
        await using SchedulingWorld b = foreign;
        Room foreignRoom = await foreign.AddRoom();
        TestAppointmentSpec request = mine.CreateRequest(SchedulingWorld.Future(10));
        request.RoomId = foreignRoom.Id;

        await SchedulingAssert.NotFound(() => mine.CreateAppointment(request));
    }

    [Fact]
    public async Task ChangingAnAppointmentOfAnotherOrganization_IsNotFound_ForSegmentTimeNoteCancelAndComplete()
    {
        (SchedulingWorld mine, SchedulingWorld foreign) = await TwoTenants(nameof(ChangingAnAppointmentOfAnotherOrganization_IsNotFound_ForSegmentTimeNoteCancelAndComplete));
        await using SchedulingWorld a = mine;
        await using SchedulingWorld b = foreign;
        AppointmentDto theirs = await foreign.CreateAppointment(SchedulingWorld.Future(10));

        await SchedulingAssert.NotFound(() => mine.Appointments.ChangeSegmentTime(mine.OrganizationId, mine.ActorUserId, true, theirs.Segments[0].Id,
            new AppointmentSegmentTimeChangeRequest { PlannedStart = SchedulingWorld.Future(12) }));
        await SchedulingAssert.NotFound(() => mine.Appointments.Cancel(mine.OrganizationId, mine.ActorUserId, true, theirs.Id, SchedulingWorld.BusinessCancel()));
        await SchedulingAssert.NotFound(() => mine.Appointments.MarkNoShow(mine.OrganizationId, mine.ActorUserId, true, theirs.Id, new NoShowRequest()));
        await SchedulingAssert.NotFound(() => mine.Appointments.ChangeNote(mine.OrganizationId, mine.ActorUserId, true, theirs.Id,
            new AppointmentNoteChangeRequest { Note = "x" }));
        await SchedulingAssert.NotFound(() => mine.Bookings.SetParticipationStatus(mine.OrganizationId, mine.ActorUserId, true,
            Assert.Single(theirs.Bookings[0].Participations).Id, new BookingSetStatusRequest { Status = BookingStatus.Completed }));
        await SchedulingAssert.NotFound(() => mine.Bookings.SetParticipationPrice(mine.OrganizationId, mine.ActorUserId, true,
            Assert.Single(theirs.Bookings[0].Participations).Id, new ParticipationPriceChangeRequest { Amount = 1m }));
        await SchedulingAssert.NotFound(() => mine.Appointments.CompleteGroupAppointment(mine.OrganizationId, mine.ActorUserId, true, theirs.Id));

        // ... and the foreign appointment is untouched.
        Assert.Equal(AppointmentStatus.Scheduled, (await foreign.LoadAppointment(theirs.Id)).Status);
        Assert.Equal(SchedulingWorld.Future(10), (await foreign.LoadAppointment(theirs.Id)).StartsAt);
    }

    [Fact]
    public async Task ChangingABookingOfAnotherOrganization_IsNotFound()
    {
        (SchedulingWorld mine, SchedulingWorld foreign) = await TwoTenants(nameof(ChangingABookingOfAnotherOrganization_IsNotFound));
        await using SchedulingWorld a = mine;
        await using SchedulingWorld b = foreign;
        AppointmentDto theirs = await foreign.CreateAppointment(SchedulingWorld.Future(10));

        await SchedulingAssert.NotFound(() => mine.SetBookingStatus(theirs.Id, foreign.Client, BookingStatus.Cancelled));
        await SchedulingAssert.NotFound(() => mine.Appointments.AddClient(mine.OrganizationId, mine.ActorUserId, true, theirs.Id,
            new AppointmentClientAddRequest
            {
                ClientId = mine.Client.Id.Value,
                Participations = new List<AppointmentClientParticipationRequest> { new() { SegmentId = theirs.Segments[0].Id } }
            }));

        Assert.Equal(BookingStatus.Confirmed, (await foreign.LoadBooking(theirs.Id, foreign.Client)).Status);
    }

    [Fact]
    public async Task AddingAForeignClient_IsNotFound()
    {
        (SchedulingWorld mine, SchedulingWorld foreign) = await TwoTenants(nameof(AddingAForeignClient_IsNotFound));
        await using SchedulingWorld a = mine;
        await using SchedulingWorld b = foreign;
        AppointmentDto created = await mine.CreateAppointment(SchedulingWorld.Future(10));

        await SchedulingAssert.NotFound(() => mine.AddClientToOnlySegment(created.Id, foreign.Client));
    }

    [Fact]
    public async Task AddingAForeignBookingToACheckout_IsNotFound()
    {
        (SchedulingWorld mine, SchedulingWorld foreign) = await TwoTenants(nameof(AddingAForeignBookingToACheckout_IsNotFound));
        await using SchedulingWorld a = mine;
        await using SchedulingWorld b = foreign;
        AppointmentDto theirs = await foreign.CreateAppointment(SchedulingWorld.Future(10));
        CheckoutDto checkout = await mine.Checkouts.Create(mine.OrganizationId, mine.ActorUserId,
            new CheckoutCreateRequest { ClientId = mine.Client.Id.Value, CompanyId = mine.Company.Id.Value });

        await SchedulingAssert.NotFound(() => mine.Checkouts.AddBookingItem(mine.OrganizationId, mine.ActorUserId, checkout.Id,
            new CheckoutAddBookingItemRequest { ParticipationId = Assert.Single(theirs.Bookings[0].Participations).Id }));
    }
}
