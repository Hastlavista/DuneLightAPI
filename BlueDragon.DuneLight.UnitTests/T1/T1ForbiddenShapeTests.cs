#nullable disable
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Threading.Tasks;
using BlueDragon.DuneLight.Core.Enums;
using BlueDragon.DuneLight.Core.Shared;
using BlueDragon.DuneLight.Core.Shared.Exceptions;
using BlueDragon.DuneLight.Infrastructure.Services;
using BlueDragon.DuneLight.Infrastructure.Utils;
using BlueDragon.DuneLight.UnitTests.Scheduling;

namespace BlueDragon.DuneLight.UnitTests.T1;

/// <summary>
/// T1-5 — svaki 403 nosi details: vrsta odbijanja (reason) i SVI grantovi koji nedostaju (requiredGrants + match), da korisnik
/// odjednom vidi sve što mu treba (FE-ADR-0003). Isti oblik iz [RequireGrant] filtera i iz servisa.
/// </summary>
public class T1ForbiddenShapeTests : IClassFixture<MultiSegmentHttpContractTests.ApiHost>
{
    private readonly HttpClient _http;

    public T1ForbiddenShapeTests(MultiSegmentHttpContractTests.ApiHost host)
    {
        _http = host.Client;
    }

    [Fact]
    public async Task RequireGrant_WithoutAnyOfTheGrants_Is403_WithReasonMissingGrant_AndEveryAcceptedGrant()
    {
        await using SchedulingWorld w = await SchedulingWorld.Create(nameof(RequireGrant_WithoutAnyOfTheGrants_Is403_WithReasonMissingGrant_AndEveryAcceptedGrant));
        Guid member = await w.AddMemberUser();
        string token = new JwtService(MultiSegmentHttpContractTests.Jwt).GenerateToken(member, $"{member:N}@http.test", w.OrganizationId);

        using HttpRequestMessage request = new(HttpMethod.Get, "/api/organization/settings");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        using HttpResponseMessage response = await _http.SendAsync(request);
        JsonElement error = JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement.GetProperty("error");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Equal(ErrorCodes.Forbidden, error.GetProperty("code").GetString());
        JsonElement details = error.GetProperty("details");
        Assert.Equal("MissingGrant", details.GetProperty("reason").GetString());
        Assert.Equal(new[] { Grants.OrganizationSettingsManage },
            details.GetProperty("requiredGrants").EnumerateArray().Select(g => g.GetString()).ToArray());
    }

    [Fact]
    public async Task OrganizationClock_IsAvailableToEverySignedInUser_WithoutAGrant()
    {
        await using SchedulingWorld w = await SchedulingWorld.Create(nameof(OrganizationClock_IsAvailableToEverySignedInUser_WithoutAGrant));
        Guid member = await w.AddMemberUser();
        string token = new JwtService(MultiSegmentHttpContractTests.Jwt).GenerateToken(member, $"{member:N}@http.test", w.OrganizationId);

        using HttpRequestMessage request = new(HttpMethod.Get, "/api/organization/clock");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        using HttpResponseMessage response = await _http.SendAsync(request);
        JsonElement body = JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement;

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.False(body.GetProperty("isSimulated").GetBoolean());
        Assert.Equal("UTC", body.GetProperty("timeZone").GetString());
    }

    [Fact]
    public void Reopen_MissingBothCorrectionGrants_ListsBoth_WithMatchAll()
    {
        ForbiddenAppException ex = Assert.Throws<ForbiddenAppException>(() => AppointmentClosure.EnsureReopenAllowed(
            new GrantContext(new HashSet<string>()), new[] { ParticipationStatus.Completed, ParticipationStatus.NoShow }, "razlog"));

        Assert.Equal(ForbiddenReason.MissingGrant, ex.Details.Reason);
        Assert.Equal(GrantMatch.All, ex.Details.Match);
        Assert.Equal(new[] { Grants.AppointmentsCorrectionsCompleted, Grants.AppointmentsCorrectionsNoShow }.OrderBy(g => g),
            ex.Details.RequiredGrants.OrderBy(g => g));
    }

    [Fact]
    public void Reopen_WithoutTerminalParticipations_AnyCorrectionGrantIsEnough_WithMatchAny()
    {
        ForbiddenAppException ex = Assert.Throws<ForbiddenAppException>(() => AppointmentClosure.EnsureReopenAllowed(
            new GrantContext(new HashSet<string>()), new[] { ParticipationStatus.Confirmed }, "razlog"));

        Assert.Equal(GrantMatch.Any, ex.Details.Match);
        Assert.Equal(3, ex.Details.RequiredGrants.Count);
    }
}
