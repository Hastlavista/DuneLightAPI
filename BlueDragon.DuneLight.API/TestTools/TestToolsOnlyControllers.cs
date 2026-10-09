using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Microsoft.AspNetCore.Mvc.ApplicationParts;
using Microsoft.AspNetCore.Mvc.Controllers;

namespace BlueDragon.DuneLight.API.TestTools;

/// <summary>Kad testni alati nisu uključeni, uklanja kontrolere označene <see cref="TestToolsOnlyAttribute"/> iz MVC otkrivanja
/// (registrira se nakon zadanog <see cref="ControllerFeatureProvider"/>), pa njihove rute ne postoje.</summary>
public sealed class TestToolsOnlyControllers : IApplicationFeatureProvider<ControllerFeature>
{
    public void PopulateFeature(IEnumerable<ApplicationPart> parts, ControllerFeature feature)
    {
        foreach (TypeInfo controller in feature.Controllers.Where(c => c.GetCustomAttribute<TestToolsOnlyAttribute>() != null).ToList())
            feature.Controllers.Remove(controller);
    }
}
