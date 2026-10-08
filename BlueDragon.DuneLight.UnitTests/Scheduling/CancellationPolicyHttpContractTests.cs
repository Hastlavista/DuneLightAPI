#nullable disable
using System;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text.Json;
using System.Threading.Tasks;
using BlueDragon.DuneLight.Core.DTOs.Appointments;
using BlueDragon.DuneLight.Core.Enums;
using BlueDragon.DuneLight.Core.Shared;
using BlueDragon.DuneLight.Infrastructure.Services;

namespace BlueDragon.DuneLight.UnitTests.Scheduling;

/// <summary>
/// P1 — HTTP contract of the policy-engine surface through the REAL API pipeline (same in-process host as
/// <see cref="MultiSegmentHttpContractTests"/>): routing and [RequireGrant] of /api/cancellation-policies, binding of the
/// string enum initiator and rule types, the waive endpoint's grants, and the confirm correction reason in the body.
/// </summary>
public class CancellationPolicyHttpContractTests : IClassFixture<MultiSegmentHttpContractTests.ApiHost>
{
    private readonly HttpClient _http;

    public CancellationPolicyHttpContractTests(MultiSegmentHttpContractTests.ApiHost host)
    {
        _http = host.Client;
    }

    private Task<(HttpStatusCode Status, JsonElement Body)> Send(HttpMethod method, string url, string token, object body = null) =>
        MultiSegmentHttpContractTests.Send(_http, method, url, token, body);

    private static void AssertError(HttpStatusCode status, string code, (HttpStatusCode Status, JsonElement Body) response) =>
        MultiSegmentHttpContractTests.AssertError(status, code, response);

    /// <summary>A real GrantGroup with exactly these grants for a fresh user of the world, and its JWT.</summary>
    private static async Task<string> Token(SchedulingWorld w, params string[] grants)
    {
        Guid userId = await w.AddMemberUser();
        await w.GrantUser(userId, grants);
        return new JwtService(MultiSegmentHttpContractTests.Jwt).GenerateToken(userId, $"{userId:N}@http.test", w.OrganizationId);
    }

    private static object Rule(string feeType, decimal? feeValue = null, string packageAction = "None") =>
        new { feeType, feeValue, packageAction };

    [Fact]
    public async Task Policies_ViewReads_ManageWrites_RulesBindAsStrings_AndAnAssignedPolicyIsInUse()
    {
        await using SchedulingWorld w = await SchedulingWorld.Create(nameof(Policies_ViewReads_ManageWrites_RulesBindAsStrings_AndAnAssignedPolicyIsInUse));
        string viewer = await Token(w, Grants.CatalogCancellationPoliciesView);
        string manager = await Token(w, Grants.CatalogCancellationPoliciesView, Grants.CatalogCancellationPoliciesManage);

        var list = await Send(HttpMethod.Get, "/api/cancellation-policies", viewer);
        Assert.Equal(HttpStatusCode.OK, list.Status);
        JsonElement neutral = Assert.Single(list.Body.EnumerateArray());
        Assert.True(neutral.GetProperty("isOrganizationDefault").GetBoolean());

        object create = new
        {
            name = "Strict", cancellationWindowMinutes = 120,
            lateCancellation = Rule("Fixed", 10m), noShow = Rule("Percentage", 50m, "ConsumeUnit")
        };
        AssertError(HttpStatusCode.Forbidden, ErrorCodes.Forbidden, await Send(HttpMethod.Post, "/api/cancellation-policies", viewer, create));
        AssertError(HttpStatusCode.BadRequest, ErrorCodes.ValidationError, await Send(HttpMethod.Post, "/api/cancellation-policies", manager,
            new { name = "Broken", cancellationWindowMinutes = 10, noShow = Rule("None") })); // lateCancellation missing

        var created = await Send(HttpMethod.Post, "/api/cancellation-policies", manager, create);
        Assert.Equal(HttpStatusCode.OK, created.Status);
        Guid policyId = created.Body.GetProperty("id").GetGuid();
        JsonElement latest = created.Body.GetProperty("latestVersion");
        Assert.Equal(("Fixed", "ConsumeUnit"), (latest.GetProperty("lateCancellation").GetProperty("feeType").GetString(),
            latest.GetProperty("noShow").GetProperty("packageAction").GetString()));

        var published = await Send(HttpMethod.Post, $"/api/cancellation-policies/{policyId}/versions", manager,
            new { cancellationWindowMinutes = 60, lateCancellation = Rule("None"), noShow = Rule("None") });
        Assert.Equal(2, published.Body.GetProperty("latestVersion").GetProperty("version").GetInt32());

        AssertError(HttpStatusCode.Forbidden, ErrorCodes.Forbidden, await Send(HttpMethod.Put, "/api/cancellation-policies/assignments", viewer,
            new { serviceId = w.Service.Id, cancellationPolicyId = policyId }));
        var assigned = await Send(HttpMethod.Put, "/api/cancellation-policies/assignments", manager,
            new { serviceId = w.Service.Id, cancellationPolicyId = policyId });
        Assert.Equal(HttpStatusCode.OK, assigned.Status);

        var resolved = await Send(HttpMethod.Get, $"/api/cancellation-policies/resolve?companyId={w.Company.Id}&serviceId={w.Service.Id}", viewer);
        Assert.Equal((policyId, "Service", 2), (resolved.Body.GetProperty("cancellationPolicyId").GetGuid(),
            resolved.Body.GetProperty("resolvedFrom").GetString(), resolved.Body.GetProperty("version").GetProperty("version").GetInt32()));

        AssertError(HttpStatusCode.Conflict, ErrorCodes.CancellationPolicyInUse,
            await Send(HttpMethod.Post, $"/api/cancellation-policies/{policyId}/deactivate", manager));
        Assert.Equal(HttpStatusCode.NoContent, (await Send(HttpMethod.Delete,
            $"/api/cancellation-policies/assignments/{assigned.Body.GetProperty("id").GetGuid()}", manager)).Status);
        Assert.False((await Send(HttpMethod.Post, $"/api/cancellation-policies/{policyId}/deactivate", manager)).Body.GetProperty("isActive").GetBoolean());
    }

