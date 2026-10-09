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
using BlueDragon.DuneLight.Core.Shared;
using BlueDragon.DuneLight.Core.Shared.Exceptions;
using BlueDragon.DuneLight.Infrastructure.Domain.Models.Catalog;
using BlueDragon.DuneLight.UnitTests.Scheduling;
using ServiceEntity = BlueDragon.DuneLight.Infrastructure.Domain.Models.Catalog.Service;

namespace BlueDragon.DuneLight.UnitTests.K1;

/// <summary>
/// K1-6 (P-7) — usluga automatski zauzima svoje zadane resurse (npr. masaža → stol) kad zahtjev resurse ne navodi; poslani
/// resursi (i prazna lista) imaju prednost; promjena usluge zamjenjuje resurse zadanima nove usluge; predložak grupe ih kopira
/// pri kreiranju. Kapacitet resursa ostaje tvrda blokada.
/// </summary>
public class ServiceDefaultResourceTests
{
    private static IServiceAvailabilityService Catalog(SchedulingWorld w) => w.Resolve<IServiceAvailabilityService>();

    private static Task<List<ServiceDefaultResourceDto>> SetDefaults(SchedulingWorld w, ServiceEntity service, params (Resource Resource, int Quantity)[] resources) =>
        Catalog(w).ReplaceDefaultResources(w.OrganizationId, w.ActorUserId, service.Id.Value, new ReplaceServiceDefaultResourcesRequest
        {
            Resources = resources.Select(r => new ServiceDefaultResourceRequest { ResourceId = r.Resource.Id.Value, QuantityRequired = r.Quantity }).ToList()
        });

    private static AppointmentCreateRequest Create(SchedulingWorld w, DateTimeOffset start, List<AppointmentSegmentResourceRequest> resources = null)
    {
        AppointmentCreateRequest request = w.CreateRequest(start).ToTarget();
        request.Segments.Single().Resources = resources;
        return request;
    }

    [Fact]
    public async Task Replace_ValidatesQuantityAndActivity_AndQueryFiltersByCompany()
    {
        await using SchedulingWorld w = await SchedulingWorld.Create(nameof(Replace_ValidatesQuantityAndActivity_AndQueryFiltersByCompany));
        Resource table = await w.AddResource(capacity: 2, name: "Stol");
        Resource inactive = await w.AddResource(isActive: false);
        Resource elsewhere = await w.AddResource(await w.AddCompany("Druga"), capacity: 1);

        await Assert.ThrowsAsync<ValidationAppException>(() => SetDefaults(w, w.Service, (table, 3)));
        await SchedulingAssert.BusinessRule(ErrorCodes.InactiveResource, () => SetDefaults(w, w.Service, (inactive, 1)));

        List<ServiceDefaultResourceDto> all = await SetDefaults(w, w.Service, (table, 1), (elsewhere, 1));
        Assert.Equal(2, all.Count);

        ServiceDefaultResourceDto here = Assert.Single(await Catalog(w).GetDefaultResources(w.OrganizationId, w.Service.Id.Value, w.Company.Id.Value));
        Assert.Equal(table.Id, here.ResourceId);
    }

    [Fact]
    public async Task Create_WithoutResources_TakesTheDefaults_ExplicitListWins_AndCapacityStillBlocks()
    {
        await using SchedulingWorld w = await SchedulingWorld.Create(nameof(Create_WithoutResources_TakesTheDefaults_ExplicitListWins_AndCapacityStillBlocks));
        Resource table = await w.AddResource(capacity: 1, name: "Stol");
        await SetDefaults(w, w.Service, (table, 1));

        AppointmentDto first = await w.Appointments.Create(w.OrganizationId, w.ActorUserId, true, Create(w, SchedulingWorld.Future(10)));
        AppointmentSegmentResourceDto taken = Assert.Single(Assert.Single(first.Segments).Resources);
        Assert.Equal(table.Id, taken.ResourceId);

        // Soba/zaposlenik nisu problem (drugi zaposlenik i klijent), ali stol je zauzet.
        AppointmentCreateRequest second = Create(w, SchedulingWorld.Future(10));
        second.Segments.Single().EmployeeIds = new List<Guid> { (await w.AddEmployee("Drugi")).Id.Value };
        second.Segments.Single().Participants = new List<AppointmentParticipantCreateRequest> { new() { ClientId = (await w.AddClient("Drugi")).Id.Value } };
        await SchedulingAssert.BusinessRule(ErrorCodes.ResourceCapacityExceeded,
            () => w.Appointments.Create(w.OrganizationId, w.ActorUserId, true, second));

        // Izričito prazna lista: bez resursa, termin prolazi.
        second.Segments.Single().Resources = new List<AppointmentSegmentResourceRequest>();
        AppointmentDto withoutTable = await w.Appointments.Create(w.OrganizationId, w.ActorUserId, true, second);
        Assert.Empty(Assert.Single(withoutTable.Segments).Resources);
    }

