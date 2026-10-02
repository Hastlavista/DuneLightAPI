#nullable disable
using System;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text.Json;
using System.Threading.Tasks;
using BlueDragon.DuneLight.Core.Shared;
using BlueDragon.DuneLight.Infrastructure.Domain.Models.Employees;
using BlueDragon.DuneLight.Infrastructure.Services;
using ServiceEntity = BlueDragon.DuneLight.Infrastructure.Domain.Models.Catalog.Service;

namespace BlueDragon.DuneLight.UnitTests.Scheduling;

/// <summary>
/// Phase M1G — HTTP contract of multi-employee segments, the pricing-source endpoint, multi-employee ownership, employee price
/// list items and Group template staffing through the production API pipeline (the shared in-process host of
/// <see cref="MultiSegmentHttpContractTests"/>; no new harness).
/// </summary>
public class MultiEmployeeHttpContractTests : IClassFixture<MultiSegmentHttpContractTests.ApiHost>
{
    private readonly HttpClient _http;

    public MultiEmployeeHttpContractTests(MultiSegmentHttpContractTests.ApiHost host)
    {
        _http = host.Client;
    }

    private Task<(HttpStatusCode Status, JsonElement Body)> Send(HttpMethod method, string url, string token, object body = null) =>
        MultiSegmentHttpContractTests.Send(_http, method, url, token, body);

    private static void AssertError(HttpStatusCode status, string code, (HttpStatusCode Status, JsonElement Body) response) =>
        MultiSegmentHttpContractTests.AssertError(status, code, response);

    private static async Task<string> TokenFor(SchedulingWorld w, Guid userId, params string[] grants)
    {
        await w.GrantUser(userId, grants);
        return new JwtService(MultiSegmentHttpContractTests.Jwt).GenerateToken(userId, $"{userId:N}@http.test", w.OrganizationId, "Member");
    }

    private static bool IsNullOrAbsent(JsonElement element, string property) =>
        !element.TryGetProperty(property, out JsonElement value) || value.ValueKind == JsonValueKind.Null;

    private static JsonElement OnlySegment(JsonElement appointment) => appointment.GetProperty("segments").EnumerateArray().Single();

    private static JsonElement OnlyParticipation(JsonElement appointment) =>
        appointment.GetProperty("bookings").EnumerateArray().Single().GetProperty("participations").EnumerateArray().Single();

