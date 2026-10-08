using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;
using BlueDragon.DuneLight.Core.DTOs.Appointments;
using BlueDragon.DuneLight.Core.DTOs.Groups;
using BlueDragon.DuneLight.Core.Enums;
using BlueDragon.DuneLight.Core.Interfaces.Appointments;
using BlueDragon.DuneLight.Core.Shared;
using BlueDragon.DuneLight.Core.Shared.Exceptions;
using BlueDragon.DuneLight.Infrastructure.Domain.Contexts;
using BlueDragon.DuneLight.Infrastructure.Domain.Models.Appointments;
using BlueDragon.DuneLight.Infrastructure.Domain.Models.Catalog;
using BlueDragon.DuneLight.Infrastructure.Domain.Models.Clients;
using BlueDragon.DuneLight.Infrastructure.Domain.Models.Employees;
using BlueDragon.DuneLight.Infrastructure.UnitOfWork;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using ServiceEntity = BlueDragon.DuneLight.Infrastructure.Domain.Models.Catalog.Service;

namespace BlueDragon.DuneLight.UnitTests.Scheduling;

/// <summary>
/// Phase M1E — true multi-segment Appointments in production: atomic multi-segment create, segment-native commands
/// (SegmentId), participant commands (selected segments / ParticipationId), per-participation execution, pricing, package /
/// money settlement and late cancellation, whole-appointment authorization, legacy single-segment boundaries, group
/// non-regression and the optimistic segment check under concurrency.
/// </summary>
public class MultiSegmentAppointmentTests
{
    /// <summary>Massage 60 min (80) for employee A (the world's default employee) and Physio 30 min (40) for employee B.</summary>
    private sealed record Spa(ServiceEntity Massage, ServiceEntity Physio, Employee A, Employee B);

    private static async Task<Spa> SetUp(SchedulingWorld w)
    {
        ServiceEntity massage = await w.AddService(60, 80m, name: "Massage");
        ServiceEntity physio = await w.AddService(30, 40m, name: "Physio");
        await w.AssignEmployeeToService(w.Employee, massage);
        Employee b = await w.AddEmployee("B", serviceId: physio.Id);
        return new Spa(massage, physio, w.Employee, b);
    }

    private static AppointmentSegmentCreateRequest Seg(ServiceEntity service, DateTimeOffset start, Employee employee, params Client[] clients) => new()
    {
        ServiceId = service.Id.Value,
        PlannedStart = start,
        EmployeeIds = new List<Guid> { employee.Id.Value },
        Participants = clients.Select(c => new AppointmentParticipantCreateRequest { ClientId = c.Id.Value }).ToList()
    };

    private static Task<AppointmentDto> Create(SchedulingWorld w, params AppointmentSegmentCreateRequest[] segments) =>
        w.Appointments.Create(w.OrganizationId, w.ActorUserId, true, new AppointmentCreateRequest
        {
            CompanyId = w.Company.Id.Value,
            Segments = segments.ToList()
        });

    /// <summary>The spec's reference shape: Massage 09:00-10:00 (A, Client 1) + Physio 10:00-10:30 (B, Client 1).</summary>
    private static Task<AppointmentDto> CreateMassageThenPhysio(SchedulingWorld w, Spa spa) => Create(w,
        Seg(spa.Massage, SchedulingWorld.Future(9), spa.A, w.Client),
        Seg(spa.Physio, SchedulingWorld.Future(10), spa.B, w.Client));

    private static AppointmentSegmentDto SegmentOf(AppointmentDto dto, Guid serviceId) => dto.Segments.Single(s => s.ServiceId == serviceId);

    private static BookingParticipationDto ParticipationOn(AppointmentDto dto, Client client, Guid segmentId) =>
        dto.Bookings.Single(b => b.ClientId == client.Id).Participations.Single(p => p.AppointmentSegmentId == segmentId);

    private static Task<BookingDto> SetParticipation(SchedulingWorld w, Guid participationId, BookingStatus status, bool fullScope = true,
        Guid? userId = null, Guid? clientPackageId = null, PaymentMethod? paymentMethod = null) =>
        w.Bookings.SetParticipationStatus(w.OrganizationId, userId ?? w.ActorUserId, fullScope, participationId, new BookingSetStatusRequest
        {
            Status = status,
            CancellationInitiator = status == BookingStatus.Cancelled ? CancellationInitiator.Client : null,
            ClientPackageId = clientPackageId,
            PaymentMethod = paymentMethod
        });

    private static Task<AppointmentDto> Reload(SchedulingWorld w, Guid id) => w.Appointments.GetById(w.OrganizationId, id);

    #region Create

    [Fact]
    public async Task Create_MassageThenPhysio_IsOneAppointment_OneBooking_TwoParticipations_SpanningBothSegments()
    {
        await using SchedulingWorld w = await SchedulingWorld.Create(nameof(Create_MassageThenPhysio_IsOneAppointment_OneBooking_TwoParticipations_SpanningBothSegments));
        Spa spa = await SetUp(w);

        AppointmentDto dto = await CreateMassageThenPhysio(w, spa);

        Assert.Equal(1, await w.CountAppointments());
        Assert.Equal(2, dto.Segments.Count);
        Assert.Equal(SchedulingWorld.Future(9), dto.PlannedStart);
        Assert.Equal(SchedulingWorld.Future(10, 30), dto.PlannedEnd);
        Assert.Equal(AppointmentStatus.Scheduled, dto.Status);
        BookingDto booking = Assert.Single(dto.Bookings);
        Assert.Equal(BookingStatusSummary.Confirmed, booking.Status);
        Assert.Equal(2, booking.Participations.Count);

        AppointmentSegmentDto massage = SegmentOf(dto, spa.Massage.Id.Value);
        AppointmentSegmentDto physio = SegmentOf(dto, spa.Physio.Id.Value);
        Assert.Equal(spa.A.Id, Assert.Single(massage.Employees).EmployeeId);
        Assert.Equal(spa.B.Id, Assert.Single(physio.Employees).EmployeeId);
        Assert.Equal(SchedulingWorld.Future(10), massage.PlannedEnd);
        Assert.Equal(SchedulingWorld.Future(10, 30), physio.PlannedEnd);
        // Price per participation: each segment's own service.
        Assert.Equal(80m, ParticipationOn(dto, w.Client, massage.Id).Amount);
        Assert.Equal(40m, ParticipationOn(dto, w.Client, physio.Id).Amount);
        Assert.Equal(120m, booking.Amount);
    }

    [Fact]
    public async Task Create_ParticipantSubsets_GiveOneBookingPerClient_WithParticipationsOnlyOnSelectedSegments()
    {
        await using SchedulingWorld w = await SchedulingWorld.Create(nameof(Create_ParticipantSubsets_GiveOneBookingPerClient_WithParticipationsOnlyOnSelectedSegments));
        Spa spa = await SetUp(w);
        Employee c = await w.AddEmployee("C");
        Client a = w.Client;
        Client b = await w.AddClient("B");
        Client cc = await w.AddClient("C");

        // Segment 1: A+B, segment 2: B+C, segment 3: C.
        AppointmentDto dto = await Create(w,
            Seg(w.Service, SchedulingWorld.Future(9), spa.A, a, b),
            Seg(spa.Physio, SchedulingWorld.Future(9, 30), spa.B, b, cc),
            Seg(w.Service, SchedulingWorld.Future(10), c, cc));

        Assert.Equal(3, dto.Bookings.Count);
        Assert.Equal(new[] { 1, 2, 2 }, new[] { a, b, cc }.Select(x => dto.Bookings.Single(bk => bk.ClientId == x.Id).Participations.Count).OrderBy(n => n));
        Assert.Equal(5, dto.Bookings.Sum(bk => bk.Participations.Count));
        Guid first = dto.Segments.OrderBy(s => s.PlannedStart).First().Id;
        Guid last = dto.Segments.OrderBy(s => s.PlannedStart).Last().Id;
        Assert.Equal(first, Assert.Single(dto.Bookings.Single(bk => bk.ClientId == a.Id).Participations).AppointmentSegmentId);
        Assert.DoesNotContain(dto.Bookings.Single(bk => bk.ClientId == b.Id).Participations, p => p.AppointmentSegmentId == last);
    }

