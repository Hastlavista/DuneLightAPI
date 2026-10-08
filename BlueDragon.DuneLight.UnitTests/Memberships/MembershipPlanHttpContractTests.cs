#nullable disable
using System;
using System.Net;
using System.Net.Http;
using System.Text.Json;
using System.Threading.Tasks;
using BlueDragon.DuneLight.Core.Shared;
using BlueDragon.DuneLight.Infrastructure.Services;
using BlueDragon.DuneLight.UnitTests.Scheduling;

namespace BlueDragon.DuneLight.UnitTests.Memberships;

/// <summary>
/// P2 phase 2A — HTTP contract of /api/membership-plans through the REAL API pipeline (same in-process host as
/// <see cref="MultiSegmentHttpContractTests"/>): routing, [RequireGrant] view/manage, string enums, the domain-specific 400
/// codes and the version publish.
/// </summary>
public class MembershipPlanHttpContractTests : IClassFixture<MultiSegmentHttpContractTests.ApiHost>
{
    private readonly HttpClient _http;

    public MembershipPlanHttpContractTests(MultiSegmentHttpContractTests.ApiHost host)
    {
        _http = host.Client;
    }

    private Task<(HttpStatusCode Status, JsonElement Body)> Send(HttpMethod method, string url, string token, object body = null) =>
        MultiSegmentHttpContractTests.Send(_http, method, url, token, body);

    private static void AssertError(HttpStatusCode status, string code, (HttpStatusCode Status, JsonElement Body) response) =>
        MultiSegmentHttpContractTests.AssertError(status, code, response);

    private static async Task<string> Token(SchedulingWorld w, params string[] grants)
    {
        Guid userId = await w.AddMemberUser();
        await w.GrantUser(userId, grants);
        return new JwtService(MultiSegmentHttpContractTests.Jwt).GenerateToken(userId, $"{userId:N}@http.test", w.OrganizationId);
    }

    private static object Terms(SchedulingWorld w, string companyScope = "AllCompanies", Guid[] companyIds = null, object[] limits = null) => new
    {
        name = "Unlimited",
        price = 49.90m,
        startFee = 15m,
        billingInterval = "Monthly",
        renewalAnchor = "PurchaseDate",
        companyScope,
        companyIds = companyIds ?? Array.Empty<Guid>(),
        services = new[] { new { serviceId = w.Service.Id } },
        usageLimits = limits ?? new object[] { new { window = "Period", maxUses = 8 }, new { window = "Day", maxUses = 1 } },
        minimumCommitmentPeriods = 3,
        cancellationNoticeDays = 15,
        pause = new { allowed = true, maxPauseDays = 30, maxPausesPer12Months = 2, extendsPeriod = true }
    };

    [Fact]
    public async Task Plans_ViewReads_ManageWrites_EnumsBindAsStrings_AndVersionsArePublished()
    {
        await using SchedulingWorld w = await SchedulingWorld.Create(nameof(Plans_ViewReads_ManageWrites_EnumsBindAsStrings_AndVersionsArePublished));
        string none = await Token(w);
        string viewer = await Token(w, Grants.CatalogMembershipsView);
        string manager = await Token(w, Grants.CatalogMembershipsView, Grants.CatalogMembershipsManage);

        AssertError(HttpStatusCode.Forbidden, ErrorCodes.Forbidden, await Send(HttpMethod.Get, "/api/membership-plans", none));
        AssertError(HttpStatusCode.Forbidden, ErrorCodes.Forbidden, await Send(HttpMethod.Post, "/api/membership-plans", viewer, Terms(w)));

        var created = await Send(HttpMethod.Post, "/api/membership-plans", manager, Terms(w));
        Assert.Equal(HttpStatusCode.OK, created.Status);
        Guid planId = created.Body.GetProperty("id").GetGuid();
        JsonElement latest = created.Body.GetProperty("latestVersion");
        Assert.Equal(("Monthly", "PurchaseDate", "AllCompanies"), (latest.GetProperty("billingInterval").GetString(),
            latest.GetProperty("renewalAnchor").GetString(), latest.GetProperty("companyScope").GetString()));
        Assert.Equal(49.90m, latest.GetProperty("price").GetDecimal());
        Assert.Equal(2, latest.GetProperty("usageLimits").GetArrayLength());

        var list = await Send(HttpMethod.Get, "/api/membership-plans", viewer);
        Assert.Equal(HttpStatusCode.OK, list.Status);
        Assert.Equal(planId, Assert.Single(list.Body.EnumerateArray()).GetProperty("id").GetGuid());

        var published = await Send(HttpMethod.Post, $"/api/membership-plans/{planId}/versions", manager, Terms(w));
        Assert.Equal(2, published.Body.GetProperty("plan").GetProperty("latestVersion").GetProperty("version").GetInt32());
        Assert.Equal(("Favorable", "NewSalesOnly"), (published.Body.GetProperty("classification").GetString(), published.Body.GetProperty("applyTo").GetString()));

        var renamed = await Send(HttpMethod.Patch, $"/api/membership-plans/{planId}/details", manager, new { name = "Unlimited Gold" });
        Assert.Equal("Unlimited Gold", renamed.Body.GetProperty("name").GetString());

        var capacity = await Send(HttpMethod.Put, $"/api/membership-plans/{planId}/capacity", manager, new { maxActiveMemberships = 40 });
        Assert.Equal(40, capacity.Body.GetProperty("maxActiveMemberships").GetInt32());

        // Deaktivacija je zaseban grant (2B): .manage sam nije dovoljan.
        AssertError(HttpStatusCode.Forbidden, ErrorCodes.Forbidden, await Send(HttpMethod.Post, $"/api/membership-plans/{planId}/deactivate", manager));
        string deactivator = await Token(w, Grants.CatalogMembershipsDeactivate);
        var deactivated = await Send(HttpMethod.Post, $"/api/membership-plans/{planId}/deactivate", deactivator);
        Assert.False(deactivated.Body.GetProperty("isActive").GetBoolean());
    }

    [Fact]
    public async Task InvalidTerms_ReturnTheDomainSpecific400Codes()
    {
        await using SchedulingWorld w = await SchedulingWorld.Create(nameof(InvalidTerms_ReturnTheDomainSpecific400Codes));
        string manager = await Token(w, Grants.CatalogMembershipsView, Grants.CatalogMembershipsManage);

        AssertError(HttpStatusCode.BadRequest, ErrorCodes.MembershipPlanCompaniesRequired,
            await Send(HttpMethod.Post, "/api/membership-plans", manager, Terms(w, companyScope: "SelectedCompanies")));
        AssertError(HttpStatusCode.BadRequest, ErrorCodes.MembershipUsageLimitInvalid,
            await Send(HttpMethod.Post, "/api/membership-plans", manager, Terms(w, limits: new object[]
            {
                new { window = "Period", maxUses = 8 }, new { window = "Month", maxUses = 4 }
            })));
        AssertError(HttpStatusCode.NotFound, ErrorCodes.NotFound,
            await Send(HttpMethod.Get, $"/api/membership-plans/{Guid.NewGuid()}", manager));
    }
}