    [Fact]
    public async Task MultiEmployeeSegment_CreatePricingSourceEmployeeSetAndOwnership_OverHttp()
    {
        await using SchedulingWorld w = await SchedulingWorld.Create(nameof(MultiEmployeeSegment_CreatePricingSourceEmployeeSetAndOwnership_OverHttp));
        ServiceEntity duo = await w.AddService(60, 50m, name: "Duo");
        Employee ana = await w.AddEmployee("Ana", assignedToService: false);
        Employee marko = await w.AddEmployee("Marko", assignedToService: false);
        Employee ivana = await w.AddEmployee("Ivana", assignedToService: false);
        string writeAll = await TokenFor(w, w.ActorUserId, Grants.AppointmentsView, Grants.AppointmentsWriteAll, Grants.CatalogPriceListManage, Grants.CatalogPriceListView);
        string ownAna = await TokenFor(w, ana.UserId, Grants.AppointmentsView, Grants.AppointmentsWriteOwn);
        string ownMarko = await TokenFor(w, marko.UserId, Grants.AppointmentsView, Grants.AppointmentsWriteOwn);
        string ownIvana = await TokenFor(w, ivana.UserId, Grants.AppointmentsView, Grants.AppointmentsWriteOwn);
        string viewOnly = await TokenFor(w, await w.AddMemberUser(), Grants.AppointmentsView);

        // Employee price list items (services only).
        var anaPrice = await Send(HttpMethod.Post, "/api/catalog/price-list", writeAll, new
        {
            subjectType = "Service", serviceId = duo.Id, employeeId = ana.Id, price = 60m, validFrom = SchedulingWorld.PastDay
        });
        Assert.True(anaPrice.Status is HttpStatusCode.OK or HttpStatusCode.Created, anaPrice.Body.ToString());
        Assert.Equal(ana.Id, anaPrice.Body.GetProperty("employeeId").GetGuid());
        await Send(HttpMethod.Post, "/api/catalog/price-list", writeAll, new
        {
            subjectType = "Service", serviceId = duo.Id, employeeId = marko.Id, price = 70m, validFrom = SchedulingWorld.PastDay
        });

        object Body(object segment) => new { companyId = w.Company.Id, segments = new[] { segment } };
        var missing = await Send(HttpMethod.Post, "/api/appointments", writeAll, Body(new
        {
            serviceId = duo.Id, plannedStart = SchedulingWorld.Future(10), employeeIds = new[] { ana.Id, marko.Id },
            participants = new[] { new { clientId = w.Client.Id } }
        }));
        AssertError(HttpStatusCode.BadRequest, ErrorCodes.PricingSourceRequired, missing);
        AssertError(HttpStatusCode.BadRequest, ErrorCodes.InvalidPricingSource, await Send(HttpMethod.Post, "/api/appointments", writeAll, Body(new
        {
            serviceId = duo.Id, plannedStart = SchedulingWorld.Future(10), employeeIds = new[] { ana.Id, marko.Id },
            pricingMode = "Employee", pricingEmployeeId = ivana.Id, participants = new[] { new { clientId = w.Client.Id } }
        })));

        var created = await Send(HttpMethod.Post, "/api/appointments", writeAll, Body(new
        {
            serviceId = duo.Id, plannedStart = SchedulingWorld.Future(10), employeeIds = new[] { ana.Id, marko.Id },
            pricingMode = "Employee", pricingEmployeeId = ana.Id, participants = new[] { new { clientId = w.Client.Id } }
        }));
        Assert.Equal(HttpStatusCode.Created, created.Status);
        JsonElement segment = OnlySegment(created.Body);
        Guid segmentId = segment.GetProperty("id").GetGuid();
        Guid appointmentId = created.Body.GetProperty("id").GetGuid();
        Assert.Equal(2, segment.GetProperty("employees").GetArrayLength());
        Assert.Equal("Employee", segment.GetProperty("pricingMode").GetString());
        Assert.Equal(ana.Id, segment.GetProperty("pricingEmployeeId").GetGuid());
        Assert.Equal(60m, OnlyParticipation(created.Body).GetProperty("amount").GetDecimal());
        Assert.Equal("EmployeeAllCompanies", OnlyParticipation(created.Body).GetProperty("baseAmountSource").GetString());
        Assert.True(IsNullOrAbsent(created.Body, "employeeId")); // legacy projection: no fake employee

        // Pricing source without changing employees.
        var toStandard = await Send(HttpMethod.Patch, $"/api/segments/{segmentId}/pricing-source", writeAll, new { pricingMode = "Standard" });
        Assert.Equal(HttpStatusCode.OK, toStandard.Status);
        Assert.Equal("Standard", OnlySegment(toStandard.Body).GetProperty("pricingMode").GetString());
        Assert.Equal(50m, OnlyParticipation(toStandard.Body).GetProperty("amount").GetDecimal());
        AssertError(HttpStatusCode.Forbidden, ErrorCodes.Forbidden,
            await Send(HttpMethod.Patch, $"/api/segments/{segmentId}/pricing-source", viewOnly, new { pricingMode = "Standard" }));

        // Both assigned employees own the segment; others do not; own scope cannot change the roster.
        Assert.Equal(HttpStatusCode.OK, (await Send(HttpMethod.Patch, $"/api/segments/{segmentId}/time", ownAna, new { plannedStart = SchedulingWorld.Future(11) })).Status);
        Assert.Equal(HttpStatusCode.OK, (await Send(HttpMethod.Patch, $"/api/segments/{segmentId}/time", ownMarko, new { plannedStart = SchedulingWorld.Future(12) })).Status);
        AssertError(HttpStatusCode.Conflict, ErrorCodes.NotOwner,
            await Send(HttpMethod.Patch, $"/api/segments/{segmentId}/time", ownIvana, new { plannedStart = SchedulingWorld.Future(13) }));
        AssertError(HttpStatusCode.Conflict, ErrorCodes.NotOwner,
            await Send(HttpMethod.Patch, $"/api/segments/{segmentId}/employees", ownAna, new { employeeIds = new[] { ana.Id } }));
        AssertError(HttpStatusCode.Conflict, ErrorCodes.NotOwner,
            await Send(HttpMethod.Post, $"/api/appointments/{appointmentId}/cancel", ownAna, new { }));

        // Employee-set change (write.all): 2+ needs a choice; down to one is automatic.
        AssertError(HttpStatusCode.BadRequest, ErrorCodes.PricingSourceRequired,
            await Send(HttpMethod.Patch, $"/api/segments/{segmentId}/employees", writeAll, new { employeeIds = new[] { marko.Id, ivana.Id } }));
        var toMarko = await Send(HttpMethod.Patch, $"/api/segments/{segmentId}/employees", writeAll, new { employeeIds = new[] { marko.Id } });
        Assert.Equal(HttpStatusCode.OK, toMarko.Status);
        Assert.Equal(marko.Id, OnlySegment(toMarko.Body).GetProperty("pricingEmployeeId").GetGuid());
        Assert.Equal(70m, OnlyParticipation(toMarko.Body).GetProperty("amount").GetDecimal());
        AssertError(HttpStatusCode.BadRequest, ErrorCodes.InvalidPricingSource,
            await Send(HttpMethod.Patch, $"/api/segments/{segmentId}/pricing-source", writeAll, new { pricingMode = "Standard" }));

        // Legacy flat Move on a multi-employee segment is a business error, never a silent collapse.
        await Send(HttpMethod.Patch, $"/api/segments/{segmentId}/employees", writeAll, new { employeeIds = new[] { marko.Id, ivana.Id }, pricingMode = "Standard" });
        AssertError(HttpStatusCode.Conflict, ErrorCodes.EmployeeSetCommandRequired,
            await Send(HttpMethod.Patch, $"/api/appointments/{appointmentId}/move", writeAll, new { startsAt = SchedulingWorld.Future(15) }));
    }