    [Fact]
    public async Task Create_ParallelSegments_DifferentEmployeesAndClients_AreAccepted()
    {
        await using SchedulingWorld w = await SchedulingWorld.Create(nameof(Create_ParallelSegments_DifferentEmployeesAndClients_AreAccepted));
        Spa spa = await SetUp(w);
        Client partner = await w.AddClient("Partner");

        AppointmentDto dto = await Create(w,
            Seg(spa.Massage, SchedulingWorld.Future(9), spa.A, w.Client),
            Seg(spa.Physio, SchedulingWorld.Future(9), spa.B, partner));

        Assert.Equal(2, dto.Segments.Count);
        Assert.Equal(SchedulingWorld.Future(10), dto.PlannedEnd);
        Assert.All(dto.Bookings, b => Assert.Single(b.Participations));
    }

    [Fact]
    public async Task Create_SiblingSegmentsAreValidatedAgainstEachOther_AndNothingIsPersisted()
    {
        await using SchedulingWorld w = await SchedulingWorld.Create(nameof(Create_SiblingSegmentsAreValidatedAgainstEachOther_AndNothingIsPersisted));
        Spa spa = await SetUp(w);
        Client partner = await w.AddClient("Partner");

        // Same client in two parallel segments.
        await SchedulingAssert.BusinessRule(ErrorCodes.AppointmentOverlap, () => Create(w,
            Seg(spa.Massage, SchedulingWorld.Future(9), spa.A, w.Client),
            Seg(spa.Physio, SchedulingWorld.Future(9, 30), spa.B, w.Client)));
        // Same employee in two parallel segments.
        await SchedulingAssert.BusinessRule(ErrorCodes.AppointmentOverlap, () => Create(w,
            Seg(spa.Massage, SchedulingWorld.Future(9), spa.A, w.Client),
            Seg(w.Service, SchedulingWorld.Future(9, 30), spa.A, partner)));

        Assert.Equal(0, await w.CountAppointments());
    }

    [Fact]
    public async Task Create_MultiEmployeeSegment_WithoutAPricingSource_IsRejected()
    {
        await using SchedulingWorld w = await SchedulingWorld.Create(nameof(Create_MultiEmployeeSegment_WithoutAPricingSource_IsRejected));
        Spa spa = await SetUp(w);
        AppointmentSegmentCreateRequest two = Seg(spa.Physio, SchedulingWorld.Future(10), spa.B, w.Client);
        two.EmployeeIds.Add(spa.A.Id.Value);

        ValidationAppException ex = await SchedulingAssert.Validation(() => Create(w, Seg(spa.Massage, SchedulingWorld.Future(9), spa.A, w.Client), two));

        // CHANGED in M1G: multi-employee segments are enabled, but 2+ employees require an explicit pricing source.
        Assert.Equal(ErrorCodes.PricingSourceRequired, ex.Code);
        Assert.Equal(0, await w.CountAppointments());
    }

    [Fact]
    public async Task LegacyFlatCreate_StillCreatesExactlyOneSegment()
    {
        await using SchedulingWorld w = await SchedulingWorld.Create(nameof(LegacyFlatCreate_StillCreatesExactlyOneSegment));

        AppointmentDto dto = await w.CreateAppointment(SchedulingWorld.Future(10));

        Assert.Single(dto.Segments);
        Assert.Single(Assert.Single(dto.Bookings).Participations);
    }

    #endregion

    #region Segment commands

    [Fact]
    public async Task AddSegment_ReusesTheClientsBooking_AndExtendsTheRange()
    {
        await using SchedulingWorld w = await SchedulingWorld.Create(nameof(AddSegment_ReusesTheClientsBooking_AndExtendsTheRange));
        Spa spa = await SetUp(w);
        AppointmentDto created = await Create(w, Seg(spa.Massage, SchedulingWorld.Future(9), spa.A, w.Client));

        AppointmentSegmentAddRequest add = new()
        {
            ServiceId = spa.Physio.Id.Value,
            PlannedStart = SchedulingWorld.Future(10),
            EmployeeIds = new List<Guid> { spa.B.Id.Value },
            Participants = new List<AppointmentParticipantCreateRequest> { new() { ClientId = w.Client.Id.Value } }
        };
        AppointmentDto dto = await w.Appointments.AddSegment(w.OrganizationId, w.ActorUserId, true, created.Id, add);

        Assert.Equal(2, dto.Segments.Count);
        Assert.Equal(SchedulingWorld.Future(10, 30), dto.PlannedEnd);
        BookingDto booking = Assert.Single(dto.Bookings);
        Assert.Equal(created.Bookings[0].Id, booking.Id);
        Assert.Equal(2, booking.Participations.Count);
        Assert.Equal(40m, ParticipationOn(dto, w.Client, SegmentOf(dto, spa.Physio.Id.Value).Id).Amount);
        Assert.Contains(await w.LoadAuditLog(created.Id), l => l.ChangeType == "SegmentAdded");
    }

    [Fact]
    public async Task AddSegment_ValidatesAgainstSiblings_AndOtherAppointments()
    {
        await using SchedulingWorld w = await SchedulingWorld.Create(nameof(AddSegment_ValidatesAgainstSiblings_AndOtherAppointments));
        Spa spa = await SetUp(w);
        AppointmentDto created = await Create(w, Seg(spa.Massage, SchedulingWorld.Future(9), spa.A, w.Client));

        // The client is already in the sibling massage 09:00-10:00.
        await SchedulingAssert.BusinessRule(ErrorCodes.AppointmentOverlap, () => w.Appointments.AddSegment(
            w.OrganizationId, w.ActorUserId, true, created.Id, new AppointmentSegmentAddRequest
            {
                ServiceId = spa.Physio.Id.Value,
                PlannedStart = SchedulingWorld.Future(9, 30),
                EmployeeIds = new List<Guid> { spa.B.Id.Value },
                Participants = new List<AppointmentParticipantCreateRequest> { new() { ClientId = w.Client.Id.Value } }
            }));

        Assert.Single((await w.LoadAppointment(created.Id)).Segments);
    }

    [Fact]
    public async Task RemoveSegment_OnlyUntouched_NeverTheLast_AndEmptyBookingsAreRemoved()
    {
        await using SchedulingWorld w = await SchedulingWorld.Create(nameof(RemoveSegment_OnlyUntouched_NeverTheLast_AndEmptyBookingsAreRemoved));
        Spa spa = await SetUp(w);
        Client partner = await w.AddClient("Partner");
        AppointmentDto created = await Create(w,
            Seg(spa.Massage, SchedulingWorld.Future(9), spa.A, w.Client),
            Seg(spa.Physio, SchedulingWorld.Future(10), spa.B, w.Client, partner));
        Guid massage = SegmentOf(created, spa.Massage.Id.Value).Id;
        Guid physio = SegmentOf(created, spa.Physio.Id.Value).Id;

        // A segment with history cannot be removed.
        await SetParticipation(w, ParticipationOn(created, w.Client, massage).Id, BookingStatus.Cancelled);
        await SchedulingAssert.BusinessRule(ErrorCodes.ReferencedCannotDelete,
            () => w.Appointments.RemoveSegment(w.OrganizationId, w.ActorUserId, true, massage));

        // The untouched physio segment can — the partner's Booking (only on it) disappears, the client's Booking stays.
        AppointmentDto dto = await w.Appointments.RemoveSegment(w.OrganizationId, w.ActorUserId, true, physio);
        AppointmentSegmentDto remaining = Assert.Single(dto.Segments);
        Assert.Equal(massage, remaining.Id);
        BookingDto booking = Assert.Single(dto.Bookings);
        Assert.Equal(w.Client.Id, booking.ClientId);
        Assert.Single(booking.Participations);

        await SchedulingAssert.BusinessRule(ErrorCodes.LastSegmentCannotBeRemoved,
            () => w.Appointments.RemoveSegment(w.OrganizationId, w.ActorUserId, true, massage));
    }

