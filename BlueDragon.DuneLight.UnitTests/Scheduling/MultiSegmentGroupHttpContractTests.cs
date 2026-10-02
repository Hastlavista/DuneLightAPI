#nullable disable
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text.Json;
using System.Threading.Tasks;
using BlueDragon.DuneLight.Core.Shared;
using BlueDragon.DuneLight.Infrastructure.Domain.Models.Appointments;
using BlueDragon.DuneLight.Infrastructure.Domain.Models.Clients;
using BlueDragon.DuneLight.Infrastructure.Services;
using ServiceEntity = BlueDragon.DuneLight.Infrastructure.Domain.Models.Catalog.Service;

namespace BlueDragon.DuneLight.UnitTests.Scheduling;

/// <summary>
/// Phase M1F — HTTP contract of the multi-template Group surface through the same in-process production pipeline as the
/// M1E.1 contract tests (shared <see cref="MultiSegmentHttpContractTests.ApiHost"/>): multi-template create binding, explicit
/// member template selection, missing selection, capacity-override authorization (403 without the grant), segment-specific
/// waitlist selection, legacy no-selector calls on a multi-template group (domain error, never 500), 404s and the envelope.
/// </summary>
public class MultiSegmentGroupHttpContractTests : IClassFixture<MultiSegmentHttpContractTests.ApiHost>
{
    private readonly HttpClient _http;

    public MultiSegmentGroupHttpContractTests(MultiSegmentHttpContractTests.ApiHost host)
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

    private sealed record Arranged(SchedulingWorld World, Guid GroupId, Guid A, Guid B, string Manager, string ManagerWithOverride, string ViewOnly);

    /// <summary>A two-template group created over HTTP (POST /api/groups with segmentTemplates): A capacity 1 at +0, B at +60.</summary>
    private async Task<Arranged> Arrange(string name)
    {
        SchedulingWorld w = await SchedulingWorld.Create(name);
        ServiceEntity yoga = await w.AddGroupService(60);
        ServiceEntity massage = await w.AddGroupService(30);
        string manager = await TokenFor(w, w.ActorUserId, Grants.GroupsView, Grants.GroupsManage, Grants.AppointmentsView, Grants.AppointmentsWriteAll);
        string overrider = await TokenFor(w, await w.AddMemberUser(), Grants.GroupsView, Grants.GroupsManage, Grants.GroupsCapacityOverride);
        string viewOnly = await TokenFor(w, await w.AddMemberUser(), Grants.GroupsView);

        var created = await Send(HttpMethod.Post, "/api/groups", manager, new
        {
            name = "Wellness",
            companyId = w.Company.Id,
            defaultTrainerId = w.Employee.Id,
            slots = new[] { new { dayOfWeek = SchedulingWorld.FutureDay.DayOfWeek.ToString(), startTime = "09:00:00" } },
            segmentTemplates = new object[]
            {
                new { serviceId = yoga.Id, startOffsetMinutes = 0, capacity = 1 },
                new { serviceId = massage.Id, startOffsetMinutes = 60, durationMinutes = 30, capacity = 5 }
            }
        });
        Assert.Equal(HttpStatusCode.Created, created.Status); // CreatedAtAction (unchanged contract)
        JsonElement templates = created.Body.GetProperty("segmentTemplates");
        Assert.Equal(2, templates.GetArrayLength());
        Assert.False(created.Body.TryGetProperty("serviceId", out _)); // compatibility projection is null (omitted) for multi
        Guid a = templates.EnumerateArray().Single(t => t.GetProperty("startOffsetMinutes").GetInt32() == 0).GetProperty("id").GetGuid();
        Guid b = templates.EnumerateArray().Single(t => t.GetProperty("startOffsetMinutes").GetInt32() == 60).GetProperty("id").GetGuid();
        Assert.Equal(60, templates.EnumerateArray().Single(t => t.GetProperty("id").GetGuid() == a).GetProperty("durationMinutes").GetInt32());
        return new Arranged(w, created.Body.GetProperty("id").GetGuid(), a, b, manager, overrider, viewOnly);
    }

