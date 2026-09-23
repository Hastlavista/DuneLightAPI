using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using BlueDragon.DuneLight.API.Authorization;
using BlueDragon.DuneLight.Core.DTOs.Diagnostics;
using BlueDragon.DuneLight.Core.Interfaces.Diagnostics;
using Microsoft.AspNetCore.Mvc.ActionConstraints;
using Microsoft.AspNetCore.Mvc.Controllers;
using Microsoft.AspNetCore.Mvc.Infrastructure;

namespace BlueDragon.DuneLight.API.Diagnostics;

/// <summary>
/// Reflection/action-descriptor inspekcija svih kontroler akcija radi izvlačenja RequireGrant/
/// RequireGrantOrAssignedCompany metapodataka (FAZA 1 Part G) — koristi ASP.NET Core
/// IActionDescriptorCollectionProvider (već izgrađen routing model, ne ručni Assembly.GetTypes scan), pa Route
/// dolazi točno onakav kakav MVC stvarno koristi za attribute-routed kontrolere. Isključivo diagnostika —
/// NE mijenja rutiranje/autorizaciju, samo čita već postojeće ControllerActionDescriptor podatke. Grant-only
/// Tenant Authorization Refactor — RequireOwnerAttribute je uklonjen, svi tenant endpointi sada nose
/// RequireGrant/RequireGrantOrAssignedCompany.
/// </summary>
public class EndpointGrantMetadataProvider : IEndpointGrantMetadataProvider
{
    private readonly IActionDescriptorCollectionProvider _actionDescriptorCollectionProvider;

    public EndpointGrantMetadataProvider(IActionDescriptorCollectionProvider actionDescriptorCollectionProvider)
    {
        _actionDescriptorCollectionProvider = actionDescriptorCollectionProvider;
    }

    public List<EndpointGrantMetadata> GetEndpointGrantMetadata()
    {
        List<EndpointGrantMetadata> result = new();

        foreach (Microsoft.AspNetCore.Mvc.Abstractions.ActionDescriptor descriptor in _actionDescriptorCollectionProvider.ActionDescriptors.Items)
        {
            if (descriptor is not ControllerActionDescriptor controllerActionDescriptor)
                continue;

            MethodInfo methodInfo = controllerActionDescriptor.MethodInfo;
            TypeInfo controllerType = controllerActionDescriptor.ControllerTypeInfo;

            RequireGrantAttribute requireGrant = methodInfo.GetCustomAttribute<RequireGrantAttribute>()
                                                  ?? controllerType.GetCustomAttribute<RequireGrantAttribute>();
            RequireGrantOrAssignedCompanyAttribute requireGrantOrCompany = methodInfo.GetCustomAttribute<RequireGrantOrAssignedCompanyAttribute>()
                                                                            ?? controllerType.GetCustomAttribute<RequireGrantOrAssignedCompanyAttribute>();

            List<string> requiredGrants = new();
            if (requireGrant != null)
                requiredGrants.AddRange(requireGrant.Grants);
            if (requireGrantOrCompany != null)
                requiredGrants.AddRange(requireGrantOrCompany.Grants);

            string httpMethod = controllerActionDescriptor.ActionConstraints?
                .OfType<HttpMethodActionConstraint>()
                .SelectMany(c => c.HttpMethods)
                .FirstOrDefault() ?? "ANY";

            string route = controllerActionDescriptor.AttributeRouteInfo?.Template ?? string.Empty;

            result.Add(new EndpointGrantMetadata(
                Controller: controllerActionDescriptor.ControllerName,
                Action: controllerActionDescriptor.ActionName,
                HttpMethod: httpMethod,
                Route: route,
                RequiredGrants: requiredGrants.Distinct().OrderBy(g => g).ToList(),
                RequireGrantOrAssignedCompany: requireGrantOrCompany != null));
        }

        return result;
    }
}