    [Fact]
    public async Task ChangeSegmentTime_MovesOnlyThatSegment_AndValidatesSiblings()
    {
        await using SchedulingWorld w = await SchedulingWorld.Create(nameof(ChangeSegmentTime_MovesOnlyThatSegment_AndValidatesSiblings));
        Spa spa = await SetUp(w);
        AppointmentDto created = await CreateMassageThenPhysio(w, spa);
        Guid physio = SegmentOf(created, spa.Physio.Id.Value).Id;

        // Into the massage of the same client: rejected (sibling stays visible), nothing changes.
        await SchedulingAssert.BusinessRule(ErrorCodes.AppointmentOverlap, () => w.Appointments.ChangeSegmentTime(
            w.OrganizationId, w.ActorUserId, true, physio, new AppointmentSegmentTimeChangeRequest { PlannedStart = SchedulingWorld.Future(9, 45) }));

        AppointmentDto dto = await w.Appointments.ChangeSegmentTime(
            w.OrganizationId, w.ActorUserId, true, physio, new AppointmentSegmentTimeChangeRequest { PlannedStart = SchedulingWorld.Future(11) });

        Assert.Equal(SchedulingWorld.Future(9), SegmentOf(dto, spa.Massage.Id.Value).PlannedStart);
        Assert.Equal(SchedulingWorld.Future(11), SegmentOf(dto, spa.Physio.Id.Value).PlannedStart);
        Assert.Equal(SchedulingWorld.Future(11, 30), dto.PlannedEnd); // duration kept
    }

    [Fact]
    public async Task ChangeSegmentService_PersistsAndReprices_KeepingAManualOverride()
    {
        await using SchedulingWorld w = await SchedulingWorld.Create(nameof(ChangeSegmentService_PersistsAndReprices_KeepingAManualOverride));
        Spa spa = await SetUp(w);
        ServiceEntity deep = await w.AddService(45, 70m, name: "Deep physio");
        await w.AssignEmployeeToService(spa.B, deep);
        Client manual = await w.AddClient("Manual");
        AppointmentSegmentCreateRequest physioRequest = Seg(spa.Physio, SchedulingWorld.Future(10), spa.B, w.Client, manual);
        physioRequest.Participants[1].Amount = 33m;
        AppointmentDto created = await Create(w, Seg(spa.Massage, SchedulingWorld.Future(9), spa.A, w.Client), physioRequest);
        Guid physio = SegmentOf(created, spa.Physio.Id.Value).Id;

        AppointmentDto dto = await w.Appointments.ChangeSegmentService(w.OrganizationId, w.ActorUserId, true, physio,
            new AppointmentSegmentServiceChangeRequest { ServiceId = deep.Id.Value, UseServiceDuration = true });

        AppointmentSegmentDto changed = dto.Segments.Single(s => s.Id == physio);
        Assert.Equal(deep.Id, changed.ServiceId);
        Assert.Equal(SchedulingWorld.Future(10, 45), changed.PlannedEnd);
        Assert.Equal(70m, ParticipationOn(dto, w.Client, physio).Amount);
        BookingParticipationDto overridden = ParticipationOn(dto, manual, physio);
        Assert.Equal(33m, overridden.Amount);
        Assert.Equal(70m, overridden.SuggestedAmount);
        Assert.True(overridden.IsAmountManuallyOverridden);
        // The sibling massage is untouched.
        Assert.Equal(80m, ParticipationOn(dto, w.Client, SegmentOf(dto, spa.Massage.Id.Value).Id).Amount);
    }

    [Fact]
    public async Task ChangeSegmentService_RejectsAnEmployeeNotAssignedToTheNewService()
    {
        await using SchedulingWorld w = await SchedulingWorld.Create(nameof(ChangeSegmentService_RejectsAnEmployeeNotAssignedToTheNewService));
        Spa spa = await SetUp(w);
        AppointmentDto created = await CreateMassageThenPhysio(w, spa);

        await SchedulingAssert.BusinessRule(ErrorCodes.EmployeeNotAssignedToService, () => w.Appointments.ChangeSegmentService(
            w.OrganizationId, w.ActorUserId, true, SegmentOf(created, spa.Physio.Id.Value).Id,
            new AppointmentSegmentServiceChangeRequest { ServiceId = spa.Massage.Id.Value }));
    }

    [Fact]
    public async Task ChangeSegmentEmployees_ExactlyOne_PersistsAndRevalidates()
    {
        await using SchedulingWorld w = await SchedulingWorld.Create(nameof(ChangeSegmentEmployees_ExactlyOne_PersistsAndRevalidates));
        Spa spa = await SetUp(w);
        Employee c = await w.AddEmployee("C", serviceId: spa.Physio.Id);
        AppointmentDto created = await CreateMassageThenPhysio(w, spa);
        Guid physio = SegmentOf(created, spa.Physio.Id.Value).Id;

        ValidationAppException two = await SchedulingAssert.Validation(() => w.Appointments.ChangeSegmentEmployees(w.OrganizationId, w.ActorUserId, true, physio,
            new AppointmentSegmentEmployeesChangeRequest { EmployeeIds = new List<Guid> { spa.B.Id.Value, c.Id.Value } }));
        Assert.Equal(ErrorCodes.PricingSourceRequired, two.Code); // CHANGED in M1G: allowed with an explicit pricing source
        // A is busy in the sibling massage until 10:00? No — physio starts at 10:00; A is free but not assigned to Physio.
        await SchedulingAssert.BusinessRule(ErrorCodes.EmployeeNotAssignedToService, () => w.Appointments.ChangeSegmentEmployees(
            w.OrganizationId, w.ActorUserId, true, physio, new AppointmentSegmentEmployeesChangeRequest { EmployeeIds = new List<Guid> { spa.A.Id.Value } }));

        AppointmentDto dto = await w.Appointments.ChangeSegmentEmployees(w.OrganizationId, w.ActorUserId, true, physio,
            new AppointmentSegmentEmployeesChangeRequest { EmployeeIds = new List<Guid> { c.Id.Value } });

        Assert.Equal(c.Id, Assert.Single(dto.Segments.Single(s => s.Id == physio).Employees).EmployeeId);
        Assert.Equal(spa.A.Id, Assert.Single(SegmentOf(dto, spa.Massage.Id.Value).Employees).EmployeeId);
    }

    [Fact]
    public async Task ChangeSegmentRoom_EnforcesRoomCapacity_AndCanClearTheRoom()
    {
        await using SchedulingWorld w = await SchedulingWorld.Create(nameof(ChangeSegmentRoom_EnforcesRoomCapacity_AndCanClearTheRoom));
        Spa spa = await SetUp(w);
        Room single = await w.AddRoom(capacity: 1);
        Room pair = await w.AddRoom(capacity: 2);
        AppointmentDto created = await CreateMassageThenPhysio(w, spa);
        Guid physio = SegmentOf(created, spa.Physio.Id.Value).Id;

        // Employee B + the client = 2 people.
        await SchedulingAssert.BusinessRule(ErrorCodes.RoomCapacityExceeded, () => w.Appointments.ChangeSegmentRoom(
            w.OrganizationId, w.ActorUserId, true, physio, new AppointmentSegmentRoomChangeRequest { RoomId = single.Id }));

        AppointmentDto dto = await w.Appointments.ChangeSegmentRoom(
            w.OrganizationId, w.ActorUserId, true, physio, new AppointmentSegmentRoomChangeRequest { RoomId = pair.Id });
        Assert.Equal(pair.Id, dto.Segments.Single(s => s.Id == physio).RoomId);
        Assert.Null(SegmentOf(dto, spa.Massage.Id.Value).RoomId);

        dto = await w.Appointments.ChangeSegmentRoom(w.OrganizationId, w.ActorUserId, true, physio, new AppointmentSegmentRoomChangeRequest());
        Assert.Null(dto.Segments.Single(s => s.Id == physio).RoomId);
    }