    [Fact]
    public async Task MembersAndTemplates_BindExplicitSelection_MissingSelectionIsADomainError_OverrideNeedsTheGrant()
    {
        Arranged x = await Arrange(nameof(MembersAndTemplates_BindExplicitSelection_MissingSelectionIsADomainError_OverrideNeedsTheGrant));
        await using SchedulingWorld w = x.World;
        Client ana = await w.AddClient("Ana");
        Client marko = await w.AddClient("Marko");

        // Missing selection on a multi-template group → 409 SEGMENT_SELECTION_REQUIRED (never "all templates").
        AssertError(HttpStatusCode.Conflict, ErrorCodes.SegmentSelectionRequired,
            await Send(HttpMethod.Post, $"/api/groups/{x.GroupId}/members", x.Manager, new { clientId = ana.Id }));

        var joined = await Send(HttpMethod.Post, $"/api/groups/{x.GroupId}/members", x.Manager, new { clientId = ana.Id, segmentTemplateIds = new[] { x.A } });
        Assert.Equal(HttpStatusCode.OK, joined.Status);

        // A is full (capacity 1): normal add → 409; override without the grant → 403; with the grant → 200.
        object overBody = new { clientId = marko.Id, segmentTemplateIds = new[] { x.A }, overrideCapacity = true };
        AssertError(HttpStatusCode.Conflict, ErrorCodes.GroupCapacityReached,
            await Send(HttpMethod.Post, $"/api/groups/{x.GroupId}/members", x.Manager, new { clientId = marko.Id, segmentTemplateIds = new[] { x.A } }));
        AssertError(HttpStatusCode.Forbidden, ErrorCodes.Forbidden, await Send(HttpMethod.Post, $"/api/groups/{x.GroupId}/members", x.Manager, overBody));
        Assert.Equal(HttpStatusCode.OK, (await Send(HttpMethod.Post, $"/api/groups/{x.GroupId}/members", x.ManagerWithOverride, overBody)).Status);

        // Selection change binds the member route id and body.
        var detail = await Send(HttpMethod.Get, $"/api/groups/{x.GroupId}", x.Manager);
        JsonElement anaMember = detail.Body.GetProperty("members").EnumerateArray().Single(m => m.GetProperty("clientId").GetGuid() == ana.Id);
        Assert.Equal(x.A, anaMember.GetProperty("segmentTemplateIds")[0].GetGuid());
        var changed = await Send(HttpMethod.Put, $"/api/groups/{x.GroupId}/members/{anaMember.GetProperty("id").GetGuid()}/segment-templates", x.Manager,
            new { segmentTemplateIds = new[] { x.B } });
        Assert.Equal(HttpStatusCode.OK, changed.Status);

        // Template routes: add, unknown ids → 404, last-template / validation errors through the envelope.
        AssertError(HttpStatusCode.NotFound, ErrorCodes.NotFound,
            await Send(HttpMethod.Delete, $"/api/groups/{x.GroupId}/segment-templates/{Guid.NewGuid()}", x.Manager));
        AssertError(HttpStatusCode.NotFound, ErrorCodes.NotFound,
            await Send(HttpMethod.Post, $"/api/groups/{Guid.NewGuid()}/members", x.Manager, new { clientId = ana.Id, segmentTemplateIds = new[] { x.A } }));
        Client newcomer = await w.AddClient("Newcomer");
        AssertError(HttpStatusCode.NotFound, ErrorCodes.NotFound,
            await Send(HttpMethod.Post, $"/api/groups/{x.GroupId}/members", x.Manager, new { clientId = newcomer.Id, segmentTemplateIds = new[] { Guid.NewGuid() } }));
        AssertError(HttpStatusCode.BadRequest, ErrorCodes.ValidationError,
            await Send(HttpMethod.Post, $"/api/groups/{x.GroupId}/segment-templates", x.Manager, new { serviceId = Guid.NewGuid(), startOffsetMinutes = 2000, capacity = 0 }));

        // Authorization: view-only cannot manage; anonymous is 401.
        AssertError(HttpStatusCode.Forbidden, ErrorCodes.Forbidden,
            await Send(HttpMethod.Post, $"/api/groups/{x.GroupId}/members", x.ViewOnly, new { clientId = ana.Id, segmentTemplateIds = new[] { x.A } }));
        Assert.Equal(HttpStatusCode.Unauthorized, (await Send(HttpMethod.Get, $"/api/groups/{x.GroupId}", null)).Status);
    }

