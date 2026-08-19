using System;
using System.Threading.Tasks;
using BlueDragon.DuneLight.Core.Shared;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;

namespace BlueDragon.DuneLight.API.Authorization;

/// <summary>
/// Zamjena za [Authorize(Roles=...)]. Kad je navedeno više grantova, dovoljan je BILO KOJI (OR logika) —
/// npr. [RequireGrant("roster.entries.write.own", "roster.entries.write.all")]. Owner korisnik (User.IsOwner)
/// uvijek prolazi, bez obzira na navedene grantove — vidi GrantContext.HasAny.
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

    public async Task OnAuthorizationAsync(AuthorizationFilterContext context)
    {
        GrantContext grantContext = await GrantAuthorization.Resolve(context);
        if (grantContext == null)
            return;

        if (!grantContext.HasAny(_grants))
            context.Result = new ForbidResult();
    }
}