    [Fact]
    public async Task ServiceChange_WithoutResources_ReplacesThemWithTheNewServiceDefaults()
    {
        await using SchedulingWorld w = await SchedulingWorld.Create(nameof(ServiceChange_WithoutResources_ReplacesThemWithTheNewServiceDefaults));
        Resource table = await w.AddResource(name: "Stol");
        Resource bike = await w.AddResource(name: "Bicikl");
        ServiceEntity other = await w.AddService(30, 40m, name: "Spinning");
        await w.AssignEmployeeToService(w.Employee, other);
        await SetDefaults(w, w.Service, (table, 1));
        await SetDefaults(w, other, (bike, 2));
        AppointmentDto created = await w.Appointments.Create(w.OrganizationId, w.ActorUserId, true, Create(w, SchedulingWorld.Future(10)));
        Guid segmentId = Assert.Single(created.Segments).Id;

        AppointmentDto changed = await w.Appointments.ChangeSegmentService(w.OrganizationId, w.ActorUserId, true, segmentId,
            new AppointmentSegmentServiceChangeRequest { ServiceId = other.Id.Value });

        AppointmentSegmentResourceDto resource = Assert.Single(Assert.Single(changed.Segments).Resources);
        Assert.Equal(bike.Id, resource.ResourceId);
        Assert.Equal(2, resource.QuantityRequired);
    }

    [Fact]
    public async Task Recurring_TakesTheDefaults()
    {
        await using SchedulingWorld w = await SchedulingWorld.Create(nameof(Recurring_TakesTheDefaults));
        Resource table = await w.AddResource(name: "Stol");
        await SetDefaults(w, w.Service, (table, 1));

        List<AppointmentDto> series = await w.Appointments.CreateRecurring(w.OrganizationId, w.ActorUserId, true, new RecurringAppointmentCreateRequest
        {
            CompanyId = w.Company.Id.Value,
            ServiceId = w.Service.Id.Value,
            EmployeeId = w.Employee.Id.Value,
            ClientIds = new List<Guid> { w.Client.Id.Value },
            FirstOccurrenceStartsAt = SchedulingWorld.Future(10),
            EndDate = SchedulingWorld.Future(10).AddDays(7),
            RecurrenceType = RecurrenceType.Weekly
        });

        Assert.Equal(2, series.Count);
        Assert.All(series, a => Assert.Equal(table.Id, Assert.Single(Assert.Single(a.Segments).Resources).ResourceId));
    }

    [Fact]
    public async Task Recurring_ResourceClash_ListsEveryClashingDateWithResourceCapacityAndUsage()
    {
        await using SchedulingWorld w = await SchedulingWorld.Create(nameof(Recurring_ResourceClash_ListsEveryClashingDateWithResourceCapacityAndUsage));
        Resource table = await w.AddResource(capacity: 1, name: "Stol");
        await SetDefaults(w, w.Service, (table, 1));
        // Drugi zaposlenik i klijent drže stol u drugom tjednu niza.
        DateTimeOffset clash = SchedulingWorld.Future(10).AddDays(7);
        AppointmentCreateRequest other = Create(w, clash);
        other.Segments.Single().EmployeeIds = new List<Guid> { (await w.AddEmployee("Drugi")).Id.Value };
        other.Segments.Single().Participants = new List<AppointmentParticipantCreateRequest> { new() { ClientId = (await w.AddClient("Drugi")).Id.Value } };
        await w.Appointments.Create(w.OrganizationId, w.ActorUserId, true, other);

        BusinessRuleException ex = await SchedulingAssert.BusinessRule(ErrorCodes.ResourceCapacityExceeded, () =>
            w.Appointments.CreateRecurring(w.OrganizationId, w.ActorUserId, true, new RecurringAppointmentCreateRequest
            {
                CompanyId = w.Company.Id.Value, ServiceId = w.Service.Id.Value, EmployeeId = w.Employee.Id.Value,
                ClientIds = new List<Guid> { w.Client.Id.Value },
                FirstOccurrenceStartsAt = SchedulingWorld.Future(10), EndDate = SchedulingWorld.Future(10).AddDays(14),
                RecurrenceType = RecurrenceType.Weekly
            }));

        System.Text.Json.JsonElement conflicts = System.Text.Json.JsonSerializer.SerializeToElement(ex.Details).GetProperty("conflicts");
        System.Text.Json.JsonElement conflict = Assert.Single(conflicts.EnumerateArray());
        Assert.Equal(clash, conflict.GetProperty("Date").GetDateTimeOffset());
        Assert.Equal(ErrorCodes.ResourceCapacityExceeded, conflict.GetProperty("Reason").GetString());
        Assert.Equal(table.Id.Value, conflict.GetProperty("ResourceId").GetGuid());
        Assert.Equal("Stol", conflict.GetProperty("ResourceName").GetString());
        Assert.Equal((1, 2), (conflict.GetProperty("Capacity").GetInt32(), conflict.GetProperty("PeakUsage").GetInt32()));
        Assert.Equal(1, await w.CountAppointments()); // ništa iz niza nije spremljeno
    }

    [Fact]
    public async Task GroupTemplate_CopiesTheDefaultsWhenCreated()
    {
        await using SchedulingWorld w = await SchedulingWorld.Create(nameof(GroupTemplate_CopiesTheDefaultsWhenCreated));
        ServiceEntity svc = await w.AddGroupService();
        Resource mat = await w.AddResource(capacity: 10, name: "Prostirka");
        await SetDefaults(w, svc, (mat, 5));

        GroupDto group = await w.CreateGroup(svc, capacity: 5);

        GroupSegmentTemplateResourceDto copied = Assert.Single(Assert.Single(group.SegmentTemplates).Resources);
        Assert.Equal(mat.Id, copied.ResourceId);
        Assert.Equal(5, copied.QuantityRequired);
    }
}