    [Fact]
    public async Task ChangeSegmentResources_ReplacesTheAssignment_AndEnforcesQuantityCapacity()
    {
        await using SchedulingWorld w = await SchedulingWorld.Create(nameof(ChangeSegmentResources_ReplacesTheAssignment_AndEnforcesQuantityCapacity));
        Spa spa = await SetUp(w);
        Resource table = await w.AddResource(capacity: 2, name: "Table");
        Resource lamp = await w.AddResource(capacity: 1, name: "Lamp");
        AppointmentDto created = await CreateMassageThenPhysio(w, spa);
        Guid physio = SegmentOf(created, spa.Physio.Id.Value).Id;

        await SchedulingAssert.BusinessRule(ErrorCodes.ResourceCapacityExceeded, () => w.Appointments.ChangeSegmentResources(
            w.OrganizationId, w.ActorUserId, true, physio, new AppointmentSegmentResourcesChangeRequest
            {
                Resources = new List<AppointmentSegmentResourceRequest> { new() { ResourceId = table.Id.Value, QuantityRequired = 3 } }
            }));

        AppointmentDto dto = await w.Appointments.ChangeSegmentResources(w.OrganizationId, w.ActorUserId, true, physio,
            new AppointmentSegmentResourcesChangeRequest
            {
                Resources = new List<AppointmentSegmentResourceRequest>
                {
                    new() { ResourceId = table.Id.Value, QuantityRequired = 2 },
                    new() { ResourceId = lamp.Id.Value, QuantityRequired = 1 }
                }
            });
        Assert.Equal(2, dto.Segments.Single(s => s.Id == physio).Resources.Count);

        dto = await w.Appointments.ChangeSegmentResources(w.OrganizationId, w.ActorUserId, true, physio,
            new AppointmentSegmentResourcesChangeRequest
            {
                Resources = new List<AppointmentSegmentResourceRequest> { new() { ResourceId = lamp.Id.Value, QuantityRequired = 1 } }
            });
        AppointmentSegmentResourceDto only = Assert.Single(dto.Segments.Single(s => s.Id == physio).Resources);
        Assert.Equal(lamp.Id, only.ResourceId);
        Assert.Empty(SegmentOf(dto, spa.Massage.Id.Value).Resources);
    }

    [Fact]
    public async Task SegmentCommands_OnAnExplicitlyCancelledAppointment_AreRefused()
    {
        await using SchedulingWorld w = await SchedulingWorld.Create(nameof(SegmentCommands_OnAnExplicitlyCancelledAppointment_AreRefused));
        Spa spa = await SetUp(w);
        AppointmentDto created = await CreateMassageThenPhysio(w, spa);
        await w.Appointments.Cancel(w.OrganizationId, w.ActorUserId, true, created.Id, SchedulingWorld.BusinessCancel());

        await SchedulingAssert.BusinessRule(ErrorCodes.AppointmentNotMovable, () => w.Appointments.ChangeSegmentTime(w.OrganizationId, w.ActorUserId, true,
            SegmentOf(created, spa.Physio.Id.Value).Id, new AppointmentSegmentTimeChangeRequest { PlannedStart = SchedulingWorld.Future(12) }));
    }

    #endregion

    #region Client / participation commands

    [Fact]
    public async Task AddClient_ToSelectedSegments_ReusesTheBooking_AndPricesPerParticipation()
    {
        await using SchedulingWorld w = await SchedulingWorld.Create(nameof(AddClient_ToSelectedSegments_ReusesTheBooking_AndPricesPerParticipation));
        Spa spa = await SetUp(w);
        Client partner = await w.AddClient("Partner");
        Room pair = await w.AddRoom(capacity: 3);
        AppointmentDto created = await CreateMassageThenPhysio(w, spa);
        Guid massage = SegmentOf(created, spa.Massage.Id.Value).Id;
        Guid physio = SegmentOf(created, spa.Physio.Id.Value).Id;
        await w.Appointments.ChangeSegmentRoom(w.OrganizationId, w.ActorUserId, true, physio, new AppointmentSegmentRoomChangeRequest { RoomId = pair.Id });

        AppointmentDto dto = await w.Appointments.AddClient(w.OrganizationId, w.ActorUserId, true, created.Id, new AppointmentClientAddRequest
        {
            ClientId = partner.Id.Value,
            Participations = new List<AppointmentClientParticipationRequest> { new() { SegmentId = physio, Amount = 25m } }
        });
        BookingDto partnerBooking = dto.Bookings.Single(b => b.ClientId == partner.Id);
        BookingParticipationDto onPhysio = Assert.Single(partnerBooking.Participations);
        Assert.Equal(physio, onPhysio.AppointmentSegmentId);
        Assert.Equal(25m, onPhysio.Amount);
        Assert.Equal(40m, onPhysio.SuggestedAmount);

        // Adding the same client to another segment reuses the Booking; a duplicate selection is refused.
        await SchedulingAssert.BusinessRule(ErrorCodes.DuplicateParticipation, () => w.Appointments.AddClient(w.OrganizationId, w.ActorUserId, true,
            created.Id, new AppointmentClientAddRequest
            {
                ClientId = partner.Id.Value,
                Participations = new List<AppointmentClientParticipationRequest> { new() { SegmentId = physio } }
            }));
        dto = await w.Appointments.AddClient(w.OrganizationId, w.ActorUserId, true, created.Id, new AppointmentClientAddRequest
        {
            ClientId = partner.Id.Value,
            Participations = new List<AppointmentClientParticipationRequest> { new() { SegmentId = massage } }
        });
        BookingDto reused = dto.Bookings.Single(b => b.ClientId == partner.Id);
        Assert.Equal(partnerBooking.Id, reused.Id);
        Assert.Equal(2, reused.Participations.Count);
        Assert.Equal(80m, reused.Participations.Single(p => p.AppointmentSegmentId == massage).Amount);
        Assert.Equal(2, dto.Bookings.Count);
    }

    [Fact]
    public async Task AddClient_ValidatesTheClientsScheduleOnTheSelectedSegment()
    {
        await using SchedulingWorld w = await SchedulingWorld.Create(nameof(AddClient_ValidatesTheClientsScheduleOnTheSelectedSegment));
        Spa spa = await SetUp(w);
        Client busy = await w.AddClient("Busy");
        Employee other = await w.AddEmployee("Other");
        await w.CreateAppointment(SchedulingWorld.Future(10), client: busy, employee: other);
        AppointmentDto created = await CreateMassageThenPhysio(w, spa);

        await SchedulingAssert.BusinessRule(ErrorCodes.AppointmentOverlap, () => w.Appointments.AddClient(w.OrganizationId, w.ActorUserId, true,
            created.Id, new AppointmentClientAddRequest
            {
                ClientId = busy.Id.Value,
                Participations = new List<AppointmentClientParticipationRequest> { new() { SegmentId = SegmentOf(created, spa.Physio.Id.Value).Id } }
            }));

        // The massage 09:00-10:00 does not overlap the 10:00 appointment (half-open) — allowed.
        AppointmentDto dto = await w.Appointments.AddClient(w.OrganizationId, w.ActorUserId, true, created.Id, new AppointmentClientAddRequest
        {
            ClientId = busy.Id.Value,
            Participations = new List<AppointmentClientParticipationRequest> { new() { SegmentId = SegmentOf(created, spa.Massage.Id.Value).Id } }
        });
        Assert.Equal(2, dto.Bookings.Count);
    }

    [Fact]
    public async Task RemoveParticipation_UntouchedIsDeleted_EmptyBookingRemoved_HistoryMustBeCancelled()
    {
        await using SchedulingWorld w = await SchedulingWorld.Create(nameof(RemoveParticipation_UntouchedIsDeleted_EmptyBookingRemoved_HistoryMustBeCancelled));
        Spa spa = await SetUp(w);
        Client partner = await w.AddClient("Partner");
        AppointmentDto created = await Create(w,
            Seg(spa.Massage, SchedulingWorld.Future(9), spa.A, w.Client),
            Seg(spa.Physio, SchedulingWorld.Future(10), spa.B, w.Client, partner));
        Guid massage = SegmentOf(created, spa.Massage.Id.Value).Id;
        Guid physio = SegmentOf(created, spa.Physio.Id.Value).Id;

        AppointmentDto dto = await w.Appointments.RemoveParticipation(w.OrganizationId, w.ActorUserId, true, ParticipationOn(created, w.Client, physio).Id);
        Assert.Equal(massage, Assert.Single(dto.Bookings.Single(b => b.ClientId == w.Client.Id).Participations).AppointmentSegmentId);

        dto = await w.Appointments.RemoveParticipation(w.OrganizationId, w.ActorUserId, true, ParticipationOn(created, partner, physio).Id);
        Assert.DoesNotContain(dto.Bookings, b => b.ClientId == partner.Id);
        Assert.Equal(2, dto.Segments.Count); // the segment itself stays (now without participants)

        Guid clientOnMassage = ParticipationOn(created, w.Client, massage).Id;
        await SetParticipation(w, clientOnMassage, BookingStatus.Cancelled);
        await SchedulingAssert.BusinessRule(ErrorCodes.ReferencedCannotDelete,
            () => w.Appointments.RemoveParticipation(w.OrganizationId, w.ActorUserId, true, clientOnMassage));
    }