    [Fact]
    public async Task Cancel_RequiresAndBindsTheInitiator_WaiveNeedsTheOverride_ConfirmTakesTheCorrectionReasonInTheBody()
    {
        await using SchedulingWorld w = await SchedulingWorld.Create(nameof(Cancel_RequiresAndBindsTheInitiator_WaiveNeedsTheOverride_ConfirmTakesTheCorrectionReasonInTheBody));
        await w.PublishDefaultPolicyVersion((int)(SchedulingWorld.Future(10) - DateTimeOffset.UtcNow).TotalMinutes + 60,
            CancellationFeeType.Fixed, 10m);
        AppointmentDto appointment = await w.CreateAppointment(SchedulingWorld.Future(10));
        Guid participationId = appointment.Bookings.Single().Participations.Single().Id;
        string writer = await Token(w, Grants.AppointmentsView, Grants.AppointmentsWriteAll);
        string overrider = await Token(w, Grants.AppointmentsView, Grants.AppointmentsWriteAll, Grants.AppointmentsPolicyOverride);

        AssertError(HttpStatusCode.BadRequest, ErrorCodes.ValidationError,
            await Send(HttpMethod.Patch, $"/api/participations/{participationId}/cancel", writer, new { }));
        AssertError(HttpStatusCode.BadRequest, ErrorCodes.ValidationError, await Send(HttpMethod.Patch, $"/api/participations/{participationId}/cancel",
            writer, new { cancellationInitiator = "Client", packageSelections = new[] { new { participationId, clientPackageId = Guid.NewGuid() } } }));

        var cancelled = await Send(HttpMethod.Patch, $"/api/participations/{participationId}/cancel", writer, new { cancellationInitiator = "Client" });
        Assert.Equal(HttpStatusCode.OK, cancelled.Status);
        JsonElement p = cancelled.Body.GetProperty("participations")[0];
        Assert.Equal(("Client", true, "Active", 10m), (p.GetProperty("cancellationInitiator").GetString(), p.GetProperty("isLateCancellation").GetBoolean(),
            p.GetProperty("policyConsequence").GetProperty("status").GetString(), p.GetProperty("monetaryDue").GetDecimal()));

        // Correction of a consequence with a real effect: the reason travels in the BODY, and the override is required.
        AssertError(HttpStatusCode.BadRequest, ErrorCodes.ValidationError,
            await Send(HttpMethod.Patch, $"/api/participations/{participationId}/confirm", overrider));
        AssertError(HttpStatusCode.Forbidden, ErrorCodes.Forbidden,
            await Send(HttpMethod.Patch, $"/api/participations/{participationId}/confirm", writer, new { correctionReason = "client came" }));
        var confirmed = await Send(HttpMethod.Patch, $"/api/participations/{participationId}/confirm", overrider, new { correctionReason = "client came" });
        Assert.Equal(HttpStatusCode.OK, confirmed.Status);
        Assert.Equal("Reversed", confirmed.Body.GetProperty("participations")[0].GetProperty("policyConsequence").GetProperty("status").GetString());

        // Late again -> waive: 403 without the override grant, NO_ACTIVE_POLICY_CONSEQUENCE once nothing is active.
        await Send(HttpMethod.Patch, $"/api/participations/{participationId}/cancel", writer, new { cancellationInitiator = "Client" });
        string waiveUrl = $"/api/participations/{participationId}/policy-consequence/waive";
        AssertError(HttpStatusCode.Forbidden, ErrorCodes.Forbidden, await Send(HttpMethod.Post, waiveUrl, writer, new { waiverReason = "goodwill" }));
        AssertError(HttpStatusCode.BadRequest, ErrorCodes.ValidationError, await Send(HttpMethod.Post, waiveUrl, overrider, new { }));
        var waived = await Send(HttpMethod.Post, waiveUrl, overrider, new { waiverReason = "goodwill" });
        Assert.Equal(HttpStatusCode.OK, waived.Status);
        Assert.Equal("Waived", waived.Body.GetProperty("participations")[0].GetProperty("policyConsequence").GetProperty("status").GetString());
        AssertError(HttpStatusCode.Conflict, ErrorCodes.NoActivePolicyConsequence, await Send(HttpMethod.Post, waiveUrl, overrider, new { waiverReason = "again" }));

        // The removed cutoff endpoint is gone.
        Assert.Equal(HttpStatusCode.NotFound, (await Send(HttpMethod.Put, "/api/organization/settings/cancellation-cutoff", overrider,
            new { cancellationCutoffMinutes = 60 })).Status);
    }
}
