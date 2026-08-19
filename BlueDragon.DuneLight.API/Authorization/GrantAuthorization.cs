using System;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Threading.Tasks;
using BlueDragon.DuneLight.Core.Interfaces;
using BlueDragon.DuneLight.Core.Shared;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.Extensions.DependencyInjection;

namespace BlueDragon.DuneLight.API.Authorization;

/// <summary>
/// Zajednička logika za RequireGrantAttribute i RequireOwnerAttribute: provjerava autentikaciju, razrješava
/// GrantContext (jednom po zahtjevu, keširano u HttpContext.Items) da ga ControllerExtensions.HasGrant/
/// CurrentIsOwner mogu ponovno iskoristiti unutar akcije bez dodatnog DB upita.
/// </summary>
internal static class GrantAuthorization
{
    public const string HttpContextItemsKey = "GrantContext";

    public static async Task<GrantContext> Resolve(AuthorizationFilterContext context)
    {
        ClaimsPrincipal user = context.HttpContext.User;
        if (user.Identity == null || !user.Identity.IsAuthenticated)
        {
            context.Result = new UnauthorizedResult();
            return null;
        }

        if (context.HttpContext.Items.TryGetValue(HttpContextItemsKey, out object cached))
            return (GrantContext)cached;

        string userIdValue = user.FindFirstValue(ClaimTypes.NameIdentifier) ?? user.FindFirstValue(JwtRegisteredClaimNames.Sub);
        string organizationIdValue = user.FindFirstValue("organizationId");
        if (!Guid.TryParse(userIdValue, out Guid userId) || !Guid.TryParse(organizationIdValue, out Guid organizationId))
        {
            context.Result = new UnauthorizedResult();
            return null;
        }

        IGrantResolver grantResolver = context.HttpContext.RequestServices.GetRequiredService<IGrantResolver>();
        GrantContext grantContext = await grantResolver.Resolve(organizationId, userId);
        context.HttpContext.Items[HttpContextItemsKey] = grantContext;
        return grantContext;
    }
}