    #endregion

    #region Execution, lifecycle and settlement per participation

    [Fact]
    public async Task Execution_CompleteMassage_BookingMixed_AppointmentScheduled_ThenCompletePhysio_Closed()
    {
        await using SchedulingWorld w = await SchedulingWorld.Create(nameof(Execution_CompleteMassage_BookingMixed_AppointmentScheduled_ThenCompletePhysio_Closed));
        Spa spa = await SetUp(w);
        AppointmentDto created = await CreateMassageThenPhysio(w, spa);

        BookingDto afterA = await SetParticipation(w, ParticipationOn(created, w.Client, SegmentOf(created, spa.Massage.Id.Value).Id).Id, BookingStatus.Completed);
        Assert.Equal(BookingStatusSummary.Mixed, afterA.Status);
        Assert.Equal(AppointmentStatus.Scheduled, (await w.LoadAppointment(created.Id)).Status);

        BookingDto afterB = await SetParticipation(w, ParticipationOn(created, w.Client, SegmentOf(created, spa.Physio.Id.Value).Id).Id, BookingStatus.Completed);
        Assert.Equal(BookingStatusSummary.Completed, afterB.Status);
        Assert.Equal(AppointmentStatus.Closed, (await w.LoadAppointment(created.Id)).Status);
    }

    [Fact]
    public async Task Execution_CompleteMassage_CancelPhysio_BookingMixed_AppointmentClosed()
    {
        await using SchedulingWorld w = await SchedulingWorld.Create(nameof(Execution_CompleteMassage_CancelPhysio_BookingMixed_AppointmentClosed));
        Spa spa = await SetUp(w);
        AppointmentDto created = await CreateMassageThenPhysio(w, spa);

        await SetParticipation(w, ParticipationOn(created, w.Client, SegmentOf(created, spa.Massage.Id.Value).Id).Id, BookingStatus.Completed);
        BookingDto booking = await SetParticipation(w, ParticipationOn(created, w.Client, SegmentOf(created, spa.Physio.Id.Value).Id).Id, BookingStatus.Cancelled);

        Assert.Equal(BookingStatusSummary.Mixed, booking.Status);
        Assert.Equal(AppointmentStatus.Closed, (await w.LoadAppointment(created.Id)).Status);
    }

    [Fact]
    public async Task Settlement_PackageOnMassage_MoneyOnPhysio_InTheSameBooking()
    {
        await using SchedulingWorld w = await SchedulingWorld.Create(nameof(Settlement_PackageOnMassage_MoneyOnPhysio_InTheSameBooking));
        Spa spa = await SetUp(w);
        ClientPackage package = await w.AddClientPackage(w.Client, spa.Massage, 5, new DateOnly(2032, 1, 1));
        AppointmentDto created = await CreateMassageThenPhysio(w, spa);
        BookingParticipationDto massage = ParticipationOn(created, w.Client, SegmentOf(created, spa.Massage.Id.Value).Id);
        BookingParticipationDto physio = ParticipationOn(created, w.Client, SegmentOf(created, spa.Physio.Id.Value).Id);

        // The massage package does not cover Physio.
        await SchedulingAssert.BusinessRule(ErrorCodes.PackageNotEligible,
            () => SetParticipation(w, physio.Id, BookingStatus.Completed, clientPackageId: package.Id));

        await SetParticipation(w, massage.Id, BookingStatus.Completed, clientPackageId: package.Id);
        BookingDto booking = await SetParticipation(w, physio.Id, BookingStatus.Completed, paymentMethod: PaymentMethod.Cash);

        Assert.Equal(4, Assert.Single((await w.LoadClientPackage(package.Id.Value)).ServiceEntries).RemainingEntries);
        BookingParticipationDto paidPhysio = booking.Participations.Single(p => p.Id == physio.Id);
        BookingParticipationDto coveredMassage = booking.Participations.Single(p => p.Id == massage.Id);
        Assert.True(coveredMassage.PackageCovered);
        Assert.False(paidPhysio.PackageCovered);
        Assert.True(paidPhysio.IsPaid);
        Assert.Equal(40m, paidPhysio.PaidAmount);
        Assert.Equal(0m, coveredMassage.PaidAmount);
        Payment payment = Assert.Single(await w.LoadPayments(booking.Id));
        Assert.Equal(40m, payment.Amount);
        Assert.Equal(AppointmentStatus.Closed, (await w.LoadAppointment(created.Id)).Status);
    }

    [Fact]
    public async Task LateCancellation_IsJudgedPerParticipationSegmentStart_ForABookingWideCancel()
    {
        await using SchedulingWorld w = await SchedulingWorld.Create(nameof(LateCancellation_IsJudgedPerParticipationSegmentStart_ForABookingWideCancel));
        Spa spa = await SetUp(w);
        AppointmentDto created = await CreateMassageThenPhysio(w, spa);
        // The cutoff ends between the massage (09:00) and the physio (10:00).
        await w.SetCancellationWindowMinutes((int)(SchedulingWorld.Future(9, 30) - DateTimeOffset.UtcNow).TotalMinutes);

        BookingDto booking = await w.Bookings.CancelBooking(w.OrganizationId, w.ActorUserId, true, created.Id, w.Client.Id.Value, SchedulingWorld.ClientCancel());

        Assert.Equal(BookingStatusSummary.Cancelled, booking.Status);
        Assert.True(booking.Participations.Single(p => p.AppointmentSegmentId == SegmentOf(created, spa.Massage.Id.Value).Id).IsLateCancellation);
        Assert.False(booking.Participations.Single(p => p.AppointmentSegmentId == SegmentOf(created, spa.Physio.Id.Value).Id).IsLateCancellation);
        // Lifecycle unchanged (M1A.1): participations cancelled one by one never make the appointment explicitly Cancelled.
        Assert.Equal(AppointmentStatus.Scheduled, (await w.LoadAppointment(created.Id)).Status);
    }

    [Fact]
    public async Task LateCancellation_ForASingleParticipation_UsesItsOwnSegmentStart()
    {
        await using SchedulingWorld w = await SchedulingWorld.Create(nameof(LateCancellation_ForASingleParticipation_UsesItsOwnSegmentStart));
        Spa spa = await SetUp(w);
        AppointmentDto created = await CreateMassageThenPhysio(w, spa);
        await w.SetCancellationWindowMinutes((int)(SchedulingWorld.Future(9, 30) - DateTimeOffset.UtcNow).TotalMinutes);

        BookingDto booking = await SetParticipation(w, ParticipationOn(created, w.Client, SegmentOf(created, spa.Physio.Id.Value).Id).Id, BookingStatus.Cancelled);

        Assert.False(booking.Participations.Single(p => p.AppointmentSegmentId == SegmentOf(created, spa.Physio.Id.Value).Id).IsLateCancellation);
        Assert.Equal(BookingStatusSummary.Mixed, booking.Status);
    }

    /// <summary>Organization cutoff that ends at 09:30 on the test day: a segment starting 09:00 is inside the late window, any
    /// segment starting 10:00 or later is outside it.</summary>
    private static Task CutoffEndingAt0930(SchedulingWorld w) =>
        w.SetCancellationWindowMinutes((int)(SchedulingWorld.Future(9, 30) - DateTimeOffset.UtcNow).TotalMinutes);

