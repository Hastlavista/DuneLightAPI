using System;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using BlueDragon.DuneLight.API.Authorization;
using BlueDragon.DuneLight.Core.Shared;
using Microsoft.AspNetCore.Mvc;

namespace BlueDragon.DuneLight.API.Extensions;

public static class ControllerExtensions
{
    /// <summary>
    /// JWT bearer middleware mapira "sub" na ClaimTypes.NameIdentifier, dok ApiKey shema
    /// (i DevAuthBypass) postavljaju oba claima eksplicitno — provjeravamo oboje radi robusnosti.
    /// </summary>
    public static Guid CurrentUserId(this ControllerBase controller)
    {
        string value = controller.User.FindFirstValue(ClaimTypes.NameIdentifier)
                       ?? controller.User.FindFirstValue(JwtRegisteredClaimNames.Sub);
        return Guid.Parse(value!);
    }

    public static Guid CurrentOrganizationId(this ControllerBase controller)
    {
        string value = controller.User.FindFirstValue("organizationId");
        return Guid.Parse(value!);
    }

    /// <summary>
    /// Čita GrantContext koji je [RequireGrant]/[RequireOwner] već razriješio za ovaj zahtjev (vidi
    /// GrantAuthorization) — bez dodatnog DB upita. Koristi se za own/all razlikovanje unutar akcije
    /// (npr. treba li servisu proslijediti puni opseg ili samo vlastiti employeeId).
    /// </summary>
    public static bool HasGrant(this ControllerBase controller, string grant)
    {
        return controller.HttpContext.Items.TryGetValue(GrantAuthorization.HttpContextItemsKey, out object cached)
               && cached is GrantContext grantContext
               && grantContext.Has(grant);
    }

    public static bool CurrentIsOwner(this ControllerBase controller)
    {
        return controller.HttpContext.Items.TryGetValue(GrantAuthorization.HttpContextItemsKey, out object cached)
               && cached is GrantContext grantContext
               && grantContext.IsOwner;
    }
}