    [Fact]
    public async Task WaitlistAndGuest_AreSegmentSpecific_LegacyNoSelectorCallsAreDomainErrors_Never500()
    {
        Arranged x = await Arrange(nameof(WaitlistAndGuest_AreSegmentSpecific_LegacyNoSelectorCallsAreDomainErrors_Never500));
        await using SchedulingWorld w = x.World;
        Client holder = await w.AddClient("Holder");
        Client waiter = await w.AddClient("Waiter");
        Assert.Equal(HttpStatusCode.OK, (await Send(HttpMethod.Post, $"/api/groups/{x.GroupId}/members", x.Manager,
            new { clientId = holder.Id, segmentTemplateIds = new[] { x.A } })).Status);
        var generated = await Send(HttpMethod.Post, "/api/groups/generate-appointments", x.Manager,
            new { groupId = x.GroupId, fromDate = SchedulingWorld.FutureDay, toDate = SchedulingWorld.FutureDay });
        Assert.Equal(HttpStatusCode.OK, generated.Status);
        Guid appointmentId = generated.Body.GetProperty("created")[0].GetProperty("id").GetGuid();
        Appointment occurrence = await w.LoadAppointment(appointmentId);
        Guid segA = occurrence.Segments.Single(s => s.GroupSegmentTemplateId == x.A).Id.Value;
        Guid segB = occurrence.Segments.Single(s => s.GroupSegmentTemplateId == x.B).Id.Value;

        // Waitlist: no selector → 409; selector → bound to that segment.
        AssertError(HttpStatusCode.Conflict, ErrorCodes.SegmentSelectionRequired,
            await Send(HttpMethod.Post, $"/api/appointments/{appointmentId}/waitlist", x.Manager, new { clientId = waiter.Id }));
        var waiting = await Send(HttpMethod.Post, $"/api/appointments/{appointmentId}/waitlist", x.Manager, new { clientId = waiter.Id, segmentId = segA });
        Assert.Equal(HttpStatusCode.OK, waiting.Status);
        Assert.Equal(segA, waiting.Body.GetProperty("appointmentSegmentId").GetGuid());
        AssertError(HttpStatusCode.Conflict, ErrorCodes.SegmentSelectionRequired,
            await Send(HttpMethod.Delete, $"/api/appointments/{appointmentId}/waitlist/{waiter.Id}", x.Manager));
        Assert.Equal(HttpStatusCode.OK,
            (await Send(HttpMethod.Delete, $"/api/appointments/{appointmentId}/waitlist/{waiter.Id}?segmentId={segA}", x.Manager)).Status);
        AssertError(HttpStatusCode.NotFound, ErrorCodes.NotFound,
            await Send(HttpMethod.Post, $"/api/appointments/{appointmentId}/waitlist", x.Manager, new { clientId = waiter.Id, segmentId = Guid.NewGuid() }));

        // Guest: legacy add without a segment → 409; with the segment → only that participation.
        AssertError(HttpStatusCode.Conflict, ErrorCodes.SegmentSelectionRequired,
            await Send(HttpMethod.Post, $"/api/appointments/{appointmentId}/bookings", x.Manager, new { clientId = waiter.Id }));
        var guest = await Send(HttpMethod.Post, $"/api/appointments/{appointmentId}/bookings", x.Manager, new { clientId = waiter.Id, segmentId = segB });
        Assert.Equal(HttpStatusCode.OK, guest.Status);
        Assert.Equal(segB, guest.Body.GetProperty("participations")[0].GetProperty("appointmentSegmentId").GetGuid());
        Assert.Equal(1, guest.Body.GetProperty("participations").GetArrayLength());
    }
}
