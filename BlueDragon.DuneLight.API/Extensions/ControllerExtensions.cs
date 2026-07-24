using System;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using BlueDragon.DuneLight.Infrastructure.Utils;
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

    /// <summary>Koristi se za provjere vlasništva na razini servisa (npr. trener smije mijenjati samo svoje termine).</summary>
    public static bool CurrentIsAdmin(this ControllerBase controller)
    {
        return controller.User.IsInRole(UserRoleClaims.Admin);
    }
}
