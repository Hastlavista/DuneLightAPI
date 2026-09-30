#nullable disable
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using BlueDragon.DuneLight.Core.DTOs.Appointments;
using BlueDragon.DuneLight.Core.DTOs.Catalog;
using BlueDragon.DuneLight.Core.DTOs.Groups;
using BlueDragon.DuneLight.Core.Enums;
using BlueDragon.DuneLight.Core.Interfaces.Catalog;
using BlueDragon.DuneLight.Core.Interfaces.Employees;
using BlueDragon.DuneLight.Core.Shared;
using BlueDragon.DuneLight.Infrastructure.Domain.Contexts;
using BlueDragon.DuneLight.Infrastructure.Domain.Models.Appointments;
using BlueDragon.DuneLight.Infrastructure.Domain.Models.Catalog;
using BlueDragon.DuneLight.Infrastructure.Domain.Models.Employees;
using BlueDragon.DuneLight.Infrastructure.Handlers.Interfaces;
using Microsoft.EntityFrameworkCore;
using ServiceEntity = BlueDragon.DuneLight.Infrastructure.Domain.Models.Catalog.Service;

namespace BlueDragon.DuneLight.UnitTests.Scheduling;

/// <summary>
/// Phase D1 — AppointmentSegment persistence foundation: segments (Service, planned/actual UTC time, optional Room) with
/// Employee and Resource assignments, stored ALONGSIDE the legacy Appointment fields. Nothing in production creates or
/// reads segments yet; these tests exercise the persistence model through the narrow IAppointmentSegmentHandler and the
/// database constraints directly.
/// </summary>
public class AppointmentSegmentPersistenceTests
{
    private static DateTimeOffset Z(int h, int mi = 0) => SchedulingWorld.Future(h, mi);

    private static IAppointmentSegmentHandler Segments(SchedulingWorld w) => w.Resolve<IAppointmentSegmentHandler>();

    private static AppointmentSegment NewSegment(SchedulingWorld w, Guid appointmentId, DateTimeOffset start, DateTimeOffset end,
        Guid? serviceId = null, Guid? roomId = null) => new()
    {
        Id = Guid.NewGuid(),
        OrganizationId = w.OrganizationId,
        AppointmentId = appointmentId,
        ServiceId = serviceId ?? w.Service.Id.Value,
        PlannedStart = start,
        PlannedEnd = end,
        RoomId = roomId,
        CreatedAt = DateTimeOffset.UtcNow
    };

    private static async Task<Guid> AnAppointment(SchedulingWorld w, int hour = 10) => (await w.CreateAppointment(Z(hour))).Id;

    private static async Task<Resource> AddResource(SchedulingWorld w, string name, int capacity = 5)
    {
        ResourceDto dto = await w.Resolve<IResourceService>().Create(w.OrganizationId, w.ActorUserId,
            new ResourceCreateRequest { CompanyId = w.Company.Id.Value, Name = name, Capacity = capacity });
        await using DatabaseContext db = w.NewDb();
        return await db.Resources.AsNoTracking().SingleAsync(r => r.Id == dto.Id);
    }