    [Fact]
    public async Task BookingWideClientCancel_ClassifiesEachParticipationFromItsOwnSegmentStart()
    {
        // P1 (D2/D3): classification belongs to a CLIENT cancellation and is per participation segment (an appointment-wide
        // cancellation is Business and is never classified — see AppointmentCancel_AfterPartialExecution).
        await using SchedulingWorld w = await SchedulingWorld.Create(nameof(BookingWideClientCancel_ClassifiesEachParticipationFromItsOwnSegmentStart));
        Spa spa = await SetUp(w);
        AppointmentDto created = await CreateMassageThenPhysio(w, spa);
        await CutoffEndingAt0930(w);

        BookingDto booking = await w.Bookings.CancelBooking(w.OrganizationId, w.ActorUserId, true, created.Id, w.Client.Id.Value,
            SchedulingWorld.ClientCancel("cannot come"));

        Assert.Equal(BookingStatusSummary.Cancelled, booking.Status);
        BookingParticipationDto massage = booking.Participations.Single(p => p.AppointmentSegmentId == SegmentOf(created, spa.Massage.Id.Value).Id);
        BookingParticipationDto physio = booking.Participations.Single(p => p.AppointmentSegmentId == SegmentOf(created, spa.Physio.Id.Value).Id);
        Assert.True(massage.IsLateCancellation);
        Assert.False(physio.IsLateCancellation);
        Assert.All(booking.Participations, p =>
        {
            Assert.Equal(CancellationInitiator.Client, p.CancellationInitiator);
            Assert.Equal("cannot come", p.CancellationReason);
            Assert.Equal(p.CancelledAt, booking.Participations[0].CancelledAt); // one server timestamp for the whole command
        });

        // D2 debt: a Booking-wide client cancel of the last client leaves the appointment Scheduled (no explicit cancel).
        Appointment a = await w.LoadAppointment(created.Id);
        Assert.Null(a.CancelledAt);
        Assert.Equal(AppointmentStatus.Scheduled, a.Status);
    }

    [Fact]
    public async Task ClientCancel_ManyBookingsAndSegments_NeverSharesOneClassification()
    {
        await using SchedulingWorld w = await SchedulingWorld.Create(nameof(ClientCancel_ManyBookingsAndSegments_NeverSharesOneClassification));
        Spa spa = await SetUp(w);
        Employee c = await w.AddEmployee("C");
        Client first = w.Client;
        Client second = await w.AddClient("Second");
        Client third = await w.AddClient("Third");
        // 09:00 (inside the window): first + second; 10:00: second + third; 11:00: third.
        AppointmentDto created = await Create(w,
            Seg(spa.Massage, SchedulingWorld.Future(9), spa.A, first, second),
            Seg(spa.Physio, SchedulingWorld.Future(10), spa.B, second, third),
            Seg(w.Service, SchedulingWorld.Future(11), c, third));
        await CutoffEndingAt0930(w);

        foreach (Client client in new[] { first, second, third })
            await w.Bookings.CancelBooking(w.OrganizationId, w.ActorUserId, true, created.Id, client.Id.Value, SchedulingWorld.ClientCancel());
        AppointmentDto dto = await w.Appointments.GetById(w.OrganizationId, created.Id);

        Guid early = dto.Segments.OrderBy(s => s.PlannedStart).First().Id;
        foreach (BookingDto booking in dto.Bookings)
        {
            Assert.Equal(BookingStatusSummary.Cancelled, booking.Status);
            Assert.All(booking.Participations, p =>
            {
                Assert.Equal(BookingStatus.Cancelled, p.Status);
                Assert.Equal(p.AppointmentSegmentId == early, p.IsLateCancellation);
            });
        }
        // One Booking late only, one mixed, one never late although the appointment itself starts inside the window.
        Assert.All(dto.Bookings.Single(b => b.ClientId == first.Id).Participations, p => Assert.True(p.IsLateCancellation));
        Assert.Equal(new bool?[] { false, true },
            dto.Bookings.Single(b => b.ClientId == second.Id).Participations.Select(p => p.IsLateCancellation).OrderBy(x => x));
        Assert.All(dto.Bookings.Single(b => b.ClientId == third.Id).Participations, p => Assert.False(p.IsLateCancellation));
    }

    [Fact]
    public async Task AppointmentCancel_AfterPartialExecution_LeavesCompletedUntouched_AndClosesTheAppointment()
    {
        await using SchedulingWorld w = await SchedulingWorld.Create(nameof(AppointmentCancel_AfterPartialExecution_LeavesCompletedUntouched_AndClosesTheAppointment));
        Spa spa = await SetUp(w);
        AppointmentDto created = await CreateMassageThenPhysio(w, spa);
        Guid massageSegment = SegmentOf(created, spa.Massage.Id.Value).Id;
        Guid physioSegment = SegmentOf(created, spa.Physio.Id.Value).Id;
        await SetParticipation(w, ParticipationOn(created, w.Client, massageSegment).Id, BookingStatus.Completed);
        await CutoffEndingAt0930(w);

        AppointmentDto dto = await w.Appointments.Cancel(w.OrganizationId, w.ActorUserId, true, created.Id, SchedulingWorld.BusinessCancel());

        BookingParticipationDto massage = ParticipationOn(dto, w.Client, massageSegment);
        BookingParticipationDto physio = ParticipationOn(dto, w.Client, physioSegment);
        Assert.Equal(BookingStatus.Completed, massage.Status);
        Assert.Null(massage.IsLateCancellation);
        Assert.Equal(BookingStatus.Cancelled, physio.Status);
        Assert.Null(physio.IsLateCancellation); // P1 (D2): an appointment-wide cancellation is Business — never classified
        Assert.Equal(BookingStatusSummary.Mixed, Assert.Single(dto.Bookings).Status);
        Appointment a = await w.LoadAppointment(created.Id);
        Assert.Equal(AppointmentStatus.Closed, a.Status);
        Assert.NotNull(a.CancelledAt);
        Assert.Single(await w.LoadAuditLog(created.Id), l => l.ChangeType == "AppointmentCancelled");
    }

    #endregion

    #region Authorization

    [Fact]
    public async Task OwnScope_MayChangeOnlyItsOwnSegment_AndItsOwnParticipations()
    {
        await using SchedulingWorld w = await SchedulingWorld.Create(nameof(OwnScope_MayChangeOnlyItsOwnSegment_AndItsOwnParticipations));
        Spa spa = await SetUp(w);
        AppointmentDto created = await CreateMassageThenPhysio(w, spa);
        Guid massage = SegmentOf(created, spa.Massage.Id.Value).Id;
        Guid physio = SegmentOf(created, spa.Physio.Id.Value).Id;

        await SchedulingAssert.BusinessRule(ErrorCodes.NotOwner, () => w.Appointments.ChangeSegmentTime(w.OrganizationId, spa.A.UserId, false, physio,
            new AppointmentSegmentTimeChangeRequest { PlannedStart = SchedulingWorld.Future(12) }));
        await SchedulingAssert.BusinessRule(ErrorCodes.NotOwner,
            () => SetParticipation(w, ParticipationOn(created, w.Client, physio).Id, BookingStatus.Cancelled, fullScope: false, userId: spa.A.UserId));
        await SchedulingAssert.BusinessRule(ErrorCodes.NotOwner,
            () => w.Appointments.RemoveParticipation(w.OrganizationId, spa.A.UserId, false, ParticipationOn(created, w.Client, physio).Id));

        AppointmentDto moved = await w.Appointments.ChangeSegmentTime(w.OrganizationId, spa.A.UserId, false, massage,
            new AppointmentSegmentTimeChangeRequest { PlannedStart = SchedulingWorld.Future(8, 30) });
        Assert.Equal(SchedulingWorld.Future(8, 30), SegmentOf(moved, spa.Massage.Id.Value).PlannedStart);
        BookingDto own = await SetParticipation(w, ParticipationOn(created, w.Client, massage).Id, BookingStatus.Cancelled, fullScope: false, userId: spa.A.UserId);
        Assert.Equal(BookingStatusSummary.Mixed, own.Status);
    }

    [Fact]
    public async Task OwnScope_BookingWideCancel_IsAllOrNothing_OverTheAffectedSegments()
    {
        await using SchedulingWorld w = await SchedulingWorld.Create(nameof(OwnScope_BookingWideCancel_IsAllOrNothing_OverTheAffectedSegments));
        Spa spa = await SetUp(w);
        AppointmentDto created = await CreateMassageThenPhysio(w, spa);

        await SchedulingAssert.BusinessRule(ErrorCodes.NotOwner, () => w.Bookings.CancelBooking(
            w.OrganizationId, spa.A.UserId, false, created.Id, w.Client.Id.Value, SchedulingWorld.ClientCancel()));

        Assert.All(await w.LoadParticipations(created.Id, w.Client), p => Assert.Equal(ParticipationStatus.Confirmed, p.Status));
    }

