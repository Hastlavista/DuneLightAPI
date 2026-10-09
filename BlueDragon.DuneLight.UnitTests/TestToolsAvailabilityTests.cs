#nullable disable
using System;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using System.Threading.Tasks;
using BlueDragon.DuneLight.API;
using BlueDragon.DuneLight.API.Controllers.Management;
using BlueDragon.DuneLight.API.TestTools;
using BlueDragon.DuneLight.Infrastructure.Domain.Settings;
using BlueDragon.DuneLight.UnitTests.Scheduling;
using Microsoft.AspNetCore.Mvc.ApplicationParts;
using Microsoft.AspNetCore.Mvc.Controllers;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;

namespace BlueDragon.DuneLight.UnitTests;

/// <summary>
/// T1 (CHANGED in T1: zamjenjuje DevelopmentOnlyToolsTests) — testni alati ([TestToolsOnly]: pomak sata, seed) postoje samo uz
/// izričitu postavku TestTools:Enabled, neovisno o imenu okruženja; bez nje ruta ne postoji (404, ne 401/403). Uključena postavka
/// u okruženju Production zaustavlja pokretanje.
/// </summary>
public class TestToolsAvailabilityTests : IClassFixture<MultiSegmentHttpContractTests.ApiHost>
{
    private readonly HttpClient _http;

    public TestToolsAvailabilityTests(MultiSegmentHttpContractTests.ApiHost host)
    {
        _http = host.Client;
    }

    [Fact]
    public async Task WithoutTheSetting_TheClockRouteDoesNotExist_EvenBeforeAuthentication()
    {
        HttpResponseMessage response = await _http.PostAsJsonAsync(
            $"/api/management/organizations/{Guid.NewGuid()}/test-tools/clock/advance", new { days = 1 });

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task TheOldDevelopmentRenewalRoute_IsRemoved()
    {
        HttpResponseMessage response = await _http.PostAsJsonAsync("/api/dev/time/membership-renewal-run", new { date = "2030-01-01" });

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void TheControllerFilter_RemovesTestToolControllers_OnlyWhenDisabled(bool enabled)
    {
        ApplicationPartManager manager = new();
        manager.ApplicationParts.Add(new AssemblyPart(typeof(Startup).Assembly));
        manager.FeatureProviders.Add(new ControllerFeatureProvider());
        if (!enabled)
            manager.FeatureProviders.Add(new TestToolsOnlyControllers());

        ControllerFeature feature = new();
        manager.PopulateFeature(feature);

        Assert.Equal(enabled, feature.Controllers.Any(c => c.AsType() == typeof(ManagementTestToolsController)));
        Assert.True(feature.Controllers.Count > 10); // ostali kontroleri ostaju
    }

    [Theory]
    [InlineData("Production", true, true)]
    [InlineData("Production", false, false)]
    [InlineData("Development", true, false)]
    [InlineData("Staging", true, false)]
    public void StartupGuard_RefusesTestToolsOnlyInProduction(string environment, bool enabled, bool refused)
    {
        Exception error = Record.Exception(() =>
            TestToolsStartupGuard.Ensure(new TestToolsSettings { Enabled = enabled }, new Environment(environment)));

        Assert.Equal(refused, error is InvalidOperationException);
        if (!refused)
            Assert.Null(error);
    }

    private sealed class Environment : IHostEnvironment
    {
        public Environment(string name) => EnvironmentName = name;
        public string EnvironmentName { get; set; }
        public string ApplicationName { get; set; } = "test";
        public string ContentRootPath { get; set; } = AppContext.BaseDirectory;
        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
    }
}