    /// <summary>Saves an entity in a fresh context and returns the database error (constraint name is in the message).</summary>
    private static async Task<string> SaveFails(SchedulingWorld w, Action<DatabaseContext> add)
    {
        await using DatabaseContext db = w.NewDb();
        add(db);
        DbUpdateException ex = await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync());
        return ex.InnerException?.Message;
    }

    #region AppointmentSegment

    [Fact]
    public async Task Segment_WithRoomEmployeeAndResource_IsPersistedAndRead()
    {
        await using SchedulingWorld w = await SchedulingWorld.Create(nameof(Segment_WithRoomEmployeeAndResource_IsPersistedAndRead));
        Guid appointmentId = await AnAppointment(w);
        Room room = await w.AddRoom(capacity: 4);
        Resource table = await AddResource(w, "Table");

        AppointmentSegment segment = NewSegment(w, appointmentId, Z(10), Z(10, 30), roomId: room.Id);
        segment.Employees.Add(new AppointmentSegmentEmployee { EmployeeId = w.Employee.Id.Value });
        segment.Resources.Add(new AppointmentSegmentResource { ResourceId = table.Id.Value, QuantityRequired = 2 });
        await Segments(w).Add(segment);

        AppointmentSegment read = await Segments(w).GetById(w.OrganizationId, segment.Id.Value);

        Assert.Equal(appointmentId, read.AppointmentId);
        Assert.Equal(w.Service.Id, read.Service.Id);
        Assert.Equal(room.Id, read.Room.Id);
        Assert.Equal(Z(10), read.PlannedStart);
        Assert.Equal(Z(10, 30), read.PlannedEnd);
        Assert.Null(read.ActualStart);
        Assert.Null(read.ActualEnd);
        Assert.Equal(w.Employee.Id.Value, Assert.Single(read.Employees).EmployeeId);
        AppointmentSegmentResource resource = Assert.Single(read.Resources);
        Assert.Equal(table.Id.Value, resource.ResourceId);
        Assert.Equal(2, resource.QuantityRequired);
    }

    [Fact]
    public async Task Times_RoundTripAsUtcInstants()
    {
        await using SchedulingWorld w = await SchedulingWorld.Create(nameof(Times_RoundTripAsUtcInstants), "Europe/Zagreb");
        Guid appointmentId = await AnAppointment(w);
        DateTimeOffset plannedStart = new(2031, 3, 3, 11, 0, 0, TimeSpan.FromHours(2)); // same instant as 09:00Z
        AppointmentSegment segment = NewSegment(w, appointmentId, plannedStart, plannedStart.AddMinutes(45));
        segment.ActualStart = new DateTimeOffset(2031, 3, 3, 18, 5, 0, TimeSpan.FromHours(9)); // 09:05Z
        segment.ActualEnd = new DateTimeOffset(2031, 3, 3, 4, 50, 0, TimeSpan.FromHours(-5)); // 09:50Z
        await Segments(w).Add(segment);

        AppointmentSegment read = await Segments(w).GetById(w.OrganizationId, segment.Id.Value);

        Assert.Equal(new DateTimeOffset(2031, 3, 3, 9, 0, 0, TimeSpan.Zero), read.PlannedStart);
        Assert.Equal(new DateTimeOffset(2031, 3, 3, 9, 45, 0, TimeSpan.Zero), read.PlannedEnd);
        Assert.Equal(new DateTimeOffset(2031, 3, 3, 9, 5, 0, TimeSpan.Zero), read.ActualStart);
        Assert.Equal(new DateTimeOffset(2031, 3, 3, 9, 50, 0, TimeSpan.Zero), read.ActualEnd);
        Assert.All(new[] { read.PlannedStart, read.PlannedEnd, read.ActualStart.Value, read.ActualEnd.Value },
            t => Assert.Equal(TimeSpan.Zero, t.Offset));
    }

    [Fact]
    public async Task Segment_RequiresAnExistingAppointment()
    {
        await using SchedulingWorld w = await SchedulingWorld.Create(nameof(Segment_RequiresAnExistingAppointment));

        string error = await SaveFails(w, db => db.AppointmentSegments.Add(NewSegment(w, Guid.NewGuid(), Z(10), Z(11))));

        Assert.Contains("fk_appointment_segments_appointment_id", error);
    }

    [Fact]
    public async Task Segment_RequiresAnExistingService()
    {
        await using SchedulingWorld w = await SchedulingWorld.Create(nameof(Segment_RequiresAnExistingService));
        Guid appointmentId = await AnAppointment(w);

        string error = await SaveFails(w, db => db.AppointmentSegments.Add(NewSegment(w, appointmentId, Z(10), Z(11), serviceId: Guid.NewGuid())));

        Assert.Contains("fk_appointment_segments_service_id", error);
    }

    [Fact]
    public async Task Room_IsOptional_ButMustExistWhenSet()
    {
        await using SchedulingWorld w = await SchedulingWorld.Create(nameof(Room_IsOptional_ButMustExistWhenSet));
        Guid appointmentId = await AnAppointment(w);
        AppointmentSegment withoutRoom = NewSegment(w, appointmentId, Z(10), Z(11));
        await Segments(w).Add(withoutRoom);

        AppointmentSegment read = await Segments(w).GetById(w.OrganizationId, withoutRoom.Id.Value);
        Assert.Null(read.RoomId);
        Assert.Null(read.Room);

        string error = await SaveFails(w, db => db.AppointmentSegments.Add(NewSegment(w, appointmentId, Z(10), Z(11), roomId: Guid.NewGuid())));
        Assert.Contains("fk_appointment_segments_room_id", error);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-15)]
    public async Task PlannedEnd_MustBeAfterPlannedStart(int minutes)
    {
        await using SchedulingWorld w = await SchedulingWorld.Create($"{nameof(PlannedEnd_MustBeAfterPlannedStart)}{minutes}");
        Guid appointmentId = await AnAppointment(w);

        string error = await SaveFails(w, db => db.AppointmentSegments.Add(NewSegment(w, appointmentId, Z(10), Z(10).AddMinutes(minutes))));

        Assert.Contains("ck_appointment_segments_planned_range", error);
    }

    [Fact]
    public async Task ActualEnd_WithoutActualStart_IsRejected()
    {
        await using SchedulingWorld w = await SchedulingWorld.Create(nameof(ActualEnd_WithoutActualStart_IsRejected));
        Guid appointmentId = await AnAppointment(w);
        AppointmentSegment segment = NewSegment(w, appointmentId, Z(10), Z(11));
        segment.ActualEnd = Z(11);

        string error = await SaveFails(w, db => db.AppointmentSegments.Add(segment));

        Assert.Contains("ck_appointment_segments_actual_range", error);
    }

    [Fact]
    public async Task ActualEnd_BeforeActualStart_IsRejected()
    {
        await using SchedulingWorld w = await SchedulingWorld.Create(nameof(ActualEnd_BeforeActualStart_IsRejected));
        Guid appointmentId = await AnAppointment(w);
        AppointmentSegment segment = NewSegment(w, appointmentId, Z(10), Z(11));
        segment.ActualStart = Z(10, 30);
        segment.ActualEnd = Z(10, 29);

        string error = await SaveFails(w, db => db.AppointmentSegments.Add(segment));

        Assert.Contains("ck_appointment_segments_actual_range", error);
    }

    [Fact]
    public async Task ActualTimes_OnlyStart_EqualAndOrderedPairs_AreAccepted()
    {
        await using SchedulingWorld w = await SchedulingWorld.Create(nameof(ActualTimes_OnlyStart_EqualAndOrderedPairs_AreAccepted));
        Guid appointmentId = await AnAppointment(w);
        AppointmentSegment onlyStart = NewSegment(w, appointmentId, Z(10), Z(11));
        onlyStart.ActualStart = Z(10, 2);
        AppointmentSegment zeroLength = NewSegment(w, appointmentId, Z(11), Z(12));
        zeroLength.ActualStart = Z(11, 5);
        zeroLength.ActualEnd = Z(11, 5);
        AppointmentSegment ordered = NewSegment(w, appointmentId, Z(12), Z(13));
        ordered.ActualStart = Z(12);
        ordered.ActualEnd = Z(13, 10); // may overrun the plan

        foreach (AppointmentSegment segment in new[] { onlyStart, zeroLength, ordered })
            await Segments(w).Add(segment);

        List<AppointmentSegment> read = await Segments(w).GetForAppointment(w.OrganizationId, appointmentId);
        Assert.Equal(new[] { onlyStart.Id, zeroLength.Id, ordered.Id }, read.Select(s => s.Id).ToArray());
        Assert.Null(read[0].ActualEnd);
        Assert.Equal(Z(13, 10), read[2].ActualEnd);
    }

    [Fact]
    public async Task Segments_OfOneAppointment_CanUseDifferentServices_AndAreOrderedByPlannedStart()
    {
        await using SchedulingWorld w = await SchedulingWorld.Create(nameof(Segments_OfOneAppointment_CanUseDifferentServices_AndAreOrderedByPlannedStart));
        Guid appointmentId = await AnAppointment(w);
        ServiceEntity second = await w.AddService(45, 30m, name: "Second");
        await Segments(w).Add(NewSegment(w, appointmentId, Z(10, 30), Z(11, 15), serviceId: second.Id));
        await Segments(w).Add(NewSegment(w, appointmentId, Z(10), Z(10, 30)));

        List<AppointmentSegment> read = await Segments(w).GetForAppointment(w.OrganizationId, appointmentId);

        Assert.Equal(new[] { w.Service.Id.Value, second.Id.Value }, read.Select(s => s.ServiceId).ToArray());
        Assert.Equal(new[] { w.Service.Name, second.Name }, read.Select(s => s.Service.Name).ToArray());
    }

    [Fact]
    public async Task Reads_AreScopedToTheOrganization()
    {
        await using SchedulingWorld w = await SchedulingWorld.Create(nameof(Reads_AreScopedToTheOrganization));
        await using SchedulingWorld other = await SchedulingWorld.Create($"{nameof(Reads_AreScopedToTheOrganization)}-other");
        Guid appointmentId = await AnAppointment(w);
        AppointmentSegment segment = NewSegment(w, appointmentId, Z(10), Z(11));
        await Segments(w).Add(segment);

        Assert.Null(await Segments(w).GetById(other.OrganizationId, segment.Id.Value));
        Assert.Empty(await Segments(w).GetForAppointment(other.OrganizationId, appointmentId));
        Assert.NotNull(await Segments(w).GetById(w.OrganizationId, segment.Id.Value));
    }

    #endregion

    #region Employees

    [Fact]
    public async Task Employees_ZeroOneOrMany_ArePersisted()
    {
        await using SchedulingWorld w = await SchedulingWorld.Create(nameof(Employees_ZeroOneOrMany_ArePersisted));
        Guid appointmentId = await AnAppointment(w);
        Employee second = await w.AddEmployee("Second");
        Employee third = await w.AddEmployee("Third");

        AppointmentSegment none = NewSegment(w, appointmentId, Z(10), Z(11));
        AppointmentSegment one = NewSegment(w, appointmentId, Z(11), Z(12));
        one.Employees.Add(new AppointmentSegmentEmployee { EmployeeId = w.Employee.Id.Value });
        AppointmentSegment many = NewSegment(w, appointmentId, Z(12), Z(13));
        foreach (Employee e in new[] { w.Employee, second, third })
            many.Employees.Add(new AppointmentSegmentEmployee { EmployeeId = e.Id.Value });

        foreach (AppointmentSegment s in new[] { none, one, many })
            await Segments(w).Add(s);

        Assert.Empty((await Segments(w).GetById(w.OrganizationId, none.Id.Value)).Employees);
        Assert.Single((await Segments(w).GetById(w.OrganizationId, one.Id.Value)).Employees);
        Assert.Equal(new[] { w.Employee.Id.Value, second.Id.Value, third.Id.Value }.OrderBy(i => i),
            (await Segments(w).GetById(w.OrganizationId, many.Id.Value)).Employees.Select(e => e.EmployeeId).OrderBy(i => i));
    }

    [Fact]
    public async Task SameEmployee_TwiceOnOneSegment_IsRejected_ButAllowedOnDifferentSegments()
    {
        await using SchedulingWorld w = await SchedulingWorld.Create(nameof(SameEmployee_TwiceOnOneSegment_IsRejected_ButAllowedOnDifferentSegments));
        Guid appointmentId = await AnAppointment(w);
        AppointmentSegment first = NewSegment(w, appointmentId, Z(10), Z(11));
        first.Employees.Add(new AppointmentSegmentEmployee { EmployeeId = w.Employee.Id.Value });
        AppointmentSegment second = NewSegment(w, appointmentId, Z(11), Z(12));
        second.Employees.Add(new AppointmentSegmentEmployee { EmployeeId = w.Employee.Id.Value });
        await Segments(w).Add(first);
        await Segments(w).Add(second);

        string error = await SaveFails(w, db => db.AppointmentSegmentEmployees.Add(
            new AppointmentSegmentEmployee { AppointmentSegmentId = first.Id.Value, EmployeeId = w.Employee.Id.Value }));

        Assert.Contains("pk_appointment_segment_employees", error);
        await using DatabaseContext verify = w.NewDb();
        Assert.Equal(2, await verify.AppointmentSegmentEmployees.CountAsync(e => e.EmployeeId == w.Employee.Id.Value));
    }

    [Fact]
    public async Task EmployeeAssignment_RequiresAnExistingEmployee()
    {
        await using SchedulingWorld w = await SchedulingWorld.Create(nameof(EmployeeAssignment_RequiresAnExistingEmployee));
        Guid appointmentId = await AnAppointment(w);
        AppointmentSegment segment = NewSegment(w, appointmentId, Z(10), Z(11));
        await Segments(w).Add(segment);

        string error = await SaveFails(w, db => db.AppointmentSegmentEmployees.Add(
            new AppointmentSegmentEmployee { AppointmentSegmentId = segment.Id.Value, EmployeeId = Guid.NewGuid() }));

        Assert.Contains("fk_appointment_segment_employees_employee_id", error);
    }

    #endregion

    #region Resources

    [Fact]
    public async Task Resources_OneOrManyWithQuantities_ArePersisted()
    {
        await using SchedulingWorld w = await SchedulingWorld.Create(nameof(Resources_OneOrManyWithQuantities_ArePersisted));
        Guid appointmentId = await AnAppointment(w);
        Resource bike = await AddResource(w, "Bike", 10);
        Resource mat = await AddResource(w, "Mat", 20);

        AppointmentSegment one = NewSegment(w, appointmentId, Z(10), Z(11));
        one.Resources.Add(new AppointmentSegmentResource { ResourceId = bike.Id.Value, QuantityRequired = 1 });
        AppointmentSegment many = NewSegment(w, appointmentId, Z(11), Z(12));
        many.Resources.Add(new AppointmentSegmentResource { ResourceId = bike.Id.Value, QuantityRequired = 4 });
        many.Resources.Add(new AppointmentSegmentResource { ResourceId = mat.Id.Value, QuantityRequired = 25 }); // above capacity: not enforced yet
        await Segments(w).Add(one);
        await Segments(w).Add(many);

        Assert.Equal(1, Assert.Single((await Segments(w).GetById(w.OrganizationId, one.Id.Value)).Resources).QuantityRequired);
        Dictionary<Guid, int> quantities = (await Segments(w).GetById(w.OrganizationId, many.Id.Value)).Resources
            .ToDictionary(r => r.ResourceId, r => r.QuantityRequired);
        Assert.Equal(new Dictionary<Guid, int> { [bike.Id.Value] = 4, [mat.Id.Value] = 25 }, quantities);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public async Task QuantityRequired_MustBePositive(int quantity)
    {
        await using SchedulingWorld w = await SchedulingWorld.Create($"{nameof(QuantityRequired_MustBePositive)}{quantity}");
        Guid appointmentId = await AnAppointment(w);
        Resource bike = await AddResource(w, "Bike");
        AppointmentSegment segment = NewSegment(w, appointmentId, Z(10), Z(11));
        await Segments(w).Add(segment);

        string error = await SaveFails(w, db => db.AppointmentSegmentResources.Add(
            new AppointmentSegmentResource { AppointmentSegmentId = segment.Id.Value, ResourceId = bike.Id.Value, QuantityRequired = quantity }));

        Assert.Contains("ck_appointment_segment_resources_quantity_positive", error);
    }

    [Fact]
    public async Task SameResource_TwiceOnOneSegment_IsRejected_ButAllowedOnDifferentSegments()
    {
        await using SchedulingWorld w = await SchedulingWorld.Create(nameof(SameResource_TwiceOnOneSegment_IsRejected_ButAllowedOnDifferentSegments));
        Guid appointmentId = await AnAppointment(w);
        Resource bike = await AddResource(w, "Bike");
        AppointmentSegment first = NewSegment(w, appointmentId, Z(10), Z(11));
        first.Resources.Add(new AppointmentSegmentResource { ResourceId = bike.Id.Value, QuantityRequired = 1 });
        AppointmentSegment second = NewSegment(w, appointmentId, Z(10), Z(11)); // overlapping: capacity is not enforced yet
        second.Resources.Add(new AppointmentSegmentResource { ResourceId = bike.Id.Value, QuantityRequired = 5 });
        await Segments(w).Add(first);
        await Segments(w).Add(second);

        string error = await SaveFails(w, db => db.AppointmentSegmentResources.Add(
            new AppointmentSegmentResource { AppointmentSegmentId = first.Id.Value, ResourceId = bike.Id.Value, QuantityRequired = 2 }));

        Assert.Contains("pk_appointment_segment_resources", error);
    }

    [Fact]
    public async Task ResourceAssignment_RequiresAnExistingResource()
    {
        await using SchedulingWorld w = await SchedulingWorld.Create(nameof(ResourceAssignment_RequiresAnExistingResource));
        Guid appointmentId = await AnAppointment(w);
        AppointmentSegment segment = NewSegment(w, appointmentId, Z(10), Z(11));
        await Segments(w).Add(segment);

        string error = await SaveFails(w, db => db.AppointmentSegmentResources.Add(
            new AppointmentSegmentResource { AppointmentSegmentId = segment.Id.Value, ResourceId = Guid.NewGuid(), QuantityRequired = 1 }));

        Assert.Contains("fk_appointment_segment_resources_resource_id", error);
    }

    #endregion

    #region Relationships and referenced-entity deletes

    [Fact]
    public async Task Appointment_ExposesItsSegments_AndDeletingItCascadesToSegmentsAndAssignments()
    {
        await using SchedulingWorld w = await SchedulingWorld.Create(nameof(Appointment_ExposesItsSegments_AndDeletingItCascadesToSegmentsAndAssignments));
        Guid appointmentId = await AnAppointment(w);
        Resource bike = await AddResource(w, "Bike");
        AppointmentSegment segment = NewSegment(w, appointmentId, Z(10), Z(11));
        segment.Employees.Add(new AppointmentSegmentEmployee { EmployeeId = w.Employee.Id.Value });
        segment.Resources.Add(new AppointmentSegmentResource { ResourceId = bike.Id.Value, QuantityRequired = 1 });
        await Segments(w).Add(segment);

        await using (DatabaseContext db = w.NewDb())
        {
            Appointment appointment = await db.Appointments.Include(a => a.Segments).ThenInclude(s => s.Employees)
                .SingleAsync(a => a.Id == appointmentId);
            AppointmentSegment loaded = Assert.Single(appointment.Segments);
            Assert.Equal(segment.Id, loaded.Id);
            Assert.Same(appointment, loaded.Appointment);
        }

        // The existing same-day hard delete keeps working; the segment rows go with the appointment (like Bookings).
        await w.Appointments.Delete(w.OrganizationId, w.ActorUserId, appointmentId);

        await using DatabaseContext verify = w.NewDb();
        Assert.False(await verify.AppointmentSegments.AnyAsync(s => s.Id == segment.Id));
        Assert.False(await verify.AppointmentSegmentEmployees.AnyAsync(e => e.AppointmentSegmentId == segment.Id));
        Assert.False(await verify.AppointmentSegmentResources.AnyAsync(r => r.AppointmentSegmentId == segment.Id));
    }

    [Fact]
    public async Task Resource_UsedByASegment_CannotBeDeleted()
    {
        await using SchedulingWorld w = await SchedulingWorld.Create(nameof(Resource_UsedByASegment_CannotBeDeleted));
        IResourceService resources = w.Resolve<IResourceService>();
        Guid appointmentId = await AnAppointment(w);
        Resource used = await AddResource(w, "Used");
        Resource unused = await AddResource(w, "Unused");
        AppointmentSegment segment = NewSegment(w, appointmentId, Z(10), Z(11));
        segment.Resources.Add(new AppointmentSegmentResource { ResourceId = used.Id.Value, QuantityRequired = 1 });
        await Segments(w).Add(segment);

        await SchedulingAssert.BusinessRule(ErrorCodes.ReferencedCannotDelete, () => resources.Delete(w.OrganizationId, used.Id.Value));
        await resources.Delete(w.OrganizationId, unused.Id.Value);

        await SchedulingAssert.NotFound(() => resources.GetById(w.OrganizationId, unused.Id.Value));
        Assert.True((await resources.GetById(w.OrganizationId, used.Id.Value)).IsActive);
    }

    [Fact]
    public async Task RoomServiceAndEmployee_ReferencedOnlyByASegment_CannotBeDeleted()
    {
        await using SchedulingWorld w = await SchedulingWorld.Create(nameof(RoomServiceAndEmployee_ReferencedOnlyByASegment_CannotBeDeleted));
        Guid appointmentId = await AnAppointment(w);
        Room room = await w.AddRoom();
        ServiceEntity service = await w.AddService(30, 10m, availableAtCompany: false, name: "SegmentOnly");
        Employee employee = await w.AddEmployee("SegmentOnly", assignedToCompany: false, assignedToService: false, withWorkingHours: false);
        AppointmentSegment segment = NewSegment(w, appointmentId, Z(10), Z(11), serviceId: service.Id, roomId: room.Id);
        segment.Employees.Add(new AppointmentSegmentEmployee { EmployeeId = employee.Id.Value });
        await Segments(w).Add(segment);

        await SchedulingAssert.BusinessRule(ErrorCodes.ReferencedCannotDelete, () => w.Resolve<IRoomService>().Delete(w.OrganizationId, room.Id.Value));
        await SchedulingAssert.BusinessRule(ErrorCodes.ReferencedCannotDelete, () => w.Resolve<IServiceCatalogService>().Delete(w.OrganizationId, service.Id.Value));
        await SchedulingAssert.BusinessRule(ErrorCodes.ReferencedCannotDelete, () => w.Resolve<IEmployeeService>().Delete(w.OrganizationId, employee.Id.Value));

        // Control: the same kinds of unreferenced entities are still deletable.
        Room freeRoom = await w.AddRoom();
        ServiceEntity freeService = await w.AddService(30, 10m, availableAtCompany: false, name: "Free");
        Employee freeEmployee = await w.AddEmployee("Free", assignedToCompany: false, assignedToService: false, withWorkingHours: false);
        await w.Resolve<IRoomService>().Delete(w.OrganizationId, freeRoom.Id.Value);
        await w.Resolve<IServiceCatalogService>().Delete(w.OrganizationId, freeService.Id.Value);
        await w.Resolve<IEmployeeService>().Delete(w.OrganizationId, freeEmployee.Id.Value);
    }

    #endregion

    #region Not authoritative: production flows create no segments

    [Fact]
    public async Task ProductionSchedulingFlows_CreateNoSegments()
    {
        await using SchedulingWorld w = await SchedulingWorld.Create(nameof(ProductionSchedulingFlows_CreateNoSegments));
        Room room = await w.AddRoom();

        AppointmentDto single = await w.CreateAppointment(Z(9), room: room);
        await w.Appointments.CreateRecurring(w.OrganizationId, w.ActorUserId, true, new RecurringAppointmentCreateRequest
        {
            RecurrenceType = RecurrenceType.Weekly, ServiceId = w.Service.Id.Value, EmployeeId = w.Employee.Id.Value,
            CompanyId = w.Company.Id.Value, ClientIds = new List<Guid> { w.Client.Id.Value },
            FirstOccurrenceStartsAt = Z(12), EndDate = Z(12).AddDays(14)
        });
        ServiceEntity groupService = await w.AddGroupService();
        GroupDto group = await w.CreateGroup(groupService, capacity: 5, room: room);
        await w.GenerateOccurrences(group, SchedulingWorld.FutureDay);

        await using DatabaseContext db = w.NewDb();
        Assert.True(await db.Appointments.CountAsync(a => a.OrganizationId == w.OrganizationId) >= 5);
        Assert.False(await db.AppointmentSegments.AnyAsync(s => s.OrganizationId == w.OrganizationId));

        // The legacy singular fields are still what scheduling writes.
        Appointment legacy = await db.Appointments.SingleAsync(a => a.Id == single.Id);
        Assert.Equal(w.Service.Id.Value, legacy.ServiceId);
        Assert.Equal(w.Employee.Id, legacy.EmployeeId);
        Assert.Equal(room.Id, legacy.RoomId);
        Assert.Equal(Z(9), legacy.StartsAt);
        Assert.Equal(SchedulingWorld.DefaultServiceDuration, legacy.DurationMinutes);
    }

    #endregion
}