    [Fact]
    public async Task WholeAppointmentOperations_RequireWriteAll_EvenWhenTheCallerIsAssignedToEverySegment()
    {
        await using SchedulingWorld w = await SchedulingWorld.Create(nameof(WholeAppointmentOperations_RequireWriteAll_EvenWhenTheCallerIsAssignedToEverySegment));
        Spa spa = await SetUp(w);
        await w.AssignEmployeeToService(spa.A, spa.Physio);
        AppointmentDto created = await Create(w,
            Seg(spa.Massage, SchedulingWorld.Future(9), spa.A, w.Client),
            Seg(spa.Physio, SchedulingWorld.Future(10), spa.A, w.Client));

        await SchedulingAssert.BusinessRule(ErrorCodes.NotOwner,
            () => w.Appointments.Cancel(w.OrganizationId, spa.A.UserId, false, created.Id, SchedulingWorld.BusinessCancel()));
        await SchedulingAssert.BusinessRule(ErrorCodes.NotOwner,
            () => w.Appointments.MarkNoShow(w.OrganizationId, spa.A.UserId, false, created.Id, new NoShowRequest()));
        Assert.Equal(AppointmentStatus.Scheduled, (await w.LoadAppointment(created.Id)).Status);

        // The same caller owns every affected segment, so the Booking-wide cancel is allowed.
        BookingDto booking = await w.Bookings.CancelBooking(w.OrganizationId, spa.A.UserId, false, created.Id, w.Client.Id.Value, SchedulingWorld.ClientCancel());
        Assert.Equal(BookingStatusSummary.Cancelled, booking.Status);
    }

    [Fact]
    public async Task OwnScope_AddSegmentAssignedToSomeoneElse_IsRefused()
    {
        await using SchedulingWorld w = await SchedulingWorld.Create(nameof(OwnScope_AddSegmentAssignedToSomeoneElse_IsRefused));
        Spa spa = await SetUp(w);
        AppointmentDto created = await Create(w, Seg(spa.Massage, SchedulingWorld.Future(9), spa.A, w.Client));

        await SchedulingAssert.BusinessRule(ErrorCodes.NotOwner, () => w.Appointments.AddSegment(w.OrganizationId, spa.A.UserId, false, created.Id,
            new AppointmentSegmentAddRequest
            {
                ServiceId = spa.Physio.Id.Value,
                PlannedStart = SchedulingWorld.Future(10),
                EmployeeIds = new List<Guid> { spa.B.Id.Value }
            }));
        Assert.Single((await w.LoadAppointment(created.Id)).Segments);
    }

    #endregion

    #region Target-only addressing (M1H)

    [Fact]
    public async Task GroupOnlyBookingPaths_OnAnIndividualAppointment_AreRejected_AndChangeNothing()
    {
        await using SchedulingWorld w = await SchedulingWorld.Create(nameof(GroupOnlyBookingPaths_OnAnIndividualAppointment_AreRejected_AndChangeNothing));
        Spa spa = await SetUp(w);
        Client partner = await w.AddClient("Partner");
        AppointmentDto created = await CreateMassageThenPhysio(w, spa);
        Guid firstSegment = created.Segments.OrderBy(s => s.PlannedStart).First().Id;

        // M1H: the flat single-segment commands are gone; the (appointment, client, segment) paths are the GROUP guest and
        // attendance paths — an individual appointment adds clients via AddClient and transitions participations by id.
        await SchedulingAssert.Validation(() => w.Bookings.AddGroupGuest(w.OrganizationId, w.ActorUserId, true, created.Id,
            new BookingCreateRequest { ClientId = partner.Id.Value, SegmentId = firstSegment }));
        await SchedulingAssert.Validation(() => w.Bookings.SetStatusOnSegment(w.OrganizationId, w.ActorUserId, true, created.Id, w.Client.Id.Value,
            new BookingSetStatusRequest { Status = BookingStatus.Cancelled, CancellationInitiator = CancellationInitiator.Client, SegmentId = firstSegment }));

        AppointmentDto after = await Reload(w, created.Id);
        Assert.Equal(new[] { SchedulingWorld.Future(9), SchedulingWorld.Future(10) }, after.Segments.Select(s => s.PlannedStart).OrderBy(x => x));
        Assert.Single(after.Bookings);
        Assert.All(await w.LoadParticipations(created.Id, w.Client), p => Assert.Equal(ParticipationStatus.Confirmed, p.Status));
    }

    #endregion

    #region Groups (non-regression)

    [Fact]
    public async Task Groups_StaySingleSegment_SegmentAndClientCommandsAreGenericOnly()
    {
        await using SchedulingWorld w = await SchedulingWorld.Create(nameof(Groups_StaySingleSegment_SegmentAndClientCommandsAreGenericOnly));
        ServiceEntity groupService = await w.AddGroupService();
        GroupDto group = await w.CreateGroup(groupService, capacity: 5);
        Appointment occurrence = await w.GenerateSingleOccurrence(group);
        Guid occurrenceSegment = Assert.Single(occurrence.Segments).Id.Value;
        Client guest = await w.AddClient("Guest");

        await SchedulingAssert.Validation(() => w.Appointments.AddSegment(w.OrganizationId, w.ActorUserId, true, occurrence.Id.Value,
            new AppointmentSegmentAddRequest
            {
                ServiceId = groupService.Id.Value,
                PlannedStart = SchedulingWorld.Future(12),
                EmployeeIds = new List<Guid> { w.Employee.Id.Value }
            }));
        await SchedulingAssert.Validation(() => w.Appointments.AddClient(w.OrganizationId, w.ActorUserId, true, occurrence.Id.Value,
            new AppointmentClientAddRequest
            {
                ClientId = guest.Id.Value,
                Participations = new List<AppointmentClientParticipationRequest> { new() { SegmentId = occurrenceSegment } }
            }));
        await SchedulingAssert.Validation(() => w.Appointments.RemoveSegment(w.OrganizationId, w.ActorUserId, true, occurrenceSegment));

        // Group-only flows keep working: guest booking, waitlist, members.
        await w.AddGuest(occurrence, guest);
        Assert.Single((await w.LoadAppointment(occurrence.Id.Value)).Segments);
    }

    [Fact]
    public async Task Groups_SegmentTimeChange_KeepsTheOccurrenceUsableForMembers()
    {
        await using SchedulingWorld w = await SchedulingWorld.Create(nameof(Groups_SegmentTimeChange_KeepsTheOccurrenceUsableForMembers));
        ServiceEntity groupService = await w.AddGroupService();
        GroupDto group = await w.CreateGroup(groupService, capacity: 5);
        Appointment occurrence = await w.GenerateSingleOccurrence(group);
        Guid segment = Assert.Single(occurrence.Segments).Id.Value;

        AppointmentDto moved = await w.Appointments.ChangeSegmentTime(w.OrganizationId, w.ActorUserId, true, segment,
            new AppointmentSegmentTimeChangeRequest { PlannedStart = SchedulingWorld.Future(11) });
        Assert.Equal(SchedulingWorld.Future(11), Assert.Single(moved.Segments).PlannedStart);

        Client member = await w.AddClient("Member");
        await w.AddGroupMember(group, member);
        Assert.Contains((await w.LoadAppointment(occurrence.Id.Value)).Bookings, b => b.ClientId == member.Id);
    }

    [Fact]
    public async Task Waitlist_OnAMultiSegmentIndividualAppointment_IsNotAvailable()
    {
        await using SchedulingWorld w = await SchedulingWorld.Create(nameof(Waitlist_OnAMultiSegmentIndividualAppointment_IsNotAvailable));
        Spa spa = await SetUp(w);
        Client waiter = await w.AddClient("Waiter");
        AppointmentDto created = await CreateMassageThenPhysio(w, spa);

        await SchedulingAssert.BusinessRule(ErrorCodes.WaitlistNotAvailable,
            () => w.Waitlist.Join(w.OrganizationId, w.ActorUserId, true, created.Id, new WaitlistJoinRequest { ClientId = waiter.Id.Value }));
    }

    #endregion

    #region Concurrency: the optimistic segment check

    private static async Task<Exception> InOwnScope(Func<IAppointmentService, Task> action)
    {
        using IServiceScope scope = SchedulingTestHost.CreateScope();
        try
        {
            await action(scope.ServiceProvider.GetRequiredService<IAppointmentService>());
            return null;
        }
        catch (Exception ex)
        {
            return ex;
        }
    }

