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
/// P2 phase 2B — HTTP contract of client memberships through the REAL API pipeline: routes, one grant per action (view, sell,
/// cancel, pause, plan-change, end-override, void-sale; P2 log 2026-10-07), string enums and DateOnly binding.
/// </summary>
public class ClientMembershipHttpContractTests : IClassFixture<MultiSegmentHttpContractTests.ApiHost>
{
    private readonly HttpClient _http;

    public ClientMembershipHttpContractTests(MultiSegmentHttpContractTests.ApiHost host)
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

    [Fact]
    public async Task EveryMembershipAction_HasItsOwnGrant()
    {
        await using SchedulingWorld w = await SchedulingWorld.Create(nameof(EveryMembershipAction_HasItsOwnGrant));
        string catalog = await Token(w, Grants.CatalogMembershipsView, Grants.CatalogMembershipsManage);
        var plan = await Send(HttpMethod.Post, "/api/membership-plans", catalog, new
        {
            name = "Gold", price = 50m, startFee = 0m, billingInterval = "Monthly", renewalAnchor = "PurchaseDate",
            companyScope = "AllCompanies", services = new[] { new { serviceId = w.Service.Id } },
            pause = new { allowed = true, maxPauseDays = 30, extendsPeriod = true }
        });
        Guid planId = plan.Body.GetProperty("id").GetGuid();

        string viewer = await Token(w, Grants.ClientsMembershipsView);
        string seller = await Token(w, Grants.ClientsMembershipsSell);
        string canceller = await Token(w, Grants.ClientsMembershipsCancel);
        string pauser = await Token(w, Grants.ClientsMembershipsPause);
        string ender = await Token(w, Grants.ClientsMembershipsEndOverride);
        string voider = await Token(w, Grants.ClientsMembershipsVoidSale);

        object sale = new { membershipPlanId = planId, soldCompanyId = w.Company.Id };
        string clientUrl = $"/api/clients/{w.Client.Id}/memberships";
        AssertError(HttpStatusCode.Forbidden, ErrorCodes.Forbidden, await Send(HttpMethod.Post, clientUrl, viewer, sale));
        var sold = await Send(HttpMethod.Post, clientUrl, seller, sale);
        Assert.Equal(HttpStatusCode.OK, sold.Status);
        Assert.Equal(("Active", "Staff"), (sold.Body.GetProperty("state").GetString(), sold.Body.GetProperty("soldVia").GetString()));
        Guid id = sold.Body.GetProperty("id").GetGuid();

        AssertError(HttpStatusCode.Forbidden, ErrorCodes.Forbidden, await Send(HttpMethod.Get, clientUrl, seller));
        Assert.Equal(HttpStatusCode.OK, (await Send(HttpMethod.Get, $"/api/memberships/{id}", viewer)).Status);

        string start = DateTime.UtcNow.Date.AddDays(3).ToString("yyyy-MM-dd");
        string end = DateTime.UtcNow.Date.AddDays(5).ToString("yyyy-MM-dd");
        AssertError(HttpStatusCode.Forbidden, ErrorCodes.Forbidden, await Send(HttpMethod.Post, $"/api/memberships/{id}/pauses", canceller, new { startsOn = start, endsOn = end }));
        var paused = await Send(HttpMethod.Post, $"/api/memberships/{id}/pauses", pauser, new { startsOn = start, endsOn = end });
        Assert.Equal("Days", paused.Body.GetProperty("pauses")[0].GetProperty("kind").GetString());

        AssertError(HttpStatusCode.Forbidden, ErrorCodes.Forbidden, await Send(HttpMethod.Post, $"/api/memberships/{id}/cancellation", pauser, new { }));
        Assert.Equal(HttpStatusCode.OK, (await Send(HttpMethod.Get, $"/api/memberships/{id}/cancellation-preview", canceller)).Status);
        var cancelled = await Send(HttpMethod.Post, $"/api/memberships/{id}/cancellation", canceller, new { reason = "Seli se" });
        Assert.Equal("Cancelled", cancelled.Body.GetProperty("endReason").GetString());
        Assert.Equal(HttpStatusCode.OK, (await Send(HttpMethod.Delete, $"/api/memberships/{id}/cancellation", canceller)).Status);

        string endsOn = DateTime.UtcNow.Date.AddDays(7).ToString("yyyy-MM-dd");
        AssertError(HttpStatusCode.Forbidden, ErrorCodes.Forbidden, await Send(HttpMethod.Post, $"/api/memberships/{id}/end", canceller, new { endsOn, reason = "x" }));
        var ended = await Send(HttpMethod.Post, $"/api/memberships/{id}/end", ender, new { endsOn, reason = "Ozljeda" });
        Assert.Equal(("EndOverride", endsOn), (ended.Body.GetProperty("endReason").GetString(), ended.Body.GetProperty("endsOn").GetString()));

        // 2C — zaduženja: pregled uz view, plaćanje kroz checkout uz checkout.manage, otpis samo uz zaseban grant.
        var charges = await Send(HttpMethod.Get, $"/api/memberships/{id}/charges", viewer);
        JsonElement charge = Assert.Single(charges.Body.EnumerateArray());
        Assert.Equal(("Period", "Pending"), (charge.GetProperty("kind").GetString(), charge.GetProperty("status").GetString()));
        Guid chargeId = charge.GetProperty("id").GetGuid();
        Assert.Equal(HttpStatusCode.OK, (await Send(HttpMethod.Get, $"/api/memberships/{id}/periods", viewer)).Status);

        string cashier = await Token(w, Grants.CheckoutView, Grants.CheckoutManage);
        var checkout = await Send(HttpMethod.Post, "/api/checkouts", cashier, new { clientId = w.Client.Id, companyId = w.Company.Id });
        var withItem = await Send(HttpMethod.Post, $"/api/checkouts/{checkout.Body.GetProperty("id").GetGuid()}/items/membership-charge", cashier,
            new { membershipChargeId = chargeId, amount = 10m });
        Assert.Equal("MembershipCharge", Assert.Single(withItem.Body.GetProperty("items").EnumerateArray()).GetProperty("type").GetString());
        await Send(HttpMethod.Post, $"/api/checkouts/{checkout.Body.GetProperty("id").GetGuid()}/cancel", cashier);

        string writer = await Token(w, Grants.MembershipsChargesWriteOff);
        AssertError(HttpStatusCode.Forbidden, ErrorCodes.Forbidden, await Send(HttpMethod.Post, $"/api/membership-charges/{chargeId}/write-off", voider, new { reason = "x" }));
        var writtenOff = await Send(HttpMethod.Post, $"/api/membership-charges/{chargeId}/write-off", writer, new { reason = "Akcija" });
        Assert.Equal("WrittenOff", writtenOff.Body.GetProperty("status").GetString());

        AssertError(HttpStatusCode.Forbidden, ErrorCodes.Forbidden, await Send(HttpMethod.Post, $"/api/memberships/{id}/void-sale", ender, new { reason = "x" }));
        var voided = await Send(HttpMethod.Post, $"/api/memberships/{id}/void-sale", voider, new { reason = "Greška" });
        Assert.Equal("Voided", voided.Body.GetProperty("state").GetString());
    }
}
