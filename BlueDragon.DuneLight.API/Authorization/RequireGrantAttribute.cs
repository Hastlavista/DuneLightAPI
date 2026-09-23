using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using BlueDragon.DuneLight.Core.Shared;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;

namespace BlueDragon.DuneLight.API.Authorization;

/// <summary>
/// Zamjena za [Authorize(Roles=...)]. Kad je navedeno više grantova, dovoljan je BILO KOJI (OR logika) —
/// npr. [RequireGrant("roster.entries.write.own", "roster.entries.write.all")]. Grant-only Tenant Authorization
/// Refactor — nema više Owner bypass-a, GrantContext.HasAny provjerava isključivo efektivne raw grantove.
/// </summary>
[AttributeUsage(AttributeTargets.Method | AttributeTargets.Class, AllowMultiple = false)]
public class RequireGrantAttribute : Attribute, IAsyncAuthorizationFilter
{
    private readonly string[] _grants;

    public RequireGrantAttribute(params string[] grants)
    {
        if (grants == null || grants.Length == 0)
            throw new ArgumentException("RequireGrant zahtijeva barem jedan grant-ključ.", nameof(grants));

        _grants = grants;
    }

    /// <summary>Read-only pristup navedenim grantovima za reflection-based diagnostiku (vidi
    /// EndpointGrantMetadataProvider) — ne utječe na OnAuthorizationAsync ponašanje ispod.</summary>
    public IReadOnlyList<string> Grants => _grants;

    public async Task OnAuthorizationAsync(AuthorizationFilterContext context)
    {
        GrantContext grantContext = await GrantAuthorization.Resolve(context);
        if (grantContext == null)
            return;

        if (!grantContext.HasAny(_grants))
            context.Result = new ForbidResult();
    }
}