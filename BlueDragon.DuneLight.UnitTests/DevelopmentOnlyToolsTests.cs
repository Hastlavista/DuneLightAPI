#nullable disable
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using System.Threading.Tasks;
using BlueDragon.DuneLight.API;
using BlueDragon.DuneLight.API.Controllers.Development;
using BlueDragon.DuneLight.API.Development;
using BlueDragon.DuneLight.UnitTests.Scheduling;
using Microsoft.AspNetCore.Mvc.ApplicationParts;
using Microsoft.AspNetCore.Mvc.Controllers;

namespace BlueDragon.DuneLight.UnitTests;

/// <summary>
/// Razvojni alati ([DevelopmentOnly], npr. simulacija vremena članarina) moraju biti FIZIČKI nedostupni izvan Developmenta: u
/// Production hostu ruta ne postoji (404, ne 401/403), a filter kontrolera ih uklanja samo izvan Developmenta.
/// </summary>
public class DevelopmentOnlyToolsTests : IClassFixture<MultiSegmentHttpContractTests.ApiHost>
{
    private readonly HttpClient _http;

    public DevelopmentOnlyToolsTests(MultiSegmentHttpContractTests.ApiHost host)
    {
        _http = host.Client;
    }

    [Fact]
    public async Task InProduction_TheTimeToolRouteDoesNotExist_EvenBeforeAuthentication()
    {
        HttpResponseMessage response = await _http.PostAsJsonAsync("/api/dev/time/membership-renewal-run", new { date = "2030-01-01" });

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void TheControllerFilter_RemovesDevelopmentOnlyControllers_OnlyOutsideDevelopment(bool isDevelopment)
    {
        ApplicationPartManager manager = new();
        manager.ApplicationParts.Add(new AssemblyPart(typeof(Startup).Assembly));
        manager.FeatureProviders.Add(new ControllerFeatureProvider());
        if (!isDevelopment)
            manager.FeatureProviders.Add(new DevelopmentOnlyControllers());

        ControllerFeature feature = new();
        manager.PopulateFeature(feature);

        Assert.Equal(isDevelopment, feature.Controllers.Any(c => c.AsType() == typeof(DevelopmentTimeController)));
        Assert.True(feature.Controllers.Count > 10); // ostali kontroleri ostaju
    }
}