    [Fact]
    public async Task GroupTemplateStaffing_AndMultiTemplateGeneration_OverHttp()
    {
        await using SchedulingWorld w = await SchedulingWorld.Create(nameof(GroupTemplateStaffing_AndMultiTemplateGeneration_OverHttp));
        ServiceEntity yoga = await w.AddGroupService(60);
        ServiceEntity recovery = await w.AddGroupService(45);
        Employee ana = await w.AddEmployee("Ana", assignedToService: false);
        Employee ivana = await w.AddEmployee("Ivana", assignedToService: false);
        string manager = await TokenFor(w, w.ActorUserId, Grants.GroupsView, Grants.GroupsManage, Grants.AppointmentsView, Grants.AppointmentsWriteAll);

        object Group(object recoveryTemplate) => new
        {
            name = "Wellness",
            companyId = w.Company.Id,
            slots = new[] { new { dayOfWeek = SchedulingWorld.FutureDay.DayOfWeek.ToString(), startTime = "09:00:00" } },
            segmentTemplates = new[]
            {
                new { serviceId = yoga.Id, startOffsetMinutes = 0, durationMinutes = (int?)60, capacity = 10, employeeIds = new[] { ana.Id }, pricingMode = (string)null },
                recoveryTemplate
            }
        };
        AssertError(HttpStatusCode.BadRequest, ErrorCodes.PricingSourceRequired, await Send(HttpMethod.Post, "/api/groups", manager, Group(new
        {
            serviceId = recovery.Id, startOffsetMinutes = 60, durationMinutes = (int?)45, capacity = 10, employeeIds = new[] { ana.Id, ivana.Id }, pricingMode = (string)null
        })));

        var created = await Send(HttpMethod.Post, "/api/groups", manager, Group(new
        {
            serviceId = recovery.Id, startOffsetMinutes = 60, durationMinutes = (int?)45, capacity = 10, employeeIds = new[] { ana.Id, ivana.Id }, pricingMode = "Standard"
        }));
        Assert.Equal(HttpStatusCode.Created, created.Status);
        JsonElement recoveryDto = created.Body.GetProperty("segmentTemplates").EnumerateArray().Single(t => t.GetProperty("serviceId").GetGuid() == recovery.Id);
        Assert.Equal(2, recoveryDto.GetProperty("employees").GetArrayLength());
        Assert.Equal("Standard", recoveryDto.GetProperty("pricingMode").GetString());
        Assert.True(IsNullOrAbsent(created.Body, "defaultTrainerId"));

        var generated = await Send(HttpMethod.Post, "/api/groups/generate-appointments", manager, new
        {
            groupId = created.Body.GetProperty("id").GetGuid(), fromDate = SchedulingWorld.FutureDay, toDate = SchedulingWorld.FutureDay
        });
        Assert.Equal(HttpStatusCode.OK, generated.Status);
        JsonElement occurrence = generated.Body.GetProperty("created").EnumerateArray().Single();
        JsonElement recoverySegment = occurrence.GetProperty("segments").EnumerateArray().Single(s => s.GetProperty("serviceId").GetGuid() == recovery.Id);
        Assert.Equal(new[] { ana.Id.Value, ivana.Id.Value }.OrderBy(x => x),
            recoverySegment.GetProperty("employees").EnumerateArray().Select(e => e.GetProperty("employeeId").GetGuid()).OrderBy(x => x));
        Assert.Equal("Standard", recoverySegment.GetProperty("pricingMode").GetString());
    }
}