    /// <summary>Overlapping active participations of one client (the hard invariant), straight from the database.</summary>
    private static async Task<int> ClientOverlaps(SchedulingWorld w, Client client)
    {
        await using DatabaseContext db = w.NewDb();
        var slots = await db.BookingSegmentParticipations.AsNoTracking()
            .Where(p => p.Booking.ClientId == client.Id && (p.Status == ParticipationStatus.Confirmed || p.Status == ParticipationStatus.Completed))
            .Select(p => new { p.Segment.PlannedStart, p.Segment.PlannedEnd })
            .ToListAsync();
        int overlaps = 0;
        for (int i = 0; i < slots.Count; i++)
            for (int j = i + 1; j < slots.Count; j++)
                if (slots[i].PlannedStart < slots[j].PlannedEnd && slots[j].PlannedStart < slots[i].PlannedEnd)
                    overlaps++;
        return overlaps;
    }

    [Fact]
    public async Task Race_SegmentMoveAndClientAdd_ValidatedOnTheSameOldState_NeverBothCommit()
    {
        await using SchedulingWorld w = await SchedulingWorld.Create(nameof(Race_SegmentMoveAndClientAdd_ValidatedOnTheSameOldState_NeverBothCommit));
        Spa spa = await SetUp(w);
        Client x = await w.AddClient("X");
        Employee other = await w.AddEmployee("Other");
        // X is busy at 12:00 elsewhere. Moving physio to 12:00 is valid without X; adding X to physio at 10:00 is valid too —
        // both together would double-book X.
        await w.CreateAppointment(SchedulingWorld.Future(12), client: x, employee: other);
        AppointmentDto created = await CreateMassageThenPhysio(w, spa);
        Guid physio = SegmentOf(created, spa.Physio.Id.Value).Id;

        // Gate: hold the Appointment row. Both writers pass every pre-lock check and take their (disjoint) subject locks, then
        // queue on the Appointment row; released together, the second must detect the changed segment.
        Task<Exception[]> race;
        IUnitOfWorkFactory factory = w.Resolve<IUnitOfWorkFactory>();
        await using (IUnitOfWork gate = await factory.Begin())
        {
            int gatePid = await gate.Context.Database.SqlQuery<int>($"SELECT pg_backend_pid() AS \"Value\"").SingleAsync();
            await gate.Context.Database.ExecuteSqlInterpolatedAsync($"SELECT 1 FROM dunelight.appointments WHERE id = {created.Id} FOR UPDATE");

            race = Task.WhenAll(
                Task.Run(() => InOwnScope(s => s.ChangeSegmentTime(w.OrganizationId, w.ActorUserId, true, physio,
                    new AppointmentSegmentTimeChangeRequest { PlannedStart = SchedulingWorld.Future(12) }))),
                Task.Run(() => InOwnScope(s => s.AddClient(w.OrganizationId, w.ActorUserId, true, created.Id, new AppointmentClientAddRequest
                {
                    ClientId = x.Id.Value,
                    Participations = new List<AppointmentClientParticipationRequest> { new() { SegmentId = physio } }
                }))));

            await WaitUntil(async () => await BlockedBehind(w, gatePid) == 2);
            await gate.CommitAsync();
        }

        Exception[] outcomes = await race;
        Assert.Single(outcomes, o => o == null);
        Assert.Equal(ErrorCodes.ConcurrencyConflict, Assert.IsType<BusinessRuleException>(Assert.Single(outcomes, o => o != null)).Code);
        Assert.Equal(0, await ClientOverlaps(w, x));
    }

    [Fact]
    public async Task Race_UngatedSegmentMovesAndClientAdds_NeverViolateTheClientInvariant()
    {
        await using SchedulingWorld w = await SchedulingWorld.Create(nameof(Race_UngatedSegmentMovesAndClientAdds_NeverViolateTheClientInvariant));
        Spa spa = await SetUp(w);
        Employee other = await w.AddEmployee("Other");
        const int rounds = 8;

        for (int round = 0; round < rounds; round++)
        {
            int days = 7 * round;
            Client x = await w.AddClient($"X{round}");
            await w.CreateAppointment(SchedulingWorld.Future(12).AddDays(days), client: x, employee: other);
            AppointmentDto created = await Create(w,
                Seg(spa.Massage, SchedulingWorld.Future(9).AddDays(days), spa.A, w.Client),
                Seg(spa.Physio, SchedulingWorld.Future(10).AddDays(days), spa.B, w.Client));
            Guid physio = SegmentOf(created, spa.Physio.Id.Value).Id;

            Exception[] outcomes = await Task.WhenAll(
                Task.Run(() => InOwnScope(s => s.ChangeSegmentTime(w.OrganizationId, w.ActorUserId, true, physio,
                    new AppointmentSegmentTimeChangeRequest { PlannedStart = SchedulingWorld.Future(12).AddDays(days) }))),
                Task.Run(() => InOwnScope(s => s.AddClient(w.OrganizationId, w.ActorUserId, true, created.Id, new AppointmentClientAddRequest
                {
                    ClientId = x.Id.Value,
                    Participations = new List<AppointmentClientParticipationRequest> { new() { SegmentId = physio } }
                }))));

            Assert.True(outcomes.Count(o => o == null) <= 1, "Both the move and the add committed.");
            Assert.All(outcomes.Where(o => o != null), o => Assert.Contains(Assert.IsType<BusinessRuleException>(o).Code,
                new[] { ErrorCodes.ConcurrencyConflict, ErrorCodes.AppointmentOverlap }));
            Assert.Equal(0, await ClientOverlaps(w, x));
        }
    }

    [Fact]
    public async Task Race_TwoClientAddsOnTheSameSegment_RespectRoomCapacity()
    {
        await using SchedulingWorld w = await SchedulingWorld.Create(nameof(Race_TwoClientAddsOnTheSameSegment_RespectRoomCapacity));
        Spa spa = await SetUp(w);
        Room room = await w.AddRoom(capacity: 3); // employee B + the client + ONE more person
        AppointmentDto created = await CreateMassageThenPhysio(w, spa);
        Guid physio = SegmentOf(created, spa.Physio.Id.Value).Id;
        await w.Appointments.ChangeSegmentRoom(w.OrganizationId, w.ActorUserId, true, physio, new AppointmentSegmentRoomChangeRequest { RoomId = room.Id });
        Client first = await w.AddClient("First");
        Client second = await w.AddClient("Second");

        Exception[] outcomes = await Task.WhenAll(new[] { first, second }.Select(c => Task.Run(() => InOwnScope(s => s.AddClient(
            w.OrganizationId, w.ActorUserId, true, created.Id, new AppointmentClientAddRequest
            {
                ClientId = c.Id.Value,
                Participations = new List<AppointmentClientParticipationRequest> { new() { SegmentId = physio } }
            })))));

        Assert.Single(outcomes, o => o == null);
        Assert.Contains(Assert.IsType<BusinessRuleException>(Assert.Single(outcomes, o => o != null)).Code,
            new[] { ErrorCodes.RoomCapacityExceeded, ErrorCodes.ConcurrencyConflict });
        Assert.Equal(2, (await Reload(w, created.Id)).Bookings.Count);
    }

    /// <summary>Sessions waiting (directly or behind another waiter) on the gate session.</summary>
    private static async Task<int> BlockedBehind(SchedulingWorld w, int gatePid)
    {
        await using DatabaseContext db = w.NewDb();
        return await db.Database.SqlQuery<int>($@"
            WITH RECURSIVE blocked(pid) AS (
                SELECT a.pid FROM pg_stat_activity a WHERE {gatePid} = ANY(pg_blocking_pids(a.pid))
                UNION
                SELECT a.pid FROM pg_stat_activity a JOIN blocked b ON b.pid = ANY(pg_blocking_pids(a.pid)))
            SELECT count(*)::int AS ""Value"" FROM blocked").SingleAsync();
    }

    private static async Task WaitUntil(Func<Task<bool>> condition)
    {
        Stopwatch sw = Stopwatch.StartNew();
        while (!await condition())
        {
            if (sw.Elapsed > TimeSpan.FromSeconds(30))
                throw new TimeoutException("Condition not reached.");
            await Task.Delay(20);
        }
    }

    #endregion
}
