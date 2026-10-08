using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Microsoft.AspNetCore.Mvc.ApplicationParts;
using Microsoft.AspNetCore.Mvc.Controllers;

namespace BlueDragon.DuneLight.API.Development;

/// <summary>Izvan Development okruženja uklanja kontrolere označene <see cref="DevelopmentOnlyAttribute"/> iz MVC otkrivanja
/// (registrira se nakon zadanog <see cref="ControllerFeatureProvider"/>), pa njihove rute ne postoje.</summary>
public sealed class DevelopmentOnlyControllers : IApplicationFeatureProvider<ControllerFeature>
{
    public void PopulateFeature(IEnumerable<ApplicationPart> parts, ControllerFeature feature)
    {
        foreach (TypeInfo controller in feature.Controllers.Where(c => c.GetCustomAttribute<DevelopmentOnlyAttribute>() != null).ToList())
            feature.Controllers.Remove(controller);
    }
}
